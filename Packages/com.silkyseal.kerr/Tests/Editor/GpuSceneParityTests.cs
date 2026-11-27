using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;
using static SilkySeal.Kerr.Tests.GpuParityTests;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// Meshes near the hole on the GPU (float tracer, float tree, chords at the GPU's step size) must look as they do to
    /// the double CPU reference, <see cref="SceneRay"/>: same surface hit, same shading, same frequency shift.
    /// </summary>
    public class GpuSceneParityTests
    {
        // Error is measured against the pixel's brightest channel; pixels darker than this are not "lit".
        const double ColourTolerance = 0.02, LitFloor = 1e-3, RequiredMatchFraction = 0.99;

        static readonly SceneLight Light = new SceneLight
        {
            SunDirection = new Vector3d(0.3, -0.5, 0.8).Normalized, Sun = new Vector3d(1, 0.95, 0.9),
            Ambient = new Vector3d(0.05, 0.06, 0.08), Luminance = 1,
        };

        [TestCase(0.0)]
        [TestCase(0.9)]
        public void Scene_MatchesCpuShading(double a)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            var surfaces = TestScene();
            var frame = TestFrame(a);
            var gpu = RunGpu(shader, frame, surfaces);
            var cpu = RunCpu(frame, surfaces);

            int lit = 0, matched = 0, missed = 0;
            double worst = 0;
            var diag = new List<string>();
            for (int i = 0; i < cpu.Length; i++)
            {
                var c = cpu[i];
                var g = new Vector3d(gpu[i].x, gpu[i].y, gpu[i].z);
                double brightest = Math.Max(MaxChannel(c), MaxChannel(g));
                if (brightest < LitFloor) continue;

                lit++;
                double error = MaxChannel(Abs(g - c)) / brightest;
                worst = Math.Max(worst, error);
                if (error <= ColourTolerance) matched++;
                else if (Math.Min(MaxChannel(c), MaxChannel(g)) < 0.1 * brightest) missed++;
                else if (diag.Count < 4) diag.Add($"({i % GridWidth},{i / GridWidth}) cpu {c.X:G3},{c.Y:G3},{c.Z:G3} gpu {g.X:G3},{g.Y:G3},{g.Z:G3}");
            }

            double fraction = (double)matched / lit;
            string report = $"within {ColourTolerance:P0}: {fraction:P3} of {lit} lit; {missed} one-sided, worst {worst:P1}; " + string.Join("; ", diag);
            Debug.Log($"[GpuSceneParity a={a}] {report}");
            Assert.Greater(lit, cpu.Length / 5, "the scene should fill a good part of the frame");
            Assert.GreaterOrEqual(fraction, RequiredMatchFraction, report);
        }

        // A wall 20 M behind the hole, lensed into rings, and a box beside it with a colour per corner.
        internal static SceneSurfaces TestScene()
        {
            var vertices = new List<Vector3d>();
            var normals = new List<Vector3d>();
            var albedo = new List<Vector3d>();
            var indices = new List<int>();
            void Quad(Vector3d corner, Vector3d u, Vector3d v, Vector3d c0, Vector3d c1)
            {
                int first = vertices.Count;
                var n = Vector3d.Cross(u, v).Normalized;
                foreach (var (p, c) in new[] { (corner, c0), (corner + u, c1), (corner + u + v, c0), (corner + v, c1) })
                {
                    vertices.Add(p);
                    normals.Add(n);
                    albedo.Add(c);
                }
                indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            Quad(new Vector3d(-20, -25, -25), new Vector3d(0, 50, 0), new Vector3d(0, 0, 50), new Vector3d(0.8, 0.8, 0.8), new Vector3d(0.2, 0.4, 0.9));
            var o = new Vector3d(-1, 5, -1);
            var red = new Vector3d(0.9, 0.2, 0.1);
            var green = new Vector3d(0.2, 0.8, 0.3);
            Vector3d ex = new Vector3d(2, 0, 0), ey = new Vector3d(0, 2, 0), ez = new Vector3d(0, 0, 2);
            Quad(o, ey, ex, red, green);
            Quad(o + ez, ex, ey, red, green);
            Quad(o, ex, ez, red, green);
            Quad(o + ey, ez, ex, red, green);
            Quad(o, ez, ey, red, green);
            Quad(o + ex, ey, ez, red, green);
            return new SceneSurfaces(vertices.ToArray(), indices.ToArray(), normals.ToArray(), albedo.ToArray());
        }

        static Vector3d[] RunCpu(CameraFrame frame, SceneSurfaces surfaces)
        {
            var results = new Vector3d[GridWidth * GridHeight];
            var settings = TraceSettings.Reference(frame.Spin);
            double tanHalf = Math.Tan(VerticalFov / 2);
            Parallel.For(0, results.Length, i =>
            {
                var ray = new SceneRay(surfaces, Light, frame.Spin);
                var k = RayTracer.PixelRay(frame, Ndc(i % GridWidth, GridWidth), Ndc(i / GridWidth, GridHeight), tanHalf, Aspect);
                RayTracer.Trace(frame.Position, k, settings, ref ray);
                results[i] = ray.Radiance;
            });
            return results;
        }

        // CSTrace with the scene bound, then CSShade with no sky and no stars at exposure 1.
        static Vector4[] RunGpu(ComputeShader shader, CameraFrame frame, SceneSurfaces surfaces)
        {
            int kernel = shader.FindKernel("CSTrace");
            var trace = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * GridWidth * GridHeight, 4 * sizeof(float));
            var scene = SceneBuffers.Create(surfaces);
            try
            {
                SetCamera(shader, frame);
                shader.SetInt("_DiskEnabled", 0);
                shader.SetInt("_GasView", 0);
                shader.SetTexture(kernel, "_DiskFlux", Texture2D.blackTexture);
                shader.SetTexture(kernel, "_Blackbody", Texture2D.blackTexture);
                shader.SetTexture(kernel, "_GasPowerLaw", Texture2D.blackTexture);
                shader.SetInt("_Blackout", 0);
                shader.SetMatrix("_HoleToWorld", Matrix4x4.identity);
                shader.SetBuffer(kernel, "_Trace", trace);
                scene.Bind(shader, kernel);
                SceneBuffers.SetLight(shader, Light);
                shader.SetInt("_SceneEnabled", 1);
                Dispatch(shader, kernel);

                var traced = new Vector4[2 * GridWidth * GridHeight];
                trace.GetData(traced);
                return GpuSkyParityTests.Shade(shader, traced, GridWidth, GridHeight, VerticalFov / GridHeight, null, null, 0, 1);
            }
            finally
            {
                // Compute parameters persist on the asset; leave the scene off for the other kernels' tests.
                shader.SetInt("_SceneEnabled", 0);
                trace.Release();
                scene.Dispose();
            }
        }

        static double MaxChannel(Vector3d v) => Math.Max(v.X, Math.Max(v.Y, v.Z));
        static Vector3d Abs(Vector3d v) => new Vector3d(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));
    }
}
