using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// Linear polarization carried along a light ray in Kerr. Parallel transport of the polarization vector f along the
    /// ray conserves the complex Walker–Penrose constant κ = κ1 + iκ2 (Walker &amp; Penrose 1970), so the direction of
    /// polarization the gas emits is known at the camera without transporting anything: build κ where the light is
    /// emitted, and solve for the screen direction with the same κ. Conventions follow Himwich et al. (2020).
    /// </summary>
    public static class WalkerPenrose
    {
        /// <summary>
        /// The direction synchrotron light is polarized in: f^μ = ε^μνρσ u_ν k_ρ b_σ, across the ray k and the field
        /// b as the gas (velocity u) sees them. Unnormalized; the sign is irrelevant, polarization being an axis.
        /// </summary>
        public static Vector4d Perpendicular(Vector4d u, Vector4d k, Vector4d b, Vector3d x, double a)
        {
            // √−g = 1 in Kerr-Schild coordinates, so ε is the permutation symbol on the lowered vectors.
            var p = KerrSchild.Lower(u, x, a);
            var q = KerrSchild.Lower(k, x, a);
            var s = KerrSchild.Lower(b, x, a);
            return new Vector4d(
                Minor(p.X, p.Y, p.Z, q.X, q.Y, q.Z, s.X, s.Y, s.Z),
                -Minor(p.T, p.Y, p.Z, q.T, q.Y, q.Z, s.T, s.Y, s.Z),
                Minor(p.T, p.X, p.Z, q.T, q.X, q.Z, s.T, s.X, s.Z),
                -Minor(p.T, p.X, p.Y, q.T, q.X, q.Y, s.T, s.X, s.Y));
        }

        /// <summary>
        /// κ1, κ2 for a ray with tangent k (contravariant) at x carrying polarization f: κ = (A − iB)(r − ia cos θ),
        /// with A and B the Boyer–Lindquist expressions of Himwich et al. (2020) eq. 6 rewritten for Kerr-Schild
        /// components. Linear in f and unchanged by f → f + c k.
        /// </summary>
        public static void Constant(Vector3d x, Vector4d k, Vector4d f, double a, out double k1, out double k2)
        {
            double r = KerrSchild.Radius(x, a), r2a2 = r * r + a * a;
            double rho2 = x.X * x.X + x.Y * x.Y;
            // On the spin axis φ is undefined and the φ terms are 0/0. κ is smooth there, so step off the axis along φ = 0.
            if (rho2 < 1e-24 * r2a2)
            {
                x = new Vector3d(1e-12 * r, 1e-12 * a, x.Z);
                rho2 = 1e-24 * r2a2;
            }
            double z = x.Z, den = r * r * r * r + a * a * z * z;
            // Gradients of the Kerr-Schild spherical coordinates: ∇r, sin θ ∇θ, ∇φ̃ (r and θ shared with Boyer–Lindquist).
            var gradR = new Vector3d(x.X * r * r * r, x.Y * r * r * r, z * r * r2a2) / den;
            var sinGradTheta = new Vector3d(z * x.X, z * x.Y, -r * r * rho2 / r2a2) * (r / den);
            var gradPhi = new Vector3d(-x.Y / rho2, x.X / rho2, 0) + gradR * (a / r2a2);

            var kv = k.Spatial;
            var fv = f.Spatial;
            double kr = Vector3d.Dot(gradR, kv), ks = Vector3d.Dot(sinGradTheta, kv), kp = Vector3d.Dot(gradPhi, kv);
            double fr = Vector3d.Dot(gradR, fv), fs = Vector3d.Dot(sinGradTheta, fv), fp = Vector3d.Dot(gradPhi, fv);
            double cos = z / r, sin2 = rho2 / r2a2;

            double aTerm = (k.T * fr - kr * f.T) + a * sin2 * (kr * fp - kp * fr);
            // The last term is what Boyer–Lindquist's t and φ turn into here: t̃ and φ̃ shift by functions of r.
            double bTerm = r2a2 * (kp * fs - ks * fp) - a * (k.T * fs - ks * f.T) - a * (kr * fs - ks * fr);
            k1 = aTerm * r - bTerm * a * cos;
            k2 = -(aTerm * a * cos + bTerm * r);
        }

        static double Minor(double a0, double a1, double a2, double b0, double b1, double b2, double c0, double c1, double c2) =>
            a0 * (b1 * c2 - b2 * c1) - a1 * (b0 * c2 - b2 * c0) + a2 * (b0 * c1 - b1 * c0);

        /// <summary>
        /// One pixel's polarization screen: maps a ray's κ to the angle the camera sees. EVPA χ is measured from the
        /// camera's up towards its left (north through east, as on the sky).
        /// </summary>
        public readonly struct Screen
        {
            readonly double _m00, _m01, _m10, _m11; // inverse of κ's linear map from screen components to (κ1, κ2)

            /// <param name="direction">Unit direction of the pixel in the camera's (right, up, forward) axes.</param>
            public Screen(in CameraFrame frame, Vector3d direction)
            {
                var d = direction;
                var e1 = (new Vector3d(1, 0, 0) - d * d.X).Normalized;
                var e2 = new Vector3d(0, 1, 0) - d * d.Y;
                e2 = (e2 - e1 * Vector3d.Dot(e2, e1)).Normalized;

                var n = d.X * frame.Right + d.Y * frame.Up + d.Z * frame.Forward;
                // Same orientation as the traced ray's tangent (see RayTracer.PixelRay), so κ compares sign for sign.
                var k = n - frame.U;
                Constant(frame.Position, k, Local(frame, e1), frame.Spin, out double a1, out double a2);
                Constant(frame.Position, k, Local(frame, e2), frame.Spin, out double b1, out double b2);
                double det = a1 * b2 - b1 * a2;
                _m00 = b2 / det;
                _m01 = -b1 / det;
                _m10 = -a2 / det;
                _m11 = a1 / det;
            }

            /// <summary>cos 2χ and sin 2χ of the light whose ray has this κ; both 0 when κ is 0 (no polarization direction).</summary>
            public void Polarization(double k1, double k2, out double cos2Chi, out double sin2Chi)
            {
                double right = _m00 * k1 + _m01 * k2, up = _m10 * k1 + _m11 * k2;
                double norm = right * right + up * up;
                if (!(norm > 0))
                {
                    cos2Chi = sin2Chi = 0;
                    return;
                }
                cos2Chi = (up * up - right * right) / norm;
                sin2Chi = -2 * right * up / norm;
            }

            static Vector4d Local(in CameraFrame frame, Vector3d v) => v.X * frame.Right + v.Y * frame.Up + v.Z * frame.Forward;
        }
    }
}
