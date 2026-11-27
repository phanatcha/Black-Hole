using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// The float32 GPU tracer must land within one 1080p pixel of the double CPU reference.
    /// Metal has no fp64, so this test is the only thing standing between float error and the image.
    /// </summary>
    public class GpuParityTests
    {
        internal const string ShaderPath = "Packages/com.silkyseal.kerr/Runtime/Rendering/Shaders/KerrTrace.compute";
        internal const int GridWidth = 161, GridHeight = 91;
        const int ThreadGroupSize = 8;
        internal const double VerticalFov = Math.PI / 3, Aspect = 16.0 / 9.0;
        const double ReferenceLines = 1080;
        const double CameraRadius = 15, CameraPolarDegrees = 80;
        const double RequiredWithinPixelFraction = 0.99;
        const double AllowedMaxStepsFraction = 0.005;

        // GPU defaults from the plan; changed only through this test.
        const float GpuTolerance = 1e-5f, GpuFarRadius = 5000f;
        const int GpuMaxSteps = 4096;

        enum Status { Escaped, Captured, MaxSteps }

        [TestCase(0.6)]
        [TestCase(0.999)]
        public void GpuParity_EscapeDirectionsWithinOnePixel(double a) => AssertParity(TestFrame(a), $"a={a}");

        // Falling through r₊ at a = 0.6, 0.1 M inside, looking along the orbit: sky above, black from 15° below. Along that
        // edge E − Ω_H L → 0, the light lingered at r₊ and its sky winds round fast, as at the photon ring outside: float
        // error there is amplified, all within 4 grid pixels of the black. So the 1-pixel bar applies beyond them.
        [Test]
        public void GpuParity_InsideTheHorizon()
        {
            const double a = 0.6;
            var x0 = new Vector3d(8, 0, 1.5);
            var fall = new FreeFall(x0, ObserverFrame.Zamo(x0, a), a);
            while (fall.Radius > KerrSchild.HorizonRadius(a) - 0.1) fall.Advance(0.01);
            var up = fall.Position.Normalized;
            var side = new Vector3d(-up.Y, up.X, 0).Normalized;
            AssertParity(ObserverFrame.FromVelocity(fall.Position, a, fall.Velocity, side, up, Vector3d.Cross(up, side)), "inside", 4);
        }

        // edgeBand: escaped rays this many grid pixels or fewer from a captured one are left out of the 1-pixel bar.
        static void AssertParity(CameraFrame frame, string label, int edgeBand = 0)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            Assert.IsNotNull(shader, $"Compute shader missing at {ShaderPath}");

            var gpu = RunGpu(shader, frame);
            var cpu = RunCpu(frame);

            double pixelAngle = VerticalFov / ReferenceLines;
            int bothEscaped = 0, withinPixel = 0, edgeMismatch = 0, interiorMismatch = 0, gpuMaxSteps = 0, banded = 0;
            double maxError = 0;
            for (int y = 0; y < GridHeight; y++)
            for (int x = 0; x < GridWidth; x++)
            {
                int i = y * GridWidth + x;
                var g = gpu[i];
                var gStatus = (Status)Mathf.RoundToInt(g.w);
                if (gStatus == Status.MaxSteps) gpuMaxSteps++;
                var cStatus = (Status)cpu[i].Status;

                if (gStatus != cStatus)
                {
                    if (IsEdge(cpu, x, y)) edgeMismatch++; else interiorMismatch++;
                    continue;
                }
                if (cStatus != Status.Escaped) continue;
                if (NearCapture(cpu, x, y, edgeBand)) { banded++; continue; }

                bothEscaped++;
                var d = cpu[i].SkyDirection;
                double chord = Math.Sqrt(Sq(g.x - d.X) + Sq(g.y - d.Y) + Sq(g.z - d.Z));
                double angle = 2 * Math.Asin(Math.Min(1, chord / 2));
                maxError = Math.Max(maxError, angle);
                if (angle <= pixelAngle) withinPixel++;
            }

            double fraction = (double)withinPixel / bothEscaped;
            Debug.Log($"[GpuParity {label}] within 1px {fraction:P3} of {bothEscaped}, max error {maxError / pixelAngle:F2} px, " +
                      $"edge mismatches {edgeMismatch}, interior mismatches {interiorMismatch}, GPU MaxSteps {gpuMaxSteps}, left out {banded}");

            Assert.Greater(bothEscaped, GridWidth * GridHeight / 2, "most rays should escape");
            Assert.GreaterOrEqual(fraction, RequiredWithinPixelFraction);
            Assert.AreEqual(0, interiorMismatch, "capture/escape disagreement away from the shadow edge");
            Assert.LessOrEqual(gpuMaxSteps, AllowedMaxStepsFraction * GridWidth * GridHeight);
        }

        static double Sq(double v) => v * v;

        // Camera 80° from the spin axis at radius 15, looking at the hole, at rest in the ZAMO frame.
        internal static CameraFrame TestFrame(double a)
        {
            double theta = CameraPolarDegrees * Math.PI / 180;
            var pos = new Vector3d(CameraRadius * Math.Sin(theta), 0, CameraRadius * Math.Cos(theta));
            return ObserverFrame.Build(pos, a, (-pos).Normalized, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0),
                                       default, 0);
        }

        // Pixel centres, same convention as the kernel: ndc = (id + 0.5) / size * 2 − 1.
        internal static double Ndc(int i, int size) => (i + 0.5) / size * 2 - 1;

        static RayResult[] RunCpu(CameraFrame frame)
        {
            var settings = TraceSettings.Reference(frame.Spin);
            double tanHalf = Math.Tan(VerticalFov / 2);
            var results = new RayResult[GridWidth * GridHeight];
            // Each ray is independent and Core is pure static code, so the reference runs on all cores.
            Parallel.For(0, results.Length, i =>
            {
                results[i] = RayTracer.TracePixel(frame, Ndc(i % GridWidth, GridWidth), Ndc(i / GridWidth, GridHeight),
                                                  tanHalf, Aspect, settings);
            });
            return results;
        }

        static Vector4[] RunGpu(ComputeShader shader, CameraFrame f)
        {
            int kernel = shader.FindKernel("CSTraceDirections");
            var buffer = new ComputeBuffer(GridWidth * GridHeight, sizeof(float) * 4);
            using var scene = SceneBuffers.Empty(); // declared by TraceRay; Metal wants it bound
            try
            {
                SetCamera(shader, f);
                // The disk is off, but Metal still wants every texture the kernel declares bound.
                shader.SetInt("_DiskEnabled", 0);
                shader.SetTexture(kernel, "_DiskFlux", Texture2D.blackTexture);
                shader.SetTexture(kernel, "_Blackbody", Texture2D.blackTexture);
                shader.SetTexture(kernel, "_GasPowerLaw", Texture2D.blackTexture); // declared by TraceRay; Metal wants it bound
                shader.SetBuffer(kernel, "_Directions", buffer);
                scene.Bind(shader, kernel);
                Dispatch(shader, kernel);
                var data = new Vector4[GridWidth * GridHeight];
                buffer.GetData(data);
                return data;
            }
            finally
            {
                buffer.Release();
            }
        }

        // Camera, grid and integrator settings shared by every kernel; compute parameters persist on the asset.
        internal static void SetCamera(ComputeShader shader, CameraFrame f)
        {
            shader.SetVector("_CamPos", ToFloat(f.Position));
            shader.SetVector("_CamU", ToFloat(f.U));
            shader.SetVector("_CamRight", ToFloat(f.Right));
            shader.SetVector("_CamUp", ToFloat(f.Up));
            shader.SetVector("_CamForward", ToFloat(f.Forward));
            shader.SetFloat("_Spin", (float)f.Spin);
            shader.SetFloat("_TanHalfFovY", (float)Math.Tan(VerticalFov / 2));
            shader.SetFloat("_Aspect", (float)Aspect);
            shader.SetFloat("_FarRadius", GpuFarRadius);
            shader.SetFloat("_Tolerance", GpuTolerance);
            shader.SetInt("_MaxSteps", GpuMaxSteps);
            shader.SetInts("_Size", GridWidth, GridHeight);
        }

        internal static void Dispatch(ComputeShader shader, int kernel) =>
            shader.Dispatch(kernel, (GridWidth + ThreadGroupSize - 1) / ThreadGroupSize,
                            (GridHeight + ThreadGroupSize - 1) / ThreadGroupSize, 1);

        // HLSL side stores 4-vectors as (x, y, z, t) so .xyz is the spatial part.
        static Vector4 ToFloat(Vector4d v) => new Vector4((float)v.X, (float)v.Y, (float)v.Z, (float)v.T);
        static Vector4 ToFloat(Vector3d v) => new Vector4((float)v.X, (float)v.Y, (float)v.Z, 0);

        static bool NearCapture(RayResult[] cpu, int x, int y, int band)
        {
            for (int v = Math.Max(0, y - band); v <= Math.Min(GridHeight - 1, y + band); v++)
            for (int u = Math.Max(0, x - band); u <= Math.Min(GridWidth - 1, x + band); u++)
                if (cpu[v * GridWidth + u].Status != RayStatus.Escaped) return true;
            return false;
        }

        // A ray is on the shadow edge when any 4-neighbour ended differently on the CPU.
        static bool IsEdge(RayResult[] cpu, int x, int y)
        {
            var s = cpu[y * GridWidth + x].Status;
            return (x > 0 && cpu[y * GridWidth + x - 1].Status != s)
                || (x < GridWidth - 1 && cpu[y * GridWidth + x + 1].Status != s)
                || (y > 0 && cpu[(y - 1) * GridWidth + x].Status != s)
                || (y < GridHeight - 1 && cpu[(y + 1) * GridWidth + x].Status != s);
        }
    }
}
