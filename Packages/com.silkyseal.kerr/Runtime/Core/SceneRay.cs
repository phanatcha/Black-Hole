using System;

namespace SilkySeal.Kerr
{
    /// <summary>
    /// How the scene near the hole is lit, in Kerr-Schild axes. Sun and Ambient are the scene's own linear RGB light (the
    /// renderer's diffuse terms: albedo × light × cos for the sun, albedo × light for the sky); Luminance turns that into
    /// cd/m², the units the disk and stars are in.
    /// </summary>
    public struct SceneLight
    {
        /// <summary>Unit vector towards the sun.</summary>
        public Vector3d SunDirection;
        public Vector3d Sun, Ambient;
        public double Luminance;
    }

    /// <summary>Triangles near the hole with a normal and an albedo per vertex, in Kerr-Schild coordinates.</summary>
    public sealed class SceneSurfaces
    {
        public readonly SceneBvh Bvh;
        public readonly Vector3d[] Normals, Albedo;

        public SceneSurfaces(Vector3d[] vertices, int[] indices, Vector3d[] normals, Vector3d[] albedo)
        {
            if (normals.Length != vertices.Length || albedo.Length != vertices.Length)
                throw new ArgumentException("One normal and one albedo per vertex.");
            Bvh = new SceneBvh(vertices, indices);
            Normals = normals;
            Albedo = albedo;
        }

        /// <summary>
        /// Linear RGB in cd/m² that the surface at hit sends back up a ray that reached it travelling along direction:
        /// diffuse only, both faces, no shadows. The sun's light is taken as arriving straight, unbent by the hole.
        /// </summary>
        public Vector3d Shade(in SceneHit hit, Vector3d direction, in SceneLight light)
        {
            int i0 = Bvh.Indices[3 * hit.Triangle], i1 = Bvh.Indices[3 * hit.Triangle + 1], i2 = Bvh.Indices[3 * hit.Triangle + 2];
            double w0 = 1 - hit.U - hit.V;
            var n = (w0 * Normals[i0] + hit.U * Normals[i1] + hit.V * Normals[i2]).Normalized;
            if (Vector3d.Dot(n, direction) > 0) n = -n;
            var albedo = w0 * Albedo[i0] + hit.U * Albedo[i1] + hit.V * Albedo[i2];
            var incoming = light.Sun * Math.Max(0, Vector3d.Dot(n, light.SunDirection)) + light.Ambient;
            return new Vector3d(albedo.X * incoming.X, albedo.Y * incoming.Y, albedo.Z * incoming.Z) * light.Luminance;
        }
    }

    /// <summary>
    /// Stops a ray at the first surface it meets, testing each integrator step as a straight chord, and shades it.
    /// The surfaces are at rest in the hole's static frame: their light reaches the camera shifted by
    /// g = √(−g_tt) / k_t, which no static surface has inside the ergosphere (it shows black there).
    /// Within a chord of the surfaces' reach it keeps each chord under <see cref="MaxChordPerRadius"/> × r: a straight
    /// chord sags off the bending ray, and the step size alone would make that sag the tracer's, not the scene's.
    /// </summary>
    public struct SceneRay : IRayObserver
    {
        /// <summary>Longest chord near the surfaces, per unit distance from the hole's centre. Scene.hlsl keeps the same.</summary>
        public const double MaxChordPerRadius = 0.02;

        readonly SceneSurfaces _surfaces;
        readonly SceneLight _light;
        readonly double _spin, _reach;
        Vector3d _previous;

        public bool Hit;
        public SceneHit Surface;
        public Vector3d Position;
        /// <summary>g = ν_observed / ν_emitted for the light from the hit.</summary>
        public double Shift;
        /// <summary>Linear sRGB in cd/m² as the camera sees it, shift applied.</summary>
        public Vector3d Radiance;

        public SceneRay(SceneSurfaces surfaces, SceneLight light, double spin)
        {
            _surfaces = surfaces;
            _light = light;
            _spin = spin;
            _reach = Reach(surfaces.Bvh);
            _previous = default;
            Hit = false;
            Surface = default;
            Position = default;
            Shift = 0;
            Radiance = default;
        }

        public bool OnEquatorCrossing(Vector3d x, double time, double kt, double kPhi) => true;

        public bool OnStep(Vector3d x, double time, Vector4d p, double h, ref double nextStep)
        {
            var from = _previous;
            _previous = x;
            if (h == 0) return true;
            if (!_surfaces.Bvh.Intersect(from, x, out var hit))
            {
                double chord = (x - from).Length, radius = x.Length;
                if (chord > 0 && radius - chord < _reach) nextStep = Math.Min(nextStep, h * MaxChordPerRadius * radius / chord);
                return true;
            }

            Hit = true;
            Surface = hit;
            Position = from + (x - from) * hit.T;
            double lapse = -KerrSchild.Dot(new Vector4d(1, 0, 0, 0), new Vector4d(1, 0, 0, 0), Position, _spin);
            Shift = lapse > 0 && p.T > 0 ? Math.Sqrt(lapse) / p.T : 0;
            Radiance = RgbSpectrum.Shift(_surfaces.Shade(hit, x - from, _light), Shift);
            return false;
        }

        /// <summary>Farthest any surface can lie from the hole's centre: the root box's farthest corner.</summary>
        internal static double Reach(SceneBvh bvh)
        {
            var root = bvh.Nodes[0];
            return new Vector3d(Math.Max(Math.Abs(root.Min.X), Math.Abs(root.Max.X)), Math.Max(Math.Abs(root.Min.Y), Math.Abs(root.Max.Y)),
                                Math.Max(Math.Abs(root.Min.Z), Math.Abs(root.Max.Z))).Length;
        }
    }
}
