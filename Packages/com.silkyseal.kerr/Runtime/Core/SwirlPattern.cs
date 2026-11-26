using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Procedural streaks for gas on circular orbits: Voronoi cells whose seeds ride thin rings, each ring turning at its
    /// own Kepler rate, with gradient noise frozen in each seed's frame. Neighbouring rings slide past each other as the
    /// gas shears, but no cell is ever stretched, so the texture looks the same after any number of orbits: the "swirl
    /// space" of the r/GraphicsProgramming post "WIP Kerr Black Hole render engine written in VEX". Values multiply the
    /// gas's brightness or density and average to 1, so the total light is unchanged.
    /// </summary>
    public readonly struct SwirlPattern
    {
        /// <summary>Ring width in ln r: a cell is 5 % of r across.</summary>
        public const double RingWidth = 0.05;
        /// <summary>Cells per ring, so a cell is about 5 times longer along the orbit than across it.</summary>
        public const int Slots = 24;
        /// <summary>Rings cover 1 ≤ r ≤ e^(Rings · RingWidth) ≈ 600.</summary>
        public const int Rings = 128;
        /// <summary>
        /// Cell size, in disk cells, of the hot flow's clumps: a ray samples the flow every tenth of r, too sparse for the
        /// disk's streaks. Patterns this coarse draw one smooth value per cell, rising and falling with height, instead of
        /// noise inside it, which would also cost several times the gas's own light at every sample.
        /// </summary>
        public const int GasCoarseness = 4;

        // Seeds sit in the middle 80 % of their cell. A cell's weight falls as exp(−Sharpness d²), d in cells; cells
        // under WeightFloor of the nearest are skipped. Noise frequencies are per cell, across and along the orbit; a
        // clump's value turns over once per ClumpHeight cells of height. The gains bring the blend's spread to about 0.5
        // before it is clipped to ±1.
        const double Jitter = 0.8, Sharpness = 8, WeightFloor = 1e-3, AcrossFrequency = 2, AlongFrequency = 0.6,
                     ClumpHeight = 2, NoiseGain = 1.8, ClumpGain = 0.8;
        // Strands drift across the orbit by up to about Drift cells, by a noise at DriftAcross and DriftAlong per cell.
        // Ribbons: a mask noise, per cell across and along, whose rise from RibbonLow over RibbonSpan fades the strands
        // into a broad band at RibbonFrequency across, RibbonGain strong, still holding RibbonStrands of the strands.
        const double Drift = 0.7, DriftAcross = 0.8, DriftAlong = 2,
                     RibbonMaskAcross = 0.7, RibbonMaskAlong = 2.5, RibbonLow = -0.05, RibbonSpan = 0.35,
                     RibbonFrequency = 1, RibbonGain = 0.6, RibbonStrands = 0.2;
        const int Octaves = 3;

        public readonly double Spin, Contrast, Time;
        /// <summary>Cells this many times larger across and along than the disk's, and as many times fewer rings.</summary>
        public readonly int Coarseness;
        public readonly bool BroadStreams;

        /// <param name="spin">Sets each ring's Kepler rate.</param>
        /// <param name="contrast">0 leaves the gas smooth; 1 runs from black lanes to twice the mean.</param>
        /// <param name="time">Kerr-Schild coordinate time at the camera, M.</param>
        /// <param name="coarseness">1 for the disk, <see cref="GasCoarseness"/> for the hot flow; must divide <see cref="Slots"/>.</param>
        /// <param name="broadStreams">Use the coarse orbit grid for sheared disk streams instead of fine strands.</param>
        public SwirlPattern(double spin, double contrast, double time, int coarseness = 1, bool broadStreams = false)
        {
            Spin = spin;
            Contrast = contrast;
            Time = time;
            BroadStreams = broadStreams;
            Coarseness = broadStreams ? GasCoarseness : coarseness;
        }

        /// <summary>Radius of ring j's centre line, where its Kepler rate is taken.</summary>
        public static double RingRadius(int ring, int coarseness = 1) => Math.Exp((ring + 0.5) * RingWidth * coarseness);

        /// <summary>How far ring j has turned at coordinate time t, in turns, in [0, 1).</summary>
        public static double Turns(int ring, double spin, double time, int coarseness = 1)
        {
            double turns = CircularOrbit.AngularVelocity(RingRadius(ring, coarseness), spin) * time / (2 * Math.PI);
            return turns - Math.Floor(turns);
        }

        /// <summary>
        /// The multiplier at Kerr-Schild radius r, azimuth φ = atan2(y, x) and height z, as it was delay after
        /// <see cref="Time"/> (negative for light that left the gas before reaching the camera). In [1 − Contrast, 1 + Contrast].
        /// </summary>
        public double Value(double r, double phi, double z, double delay)
        {
            if (Contrast == 0) return 1;
            double width = RingWidth * Coarseness;
            double u = Math.Log(r) / width, height = z / (r * width), angle = phi / (2 * Math.PI);
            int first = (int)Math.Floor(u) - 1, rings = Rings / Coarseness, slots = Slots / Coarseness;

            // Two passes over the 3 × 3 cells around the point: the nearest seed, then the weighted blend.
            double nearest = double.MaxValue;
            for (int ring = Math.Max(first, 0); ring < Math.Min(first + 3, rings); ring++)
            {
                double along = Along(ring, slots, angle, delay);
                for (int k = -1; k <= 1; k++)
                {
                    Offset(ring, slots, along, k, u, out double du, out double dv, out _);
                    nearest = Math.Min(nearest, du * du + dv * dv);
                }
            }
            if (nearest == double.MaxValue) return 1;

            double sum = 0, weights = 0;
            for (int ring = Math.Max(first, 0); ring < Math.Min(first + 3, rings); ring++)
            {
                double along = Along(ring, slots, angle, delay);
                for (int k = -1; k <= 1; k++)
                {
                    Offset(ring, slots, along, k, u, out double du, out double dv, out uint hash);
                    double w = Math.Exp(-Sharpness * (du * du + dv * dv - nearest));
                    if (w < WeightFloor) continue;
                    double sample;
                    if (BroadStreams)
                        sample = 1.65 * Fbm(1.25 * du + 0.35 * dv, 0.55 * dv, 0, hash, 2);
                    else if (Coarseness == 1)
                        sample = NoiseGain * Strands(du, dv, height, hash);
                    else
                        sample = ClumpGain * Math.Cos(2 * Math.PI * (Unit(hash) + height / ClumpHeight));
                    sum += w * sample;
                    weights += w;
                }
            }
            return 1 + Contrast * Math.Max(-1, Math.Min(1, sum / weights));
        }

        // The point's position along the ring, in cells from the ring's own zero, in [0, slots).
        double Along(int ring, int slots, double angle, double delay)
        {
            double along = (angle - Turns(ring, Spin, Time + delay, Coarseness)) * slots;
            return along - slots * Math.Floor(along / slots);
        }

        // The point relative to the seed k cells along from its own cell, in cells across and along the orbit.
        static void Offset(int ring, int slots, double along, int k, double u, out double du, out double dv, out uint hash)
        {
            int slot = (int)Math.Floor(along) + k;
            hash = Pcg((uint)(ring * slots + (slot + slots) % slots));
            uint next = Pcg(hash);
            du = u - (ring + 0.5 + Jitter * (Unit(hash) - 0.5));
            dv = along - (slot + 0.5 + Jitter * (Unit(next) - 0.5));
            hash = Pcg(next);
        }

        // Fine strands that drift across the orbit as they go round, bunching and spreading, and for part of the way
        // merge into one broad ribbon before parting again; all in the seed's frame, so no dark lane runs on for long.
        static double Strands(double du, double dv, double height, uint hash)
        {
            double x = (hash & 0xFFF) / 16.0, y = ((hash >> 12) & 0xFFF) / 16.0;
            double drift = du + Drift * Noise(du * DriftAcross + x + 13.1, dv * DriftAlong + y + 3.3, 9.5);
            double fine = Fbm(drift * AcrossFrequency, dv * AlongFrequency, height * AcrossFrequency, hash);
            double t = (Noise(du * RibbonMaskAcross + x + 41.3, dv * RibbonMaskAlong + y + 17.9, 5.5) - RibbonLow) / RibbonSpan;
            t = Math.Max(0, Math.Min(1, t));
            double broad = RibbonGain * Noise(du * RibbonFrequency + x + 7.7, dv * AlongFrequency + y, 0.5);
            return Lerp(t * t * (3 - 2 * t), fine, broad + RibbonStrands * fine);
        }

        // Improved gradient noise (Perlin 2002) over octaves, shifted by the seed's hash so each cell has its own patch.
        static double Fbm(double x, double y, double z, uint hash, int octaves = Octaves)
        {
            x += (hash & 0xFFF) / 16.0;
            y += ((hash >> 12) & 0xFFF) / 16.0;
            z += (hash >> 24) / 16.0;
            double sum = 0, amplitude = 1;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Noise(x, y, z);
                x = 2 * x + 19.19;
                y = 2 * y + 7.31;
                z = 2 * z + 3.73;
                amplitude *= 0.5;
            }
            return sum;
        }

        static double Noise(double x, double y, double z)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            int ix = (int)fx, iy = (int)fy, iz = (int)fz;
            double tx = x - fx, ty = y - fy, tz = z - fz;
            double sx = Fade(tx), sy = Fade(ty), sz = Fade(tz);
            // Corner hashes Pcg(x + Pcg(y + Pcg(z))), sharing their inner terms.
            uint x0 = (uint)ix, x1 = x0 + 1, z0 = Pcg((uint)iz), z1 = Pcg((uint)iz + 1);
            uint y0z0 = Pcg((uint)iy + z0), y1z0 = Pcg((uint)iy + 1 + z0), y0z1 = Pcg((uint)iy + z1), y1z1 = Pcg((uint)iy + 1 + z1);
            return Lerp(sz,
                Lerp(sy,
                    Lerp(sx, Grad(Pcg(x0 + y0z0), tx, ty, tz), Grad(Pcg(x1 + y0z0), tx - 1, ty, tz)),
                    Lerp(sx, Grad(Pcg(x0 + y1z0), tx, ty - 1, tz), Grad(Pcg(x1 + y1z0), tx - 1, ty - 1, tz))),
                Lerp(sy,
                    Lerp(sx, Grad(Pcg(x0 + y0z1), tx, ty, tz - 1), Grad(Pcg(x1 + y0z1), tx - 1, ty, tz - 1)),
                    Lerp(sx, Grad(Pcg(x0 + y1z1), tx, ty - 1, tz - 1), Grad(Pcg(x1 + y1z1), tx - 1, ty - 1, tz - 1))));
        }

        // Perlin's twelve cube-edge gradients (four repeated), dotted with the offset from the corner.
        static double Grad(uint h, double x, double y, double z)
        {
            h &= 15;
            double a = h < 8 ? x : y, b = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? a : -a) + ((h & 2) == 0 ? b : -b);
        }

        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        static double Lerp(double t, double a, double b) => a + t * (b - a);
        static double Unit(uint h) => h / 4294967296.0;

        // PCG integer hash (O'Neill 2014, as chosen by Jarzynski & Olano 2020); the shaders use the same one.
        static uint Pcg(uint v)
        {
            uint state = v * 747796405u + 2891336453u;
            uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            return (word >> 22) ^ word;
        }
    }
}
