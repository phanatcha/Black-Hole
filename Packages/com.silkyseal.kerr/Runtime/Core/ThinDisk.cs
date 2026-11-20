using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Novikov–Thorne thin accretion disk: gas on prograde circular orbits from the ISCO outwards, each ring radiating
    /// as a blackbody at the Page–Thorne (1974) local temperature, zero torque at the inner edge.
    /// Radii are Boyer–Lindquist in units of M.
    /// </summary>
    public sealed class ThinDisk
    {
        const double SolarGm = 1.32712440018e20;          // m³/s², IAU nominal GM☉
        const double LightSpeed = 2.99792458e8;
        const double StefanBoltzmann = 5.670374419e-8;
        const double ProtonMass = 1.67262192369e-27, ThomsonCrossSection = 6.6524587321e-29;

        public double Spin { get; }
        public double InnerRadius { get; }
        public double OuterRadius { get; }

        /// <summary>Rest mass accreted per second, kg/s. NaN for a disk built from its peak temperature.</summary>
        public double AccretionRate { get; }

        readonly double _temperatureToTheFourth; // T⁴ = this · FluxShape

        ThinDisk(double spin, double outerRadius, double accretionRate, double temperatureToTheFourth)
        {
            Spin = spin;
            InnerRadius = CircularOrbit.IscoRadius(spin);
            OuterRadius = outerRadius;
            AccretionRate = accretionRate;
            _temperatureToTheFourth = temperatureToTheFourth;
        }

        /// <summary>A disk around a hole of this mass accreting at this fraction of the Eddington luminosity.</summary>
        public static ThinDisk FromAccretion(double spin, double outerRadius, double massSolar, double eddingtonRatio)
        {
            double gm = SolarGm * massSolar, c2 = LightSpeed * LightSpeed;
            double eddington = 4 * Math.PI * gm * ProtonMass * LightSpeed / ThomsonCrossSection;
            double efficiency = 1 - CircularOrbit.SpecificEnergy(CircularOrbit.IscoRadius(spin), spin);
            double rate = eddingtonRatio * eddington / (efficiency * c2);
            double rg = gm / c2;
            return new ThinDisk(spin, outerRadius, rate, 3 * gm * rate / (8 * Math.PI * StefanBoltzmann * rg * rg * rg));
        }

        /// <summary>A disk with the Page–Thorne temperature profile scaled so its hottest ring is at peakKelvin.</summary>
        public static ThinDisk FromPeakTemperature(double spin, double outerRadius, double peakKelvin)
        {
            // The flux has one maximum outside the ISCO, so a ternary search finds it (or the outer edge if it is past it).
            double lo = CircularOrbit.IscoRadius(spin), hi = outerRadius;
            for (int i = 0; i < 100; i++)
            {
                double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                if (FluxShape(m1, spin) < FluxShape(m2, spin)) lo = m1; else hi = m2;
            }
            double t2 = peakKelvin * peakKelvin;
            return new ThinDisk(spin, outerRadius, double.NaN, t2 * t2 / FluxShape(0.5 * (lo + hi), spin));
        }

        /// <summary>Local effective temperature in kelvin, measured by the orbiting gas; 0 off the disk.</summary>
        public double Temperature(double r) =>
            r < InnerRadius || r > OuterRadius ? 0 : Math.Pow(_temperatureToTheFourth * FluxShape(r, Spin), 0.25);

        /// <summary>
        /// Page–Thorne flux in units of 3GMṀ / (8π r_g³), r_g = GM/c²: zero at the ISCO, → r⁻³ (the Newtonian disk) far out.
        /// Closed form of Page &amp; Thorne (1974) in x = √r, with x₁..₃ the roots of x³ − 3x + 2a.
        /// </summary>
        public static double FluxShape(double r, double a)
        {
            double r0 = CircularOrbit.IscoRadius(a);
            if (r <= r0) return 0;

            double x = Math.Sqrt(r), x0 = Math.Sqrt(r0), phase = Math.Acos(a) / 3;
            double x1 = 2 * Math.Cos(phase - Math.PI / 3), x2 = 2 * Math.Cos(phase + Math.PI / 3), x3 = -2 * Math.Cos(phase);
            double bracket = x - x0 - 1.5 * a * Math.Log(x / x0)
                - RootTerm(x, x0, a, x1, x2, x3) - RootTerm(x, x0, a, x2, x3, x1) - RootTerm(x, x0, a, x3, x1, x2);
            return bracket / (x * x * x * x * (x * x * x - 3 * x + 2 * a));
        }

        // x₂ → 0 as a → 0 and its coefficient goes to 0 like a/6; skip it rather than form 0/0.
        static double RootTerm(double x, double x0, double a, double xi, double xj, double xk) =>
            xi == 0 ? 0 : 3 * (xi - a) * (xi - a) / (xi * (xi - xj) * (xi - xk)) * Math.Log((x - xi) / (x0 - xi));
    }
}
