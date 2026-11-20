using System;

namespace SilkySeal.Kerr
{
    /// <summary>How a backward-traced light ray ended.</summary>
    public enum RayStatus { Escaped, Captured, MaxSteps }

    /// <summary>Integration limits for one ray.</summary>
    public struct TraceSettings
    {
        public double Spin;
        /// <summary>Per-step relative error bound of the adaptive integrator.</summary>
        public double Tolerance;
        /// <summary>Coordinate radius |x| beyond which a ray counts as escaped to the sky.</summary>
        public double FarRadius;
        /// <summary>Step attempts (accepted or rejected) before giving up.</summary>
        public int MaxSteps;

        /// <summary>Tight settings for the double-precision reference that the GPU is checked against.</summary>
        public static TraceSettings Reference(double spin) =>
            new TraceSettings { Spin = spin, Tolerance = 1e-10, FarRadius = 5000, MaxSteps = 200000 };
    }

    /// <summary>End state of a traced ray.</summary>
    public struct RayResult
    {
        public RayStatus Status;
        public Vector3d Position;
        /// <summary>Final covariant momentum p_μ (p_t unchanged from the start).</summary>
        public Vector4d Momentum;
        /// <summary>Unit coordinate direction the light came from, in Kerr-Schild axes.</summary>
        public Vector3d SkyDirection;
        public int Steps;
        public double MaxAbsHamiltonian;
    }

    /// <summary>Traces past-directed null geodesics from the camera back to their source.</summary>
    public static class RayTracer
    {
        /// <summary>
        /// Capture happens at r₊ + margin. In ingoing Kerr-Schild coordinates a past-directed ray only
        /// approaches the future horizon geometrically (its momentum diverges), so r ≤ r₊ is never reached.
        /// 1e-3 stays below the smallest gap between r₊ and the prograde photon orbit (0.0022 at a = 0.9999),
        /// so no escaping ray is cut off.
        /// </summary>
        public const double HorizonMargin = 1e-3;

        const double InitialStepPerRadius = 0.01;

        /// <summary>
        /// Integrates from x0 with past-directed null covector k0 until capture, escape or MaxSteps.
        /// O(steps); at most s.MaxSteps metric-gradient evaluations × 6. Allocation-free.
        /// </summary>
        public static RayResult Trace(Vector3d x0, Vector4d k0Lower, in TraceSettings s)
        {
            double a = s.Spin, pt = k0Lower.T;
            double captureRadius = KerrSchild.HorizonRadius(a) + HorizonMargin;
            var x = x0;
            var p = k0Lower.Spatial;
            double h = InitialStepPerRadius * KerrSchild.Radius(x0, a);
            double maxAbsH = Math.Abs(KerrSchild.Hamiltonian(x, k0Lower, a));

            var status = RayStatus.MaxSteps;
            int steps = 0;
            for (; steps < s.MaxSteps; steps++)
            {
                if (KerrSchild.Radius(x, a) <= captureRadius) { status = RayStatus.Captured; break; }
                if (x.Length >= s.FarRadius) { status = RayStatus.Escaped; break; }
                if (GeodesicIntegrator.Step(ref x, ref p, pt, a, ref h, s.Tolerance))
                    maxAbsH = Math.Max(maxAbsH, Math.Abs(KerrSchild.Hamiltonian(x, new Vector4d(pt, p), a)));
            }

            var momentum = new Vector4d(pt, p);
            return new RayResult
            {
                Status = status,
                Position = x,
                Momentum = momentum,
                SkyDirection = KerrSchild.Raise(momentum, x, a).Spatial.Normalized,
                Steps = steps,
                MaxAbsHamiltonian = maxAbsH,
            };
        }
    }
}
