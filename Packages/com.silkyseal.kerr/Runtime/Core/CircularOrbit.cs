using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Prograde circular equatorial geodesics (Bardeen, Press &amp; Teukolsky 1972), the orbits a thin disk's gas follows.
    /// r is the Boyer–Lindquist radius in units of M; a ≥ 0 means the gas co-rotates with the hole.
    /// </summary>
    public static class CircularOrbit
    {
        /// <summary>Innermost stable circular orbit: 6 at a = 0, 1 at a = 1.</summary>
        public static double IscoRadius(double a)
        {
            double z1 = 1 + Math.Cbrt(1 - a * a) * (Math.Cbrt(1 + a) + Math.Cbrt(1 - a));
            double z2 = Math.Sqrt(3 * a * a + z1 * z1);
            return 3 + z2 - Math.Sqrt((3 - z1) * (3 + z1 + 2 * z2));
        }

        /// <summary>Angular velocity dφ/dt seen from infinity.</summary>
        public static double AngularVelocity(double r, double a) => 1 / (r * Math.Sqrt(r) + a);

        /// <summary>Energy at infinity per unit rest mass, −u_t. 1 − E(r_isco) is the disk's radiative efficiency.</summary>
        public static double SpecificEnergy(double r, double a)
        {
            double sr = Math.Sqrt(r);
            return (r * sr - 2 * sr + a) / (Math.Sqrt(sr) * Math.Sqrt(r * sr - 3 * sr + 2 * a) * sr);
        }

        /// <summary>
        /// Kerr-Schild 4-velocity of gas on the circular orbit through equatorial point x: u^t (∂t + Ω ∂φ),
        /// with ∂φ = (0, −y, x, 0). u^t is the same in both charts because u^r = 0.
        /// </summary>
        public static Vector4d Velocity(Vector3d x, double a)
        {
            double r = KerrSchild.Radius(x, a), ut = TimeDilation(r, a), w = ut * AngularVelocity(r, a);
            return new Vector4d(ut, -w * x.Y, w * x.X, 0);
        }

        /// <summary>
        /// Frequency ratio g = ν_observed / ν_emitted for a photon leaving gas at x, from the conserved covector
        /// components k_t and k_φ = x k_y − y k_x, normalised so the observer measures k·U = 1.
        /// </summary>
        public static double Shift(Vector3d x, double kt, double kPhi, double a)
        {
            double r = KerrSchild.Radius(x, a);
            return 1 / (TimeDilation(r, a) * (kt + AngularVelocity(r, a) * kPhi));
        }

        static double TimeDilation(double r, double a)
        {
            double sr = Math.Sqrt(r);
            return (r * sr + a) / (Math.Sqrt(sr) * sr * Math.Sqrt(r * sr - 3 * sr + 2 * a));
        }
    }
}
