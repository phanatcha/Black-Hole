using System;

namespace SilkySeal.Kerr
{
    /// <summary>How a backward-traced light ray ended. Absorbed: an <see cref="IRayObserver"/> stopped it.</summary>
    public enum RayStatus { Escaped, Captured, MaxSteps, Absorbed }

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
        /// <summary>Times the ray passed through the equatorial plane z = 0 (sign changes of z between steps).</summary>
        public int EquatorCrossings;
    }

    /// <summary>
    /// Sees a ray as it is traced: each pass through the equatorial plane, where a thin disk lives, and each step, for
    /// matter that fills space. A struct so the tracer's loop stays allocation-free and the calls inline.
    /// </summary>
    public interface IRayObserver
    {
        /// <summary>
        /// x: crossing point (z ≈ 0, interpolated between steps). kt, kPhi: the ray's conserved k_t and
        /// k_φ = x k_y − y k_x. Return false to stop the ray as absorbed.
        /// </summary>
        bool OnEquatorCrossing(Vector3d x, double kt, double kPhi);

        /// <summary>
        /// Called at the camera with h = 0, then after every accepted step with the new position and covector p_μ; h is
        /// that step's length in the affine parameter. nextStep is the length the integrator tries next and may be
        /// lowered. Return false to stop the ray as absorbed.
        /// </summary>
        bool OnStep(Vector3d x, Vector4d p, double h, ref double nextStep);
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
            var none = default(NoObserver);
            return Trace(x0, k0Lower, s, ref none);
        }

        /// <summary>As <see cref="Trace(Vector3d, Vector4d, in TraceSettings)"/>, reporting the ray to observer.</summary>
        public static RayResult Trace<T>(Vector3d x0, Vector4d k0Lower, in TraceSettings s, ref T observer)
            where T : struct, IRayObserver
        {
            double a = s.Spin, pt = k0Lower.T;
            double captureRadius = KerrSchild.HorizonRadius(a) + HorizonMargin;
            var x = x0;
            var p = k0Lower.Spatial;
            double h = InitialStepPerRadius * KerrSchild.Radius(x0, a);
            double maxAbsH = Math.Abs(KerrSchild.Hamiltonian(x, k0Lower, a));

            var status = RayStatus.MaxSteps;
            int steps = 0, crossings = 0;
            if (!observer.OnStep(x, k0Lower, 0, ref h)) { status = RayStatus.Absorbed; steps = s.MaxSteps; }
            for (; steps < s.MaxSteps; steps++)
            {
                if (KerrSchild.Radius(x, a) <= captureRadius) { status = RayStatus.Captured; break; }
                if (x.Length >= s.FarRadius) { status = RayStatus.Escaped; break; }
                var before = x;
                var pBefore = p;
                double stepSize = h;
                if (!GeodesicIntegrator.Step(ref x, ref p, pt, a, ref h, s.Tolerance)) continue;
                maxAbsH = Math.Max(maxAbsH, Math.Abs(KerrSchild.Hamiltonian(x, new Vector4d(pt, p), a)));
                if (before.Z * x.Z < 0)
                {
                    crossings++;
                    var crossing = EquatorCrossing(before, pBefore, x, p, pt, a, stepSize);
                    if (!observer.OnEquatorCrossing(crossing, pt, x.X * p.Y - x.Y * p.X)) { status = RayStatus.Absorbed; break; }
                }
                if (!observer.OnStep(x, new Vector4d(pt, p), stepSize, ref h)) { status = RayStatus.Absorbed; break; }
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
                EquatorCrossings = crossings,
            };
        }

        /// <summary>
        /// Where one accepted step of length h in the affine parameter meets z = 0: the root of the cubic Hermite through
        /// both ends and their velocities dx/dλ. Error O(h⁴) where the chord's is O(h²); at the GPU tolerance a chord
        /// puts the hit radius ~0.5 % off, a 2–3 % colour error on a cool disk.
        /// </summary>
        static Vector3d EquatorCrossing(Vector3d x0, Vector3d p0, Vector3d x1, Vector3d p1, double pt, double a, double h)
        {
            var v0 = KerrSchild.Raise(new Vector4d(pt, p0), x0, a).Spatial * h;
            var v1 = KerrSchild.Raise(new Vector4d(pt, p1), x1, a).Spatial * h;
            // z changes sign over [0, 1], so Newton from the chord's root stays in the bracket.
            double t = x0.Z / (x0.Z - x1.Z);
            for (int i = 0; i < 3; i++)
            {
                double t2 = t * t;
                double z = (2 * t2 * t - 3 * t2 + 1) * x0.Z + (t2 * t - 2 * t2 + t) * v0.Z + (3 * t2 - 2 * t2 * t) * x1.Z + (t2 * t - t2) * v1.Z;
                double dz = (6 * t2 - 6 * t) * (x0.Z - x1.Z) + (3 * t2 - 4 * t + 1) * v0.Z + (3 * t2 - 2 * t) * v1.Z;
                t = Math.Min(1, Math.Max(0, t - z / dz));
            }
            double s2 = t * t, s3 = s2 * t;
            return x0 * (2 * s3 - 3 * s2 + 1) + v0 * (s3 - 2 * s2 + t) + x1 * (3 * s2 - 2 * s3) + v1 * (s3 - s2);
        }

        struct NoObserver : IRayObserver
        {
            public bool OnEquatorCrossing(Vector3d x, double kt, double kPhi) => true;
            public bool OnStep(Vector3d x, Vector4d p, double h, ref double nextStep) => true;
        }

        /// <summary>
        /// Traces the ray seen through one pixel. ndc is in [−1, 1] on both axes, +y up.
        /// The pixel direction n is built in the camera's rest frame, so aberration from the boost is exact.
        /// </summary>
        /// <exception cref="ArgumentException">Frame and settings disagree on spin.</exception>
        public static RayResult TracePixel(in CameraFrame f, double ndcX, double ndcY, double tanHalfFovY, double aspect, in TraceSettings s)
        {
            if (f.Spin != s.Spin)
                throw new ArgumentException($"Frame spin {f.Spin} differs from trace spin {s.Spin}.", nameof(s));

            return Trace(f.Position, PixelRay(f, ndcX, ndcY, tanHalfFovY, aspect), s);
        }

        /// <summary>Past-directed null covector k_μ at the camera for one pixel, before any tracing.</summary>
        public static Vector4d PixelRay(in CameraFrame f, double ndcX, double ndcY, double tanHalfFovY, double aspect)
        {
            var dir = new Vector3d(ndcX * tanHalfFovY * aspect, ndcY * tanHalfFovY, 1).Normalized;
            var n = dir.X * f.Right + dir.Y * f.Up + dir.Z * f.Forward;
            // n − U is null and past-directed: following it goes back along the incoming light.
            return KerrSchild.Lower(n - f.U, f.Position, f.Spin);
        }
    }
}
