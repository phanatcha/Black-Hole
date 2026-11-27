using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;
using static SilkySeal.Kerr.Tests.GpuParityTests;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// The GPU hot gas (float, frequencies in GHz, a baked power-law table) must shade like the double CPU reference,
    /// <see cref="HotFlowRay"/>. The test camera sits inside the flow, so rays start in the gas and graze the hole.
    /// </summary>
    public class GpuGasParityTests
    {
        // Error is measured against the pixel's own intensity (brightest channel in the eye view); pixels below LitFloor
        // of the frame's brightest are not "lit".
        const double Tolerance = 0.02, LitFloor = 1e-3, RequiredMatchFraction = 0.99;
        // The camera's coordinate time, M; late enough that the inner rings have turned many times.
        const double SwirlTime = 5000;

        [TestCase(0.0, HotGas.View.Radio, 0.0)]
        [TestCase(0.9, HotGas.View.Radio, 0.0)]
        [TestCase(0.9, HotGas.View.Polarization, 0.0)]
        [TestCase(0.9, HotGas.View.Eye, 0.0)]
        [TestCase(0.9, HotGas.View.Eye, 1.0)]
        public void Gas_MatchesCpuTransfer(double a, HotGas.View view, double swirl)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            var flow = HotFlow.SgrA(a, new Vector3d(0.5, 1, 0.3));
            var frame = TestFrame(a);
            var pattern = new SwirlPattern(a, swirl, SwirlTime);
            var gpu = RunGpu(shader, frame, flow, view, pattern);
            var cpu = RunCpu(frame, flow, pattern);

            var expected = new Vector3d[cpu.Length];
            var actual = new Vector3d[cpu.Length];
            var scale = new double[cpu.Length];
            double brightest = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                var g = gpu[2 * i + 1];
                actual[i] = new Vector3d(g.x, g.y, g.z);
                expected[i] = view == HotGas.View.Eye ? VisibleBand.LinearSrgb(cpu[i].Visible)
                            : view == HotGas.View.Radio ? new Vector3d(cpu[i].Radio, 0, 0)
                            : new Vector3d(cpu[i].Radio, cpu[i].StokesQ, cpu[i].StokesU);
                if (view == HotGas.View.Radio) actual[i] = new Vector3d(g.x, 0, 0);
                scale[i] = view == HotGas.View.Eye ? MaxChannel(expected[i]) : cpu[i].Radio;
                brightest = Math.Max(brightest, scale[i]);
            }

            int lit = 0, matched = 0;
            double worst = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                if (scale[i] < LitFloor * brightest) continue;
                lit++;
                double error = MaxChannel(Abs(actual[i] - expected[i])) / scale[i];
                worst = Math.Max(worst, error);
                if (error <= Tolerance) matched++;
            }

            double fraction = (double)matched / lit;
            Debug.Log($"[GpuGasParity a={a} {view} swirl={swirl}] within {Tolerance:P0}: {fraction:P3} of {lit} lit, worst {worst:P1}");
            Assert.Greater(lit, cpu.Length / 6, "the gas should fill a good part of the frame");
            Assert.GreaterOrEqual(fraction, RequiredMatchFraction);
        }

        static HotFlowRay[] RunCpu(CameraFrame frame, HotFlow flow, SwirlPattern pattern)
        {
            var results = new HotFlowRay[GridWidth * GridHeight];
            var settings = TraceSettings.Reference(frame.Spin);
            double tanHalf = Math.Tan(VerticalFov / 2);
            Parallel.For(0, results.Length, i =>
            {
                double x = Ndc(i % GridWidth, GridWidth), y = Ndc(i / GridWidth, GridHeight);
                var direction = new Vector3d(x * tanHalf * Aspect, y * tanHalf, 1).Normalized;
                var ray = new HotFlowRay(flow, new WalkerPenrose.Screen(frame, direction), pattern);
                RayTracer.Trace(frame.Position, RayTracer.PixelRay(frame, x, y, tanHalf, Aspect), settings, ref ray);
                results[i] = ray;
            });
            return results;
        }

        // CSTrace alone: the trace buffer's second float4 holds the gas light (rgb in cd/m², or I, Q, U in cgs).
        static Vector4[] RunGpu(ComputeShader shader, CameraFrame frame, HotFlow flow, HotGas.View view, SwirlPattern pattern)
        {
            int kernel = shader.FindKernel("CSTrace");
            var trace = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * GridWidth * GridHeight, 4 * sizeof(float));
            var table = GasTables.BakePowerLaw();
            try
            {
                SetCamera(shader, frame);
                shader.SetInt("_DiskEnabled", 0);
                shader.SetTexture(kernel, "_DiskFlux", Texture2D.blackTexture);
                shader.SetTexture(kernel, "_Blackbody", Texture2D.blackTexture);
                shader.SetInt("_GasView", (int)view);
                shader.SetFloat("_GasSwirl", (float)pattern.Contrast);
                var turns = new Vector4[SwirlTables.Size];
                SwirlTables.Turns(pattern.Spin, pattern.Time, turns);
                shader.SetVectorArray("_SwirlTurns", turns);
                var parameters = new Vector4[GasTables.ParameterCount];
                GasTables.Parameters(flow, parameters);
                shader.SetVectorArray("_Gas", parameters);
                shader.SetTexture(kernel, "_GasPowerLaw", table);
                shader.SetInt("_Blackout", 0);
                shader.SetMatrix("_HoleToWorld", Matrix4x4.identity);
                shader.SetBuffer(kernel, "_Trace", trace);
                Dispatch(shader, kernel);

                var traced = new Vector4[2 * GridWidth * GridHeight];
                trace.GetData(traced);
                return traced;
            }
            finally
            {
                // Compute parameters persist on the asset; leave the gas off for the other kernels' tests.
                shader.SetInt("_GasView", 0);
                shader.SetFloat("_GasSwirl", 0);
                trace.Release();
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        static double MaxChannel(Vector3d v) => Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z)));
        static Vector3d Abs(Vector3d v) => new Vector3d(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));
    }
}
