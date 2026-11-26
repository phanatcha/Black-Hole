using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SilkySeal.Kerr.Tests
{
    /// <summary>A camera falling through the horizon keeps seeing: light from outside still reaches it.</summary>
    public class InsideHorizonTests
    {
        const int Columns = 40, Rows = 24;

        // At a = 0, r = 1.5, falling from rest far away. Straight up is light falling in after the camera.
        [Test]
        public void LookingUp_SeesTheSky()
        {
            var ray = Centre(new Vector3d(1, 0, 0));
            Assert.AreEqual(RayStatus.Escaped, ray.Status);
            Assert.AreEqual(1, ray.SkyDirection.X, 1e-9, "a radial ray is not bent");
        }

        // Straight down is light climbing more slowly than the camera falls: it never left the outside, so it is dark.
        [Test]
        public void LookingDown_SeesBlack()
        {
            Assert.AreEqual(RayStatus.Captured, Centre(new Vector3d(-1, 0, 0)).Status);
        }

        // One fall, a few 1e-4 M either side of r₊: the same pixels see the same sky.
        [Test]
        public void CrossingTheHorizon_KeepsTheView()
        {
            const double a = 0.6;
            double horizon = KerrSchild.HorizonRadius(a);
            var x0 = new Vector3d(8, 0, 1.5);
            var fall = new FreeFall(x0, ObserverFrame.Zamo(x0, a), a);
            while (fall.Radius > horizon + 3e-4) fall.Advance(fall.Radius > horizon + 0.1 ? 0.05 : 1e-4);
            Assert.Greater(fall.Radius, horizon);

            // Sideways along the orbit, the sky overhead in the top of the frame.
            var up = fall.Position.Normalized;
            var forward = new Vector3d(-up.Y, up.X, 0).Normalized;
            var right = Vector3d.Cross(up, forward);
            var outside = Grid(fall, forward, up, right);
            while (fall.Radius > horizon - 1e-4) fall.Advance(1e-4);
            var inside = Grid(fall, forward, up, right);

            int differ = 0, open = 0, close = 0;
            for (int i = 0; i < inside.Length; i++)
            {
                if (outside[i].Status != inside[i].Status) { differ++; continue; }
                // Beside the shadow the sky winds round the photon orbit, where any move of the camera turns it fast.
                if (NearShadow(outside, i) || NearShadow(inside, i)) continue;
                open++;
                if (Vector3d.Dot(outside[i].SkyDirection, inside[i].SkyDirection) > Math.Cos(2e-3)) close++;
            }
            Assert.Greater(open, inside.Length / 4, "the sky fills part of the view");
            Assert.LessOrEqual(differ, inside.Length / 100);
            Assert.GreaterOrEqual(close, 0.99 * open);
        }

        // Pixel i or one within two pixels of it saw no sky.
        static bool NearShadow(RayResult[] grid, int i)
        {
            int x = i % Columns, y = i / Columns;
            for (int v = Math.Max(0, y - 2); v <= Math.Min(Rows - 1, y + 2); v++)
            for (int u = Math.Max(0, x - 2); u <= Math.Min(Columns - 1, x + 2); u++)
                if (grid[v * Columns + u].Status != RayStatus.Escaped) return true;
            return false;
        }

        static RayResult Centre(Vector3d forward)
        {
            var x = new Vector3d(1.5, 0, 0);
            var f = ObserverFrame.FromVelocity(x, 0, FreeFallTests.Rain(x), forward, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0));
            return RayTracer.TracePixel(f, 0, 0, 1, 1, TraceSettings.Reference(0));
        }

        static RayResult[] Grid(FreeFall fall, Vector3d forward, Vector3d up, Vector3d right)
        {
            var f = ObserverFrame.FromVelocity(fall.Position, fall.Spin, fall.Velocity, forward, up, right);
            var settings = TraceSettings.Reference(fall.Spin);
            var results = new RayResult[Columns * Rows];
            Parallel.For(0, results.Length, i =>
            {
                results[i] = RayTracer.TracePixel(f, GpuParityTests.Ndc(i % Columns, Columns), GpuParityTests.Ndc(i / Columns, Rows),
                                                  1, 16.0 / 9, settings);
            });
            return results;
        }
    }
}
