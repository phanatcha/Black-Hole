using System;
using System.Threading.Tasks;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Emissive linear sRGB as a physical spectrum, so RGB sources (a sky image, later a scene) take a frequency shift
    /// exactly instead of through a hand-tuned tint. A colour becomes a blackbody times a green bump,
    /// S(λ) = B(λ, T) · (a + b·φ(λ)) with a, b ≥ 0 and φ a Gaussian at 520 nm: colours on the Planckian locus (stars,
    /// integrated starlight) are pure blackbodies, and b reaches the green side. Seen with shift g the spectrum
    /// g⁵S(gλ) is B(λ, gT) · (a + b·φ(gλ)), so a shifted star colour is exactly the hotter or cooler blackbody.
    /// Whatever the spectrum cannot reach (magenta side, bluer than any blackbody) keeps its hue and takes the
    /// spectrum's change in brightness.
    /// </summary>
    /// <remarks>
    /// Not the sigmoid spectra of Jakob &amp; Hanika (2019): those are pinned down only inside 360–830 nm, and a shift
    /// brings their arbitrary ends into view (measured: a 10000 K colour seen at g = 2 came out 4× too bright).
    /// </remarks>
    public static class RgbSpectrum
    {
        /// <summary>Quadrature shared with the GPU: 48 wavelengths, 360 to 830 nm.</summary>
        public const int FirstNanometre = 360, StepNanometres = 10, SampleCount = 48;
        /// <summary>Fitted points per axis of each of the three table slices.</summary>
        public const int TableSize = 128;
        /// <summary>Temperature range of the fit.</summary>
        public const double MinKelvin = 800, MaxKelvin = 1e6;

        internal const double BumpNanometres = 520, BumpWidthNanometres = 30;
        // Second radiation constant hc / k in nm·K, and the wavelength the spectrum is normalised at.
        internal const double SecondRadiation = Blackbody.Planck * Blackbody.LightSpeed / Blackbody.Boltzmann * 1e9;
        internal const double NormalNanometres = 555;

        static readonly Lazy<Vector3d[]> s_Weights = new Lazy<Vector3d[]>(BuildWeights);
        static readonly Lazy<Vector3d[]> s_Table = new Lazy<Vector3d[]>(FitTable);

        /// <summary>Linear sRGB of a source with this colour seen with frequency shift g (observed / emitted).</summary>
        public static Vector3d Shift(Vector3d linearSrgb, double g)
        {
            var rgb = new Vector3d(Math.Max(linearSrgb.X, 0), Math.Max(linearSrgb.Y, 0), Math.Max(linearSrgb.Z, 0));
            var p = Parameters(rgb);
            if (p.Y == 0 && p.Z == 0) return default;

            double kelvin = Math.Exp(p.X);
            var model = Render(kelvin, p.Y, p.Z, 1);
            var shifted = Render(kelvin, p.Y, p.Z, g);
            // What the spectrum cannot hold (colours outside its gamut, table interpolation) keeps its hue and takes
            // the spectrum's change in brightness, so g = 1 gives the colour back exactly.
            double y = Luminance(model);
            var c = shifted + (rgb - model) * (y > 0 ? Luminance(shifted) / y : 1);
            return new Vector3d(Math.Max(c.X, 0), Math.Max(c.Y, 0), Math.Max(c.Z, 0));
        }

        static double Luminance(Vector3d c) => 0.2126 * c.X + 0.7152 * c.Y + 0.0722 * c.Z;

        /// <summary>
        /// Linear sRGB per unit spectrum at each quadrature wavelength, without the Planck denominator:
        /// rgb = Σ weight_k · (a + b·φ(gλ_k)) · Planck_k(gT) / Planck(555 nm, T), Planck_k(T) = 1 / (exp(hc / λ_k k T) − 1).
        /// </summary>
        internal static Vector3d[] Weights => s_Weights.Value;

        /// <summary>
        /// (ln T, a, b) on a TableSize² grid per slice. Slice m holds colours whose largest channel is m, scaled so that
        /// channel is ½; the grid axes are channels (m+1) % 3 and (m+2) % 3 over it, in [0, 1].
        /// Index (m · TableSize + j) · TableSize + i. Fitted on first use.
        /// </summary>
        internal static Vector3d[] Table => s_Table.Value;

        /// <summary>(ln T, a, b) of a colour by bilinear table lookup; mirrored by KerrSpectrumParameters on the GPU.</summary>
        internal static Vector3d Parameters(Vector3d linearSrgb)
        {
            double r = Math.Max(linearSrgb.X, 0), g = Math.Max(linearSrgb.Y, 0), b = Math.Max(linearSrgb.Z, 0);
            double max = Math.Max(r, Math.Max(g, b));
            if (max <= 0) return default;

            int m = r >= g && r >= b ? 0 : g >= b ? 1 : 2;
            double u = (m == 0 ? g : m == 1 ? b : r) / max, v = (m == 0 ? b : m == 1 ? r : g) / max;
            Cell(u, out int i, out double fu);
            Cell(v, out int j, out double fv);
            var t = Table;
            int row = (m * TableSize + j) * TableSize + i;
            var bottom = t[row] * (1 - fu) + t[row + 1] * fu;
            var top = t[row + TableSize] * (1 - fu) + t[row + TableSize + 1] * fu;
            var p = bottom * (1 - fv) + top * fv;
            return new Vector3d(p.X, p.Y * 2 * max, p.Z * 2 * max); // the table is for a largest channel of ½
        }

        /// <summary>Linear sRGB of B(λ, gT) · (a + b·φ(gλ)) / Planck(555 nm, T) by the shared quadrature.</summary>
        internal static Vector3d Render(double kelvin, double a, double b, double g)
        {
            var weights = Weights;
            double norm = Math.Exp(SecondRadiation / (NormalNanometres * kelvin)) - 1;
            var sum = default(Vector3d);
            for (int k = 0; k < SampleCount; k++)
            {
                double nm = FirstNanometre + k * StepNanometres;
                // exp overflows to ∞ for a source far redder than the eye can see: that sample is then 0, as it should be.
                sum += weights[k] * ((a + b * Bump(g * nm)) * norm / (Math.Exp(SecondRadiation / (nm * g * kelvin)) - 1));
            }
            return sum;
        }

        internal static double Bump(double nm)
        {
            double t = (nm - BumpNanometres) / BumpWidthNanometres;
            return Math.Exp(-0.5 * t * t);
        }

        static void Cell(double t, out int index, out double fraction)
        {
            double p = t * (TableSize - 1);
            index = Math.Min((int)p, TableSize - 2);
            fraction = p - index;
        }

        static Vector3d[] BuildWeights()
        {
            // Planck radiance per nm without its denominator, 2hc² / λ⁵ (W·sr⁻¹·m⁻²·nm⁻¹, λ in nm), in cd/m² per bin.
            const double c1 = 2 * Blackbody.Planck * Blackbody.LightSpeed * Blackbody.LightSpeed * 1e45 * 1e-9;
            var weights = new Vector3d[SampleCount];
            for (int k = 0; k < SampleCount; k++)
            {
                double nm = FirstNanometre + k * StepNanometres;
                var xyz = new Vector3d(Blackbody.MatchX(nm), Blackbody.MatchY(nm), Blackbody.MatchZ(nm));
                weights[k] = Blackbody.XyzToLinearSrgb(xyz) * (c1 / Math.Pow(nm, 5) * Blackbody.LuminousEfficacy * StepNanometres);
            }
            // Scale so a = 1 is about luminance 1 near 6500 K; keeps a and b near 1 for float on the GPU.
            var white = default(Vector3d);
            double norm = Math.Exp(SecondRadiation / (NormalNanometres * 6500)) - 1;
            for (int k = 0; k < SampleCount; k++)
                white += weights[k] * (norm / (Math.Exp(SecondRadiation / ((FirstNanometre + k * StepNanometres) * 6500.0)) - 1));
            double luminance = 0.2126 * white.X + 0.7152 * white.Y + 0.0722 * white.Z;
            for (int k = 0; k < SampleCount; k++) weights[k] /= luminance;
            return weights;
        }

        // The basis colours depend only on T, so every cell scans one shared ln T grid and refines its minimum with a
        // parabola through the neighbouring scan points.
        static Vector3d[] FitTable()
        {
            const int Scan = 1024;
            double lo = Math.Log(MinKelvin), step = (Math.Log(MaxKelvin) - lo) / (Scan - 1);
            var flat = new Vector3d[Scan];
            var bump = new Vector3d[Scan];
            for (int s = 0; s < Scan; s++) Basis(lo + s * step, out flat[s], out bump[s]);

            var table = new Vector3d[3 * TableSize * TableSize];
            Parallel.For(0, table.Length, index =>
            {
                int i = index % TableSize, j = index / TableSize % TableSize, m = index / (TableSize * TableSize);
                double u = 0.5 * i / (TableSize - 1), v = 0.5 * j / (TableSize - 1);
                var target = m == 0 ? new Vector3d(0.5, u, v) : m == 1 ? new Vector3d(v, 0.5, u) : new Vector3d(u, v, 0.5);

                int best = 0;
                double bestError = double.MaxValue;
                for (int s = 0; s < Scan; s++)
                {
                    double error = Solve(target, flat[s], bump[s], out _, out _);
                    if (error < bestError) { bestError = error; best = s; }
                }

                double lnKelvin = lo + best * step;
                if (best > 0 && best < Scan - 1)
                {
                    double below = Solve(target, flat[best - 1], bump[best - 1], out _, out _);
                    double above = Solve(target, flat[best + 1], bump[best + 1], out _, out _);
                    double curvature = below - 2 * bestError + above;
                    if (curvature > 0) lnKelvin += 0.5 * step * (below - above) / curvature;
                }
                Basis(lnKelvin, out var f, out var g);
                Solve(target, f, g, out double a, out double b);
                table[index] = new Vector3d(lnKelvin, a, b);
            });
            return table;
        }

        static void Basis(double lnKelvin, out Vector3d flat, out Vector3d bump)
        {
            double kelvin = Math.Exp(lnKelvin);
            flat = Render(kelvin, 1, 0, 1);
            bump = Render(kelvin, 0, 1, 1);
        }

        // Non-negative least squares on B and B·φ. A bump keeps the spectrum single-peaked, so a blueshift always makes
        // it bluer; a notch would make magenta colours redder as it slid from green into blue. Magenta colours are
        // therefore the nearest blackbody plus a residual that Shift carries along.
        static double Solve(Vector3d target, Vector3d flat, Vector3d bump, out double a, out double b)
        {
            double ff = Vector3d.Dot(flat, flat), fb = Vector3d.Dot(flat, bump), bb = Vector3d.Dot(bump, bump);
            double ft = Vector3d.Dot(flat, target), bt = Vector3d.Dot(bump, target), det = ff * bb - fb * fb;
            a = (ft * bb - bt * fb) / det;
            b = (bt * ff - ft * fb) / det;
            if (a < 0 || b < 0)
            {
                // The optimum lies on an edge: take the better one-term fit.
                double flatOnly = Math.Max(ft / ff, 0), bumpOnly = Math.Max(bt / bb, 0);
                var withFlat = flat * flatOnly - target;
                var withBump = bump * bumpOnly - target;
                if (Vector3d.Dot(withFlat, withFlat) <= Vector3d.Dot(withBump, withBump)) { a = flatOnly; b = 0; }
                else { a = 0; b = bumpOnly; }
            }
            var r = flat * a + bump * b - target;
            return Vector3d.Dot(r, r);
        }
    }
}
