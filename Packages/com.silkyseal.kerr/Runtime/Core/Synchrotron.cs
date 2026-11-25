using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Synchrotron emissivities of hot electrons in a magnetic field, per steradian, in the gas's rest frame. cgs units:
    /// densities in cm⁻³, fields in gauss, frequencies in Hz, j in erg s⁻¹ cm⁻³ Hz⁻¹ sr⁻¹. jQ is the linearly polarized
    /// part, polarized at right angles to the field as seen along the ray.
    /// Formulas from Dexter (2016), MNRAS 462, 115, Appendix A; the thermal fits are his A12, A13, A18 and A19.
    /// </summary>
    public static class Synchrotron
    {
        public const double ElectronCharge = 4.803204712570263e-10, ElectronMass = 9.1093837015e-28,
                            ProtonMass = 1.67262192369e-24, LightSpeed = 2.99792458e10,
                            Planck = 6.62607015e-27, Boltzmann = 1.380649e-16;

        /// <summary>
        /// Energy index p of the power-law electrons, N(γ) ∝ γ^−p: spectral index 1.25, so p = 2·1.25 + 1
        /// (Broderick &amp; Loeb 2006).
        /// </summary>
        public const double PowerLawIndex = 3.5;
        /// <summary>The power law's lower cutoff in Lorentz factor (Broderick &amp; Loeb 2006); it has no upper one.</summary>
        public const double MinLorentzFactor = 100;

        /// <summary>ν_B = eB / (2π m c), Hz.</summary>
        public static double GyroFrequency(double gauss) => ElectronCharge * gauss / (2 * Math.PI * ElectronMass * LightSpeed);

        /// <summary>F(x) = x ∫ₓ^∞ K₅/₃: one electron's synchrotron spectrum, x = ν / ν_c.</summary>
        public static double F(double x) => x * BesselIntegral(x, 5.0 / 3, true);

        /// <summary>G(x) = x K₂/₃(x): the linearly polarized part of the same spectrum.</summary>
        public static double G(double x) => x * BesselIntegral(x, 2.0 / 3, false);

        /// <summary>
        /// Maxwell–Jüttner electrons at θe = kT / mc², in the ultra-relativistic limit the fits assume (θe ≫ 1;
        /// at θe ≈ 1 they overestimate). O(1).
        /// </summary>
        public static void Thermal(double density, double thetaE, double gauss, double sinTheta, double nu,
                                   out double jI, out double jQ)
        {
            double x = nu / (1.5 * GyroFrequency(gauss) * sinTheta * thetaE * thetaE);
            double prefactor = density * ElectronCharge * ElectronCharge * nu / (2 * Math.Sqrt(3) * LightSpeed * thetaE * thetaE);
            double c = Math.Cbrt(x), damping = 2.5651 * Math.Exp(-1.8899 * c);
            jI = prefactor * (1 + 1.92 / c + 0.9977 / (c * c)) * damping;
            jQ = prefactor * (1 + 0.932 / c + 0.4998 / (c * c)) * damping;
        }

        /// <summary>
        /// Power-law electrons, N(γ) = n (p − 1) γ_min^(p−1) γ^−p above <see cref="MinLorentzFactor"/>. Dexter's A22 and
        /// A23 with the upper cutoff at infinity; the cutoff integral comes from a table, so this is O(1).
        /// </summary>
        public static void PowerLaw(double density, double gauss, double sinTheta, double nu, out double jI, out double jQ)
        {
            const double p = PowerLawIndex, g1 = MinLorentzFactor;
            double nuP = 1.5 * GyroFrequency(gauss) * sinTheta;
            if (!(nuP > 0))
            {
                jI = jQ = 0; // no field across the ray, no emission; x = ν / ν_p would be ∞
                return;
            }
            double prefactor = density * ElectronCharge * ElectronCharge * (p - 1) * nuP
                             / (2 * Math.Sqrt(3) * LightSpeed * Math.Pow(g1, 1 - p)) * Math.Pow(nu / nuP, -(p - 1) / 2);
            PowerLawIntegrals(nu / (g1 * g1 * nuP), out double gI, out double gQ);
            jI = prefactor * gI;
            jQ = prefactor * gQ;
        }

        /// <summary>Planck's law B_ν(T), erg s⁻¹ cm⁻² Hz⁻¹ sr⁻¹. Thermal absorption is j / B_ν (Kirchhoff).</summary>
        public static double PlanckRadiance(double nu, double kelvin)
        {
            double x = Planck * nu / (Boltzmann * kelvin);
            // e^x − 1 cancels to nothing in double at radio frequencies; the series keeps it exact there.
            double expm1 = x < 1e-5 ? x * (1 + 0.5 * x) : Math.Exp(x) - 1;
            return 2 * Planck * nu * nu * nu / (LightSpeed * LightSpeed * expm1);
        }

        // ∫₀^x z^((p−3)/2) S(z) dz for S = F and G, cumulative on a log grid in z. Below the grid S ≈ c z^(1/3).
        internal const double TableMinX = 1e-12, TableMaxX = 300;
        internal const int TableSize = 6000;
        static double[] s_TableI, s_TableQ;
        internal const double SmallXSlope = (PowerLawIndex - 3) / 2 + 1.0 / 3 + 1; // of the integrals below the grid

        /// <summary>The cutoff integrals at x = ν / (γ_min² ν_p); they level off at the Gamma-function values for x ≫ 1.</summary>
        internal static void PowerLawIntegrals(double x, out double gI, out double gQ)
        {
            if (s_TableI == null) BakeTables();
            double u = Math.Log(x / TableMinX) / Math.Log(TableMaxX / TableMinX) * (TableSize - 1);
            if (u <= 0)
            {
                double scale = Math.Pow(x / TableMinX, SmallXSlope);
                gI = s_TableI[0] * scale;
                gQ = s_TableQ[0] * scale;
                return;
            }
            int i = Math.Min((int)u, TableSize - 2);
            double f = Math.Min(u - i, 1);
            // Log-log interpolation: the integrals are power laws below x ≈ 1.
            gI = Math.Exp((1 - f) * Math.Log(s_TableI[i]) + f * Math.Log(s_TableI[i + 1]));
            gQ = Math.Exp((1 - f) * Math.Log(s_TableQ[i]) + f * Math.Log(s_TableQ[i + 1]));
        }

        static void BakeTables()
        {
            var tableI = new double[TableSize];
            var tableQ = new double[TableSize];
            double step = Math.Log(TableMaxX / TableMinX) / (TableSize - 1), weight = (PowerLawIndex - 1) / 2;
            // The integrand in ln z is z^((p−1)/2) S(z); start from its small-x power law.
            tableI[0] = 2.14953 * Math.Pow(TableMinX, SmallXSlope) / SmallXSlope;
            tableQ[0] = Math.Pow(2, -1.0 / 3) * 1.3541179394264 * Math.Pow(TableMinX, SmallXSlope) / SmallXSlope;
            double previousI = Math.Pow(TableMinX, weight) * F(TableMinX), previousQ = Math.Pow(TableMinX, weight) * G(TableMinX);
            for (int i = 1; i < TableSize; i++)
            {
                double z = TableMinX * Math.Exp(step * i), w = Math.Pow(z, weight);
                double nextI = w * F(z), nextQ = w * G(z);
                tableI[i] = tableI[i - 1] + 0.5 * step * (previousI + nextI);
                tableQ[i] = tableQ[i - 1] + 0.5 * step * (previousQ + nextQ);
                previousI = nextI;
                previousQ = nextQ;
            }
            s_TableQ = tableQ;
            s_TableI = tableI; // published last: a reader that sees it non-null also sees the Q table
        }

        // ∫₀^∞ e^(−x cosh u) cosh(νu) [/ cosh u] du, which is K_ν(x) or, divided by cosh u, ∫ₓ^∞ K_ν. Trapezoids in u
        // converge exponentially on this integrand; the upper limit is where e^(−x cosh u) has fallen below e^−80.
        static double BesselIntegral(double x, double order, bool integrated)
        {
            const int Steps = 1000;
            double top = Acosh(Math.Max(2, 80 / x)) + 1, du = top / Steps, sum = 0;
            for (int i = 0; i <= Steps; i++)
            {
                double u = i * du, ch = Math.Cosh(u);
                double f = Math.Exp(-x * ch) * Math.Cosh(order * u) / (integrated ? ch : 1);
                sum += i == 0 || i == Steps ? 0.5 * f : f;
            }
            return sum * du;
        }

        static double Acosh(double y) => Math.Log(y + Math.Sqrt(y * y - 1));
    }
}
