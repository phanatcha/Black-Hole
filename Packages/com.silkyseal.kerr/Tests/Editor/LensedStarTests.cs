using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static SilkySeal.Kerr.Tests.GpuParityTests;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>
    /// Stars seen through the whole GPU frame, CSTrace then CSShade: a star straight behind a Schwarzschild hole comes
    /// back as a closed ring at the Einstein angle the double-precision tracer finds, and lensed stars keep their light
    /// while a camera slides past the hole.
    /// </summary>
    public class LensedStarTests
    {
        const int Size = 96, StarMapSize = 256, Sectors = 16, FlyByFrames = 24;
        const double Fov = 50 * Math.PI / 180, CameraRadius = 40;
        static readonly Vector3d Forward = new Vector3d(-1, 0, 0), Right = new Vector3d(0, 1, 0);

        [Test]
        public void StarBehindTheHole_FormsAnEinsteinRing()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            var frame = ObserverFrame.Build(-CameraRadius * Forward, 0, Forward, new Vector3d(0, 0, 1), Right, default, 0);
            double tanHalf = Math.Tan(Fov / 2), pixelAngle = 2 * tanHalf / Size;
            // On the hole's axis, where Kerr–Schild (x, y, z) and Unity (x, z, y) agree.
            var map = StarMap.Bake(new[] { new Star(Forward, 1e-6, 5800) }, StarMapSize);

            var image = GpuSkyParityTests.Shade(shader, Trace(shader, frame, tanHalf), Size, Size, pixelAngle, null, map, 0, 1);

            double ring = EinsteinAngle(frame, tanHalf), light = 0, onRing = 0;
            var sectors = new double[Sectors];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var c = image[y * Size + x];
                double l = 0.2126 * c.x + 0.7152 * c.y + 0.0722 * c.z;
                if (!(l > 0)) continue;
                double nx = Ndc(x, Size) * tanHalf, ny = Ndc(y, Size) * tanHalf;
                light += l;
                if (Math.Abs(Math.Atan(Math.Sqrt(nx * nx + ny * ny)) - ring) > 2 * pixelAngle) continue;
                onRing += l;
                sectors[(int)((Math.Atan2(ny, nx) + Math.PI) / (2 * Math.PI) * Sectors) % Sectors] += l;
            }

            Debug.Log($"[Einstein ring] {ring * 180 / Math.PI:F2}°, {onRing / light:P1} of the light within 2 pixels, " +
                      $"sectors {Math.Round(Min(sectors) / (onRing / Sectors), 2)}–{Math.Round(Max(sectors) / (onRing / Sectors), 2)} of the mean");
            Assert.Greater(light, 0, "the star should be seen");
            // What is left over is the fainter n = 1 ring just outside the shadow.
            Assert.Greater(onRing, 0.9 * light, "light off the Einstein angle");
            // A point source's magnification diverges on the ring, so the pixels whose centres land nearest it take most of
            // a sector's light and the ring is knotty at any resolution. Closed means every sector carries a share.
            foreach (var s in sectors) Assert.Greater(s, 0.25 * onRing / Sectors, "the ring should close");
        }

        [Test]
        public void FlyBy_KeepsLensedStarsSteady()
        {
            // Spin 0.6 seen from 60 M, where the Einstein ring is about 15° across. The camera looks 17° to the side of the
            // hole, at the primary images of stars 4–7° behind it, and slides a third of a pixel per frame. Pixels are the
            // demo's at 1080p and 60°: the filter's error grows about as the pixel squared, 6 % jitter at 12 mrad.
            const int size = 512;
            const double spin = 0.6, radius = 60, slide = 0.014, aside = 17 * Math.PI / 180;
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            double tanHalf = size / 1080.0 * Math.Tan(Math.PI / 6), pixelAngle = 2 * tanHalf / size;
            var view = Forward * Math.Cos(aside) + Right * Math.Sin(aside);
            var random = new System.Random(11);
            var maps = new StarMap[4];
            for (int s = 0; s < maps.Length; s++)
            {
                double off = (4 + 3 * random.NextDouble()) * Math.PI / 180, around = (random.NextDouble() - 0.5) * Math.PI / 4;
                var ks = Forward * Math.Cos(off) + (Right * Math.Cos(around) + new Vector3d(0, 0, 1) * Math.Sin(around)) * Math.Sin(off);
                maps[s] = StarMap.Bake(new[] { new Star(new Vector3d(ks.X, ks.Z, ks.Y), 1e-6, 5800) }, StarMapSize);
            }

            var flux = new double[maps.Length, FlyByFrames];
            var centreX = new double[maps.Length, FlyByFrames];
            var centreY = new double[maps.Length, FlyByFrames];
            for (int f = 0; f < FlyByFrames; f++)
            {
                var position = new Vector3d(radius, (f - FlyByFrames / 2) * slide, 1.5);
                var frame = ObserverFrame.Build(position, spin, view, new Vector3d(0, 0, 1), Right, default, 0);
                var trace = Trace(shader, frame, tanHalf, size);
                for (int s = 0; s < maps.Length; s++)
                {
                    var image = GpuSkyParityTests.Shade(shader, trace, size, size, pixelAngle, null, maps[s], 0, 1);
                    double weightedX = 0, weightedY = 0;
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var c = image[y * size + x];
                        double nx = Ndc(x, size) * tanHalf, ny = Ndc(y, size) * tanHalf;
                        // Radiance times the pixel's solid angle is the flux it passes on.
                        double l = (0.2126 * c.x + 0.7152 * c.y + 0.0722 * c.z) * pixelAngle * pixelAngle / Math.Pow(1 + nx * nx + ny * ny, 1.5);
                        flux[s, f] += l;
                        weightedX += l * x;
                        weightedY += l * y;
                    }
                    centreX[s, f] = weightedX / flux[s, f];
                    centreY[s, f] = weightedY / flux[s, f];
                }
            }

            // Worst departure of a frame from the mean of its neighbours: flicker in brightness, popping in position.
            var jitter = new double[maps.Length];
            var popping = new double[maps.Length];
            var moved = new double[maps.Length];
            for (int s = 0; s < maps.Length; s++)
            {
                double mean = 0;
                for (int f = 0; f < FlyByFrames; f++) mean += flux[s, f] / FlyByFrames;
                for (int f = 1; f < FlyByFrames - 1; f++)
                {
                    jitter[s] = Math.Max(jitter[s], Math.Abs(flux[s, f] - 0.5 * (flux[s, f - 1] + flux[s, f + 1])) / mean);
                    double px = centreX[s, f] - 0.5 * (centreX[s, f - 1] + centreX[s, f + 1]);
                    double py = centreY[s, f] - 0.5 * (centreY[s, f - 1] + centreY[s, f + 1]);
                    popping[s] = Math.Max(popping[s], Math.Sqrt(px * px + py * py));
                }
                double dx = centreX[s, FlyByFrames - 1] - centreX[s, 0], dy = centreY[s, FlyByFrames - 1] - centreY[s, 0];
                moved[s] = Math.Sqrt(dx * dx + dy * dy);
                Debug.Log($"[Fly-by] star {s}: worst jitter {jitter[s]:P3} of {mean:G3} lx, worst pop {popping[s]:F3} px, " +
                          $"light moved {moved[s]:F1} px");
            }
            for (int s = 0; s < maps.Length; s++)
            {
                Assert.Greater(moved[s], 1, $"star {s} should cross pixels");
                Assert.Less(popping[s], 0.1, $"star {s} pops between pixels");
                // A 1.5 % step from one frame to the next is well under what the eye picks up on a point of light.
                Assert.Less(jitter[s], 0.015, $"star {s} flickers");
            }
        }

        // CSTrace with no disk and the hole at the world origin; returns the trace buffer.
        static Vector4[] Trace(ComputeShader shader, CameraFrame frame, double tanHalf, int size = Size)
        {
            int kernel = shader.FindKernel("CSTrace");
            var trace = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * size * size, 4 * sizeof(float));
            try
            {
                SetCamera(shader, frame);
                shader.SetFloat("_TanHalfFovY", (float)tanHalf);
                shader.SetFloat("_Aspect", 1);
                shader.SetInts("_Size", size, size);
                // The disk is off, but Metal still wants every texture the kernel declares bound.
                shader.SetInt("_DiskEnabled", 0);
                shader.SetTexture(kernel, "_DiskFlux", Texture2D.blackTexture);
                shader.SetTexture(kernel, "_Blackbody", Texture2D.blackTexture);
                shader.SetTexture(kernel, "_GasPowerLaw", Texture2D.blackTexture); // declared by TraceRay; Metal wants it bound
                shader.SetInt("_Blackout", 0);
                shader.SetMatrix("_HoleToWorld", Matrix4x4.identity);
                shader.SetBuffer(kernel, "_Trace", trace);
                shader.Dispatch(kernel, (size + 7) / 8, (size + 7) / 8, 1);
                var traced = new Vector4[2 * size * size];
                trace.GetData(traced);
                return traced;
            }
            finally
            {
                trace.Release();
            }
        }

        // Angle off the hole of the pixel ray, along the image's horizontal axis, that the reference tracer sends out
        // straight behind the hole: walk in from the edge to the first sign change, then bisect.
        static double EinsteinAngle(CameraFrame frame, double tanHalf)
        {
            var settings = TraceSettings.Reference(frame.Spin);
            Func<double, double> side = ndc =>
            {
                var ray = RayTracer.Trace(frame.Position, RayTracer.PixelRay(frame, ndc, 0, tanHalf, 1), settings);
                Assert.AreEqual(RayStatus.Escaped, ray.Status, $"ray at ndc {ndc}");
                return Vector3d.Dot(ray.SkyDirection, Right);
            };
            double outer = 1, inner = 1;
            while (side(inner) > 0) { outer = inner; inner -= 0.02; }
            for (int i = 0; i < 40; i++)
            {
                double mid = 0.5 * (outer + inner);
                if (side(mid) > 0) outer = mid; else inner = mid;
            }
            return Math.Atan(0.5 * (outer + inner) * tanHalf);
        }

        static double Min(double[] v) { double m = v[0]; foreach (var x in v) m = Math.Min(m, x); return m; }
        static double Max(double[] v) { double m = v[0]; foreach (var x in v) m = Math.Max(m, x); return m; }
    }
}
