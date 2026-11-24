using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// How the sky direction seen through a pixel changes per pixel step, from the neighbours' directions: the footprint
    /// stars and sky are filtered over. CPU reference of KerrFootprint in KerrTrace.compute.
    /// </summary>
    public static class Footprint
    {
        /// <param name="centre">Unit sky direction at the pixel.</param>
        /// <param name="left">Neighbour at x − 1; zero where its ray did not escape or it lies off screen. So the others.</param>
        /// <param name="pixelAngle">The camera's pixel angle, the fallback when neither neighbour on an axis escaped.</param>
        /// <param name="jx">Change of direction per step in x, tangent to <paramref name="centre"/>.</param>
        /// <param name="jy">The same per step in y.</param>
        public static void Estimate(Vector3d centre, Vector3d left, Vector3d right, Vector3d down, Vector3d up,
                                    double pixelAngle, out Vector3d jx, out Vector3d jy)
        {
            bool hasX = Step(centre, left, right, out jx), hasY = Step(centre, down, up, out jy);
            if (!hasX) jx = (hasY ? Vector3d.Cross(jy, centre) : AnyTangent(centre)).Normalized * pixelAngle;
            if (!hasY) jy = Vector3d.Cross(centre, jx).Normalized * pixelAngle;
        }

        // The smaller one-sided difference, so a neighbour across a lensing fold or the shadow edge cannot blow the
        // footprint up; projected onto the plane tangent at the centre.
        static bool Step(Vector3d centre, Vector3d back, Vector3d forward, out Vector3d step)
        {
            bool hasBack = back.Length > 0, hasForward = forward.Length > 0;
            Vector3d b = centre - back, f = forward - centre;
            step = hasBack && hasForward ? (Vector3d.Dot(f, f) <= Vector3d.Dot(b, b) ? f : b) : hasForward ? f : b;
            step -= centre * Vector3d.Dot(centre, step);
            return hasBack || hasForward;
        }

        static Vector3d AnyTangent(Vector3d d) =>
            Vector3d.Cross(d, Math.Abs(d.Y) < 0.9 ? new Vector3d(0, 1, 0) : new Vector3d(1, 0, 0));
    }
}
