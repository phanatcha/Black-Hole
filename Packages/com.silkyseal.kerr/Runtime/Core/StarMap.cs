using System;
using System.Collections.Generic;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Stars binned onto a cube with a mip chain, so a pixel gathers starlight from 3 × 3 texels whatever its footprint.
    /// A texel keeps the total illuminance of its stars, their lux-weighted log-mean temperature and their lux-weighted
    /// centroid: stars stay points at fixed places on every level, and no level loses light.
    /// </summary>
    /// <remarks>
    /// Face index is 2·axis + (negative ? 1 : 0), axes X, Y, Z. On the face of axis a, u and v are components
    /// (a + 1) % 3 and (a + 2) % 3 over |component a|, both in [−1, 1]. The shader mirrors this layout.
    /// </remarks>
    public sealed class StarMap
    {
        /// <summary>Texels per face edge on level 0, a power of two.</summary>
        public int Size { get; }

        public int LevelCount => Levels.Length;

        /// <summary>
        /// Per level, (lux, kelvin, u, v) for each texel, face-major then row-major, Size >> level texels per edge.
        /// Empty texels are all zero.
        /// </summary>
        public float[][] Levels { get; }

        StarMap(int size, float[][] levels) { Size = size; Levels = levels; }

        public static StarMap Bake(IReadOnlyList<Star> stars, int size)
        {
            if (size < 1 || (size & (size - 1)) != 0) throw new ArgumentException("Size must be a power of two.", nameof(size));

            // Per texel sums of lux, lux·ln T, lux·u and lux·v, in double so faint stars survive next to bright ones.
            var sums = new double[6 * size * size * 4];
            foreach (var star in stars)
            {
                if (!(star.Lux > 0)) continue;
                ToFace(star.Direction, out int face, out double u, out double v);
                int t = Index(face, Texel(u, size), Texel(v, size), size) * 4;
                sums[t] += star.Lux;
                sums[t + 1] += star.Lux * Math.Log(star.Kelvin);
                sums[t + 2] += star.Lux * u;
                sums[t + 3] += star.Lux * v;
            }

            var levels = new List<float[]> { Finish(sums) };
            for (int n = size; n > 1; n /= 2)
            {
                sums = Downsample(sums, n);
                levels.Add(Finish(sums));
            }
            return new StarMap(size, levels.ToArray());
        }

        /// <summary>
        /// Starlight seen through one pixel, linear sRGB in cd/m². Each star is weighted by a tent over the pixel's
        /// footprint and spread over the footprint's solid angle Ω′ = |jx × jy|. The tents of neighbouring pixels sum to
        /// one, so every star's light arrives in full and, through a lens, times (pixel solid angle) / Ω′.
        /// CPU reference of the shader's gather.
        /// </summary>
        /// <param name="direction">Sky direction at the pixel centre.</param>
        /// <param name="jx">Change of sky direction per pixel step in x, tangent to <paramref name="direction"/>.</param>
        /// <param name="jy">The same per pixel step in y.</param>
        /// <param name="g">Frequency shift, observed over emitted.</param>
        public Vector3d Radiance(Vector3d direction, Vector3d jx, Vector3d jy, double g)
        {
            ToFace(direction, out int face, out double u, out double v);

            // The finer level is the first whose texels span the footprint, so the 3 × 3 texels around the pixel hold every
            // star its tent reaches; the next level blends in so the choice never pops. (2 / Size) / (1 + u² + v²) is at
            // most a level-0 texel's angle, which errs towards the coarser level.
            double footprint = jx.Length + jy.Length, texel = 2.0 / Size / (1 + u * u + v * v);
            double level = Math.Log(footprint / texel, 2) + 1;
            if (!(level > 0)) return Gather(0, direction, face, u, v, jx, jy, g);
            int below = (int)level;
            if (below >= LevelCount - 1) return Gather(LevelCount - 1, direction, face, u, v, jx, jy, g);
            double blend = level - below;
            return Gather(below, direction, face, u, v, jx, jy, g) * (1 - blend) +
                   Gather(below + 1, direction, face, u, v, jx, jy, g) * blend;
        }

        /// <summary>Unit direction of face coordinates (u, v); |u| or |v| past 1 reaches onto the neighbouring faces.</summary>
        public static Vector3d FromFace(int face, double u, double v)
        {
            double s = (face & 1) == 0 ? 1 : -1;
            switch (face >> 1)
            {
                case 0: return new Vector3d(s, u, v).Normalized;
                case 1: return new Vector3d(v, s, u).Normalized;
                default: return new Vector3d(u, v, s).Normalized;
            }
        }

        internal static void ToFace(Vector3d d, out int face, out double u, out double v)
        {
            double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
            if (ax >= ay && ax >= az) { face = d.X >= 0 ? 0 : 1; u = d.Y / ax; v = d.Z / ax; }
            else if (ay >= az) { face = d.Y >= 0 ? 2 : 3; u = d.Z / ay; v = d.X / ay; }
            else { face = d.Z >= 0 ? 4 : 5; u = d.X / az; v = d.Y / az; }
        }

        Vector3d Gather(int level, Vector3d direction, int face, double u, double v, Vector3d jx, Vector3d jy, double g)
        {
            double xx = Vector3d.Dot(jx, jx), xy = Vector3d.Dot(jx, jy), yy = Vector3d.Dot(jy, jy), det = xx * yy - xy * xy;
            double solidAngle = Vector3d.Cross(jx, jy).Length;
            if (!(det > 0) || !(solidAngle > 0)) return default;

            int n = Size >> level, ci = Texel(u, n), cj = Texel(v, n);
            var texels = Levels[level];
            var sum = default(Vector3d);
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int f = face, i = ci + di, j = cj + dj;
                bool offI = i < 0 || i >= n, offJ = j < 0 || j >= n;
                // shortcut: past a cube corner there is no diagonal texel, so a pixel whose centre texel is a corner one misses
                // the stars in the corner texel of the third face; fine while corners are a tiny share of the sky.
                if (offI && offJ) continue;
                if (offI || offJ)
                {
                    // Off the face: the texel on the neighbouring face that meets this one along the edge.
                    ToFace(FromFace(face, (i + 0.5) * 2 / n - 1, (j + 0.5) * 2 / n - 1), out f, out double eu, out double ev);
                    i = Texel(eu, n);
                    j = Texel(ev, n);
                }

                int t = Index(f, i, j, n) * 4;
                double lux = texels[t];
                if (lux == 0) continue;

                // Offset to the star on the plane tangent at the pixel centre, in footprint units.
                var star = FromFace(f, texels[t + 2], texels[t + 3]);
                double cos = Vector3d.Dot(star, direction);
                if (cos <= 0) continue;
                var offset = star / cos - direction;
                double ox = Vector3d.Dot(offset, jx), oy = Vector3d.Dot(offset, jy);
                double x = (yy * ox - xy * oy) / det, y = (xx * oy - xy * ox) / det;
                double weight = Math.Max(0, 1 - Math.Abs(x)) * Math.Max(0, 1 - Math.Abs(y));
                if (weight > 0) sum += Colour(texels[t + 1], g) * (weight * lux);
            }
            return sum / solidAngle;
        }

        // Linear sRGB per lux of a blackbody at T seen with shift g: the blackbody at g·T, at the ratio of their luminances.
        static Vector3d Colour(double kelvin, double g) => Blackbody.LinearSrgb(g * kelvin) / Blackbody.Xyz(kelvin).Y;

        static float[] Finish(double[] sums)
        {
            var texels = new float[sums.Length];
            for (int t = 0; t < sums.Length; t += 4)
            {
                double lux = sums[t];
                if (!(lux > 0)) continue;
                texels[t] = (float)lux;
                texels[t + 1] = (float)Math.Exp(sums[t + 1] / lux);
                texels[t + 2] = (float)(sums[t + 2] / lux);
                texels[t + 3] = (float)(sums[t + 3] / lux);
            }
            return texels;
        }

        // Every quantity is a sum, so a parent texel adds its four children.
        static double[] Downsample(double[] sums, int n)
        {
            int half = n / 2;
            var parents = new double[6 * half * half * 4];
            for (int face = 0; face < 6; face++)
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int child = Index(face, i, j, n) * 4, parent = Index(face, i / 2, j / 2, half) * 4;
                for (int k = 0; k < 4; k++) parents[parent + k] += sums[child + k];
            }
            return parents;
        }

        static int Texel(double coordinate, int n) => Math.Min(Math.Max((int)Math.Floor((coordinate + 1) * 0.5 * n), 0), n - 1);

        static int Index(int face, int i, int j, int n) => (face * n + j) * n + i;
    }
}
