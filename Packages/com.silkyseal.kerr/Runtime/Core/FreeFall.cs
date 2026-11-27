using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// A body falling freely: a timelike geodesic in Kerr-Schild coordinates, stepped in its own proper time with the same
    /// Hamiltonian integrator as light (H = −½, affine parameter = proper time). Kerr-Schild coordinates are regular
    /// across the outer horizon, so the fall crosses it smoothly; it ends just above the inner horizon r₋, where known
    /// physics stops, which at a = 0 is just above the singularity. Each step costs six metric gradients, in double.
    /// </summary>
    public sealed class FreeFall
    {
        /// <summary>The fall ends this many M above r₋.</summary>
        public const double EndMargin = 0.01;

        const double Tolerance = 1e-10, InitialStepPerRadius = 0.01;
        // Guards a frame against a stalled integrator; a fall takes a few steps per frame.
        const int MaxStepsPerAdvance = 100000;
        // Zoom-whirl launch: a little over the speed that would just escape, and how far off the separatrix to aim.
        const double LaunchOverEscape = 1.05, MaxLaunchBeta = 0.9, WhirlMargin = 1e-5;
        // Brackets the separatrix to (π/2)/2²⁴ ≈ 1e-7 rad, well inside the margin; each halving adds a whirl to the trials.
        const int WhirlBisections = 24;

        public readonly double Spin;
        Vector3d _x, _p;
        double _t, _pt, _h;

        /// <param name="x">Start, Kerr-Schild.</param>
        /// <param name="u">4-velocity there, upper indices, u·u = −1.</param>
        /// <param name="time">Kerr-Schild time at the start, M.</param>
        public FreeFall(Vector3d x, Vector4d u, double a, double time = 0)
        {
            Spin = a;
            _x = x;
            _t = time;
            var p = KerrSchild.Lower(u, x, a);
            _pt = p.T;
            _p = p.Spatial;
            _h = InitialStepPerRadius * Radius;
        }

        public Vector3d Position => _x;
        /// <summary>4-velocity, upper indices.</summary>
        public Vector4d Velocity => KerrSchild.Raise(new Vector4d(_pt, _p), _x, Spin);
        /// <summary>Kerr-Schild coordinate time, M: the far-away clock, finite across the horizon.</summary>
        public double Time => _t;
        /// <summary>The body's own clock, M, from the start.</summary>
        public double ProperTime { get; private set; }
        public double Radius => KerrSchild.Radius(_x, Spin);
        public bool Ended => Radius <= KerrSchild.InnerHorizonRadius(Spin) + EndMargin;

        /// <summary>Falls for dTau of proper time, or until <see cref="Ended"/>. Lands on the requested time exactly.</summary>
        public void Advance(double dTau)
        {
            double end = ProperTime + dTau;
            for (int i = 0; i < MaxStepsPerAdvance && ProperTime < end && !Ended; i++)
            {
                double remaining = end - ProperTime, h = Math.Min(_h, remaining), tried = h;
                bool last = h == remaining;
                if (GeodesicIntegrator.Step(ref _x, ref _t, ref _p, _pt, Spin, ref h, Tolerance))
                {
                    ProperTime = last ? end : ProperTime + tried;
                    // A step cut short to land on the end says nothing about the size the path allows.
                    if (last && tried < _h) continue;
                }
                _h = h;
            }
        }

        /// <summary>
        /// A zoom-whirl (Levin &amp; Perez-Giz 2008): from x, falling in a little faster than escape speed, aimed so close
        /// to the line between capture and escape that the body first circles the hole's unstable orbit. side −1 then
        /// plunges; +1 swings round and leaves. Prograde, in the plane through x and the hole's azimuthal direction.
        /// Bisects the aim over whole trial orbits, about 70 ms: call it on a key press, not every frame.
        /// </summary>
        public static FreeFall Whirl(Vector3d x, double a, int side, double time = 0)
        {
            double r = KerrSchild.Radius(x, a), beta = Math.Min(MaxLaunchBeta, LaunchOverEscape * Math.Sqrt(2 / r));
            var outward = x.Normalized;
            var around = new Vector3d(-x.Y, x.X, 0);
            around = around.Length > 0 ? around.Normalized : new Vector3d(0, 1, 0);

            // Aim, as an angle off straight down: 0 falls in, π/2 skims past at more than escape speed.
            double lo = 0, hi = Math.PI / 2;
            for (int i = 0; i < WhirlBisections; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Escapes(new FreeFall(x, Launch(x, a, outward, around, beta, mid), a), r)) hi = mid; else lo = mid;
            }
            double aim = side < 0 ? lo * (1 - WhirlMargin) : hi * (1 + WhirlMargin);
            return new FreeFall(x, Launch(x, a, outward, around, beta, aim), a, time);
        }

        static Vector4d Launch(Vector3d x, double a, Vector3d outward, Vector3d around, double beta, double aim) =>
            ObserverFrame.Build(x, a, -outward, Vector3d.Cross(outward, around), around,
                                -Math.Cos(aim) * outward + Math.Sin(aim) * around, beta).U;

        static bool Escapes(FreeFall fall, double start)
        {
            double horizon = KerrSchild.HorizonRadius(fall.Spin);
            for (int i = 0; i < MaxStepsPerAdvance; i++)
            {
                fall.Advance(1);
                double r = fall.Radius;
                if (r < horizon) return false;
                if (r > start) return true;
            }
            return false;
        }
    }
}
