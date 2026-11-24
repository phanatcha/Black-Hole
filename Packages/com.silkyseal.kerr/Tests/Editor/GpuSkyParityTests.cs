using System;
using System.Collections.Generic;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static SilkySeal.Kerr.Tests.GpuParityTests;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// The shade kernel, fed a made-up trace (a flat camera's sky directions around a black disc), must gather stars like
    /// <see cref="StarMap.Radiance"/> and shift the sky like <see cref="RgbSpectrum.Shift"/>.
    /// </summary>
    public class GpuSkyParityTests
    {
        const int Width = 64, Height = 48, StarMapSize = 64;
        const double ShadowRadius = 6;
        const string MilkyWayPath = "Packages/com.silkyseal.kerr/Runtime/Data/MilkyWay.exr";
        // Faces +X and +Y meet a few degrees right of the image centre, so the gather crosses a cube edge.
        static readonly Vector3d Forward = new Vector3d(1, 0.9, 0.2).Normalized;

        [TestCase(0.004, 1.0)]
        [TestCase(0.004, 1.4)]
        [TestCase(0.02, 0.6)]
        public void Stars_MatchTheCpuGather(double pixelAngle, double g)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            var directions = Directions(pixelAngle);
            var random = new System.Random(4);
            var stars = new List<Star>();
            for (int s = 0; s < 60; s++)
            {
                var d = Direction(pixelAngle, Width * random.NextDouble(), Height * random.NextDouble());
                stars.Add(new Star(d, 1e-6 * (0.1 + random.NextDouble()), 3000 + 17000 * random.NextDouble()));
            }
            var map = StarMap.Bake(stars, StarMapSize);

            var gpu = Shade(shader, Trace(directions, _ => g), Width, Height, pixelAngle, null, map, 0, 1);

            double sum = 0, error = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var centre = directions[y * Width + x];
                if (centre.Length == 0) continue;
                Footprint.Estimate(centre, At(directions, x - 1, y), At(directions, x + 1, y), At(directions, x, y - 1),
                                   At(directions, x, y + 1), pixelAngle, out var jx, out var jy);
                var cpu = map.Radiance(centre, jx, jy, g);
                var p = gpu[y * Width + x];
                sum += Luminance(cpu);
                error += Math.Abs(Luminance(new Vector3d(p.x, p.y, p.z) - cpu));
            }

            Debug.Log($"[GpuSkyParity stars, pixel {pixelAngle}, g {g}] relative L1 error {error / sum:P2}");
            Assert.Greater(sum, 0, "some stars should be in view");
            Assert.Less(error, 0.02 * sum);
        }

        [TestCase(0.75f, 0.5f, 0.25f)]
        [TestCase(0.5f, 0.25f, 0.75f)]
        public void Sky_ShiftsLikeTheCpuSpectrum(float r, float g, float b)
        {
            const double pixelAngle = 0.004;
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            var directions = Directions(pixelAngle);
            // Exact in half precision, so the cube holds the colour the CPU sees.
            var colour = new Color(r, g, b, 1);
            var sky = new Cubemap(4, TextureFormat.RGBAHalf, false);
            try
            {
                for (int face = 0; face < 6; face++)
                {
                    var pixels = new Color[16];
                    for (int p = 0; p < pixels.Length; p++) pixels[p] = colour;
                    sky.SetPixels(pixels, (CubemapFace)face);
                }
                sky.Apply();

                Func<int, double> shift = i => 0.5 + 1.5 * (i % Width) / (Width - 1);
                var gpu = Shade(shader, Trace(directions, shift), Width, Height, pixelAngle, sky, null, 1, 1);

                int checkedPixels = 0;
                for (int i = 0; i < directions.Length; i++)
                {
                    if (directions[i].Length == 0) continue;
                    var expected = RgbSpectrum.Shift(new Vector3d(r, g, b), shift(i));
                    double tolerance = 0.01 * Math.Max(expected.X, Math.Max(expected.Y, expected.Z));
                    Assert.AreEqual(expected.X, gpu[i].x, tolerance, $"pixel {i}, g {shift(i)}");
                    Assert.AreEqual(expected.Y, gpu[i].y, tolerance, $"pixel {i}, g {shift(i)}");
                    Assert.AreEqual(expected.Z, gpu[i].z, tolerance, $"pixel {i}, g {shift(i)}");
                    checkedPixels++;
                }
                Assert.Greater(checkedPixels, directions.Length / 2);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sky);
            }
        }

        [Test]
        public void MilkyWay_LinesUpWithTheCatalogue()
        {
            // The image is centred on RA 0; Unity's latitude–longitude cube import faces its centre along +Z.
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            var sky = AssetDatabase.LoadAssetAtPath<Cubemap>(MilkyWayPath);
            Assert.IsNotNull(sky, $"Milky Way cube missing at {MilkyWayPath}");
            var bulge = StarCatalogue.Direction(266.4, -28.9);
            var northGalacticPole = StarCatalogue.Direction(192.86, 27.13);

            // Spaced by a ray that did not escape, so each footprint falls back to the 3° pixel and blurs the dust lanes.
            var gpu = Shade(shader, Trace(new[] { bulge, default, northGalacticPole }, _ => 1), 3, 1, 0.05, sky, null, 1, 1);

            double centre = Luminance(new Vector3d(gpu[0].x, gpu[0].y, gpu[0].z));
            double pole = Luminance(new Vector3d(gpu[2].x, gpu[2].y, gpu[2].z));
            Debug.Log($"[Milky Way] bulge {centre:G3}, north galactic pole {pole:G3}");
            Assert.Greater(centre, 10 * pole);
        }

        /// <summary>
        /// Runs CSShade on a trace buffer (two float4 per pixel: world sky direction and g, disk radiance and
        /// transmittance) and reads back the exposed colour. Null sky means none; null stars an empty map.
        /// </summary>
        internal static Vector4[] Shade(ComputeShader shader, Vector4[] trace, int width, int height, double pixelAngle,
                                        Cubemap sky, StarMap stars, float skyLuminance, float exposure)
        {
            int kernel = shader.FindKernel("CSShade");
            var traced = new GraphicsBuffer(GraphicsBuffer.Target.Structured, trace.Length, 4 * sizeof(float));
            var output = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
            output.Create();
            var meter = new GraphicsBuffer(GraphicsBuffer.Target.Structured, width * height, sizeof(float));
            var state = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 2 * sizeof(float));
            var blackSky = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var starTexture = SkyTables.BakeStars(stars ?? StarMap.Bake(Array.Empty<Star>(), 1));
            var spectrum = SkyTables.BakeSpectrum(out var weights);
            var blackbody = DiskTables.BakeBlackbody();
            try
            {
                traced.SetData(trace);
                shader.SetInts("_Size", width, height);
                shader.SetFloat("_TanHalfFovY", (float)(pixelAngle * height / 2));
                shader.SetInt("_AutoExposure", 0);
                shader.SetFloat("_ExposureCompensation", 0);
                shader.SetFloat("_ManualEV100", -Mathf.Log(1.2f * exposure, 2)); // 2^(−EV100) / 1.2 = exposure
                shader.SetFloat("_SkyLuminance", skyLuminance);
                shader.SetFloat("_UseSky", sky != null ? 1 : 0);
                shader.SetInt("_StarSize", starTexture.width);
                shader.SetInt("_StarLevels", starTexture.mipmapCount);
                shader.SetVector("_BlackbodyRange", DiskTables.BlackbodyRange);
                shader.SetBuffer(kernel, "_Trace", traced);
                shader.SetBuffer(kernel, "_MeterSamples", meter);
                shader.SetBuffer(kernel, "_ExposureState", state);
                shader.SetTexture(kernel, "_Sky", sky != null ? sky : blackSky);
                shader.SetTexture(kernel, "_Stars", starTexture);
                shader.SetTexture(kernel, "_Spectrum", spectrum);
                shader.SetTexture(kernel, "_SpectrumWeights", weights);
                shader.SetTexture(kernel, "_Blackbody", blackbody);
                shader.SetTexture(kernel, "_Output", output);
                shader.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);

                var request = AsyncGPUReadback.Request(output);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError, "GPU readback failed");
                return request.GetData<Vector4>().ToArray();
            }
            finally
            {
                traced.Release();
                meter.Release();
                state.Release();
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(blackSky);
                UnityEngine.Object.DestroyImmediate(starTexture);
                UnityEngine.Object.DestroyImmediate(spectrum);
                UnityEngine.Object.DestroyImmediate(weights);
                UnityEngine.Object.DestroyImmediate(blackbody);
            }
        }

        // A pinhole camera's sky directions, with no escaped ray (zero) inside a disc at the image centre.
        static Vector3d[] Directions(double pixelAngle)
        {
            var directions = new Vector3d[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double dx = x - 0.5 * (Width - 1), dy = y - 0.5 * (Height - 1);
                if (dx * dx + dy * dy < ShadowRadius * ShadowRadius) continue;
                // Through float, as the trace buffer holds it.
                var d = Direction(pixelAngle, x, y);
                directions[y * Width + x] = new Vector3d((float)d.X, (float)d.Y, (float)d.Z);
            }
            return directions;
        }

        static Vector3d Direction(double pixelAngle, double x, double y)
        {
            var right = Vector3d.Cross(new Vector3d(0, 0, 1), Forward).Normalized;
            var up = Vector3d.Cross(Forward, right);
            return (Forward + right * ((x - 0.5 * (Width - 1)) * pixelAngle) + up * ((y - 0.5 * (Height - 1)) * pixelAngle)).Normalized;
        }

        static Vector4[] Trace(Vector3d[] directions, Func<int, double> shift)
        {
            var trace = new Vector4[2 * directions.Length];
            for (int i = 0; i < directions.Length; i++)
            {
                var d = directions[i];
                bool escaped = d.Length > 0;
                trace[2 * i] = escaped ? new Vector4((float)d.X, (float)d.Y, (float)d.Z, (float)shift(i)) : Vector4.zero;
                trace[2 * i + 1] = new Vector4(0, 0, 0, escaped ? 1 : 0);
            }
            return trace;
        }

        static Vector3d At(Vector3d[] directions, int x, int y) =>
            x < 0 || x >= Width || y < 0 || y >= Height ? default : directions[y * Width + x];

        static double Luminance(Vector3d c) => 0.2126 * c.X + 0.7152 * c.Y + 0.0722 * c.Z;
    }
}
