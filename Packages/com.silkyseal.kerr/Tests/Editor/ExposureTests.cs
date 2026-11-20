using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>Eye adaptation: CSMeasureExposure meters scene luminance samples into the persistent EV100 state.</summary>
    public class ExposureTests
    {
        // Odd count so the strided loop over 256 threads has a ragged end.
        const int SampleCount = 1001;
        const double EvTolerance = 1e-4;

        ComputeShader _shader;
        int _kernel;

        [SetUp]
        public void SetUp()
        {
            _shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(GpuParityTests.ShaderPath);
            _kernel = _shader.FindKernel("CSMeasureExposure");
        }

        // Saturation-based EV100 that puts luminance L at the middle of the exposure: log2(L S / K), S = 100, K = 12.5.
        static double TargetEv(double luminance) => Math.Log(luminance * 100 / 12.5, 2);

        [Test]
        public void FirstFrame_SnapsToTheMeteredLuminance()
        {
            var state = Measure(Enumerable.Repeat(0.6f, SampleCount).ToArray(), new Vector2(0, 0), 0.016f, 2);
            Assert.AreEqual(TargetEv(0.6), state.x, EvTolerance);
            Assert.AreEqual(1, state.y);
        }

        [Test]
        public void LaterFrames_EaseTowardTheTarget()
        {
            const float start = 5, dt = 0.1f, speed = 2;
            var state = Measure(Enumerable.Repeat(19.2f, SampleCount).ToArray(), new Vector2(start, 1), dt, speed);
            Assert.AreEqual(start + (TargetEv(19.2) - start) * (1 - Math.Exp(-dt * speed)), state.x, EvTolerance);
        }

        [Test]
        public void Meter_IgnoresWhatIsFarBelowTheBrightest()
        {
            // A bright disk and a sky 20 stops dimmer: the eye adapts to the disk and the sky goes black.
            var samples = Enumerable.Range(0, SampleCount).Select(i => i % 3 == 0 ? 0.6f : i % 3 == 1 ? 0.6f * Mathf.Pow(2, -20) : 0).ToArray();
            var state = Measure(samples, new Vector2(0, 0), 0.016f, 2);
            Assert.AreEqual(TargetEv(0.6), state.x, EvTolerance);
        }

        [Test]
        public void BlackFrame_KeepsTheState()
        {
            var state = Measure(new float[SampleCount], new Vector2(7, 1), 0.016f, 2);
            Assert.AreEqual(new Vector2(7, 1), state);
        }

        Vector2 Measure(float[] samples, Vector2 initial, float dt, float speed)
        {
            var meter = new GraphicsBuffer(GraphicsBuffer.Target.Structured, samples.Length, sizeof(float));
            var state = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 2 * sizeof(float));
            try
            {
                meter.SetData(samples);
                state.SetData(new[] { initial });
                _shader.SetInt("_MeterCount", samples.Length);
                _shader.SetFloat("_DeltaTime", dt);
                _shader.SetFloat("_AdaptationSpeed", speed);
                _shader.SetBuffer(_kernel, "_MeterSamples", meter);
                _shader.SetBuffer(_kernel, "_ExposureState", state);
                _shader.Dispatch(_kernel, 1, 1, 1);
                var result = new Vector2[1];
                state.GetData(result);
                return result[0];
            }
            finally
            {
                meter.Release();
                state.Release();
            }
        }
    }
}
