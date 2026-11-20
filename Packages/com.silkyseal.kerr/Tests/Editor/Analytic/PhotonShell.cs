using System;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// Spherical photon orbits of Kerr, the shell the photon ring is made of (Teo 2003; Gralla &amp; Lupsasca 2020,
    /// arXiv:1910.12873). Radii run between the prograde and retrograde equatorial orbits. M = 1, 0 &lt; a &lt; 1.
    /// </summary>
    internal static class PhotonShell
    {
        /// <summary>λ = L/E of the spherical orbit at radius r.</summary>
        public static double Lambda(double r, double a) => a + r / a * (r - 2 * Delta(r, a) / (r - 1));

        /// <summary>η = Q/E² of the spherical orbit at radius r.</summary>
        public static double Eta(double r, double a) => r * r * r / (a * a) * (4 * Delta(r, a) / ((r - 1) * (r - 1)) - r);

        /// <summary>
        /// Lyapunov exponent per half orbit: nearby rays separate by e^γ each time θ swings from one turning point
        /// to the other, so each subring is e^{−γ} the width of the one outside it. At a double root of R,
        /// δr grows at √(R''/2) per Mino time, R'' = 12r² + 2A; a half orbit lasts 2K(m)/κ.
        /// </summary>
        public static double Lyapunov(double r, double a)
        {
            double lambda = Lambda(r, a), eta = Eta(r, a);
            double rCoefficient = a * a - eta - lambda * lambda;
            // Same stable u₊ as AnalyticRay; κ² = η/u₊, m = −a²u₊²/η.
            double uPlus = 2 * eta / (Math.Sqrt(rCoefficient * rCoefficient + 4 * a * a * eta) - rCoefficient);
            double m = -a * a * uPlus * uPlus / eta, kappa = Math.Sqrt(eta / uPlus);
            return Math.Sqrt(6 * r * r + rCoefficient) * 2 * Elliptic.K(m) / kappa;
        }

        static double Delta(double r, double a) => r * r - 2 * r + a * a;
    }
}
