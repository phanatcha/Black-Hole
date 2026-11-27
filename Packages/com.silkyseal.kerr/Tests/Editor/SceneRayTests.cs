using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class SceneRayTests
    {
        static readonly SceneLight NoonLight = new SceneLight
        {
            SunDirection = new Vector3d(0, 0, 1), Sun = new Vector3d(1, 1, 1), Ambient = new Vector3d(0.1, 0.1, 0.1), Luminance = 2,
        };

        // Lambert: albedo × (sun × cos + ambient), times the scene's luminance scale.
        [Test]
        public void Shade_IsAlbedoTimesSunAndAmbient()
        {
            var surfaces = Quad(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0.5, 0.25, 1));
            var tilted = NoonLight;
            tilted.SunDirection = new Vector3d(0, Math.Sin(Math.PI / 3), Math.Cos(Math.PI / 3));
            var hit = new SceneHit { Triangle = 0, U = 0.2, V = 0.3 };

            var c = surfaces.Shade(hit, new Vector3d(0, 0, -1), tilted);

            Assert.AreEqual(0.5 * (0.5 + 0.1) * 2, c.X, 1e-12);
            Assert.AreEqual(0.25 * (0.5 + 0.1) * 2, c.Y, 1e-12);
            Assert.AreEqual(1 * (0.5 + 0.1) * 2, c.Z, 1e-12);
            Assert.AreEqual(0.1 * 2, surfaces.Shade(hit, new Vector3d(0, 0, 1), tilted).Z, 1e-12, "the back face sees no sun");
        }

        // A surface at rest at r = 4, seen from rest at r = 2000 (a = 0): light climbing out loses √(1 − 2/r) of its frequency.
        [Test]
        public void LightFromNearTheHole_IsRedshifted()
        {
            var surfaces = Quad(new Vector3d(-4, -0.5, -0.5), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1), new Vector3d(1, 1, 1));
            var x = new Vector3d(-2000, 0, 0);
            var f = ObserverFrame.Build(x, 0, new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), new Vector3d(0, 1, 0), new Vector3d(1, 0, 0), 0);
            var ray = new SceneRay(surfaces, NoonLight, 0);

            var result = RayTracer.Trace(x, RayTracer.PixelRay(f, 0, 0, 0.1, 1), TraceSettings.Reference(0), ref ray);

            Assert.AreEqual(RayStatus.Absorbed, result.Status);
            Assert.IsTrue(ray.Hit);
            Assert.AreEqual(-4, ray.Position.X, 1e-6);
            Assert.AreEqual(Math.Sqrt(1 - 2.0 / 4) / Math.Sqrt(1 - 2.0 / 2000), ray.Shift, 1e-9);
        }

        // A small square 2000 M behind the hole, hidden by its shadow, shows as a ring at the Einstein angle
        // θ_E = √(4M D_ls / (D_l D_s)) seen from 2000 M in front.
        [Test]
        public void ObjectBehindTheHole_FormsAnEinsteinRing()
        {
            const double distance = 2000, half = 5, tanHalfFov = 0.05;
            var surfaces = Quad(new Vector3d(distance, -half, -half), new Vector3d(0, 2 * half, 0), new Vector3d(0, 0, 2 * half),
                                new Vector3d(1, 1, 1));
            var x = new Vector3d(-distance, 0, 0);
            var f = ObserverFrame.Build(x, 0, new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), new Vector3d(0, 1, 0), new Vector3d(1, 0, 0), 0);
            var settings = TraceSettings.Reference(0);

            const int samples = 300;
            var hits = new bool[samples];
            Parallel.For(0, samples, i =>
            {
                var ray = new SceneRay(surfaces, NoonLight, 0);
                RayTracer.Trace(x, RayTracer.PixelRay(f, 0.4 + 0.5 * i / samples, 0, tanHalfFov, 1), settings, ref ray);
                hits[i] = ray.Hit;
            });
            var centre = new SceneRay(surfaces, NoonLight, 0);
            var straightOn = RayTracer.Trace(x, RayTracer.PixelRay(f, 0, 0, tanHalfFov, 1), settings, ref centre);

            int first = Array.IndexOf(hits, true), last = Array.LastIndexOf(hits, true);
            Assert.GreaterOrEqual(first, 0, "the ring shows");
            for (int i = first; i <= last; i++) Assert.IsTrue(hits[i], "one unbroken band");
            double inner = Math.Atan((0.4 + 0.5 * first / samples) * tanHalfFov), outer = Math.Atan((0.4 + 0.5 * last / samples) * tanHalfFov);
            double einstein = Math.Sqrt(4 * distance / (distance * 2 * distance));
            Assert.AreEqual(einstein, 0.5 * (inner + outer), 0.05 * einstein);
            Assert.AreEqual(RayStatus.Captured, straightOn.Status, "straight behind is the shadow");
        }

        // Two triangles: the square at corner spanned by edges u and v, every vertex with one albedo, normals u × v.
        internal static SceneSurfaces Quad(Vector3d corner, Vector3d u, Vector3d v, Vector3d albedo)
        {
            var vertices = new[] { corner, corner + u, corner + u + v, corner + v };
            var n = Vector3d.Cross(u, v).Normalized;
            return new SceneSurfaces(vertices, new[] { 0, 1, 2, 0, 2, 3 }, new[] { n, n, n, n }, new[] { albedo, albedo, albedo, albedo });
        }
    }
}
