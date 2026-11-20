using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static SilkySeal.Kerr.Tests.GpuParityTests;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// The GPU disk (float tracer, baked temperature and colour tables) must shade like the double CPU reference,
    /// <see cref="ThinDiskRay"/> with exact Page–Thorne temperatures and the full CIE integral.
    /// </summary>
    public class GpuDiskParityTests
    {
        const double PeakKelvin = 6000, OuterRadius = 30;
        // Error is measured against the pixel's brightest channel; pixels darker than this are not "lit".
        const double ColourTolerance = 0.02, LitFloor = 1e-3, RequiredMatchFraction = 0.99;

        [TestCase(0.0, 1.0)]
        [TestCase(0.9, 0.5)]
        public void Disk_MatchesCpuShading(double a, double opacity)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            var disk = ThinDisk.FromPeakTemperature(a, OuterRadius, PeakKelvin);
            var frame = TestFrame(a);
            // Exposure puts the rest-frame peak-temperature blackbody at luminance 1.
            double exposure = 1 / Blackbody.Xyz(PeakKelvin).Y;
            var gpu = RunGpu(shader, frame, disk, opacity, (float)exposure);
            var cpu = RunCpu(frame, disk, opacity, exposure);

            int lit = 0, matched = 0;
            double worst = 0;
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
            }

            double fraction = (double)matched / lit;
            Debug.Log($"[GpuDiskParity a={a} opacity={opacity}] within {ColourTolerance:P0}: {fraction:P3} of {lit} lit, worst {worst:P1}");
            Assert.Greater(lit, cpu.Length / 5, "the disk should fill a good part of the frame");
            Assert.GreaterOrEqual(fraction, RequiredMatchFraction);
        }

        static Vector3d[] RunCpu(CameraFrame frame, ThinDisk disk, double opacity, double exposure)
        {
            var results = new Vector3d[GridWidth * GridHeight];
            var settings = TraceSettings.Reference(frame.Spin);
            double tanHalf = Math.Tan(VerticalFov / 2);
            Parallel.For(0, results.Length, i =>
            {
                var ray = new ThinDiskRay(disk, opacity, true);
                var k = RayTracer.PixelRay(frame, Ndc(i % GridWidth, GridWidth), Ndc(i / GridWidth, GridHeight), tanHalf, Aspect);
                RayTracer.Trace(frame.Position, k, settings, ref ray);
                results[i] = ray.Radiance * exposure;
            });
            return results;
        }

        static Vector4[] RunGpu(ComputeShader shader, CameraFrame frame, ThinDisk disk, double opacity, float exposure)
        {
            int kernel = shader.FindKernel("CSRender");
            var output = new RenderTexture(GridWidth, GridHeight, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
            output.Create();
            var sky = new Cubemap(1, TextureFormat.RGBA32, false);
            var blackbody = DiskTables.BakeBlackbody();
            var flux = DiskTables.BakeFlux(disk, out var range);
            try
            {
                SetCamera(shader, frame);
                shader.SetInt("_DiskEnabled", 1);
                shader.SetInt("_DiskShifts", 1);
                shader.SetFloat("_DiskOpacity", (float)opacity);
                shader.SetVector("_DiskRange", range);
                shader.SetVector("_BlackbodyRange", DiskTables.BlackbodyRange);
                shader.SetTexture(kernel, "_DiskFlux", flux);
                shader.SetTexture(kernel, "_Blackbody", blackbody);
                shader.SetFloat("_SkyLuminance", 0);
                shader.SetFloat("_Exposure", exposure);
                shader.SetFloat("_UseSky", 0);
                shader.SetInt("_Blackout", 0);
                shader.SetMatrix("_HoleToWorld", Matrix4x4.identity);
                shader.SetTexture(kernel, "_Sky", sky);
                shader.SetTexture(kernel, "_Output", output);
                Dispatch(shader, kernel);

                var request = AsyncGPUReadback.Request(output);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError, "GPU readback failed");
                return request.GetData<Vector4>().ToArray();
            }
            finally
            {
                // Compute parameters persist on the asset; leave the disk off for the other kernels' tests.
                shader.SetInt("_DiskEnabled", 0);
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(sky);
                UnityEngine.Object.DestroyImmediate(blackbody);
                UnityEngine.Object.DestroyImmediate(flux);
            }
        }

        static double MaxChannel(Vector3d v) => Math.Max(v.X, Math.Max(v.Y, v.Z));
        static Vector3d Abs(Vector3d v) => new Vector3d(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));
    }
}
