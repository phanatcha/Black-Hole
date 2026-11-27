using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    public class StarMapTests
    {
        const int Size = 64;

        [Test]
        public void EveryLevel_KeepsTheTotalLux()
        {
            var random = new Random(1);
            var stars = new List<Star>();
            double total = 0;
            for (int s = 0; s < 500; s++)
            {
                var direction = new Vector3d(Gauss(random), Gauss(random), Gauss(random)).Normalized;
                double lux = Math.Exp(-10 * random.NextDouble());
                stars.Add(new Star(direction, lux, 3000 + 9000 * random.NextDouble()));
                total += lux;
            }

            var map = StarMap.Bake(stars, Size);

            Assert.AreEqual(7, map.LevelCount);
            for (int level = 0; level < map.LevelCount; level++)
            {
                double sum = 0;
                var texels = map.Levels[level];
                for (int t = 0; t < texels.Length; t += 4) sum += texels[t];
                Assert.AreEqual(total, sum, total * 1e-5, $"level {level}");
            }
        }

        [Test]
        public void SingleStar_KeepsItsDirectionAndTemperatureAtEveryLevel()
        {
            var direction = new Vector3d(0.3, -0.8, 0.5).Normalized;
            var map = StarMap.Bake(new[] { new Star(direction, 2e-6, 4500) }, Size);

            for (int level = 0; level < map.LevelCount; level++)
            {
                var texels = map.Levels[level];
                int found = 0;
                for (int t = 0; t < texels.Length; t += 4)
                {
                    if (texels[t] == 0) continue;
                    found++;
                    int face = t / 4 / ((Size >> level) * (Size >> level));
                    Assert.AreEqual(2e-6, texels[t], 2e-12);
                    Assert.AreEqual(4500, texels[t + 1], 0.01);
                    Assert.AreEqual(0, (StarMap.FromFace(face, texels[t + 2], texels[t + 3]) - direction).Length, 1e-6);
                }
                Assert.AreEqual(1, found, $"level {level}");
            }
        }

        [Test]
        public void StarsInOneTexel_MixByLuxWeightedLogTemperature()
        {
            var a = new Vector3d(1, 0.1, 0.1).Normalized;
            var b = new Vector3d(1, 0.1005, 0.1).Normalized;
            var map = StarMap.Bake(new[] { new Star(a, 1, 3000), new Star(b, 3, 12000) }, Size);

            var texels = map.Levels[0];
            for (int t = 0; t < texels.Length; t += 4)
            {
                if (texels[t] == 0) continue;
                Assert.AreEqual(4, texels[t], 1e-6);
                Assert.AreEqual(Math.Exp((Math.Log(3000) + 3 * Math.Log(12000)) / 4), texels[t + 1], 0.1);
                var expected = (a / a.X * 1 + b / b.X * 3) / 4; // lux-weighted mean on the +X face plane
                Assert.AreEqual(expected.Y, texels[t + 2], 1e-6);
                Assert.AreEqual(expected.Z, texels[t + 3], 1e-6);
                return;
            }
            Assert.Fail("no texel holds the stars");
        }

        // A camera grid of W × W pixels; footprints come from neighbouring pixel directions as on the GPU.
        // Pixel radiance times pixel solid angle summed over the image is the starlight the camera receives.
        [TestCase(0.004, TestName = "Filter_ConservesStarlight(level 0)")]
        [TestCase(0.02, TestName = "Filter_ConservesStarlight(levels 1 and 2)")]
        public void Filter_ConservesStarlight(double pixelAngle)
        {
            var camera = new Camera(new Vector3d(1, 0, 0.2).Normalized, pixelAngle, 48);
            var stars = camera.StarsInCentre(new Random(2), 40, out double total);
            var map = StarMap.Bake(stars, Size);

            Assert.AreEqual(total, camera.ReceivedLux(map, out _), total * 0.01);
        }

        // The edge between the +X and +Y faces runs down the middle of the image, between pixel columns 7 and 8. The star
        // sits a tenth of a pixel onto +Y, so column 7 on +X must reach across the edge for its share.
        [TestCase(0.004, TestName = "StarBesideACubeEdge_KeepsItsLight(level 0)")]
        [TestCase(0.02, TestName = "StarBesideACubeEdge_KeepsItsLight(levels 1 and 2)")]
        public void StarBesideACubeEdge_KeepsItsLight(double pixelAngle)
        {
            var camera = new Camera(new Vector3d(1, 1, 0.2).Normalized, pixelAngle, 16);
            var map = StarMap.Bake(new[] { new Star(camera.Direction(7.6, 7.3), 1e-6, 5800) }, Size);

            Assert.AreEqual(1e-6, camera.ReceivedLux(map, out _), 1e-6 * 0.01);
        }

        [Test]
        public void Filter_MagnifiesByTheFootprintRatio()
        {
            // A lens that squeezes the sky seen through each pixel by s in both directions brightens every star by 1/s².
            const double squeeze = 0.5;
            var camera = new Camera(new Vector3d(1, 0, 0.2).Normalized, 0.004, 48, squeeze);
            var stars = camera.StarsInCentre(new Random(3), 20, out double total);
            var map = StarMap.Bake(stars, Size);

            Assert.AreEqual(total / (squeeze * squeeze), camera.ReceivedLux(map, out _), total / (squeeze * squeeze) * 0.01);
        }

        [Test]
        public void MovingCamera_KeepsAStarSteadyAndMovesItSmoothly()
        {
            const double pixelAngle = 0.004;
            var forward = new Vector3d(1, 0, 0.2).Normalized;
            var start = new Camera(forward, pixelAngle, 16);
            var map = StarMap.Bake(new[] { new Star(start.Direction(8.3, 7.6), 1e-6, 5800) }, Size);
            start.ReceivedLux(map, out double startX);

            for (int step = 1; step <= 10; step++)
            {
                // Turn the camera right by a tenth of a pixel: the star must slide left by as much, at constant flux.
                var camera = new Camera(start.Direction(7.5 + 0.1 * step, 7.5), pixelAngle, 16);
                double flux = camera.ReceivedLux(map, out double x);
                Assert.AreEqual(1e-6, flux, 1e-6 * 0.005, $"step {step}");
                Assert.AreEqual(startX - 0.1 * step, x, 0.02, $"step {step}");
            }
        }

        sealed class Camera
        {
            readonly Vector3d m_Forward, m_Right, m_Up;
            readonly double m_PixelAngle, m_Squeeze;
            readonly int m_Width;

            public Camera(Vector3d forward, double pixelAngle, int width, double squeeze = 1)
            {
                m_Forward = forward.Normalized;
                m_Right = Vector3d.Cross(new Vector3d(0, 0, 1), m_Forward).Normalized;
                m_Up = Vector3d.Cross(m_Forward, m_Right);
                m_PixelAngle = pixelAngle;
                m_Squeeze = squeeze;
                m_Width = width;
            }

            // Sky direction seen through image position (x, y): a pinhole camera behind a lens that squeezes the sky
            // about the image centre. Pixel centres at integer coordinates, the image centre at (W − 1) / 2.
            public Vector3d Direction(double x, double y)
            {
                double half = 0.5 * (m_Width - 1), step = m_PixelAngle * m_Squeeze;
                return (m_Forward + m_Right * ((x - half) * step) + m_Up * ((y - half) * step)).Normalized;
            }

            public List<Star> StarsInCentre(Random random, int count, out double total)
            {
                var stars = new List<Star>();
                total = 0;
                for (int s = 0; s < count; s++)
                {
                    double x = m_Width * (0.25 + 0.5 * random.NextDouble()), y = m_Width * (0.25 + 0.5 * random.NextDouble());
                    double lux = 1e-6 * (0.1 + random.NextDouble());
                    stars.Add(new Star(Direction(x, y), lux, 4000 + 6000 * random.NextDouble()));
                    total += lux;
                }
                return stars;
            }

            // Σ luminance × pixel solid angle, with footprints from central differences of the sky directions; also the
            // flux-weighted mean pixel x.
            public double ReceivedLux(StarMap map, out double meanX)
            {
                double sum = 0, weightedX = 0;
                for (int y = 0; y < m_Width; y++)
                for (int x = 0; x < m_Width; x++)
                {
                    var d = Direction(x, y);
                    var jx = Tangent(d, (Direction(x + 1, y) - Direction(x - 1, y)) * 0.5);
                    var jy = Tangent(d, (Direction(x, y + 1) - Direction(x, y - 1)) * 0.5);
                    double pixel = Vector3d.Cross(jx, jy).Length / (m_Squeeze * m_Squeeze);
                    var c = map.Radiance(d, jx, jy, 1);
                    double lux = (0.2126 * c.X + 0.7152 * c.Y + 0.0722 * c.Z) * pixel;
                    sum += lux;
                    weightedX += lux * x;
                }
                meanX = weightedX / sum;
                return sum;
            }

            static Vector3d Tangent(Vector3d d, Vector3d v) => v - d * Vector3d.Dot(d, v);
        }

        static double Gauss(Random random) =>
            Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
    }
}
