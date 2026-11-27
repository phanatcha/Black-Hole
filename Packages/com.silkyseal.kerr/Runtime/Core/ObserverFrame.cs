using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// A camera's orthonormal tetrad at one event. All vectors carry upper indices in the Kerr-Schild
    /// coordinate basis; U is the 4-velocity, Right/Up/Forward the spatial axes it measures with.
    /// </summary>
    public struct CameraFrame
    {
        public Vector3d Position;
        public Vector4d U, Right, Up, Forward;
        public double Spin;
    }

    /// <summary>Builds camera frames. O(1), allocation-free; runs once per frame on the CPU in double.</summary>
    public static class ObserverFrame
    {
        // Below this x² + y² the azimuthal Killing vector vanishes and ω = −g_tφ/g_φφ is 0/0.
        const double AxisRadiusSquared = 1e-12;

        static readonly Vector4d TimeTranslation = new Vector4d(1, 0, 0, 0);

        /// <summary>
        /// Zero-angular-momentum observer: u ∝ ξ_t + ω ξ_φ. Unlike a static observer it exists
        /// everywhere outside r₊, including inside the ergosphere. On the spin axis it is the static observer.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">x is at or inside the outer horizon.</exception>
        public static Vector4d Zamo(Vector3d x, double a)
        {
            if (KerrSchild.Radius(x, a) <= KerrSchild.HorizonRadius(a))
                throw new ArgumentOutOfRangeException(nameof(x), "No ZAMO at or inside the outer horizon.");

            var v = TimeTranslation;
            if (x.X * x.X + x.Y * x.Y >= AxisRadiusSquared)
            {
                var rotation = new Vector4d(0, -x.Y, x.X, 0);
                double omega = -KerrSchild.Dot(TimeTranslation, rotation, x, a) / KerrSchild.Dot(rotation, rotation, x, a);
                v = TimeTranslation + omega * rotation;
            }
            return v * (1.0 / Math.Sqrt(-KerrSchild.Dot(v, v, x, a)));
        }

        /// <summary>
        /// ZAMO tetrad at x, aligned with the camera axes (given in Kerr-Schild components), then boosted
        /// to speed beta along velocityDirKS. Gram–Schmidt order is forward, up, right, so the view
        /// direction is exact and no cross product decides handedness.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">beta outside [0, 1).</exception>
        public static CameraFrame Build(Vector3d x, double a, Vector3d forwardKS, Vector3d upKS, Vector3d rightKS,
                                        Vector3d velocityDirKS, double beta)
        {
            if (!(beta >= 0 && beta < 1))
                throw new ArgumentOutOfRangeException(nameof(beta), beta, "Speed must be in [0, 1) of c.");

            var frame = FromVelocity(x, a, Zamo(x, a), forwardKS, upKS, rightKS);
            if (beta > 0 && velocityDirKS.Length > 0)
                Boost(ref frame, new Vector4d(0, velocityDirKS), beta);
            return frame;
        }

        /// <summary>
        /// Tetrad of an observer with 4-velocity u, aligned with the camera axes as in <see cref="Build"/>. Works anywhere
        /// u is timelike, inside the horizon too, where no ZAMO exists. u is renormalized, so a slightly drifted geodesic
        /// velocity still gives an exact frame.
        /// </summary>
        public static CameraFrame FromVelocity(Vector3d x, double a, Vector4d u, Vector3d forwardKS, Vector3d upKS, Vector3d rightKS)
        {
            u *= 1.0 / Math.Sqrt(-KerrSchild.Dot(u, u, x, a));
            var forward = Orthonormalize(new Vector4d(0, forwardKS), u, default, default, x, a);
            var up = Orthonormalize(new Vector4d(0, upKS), u, forward, default, x, a);
            var right = Orthonormalize(new Vector4d(0, rightKS), u, forward, up, x, a);
            return new CameraFrame { Position = x, U = u, Right = right, Up = up, Forward = forward, Spin = a };
        }

        // Removes the components along u (timelike) and e1, e2 (unit spacelike), then normalizes.
        static Vector4d Orthonormalize(Vector4d v, Vector4d u, Vector4d e1, Vector4d e2, Vector3d x, double a)
        {
            v += KerrSchild.Dot(v, u, x, a) * u;
            v -= KerrSchild.Dot(v, e1, x, a) * e1;
            v -= KerrSchild.Dot(v, e2, x, a) * e2;
            return v * (1.0 / Math.Sqrt(KerrSchild.Dot(v, v, x, a)));
        }

        // Lorentz boost of the whole tetrad: U' = γ(U + β n), e' = e + (γ−1)(e·n)n + γβ(e·n)U.
        static void Boost(ref CameraFrame f, Vector4d velocityDir, double beta)
        {
            var x = f.Position;
            double a = f.Spin;
            var n = KerrSchild.Dot(velocityDir, f.Right, x, a) * f.Right
                  + KerrSchild.Dot(velocityDir, f.Up, x, a) * f.Up
                  + KerrSchild.Dot(velocityDir, f.Forward, x, a) * f.Forward;
            n *= 1.0 / Math.Sqrt(KerrSchild.Dot(n, n, x, a));
            double gamma = 1.0 / Math.Sqrt(1 - beta * beta);

            var u = f.U;
            f.Right = BoostAxis(f.Right, n, u, gamma, beta, x, a);
            f.Up = BoostAxis(f.Up, n, u, gamma, beta, x, a);
            f.Forward = BoostAxis(f.Forward, n, u, gamma, beta, x, a);
            f.U = gamma * (u + beta * n);
        }

        static Vector4d BoostAxis(Vector4d e, Vector4d n, Vector4d u, double gamma, double beta, Vector3d x, double a)
        {
            double en = KerrSchild.Dot(e, n, x, a);
            return e + ((gamma - 1) * en) * n + (gamma * beta * en) * u;
        }
    }
}
