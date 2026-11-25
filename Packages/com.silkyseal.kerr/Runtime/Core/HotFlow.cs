using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// A radiatively inefficient accretion flow (RIAF): hot, thick, see-through gas, the analytic model the EHT
    /// literature fits to Sgr A* (Broderick &amp; Loeb 2006, after Yuan, Quataert &amp; Narayan 2003; field from Broderick
    /// et al. 2011). Positions are Kerr-Schild with Boyer–Lindquist radius r, cylindrical radius ρ = √(r² − z²), all in
    /// units of M. Densities are cm⁻³, temperatures K, fields gauss. Every method is O(1) and allocation-free.
    /// </summary>
    public readonly struct HotFlow
    {
        /// <summary>
        /// Past this radius the flow is cut off. At r = 50 the 230 GHz emissivity has fallen by over four decades
        /// from its peak, so the image does not change; the cut bounds how far each ray samples gas.
        /// </summary>
        public const double OuterRadius = 50;
        /// <summary>Gas pressure over field pressure; the field is in rough equipartition with the ions.</summary>
        public const double PlasmaBeta = 10;
        /// <summary>
        /// Sgr A*'s mass as Broderick &amp; Loeb (2006) took it. Their densities were fitted to the spectrum with it, so the
        /// two stay together.
        /// </summary>
        public const double SgrAMassSolar = 4e6;
        /// <summary>m_e c² / k_B: kelvin per unit of θe.</summary>
        public const double ElectronRestKelvin = 5.9298e9;
        /// <summary>G M☉ / c² in cm: the length unit M for one solar mass.</summary>
        public const double SolarLengthCentimetres = 1.4766250e5;

        // Broderick & Loeb (2006), Table 1: spin, n_th⁰, T_e⁰, n_nth⁰, nonthermal density slope.
        static readonly double[,] s_Fits =
        {
            { 0.0, 3e7, 1.7e11, 8e4, -2.9 },
            { 0.5, 3e7, 1.4e11, 5e4, -2.8 },
            { 0.998, 1e7, 1.5e11, 1e5, -2.8 },
        };

        public readonly double Spin, MassSolar;
        public readonly double ThermalDensityScale, TemperatureScale, NonthermalDensityScale, NonthermalSlope;
        /// <summary>Weights of the field's (radial, toroidal, vertical) directions. Broderick &amp; Loeb use toroidal.</summary>
        public readonly Vector3d FieldMix;
        readonly double _iscoRadius, _iscoEnergy, _iscoAngularMomentum;

        public HotFlow(double spin, double massSolar, double thermalDensity, double temperature, double nonthermalDensity,
                       double nonthermalSlope, Vector3d fieldMix)
        {
            Spin = spin;
            MassSolar = massSolar;
            ThermalDensityScale = thermalDensity;
            TemperatureScale = temperature;
            NonthermalDensityScale = nonthermalDensity;
            NonthermalSlope = nonthermalSlope;
            FieldMix = fieldMix;
            _iscoRadius = CircularOrbit.IscoRadius(spin);
            _iscoEnergy = CircularOrbit.SpecificEnergy(_iscoRadius, spin);
            _iscoAngularMomentum = CircularOrbit.SpecificAngularMomentum(_iscoRadius, spin);
        }

        /// <summary>
        /// The Sgr A* fit at this spin, interpolated linearly between Broderick &amp; Loeb's three fitted spins (they
        /// interpolate their own tables the same way); clamped at a = 0.998.
        /// </summary>
        public static HotFlow SgrA(double spin, Vector3d fieldMix)
        {
            int row = spin < s_Fits[1, 0] ? 0 : 1;
            double t = Math.Min(1, (spin - s_Fits[row, 0]) / (s_Fits[row + 1, 0] - s_Fits[row, 0]));
            double Fit(int column) => s_Fits[row, column] + t * (s_Fits[row + 1, column] - s_Fits[row, column]);
            return new HotFlow(spin, SgrAMassSolar, Fit(1), Fit(2), Fit(3), Fit(4), fieldMix);
        }

        /// <summary>The length unit M in cm, which turns path lengths into column depths.</summary>
        public double LengthScale => MassSolar * SolarLengthCentimetres;

        /// <summary>Electron temperature, T_e⁰ r^−0.84.</summary>
        public double ElectronTemperature(double r) => TemperatureScale * Math.Pow(r, -0.84);

        /// <summary>Thermal electrons, n_th⁰ ρ^−1.1 e^(−z²/2ρ²): a thick flow whose height grows with ρ.</summary>
        public double ThermalDensity(Vector3d x, double r) => Profile(x, r, ThermalDensityScale, -1.1);

        /// <summary>Power-law electrons, n_nth⁰ ρ^slope e^(−z²/2ρ²).</summary>
        public double NonthermalDensity(Vector3d x, double r) => Profile(x, r, NonthermalDensityScale, NonthermalSlope);

        /// <summary>Field strength from B²/8π = n_th m_p c² / (6 r β): β times below the ions' virial pressure.</summary>
        public double FieldStrength(double thermalDensity, double r) =>
            Math.Sqrt(8 * Math.PI * thermalDensity * Synchrotron.ProtonMass * Synchrotron.LightSpeed * Synchrotron.LightSpeed
                      / (6 * r * PlasmaBeta));

        static double Profile(Vector3d x, double r, double scale, double slope)
        {
            double rho2 = r * r - x.Z * x.Z;
            // On the axis the Gaussian wins over the power law; 0 rather than ∞·0.
            return rho2 > 1e-12 ? scale * Math.Pow(rho2, 0.5 * slope) * Math.Exp(-0.5 * x.Z * x.Z / rho2) : 0;
        }

        /// <summary>
        /// Gas 4-velocity at x, the same on every shell of constant r: u ∝ ∂t + Ω ∂φ + V ∂r. Outside the ISCO Ω is the
        /// Keplerian angular velocity of that radius and V = 0. Inside, Ω and V = dr/dt are those of the ballistic
        /// plunge from the ISCO on the equator (keeping the ISCO's u_t and u_φ), so the equator follows Mummery &amp;
        /// Balbus's (2022) closed form and the gas above it falls in step. x must lie outside the horizon.
        /// </summary>
        public Vector4d Velocity(Vector3d x)
        {
            double a = Spin, r = KerrSchild.Radius(x, a), omega, drift = 0;
            if (r >= _iscoRadius) omega = CircularOrbit.AngularVelocity(r, a);
            else Plunge(r, out omega, out drift);

            var v = new Vector4d(1, -omega * x.Y, omega * x.X, 0) + Radial(x, r, a) * drift;
            double norm = KerrSchild.Dot(v, v, x, a);
            // Off the equator a shell moves slower than on it, so this holds; the ZAMO only keeps u defined if not.
            return norm < 0 ? v * (1 / Math.Sqrt(-norm)) : ObserverFrame.Zamo(x, a);
        }

        // dφ/dt and dr/dt (Kerr-Schild t and φ) of the equatorial plunge at r. There u = A ∂t + B ∂φ + C ∂r: the Killing
        // parts fix A and B for given C, and C = u^r then solves u·u = −1.
        void Plunge(double r, out double omega, out double drift)
        {
            double a = Spin;
            var x = new Vector3d(r, a, 0); // the equator at φ = 0, where ∂r is the x axis
            var time = new Vector4d(1, 0, 0, 0);
            var rotation = new Vector4d(0, -a, r, 0);
            var radial = new Vector4d(0, 1, 0, 0);
            double tt = KerrSchild.Dot(time, time, x, a), tp = KerrSchild.Dot(time, rotation, x, a);
            double pp = KerrSchild.Dot(rotation, rotation, x, a);
            double tr = KerrSchild.Dot(time, radial, x, a), pr = KerrSchild.Dot(rotation, radial, x, a);
            double rr = KerrSchild.Dot(radial, radial, x, a), det = tt * pp - tp * tp;
            double e = _iscoEnergy, l = _iscoAngularMomentum;
            double wA = (-e * pp - l * tp) / det, wB = (l * tt + e * tp) / det;  // the part with u_t = −E, u_φ = L
            double vA = -(tr * pp - pr * tp) / det, vB = -(pr * tt - tr * tp) / det; // ∂r made orthogonal to both
            double killingNorm = -e * wA + l * wB, normalNorm = vA * tr + vB * pr + rr;
            // 0 at the ISCO itself, where rounding can make it slightly negative.
            double c = -Math.Sqrt(Math.Max(0, (-1 - killingNorm) / normalNorm));
            double timeComponent = wA + vA * c;
            omega = (wB + vB * c) / timeComponent;
            drift = c / timeComponent;
        }

        /// <summary>
        /// Unit field direction b at x, orthogonal to the gas velocity u: the <see cref="FieldMix"/> of the radial,
        /// toroidal and vertical directions, each first projected into the gas's rest frame. Zero where the mix vanishes
        /// (the toroidal direction on the spin axis).
        /// </summary>
        public Vector4d FieldDirection(Vector3d x, Vector4d u)
        {
            double a = Spin, r = KerrSchild.Radius(x, a);
            var b = Spatial(Radial(x, r, a), u, x) * FieldMix.X
                  + Spatial(new Vector4d(0, -x.Y, x.X, 0), u, x) * FieldMix.Y
                  + Spatial(new Vector4d(0, 0, 0, 1), u, x) * FieldMix.Z;
            double norm = KerrSchild.Dot(b, b, x, a);
            return norm > 1e-24 ? b * (1 / Math.Sqrt(norm)) : default;

            Vector4d Spatial(Vector4d v, Vector4d gas, Vector3d at)
            {
                var s = v + gas * KerrSchild.Dot(v, gas, at, a);
                double n = KerrSchild.Dot(s, s, at, a);
                return n > 1e-24 ? s * (1 / Math.Sqrt(n)) : default;
            }
        }

        // ∂_r of Kerr-Schild spherical coordinates (t, r, θ, φ held fixed) in Cartesian components: the spatial part of l.
        static Vector4d Radial(Vector3d x, double r, double a)
        {
            double s = r * r + a * a;
            return new Vector4d(0, (r * x.X + a * x.Y) / s, (r * x.Y - a * x.X) / s, x.Z / r);
        }
    }
}
