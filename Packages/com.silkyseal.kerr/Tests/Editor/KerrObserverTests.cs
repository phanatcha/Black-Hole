using System;
using NUnit.Framework;
using SilkySeal.Kerr.Rendering;
using UnityEditor;
using UnityEngine;

namespace SilkySeal.Kerr.Tests
{
    public class KerrObserverTests
    {
        // Unity transforms are float: positions carry ~1e-7 relative error into the double frame,
        // but orthonormality is enforced in double afterwards, so 1e-9 holds.
        const double OrthonormalTolerance = 1e-9;
        const double ClampedBeta = 0.99;

        GameObject _holeObject, _cameraObject;
        BlackHole _hole;
        KerrObserver _observer;

        [SetUp]
        public void SetUp()
        {
            _holeObject = new GameObject("Hole");
            _hole = _holeObject.AddComponent<BlackHole>();
            _cameraObject = new GameObject("Camera");
            _observer = _cameraObject.AddComponent<KerrObserver>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_cameraObject);
            UnityEngine.Object.DestroyImmediate(_holeObject);
        }

        [Test]
        public void WorldToKerrSchild_SwapsYAndZ()
        {
            var ks = _hole.WorldToKerrSchild(new Vector3(1, 2, 3));
            Assert.AreEqual(1.0, ks.X, 1e-6);
            Assert.AreEqual(3.0, ks.Y, 1e-6);
            Assert.AreEqual(2.0, ks.Z, 1e-6);
        }

        [Test]
        public void TryGetFrame_InsideHorizon_ReturnsFalse()
        {
            SetFloat("spin", 0.6f);
            _cameraObject.transform.position = new Vector3(1.5f, 0, 0);
            Assert.IsFalse(_observer.TryGetFrame(_hole, out _));
        }

        [Test]
        public void TryGetFrame_HugeVelocity_ClampsBetaAndStaysOrthonormal()
        {
            SetFloat("spin", 0.6f);
            SetFloat("speedOfLight", 0.001f);
            _cameraObject.transform.position = new Vector3(0, 3, -30);
            _cameraObject.transform.LookAt(Vector3.zero);
            _observer.SetVelocityForTest(new Vector3(0, 0, 5));

            Assert.IsTrue(_observer.TryGetFrame(_hole, out var f));

            var x = f.Position;
            var basis = new[] { f.U, f.Right, f.Up, f.Forward };
            for (int i = 0; i < 4; i++)
            for (int j = i; j < 4; j++)
            {
                double expected = i != j ? 0 : i == 0 ? -1 : 1;
                Assert.AreEqual(expected, KerrSchild.Dot(basis[i], basis[j], x, f.Spin), OrthonormalTolerance, $"pair {i},{j}");
            }

            // Lorentz factor between the boosted camera and the ZAMO is γ(β = 0.99).
            double gamma = -KerrSchild.Dot(f.U, ObserverFrame.Zamo(x, f.Spin), x, f.Spin);
            Assert.AreEqual(1 / Math.Sqrt(1 - ClampedBeta * ClampedBeta), gamma, 1e-6);
        }

        [Test]
        public void FreeFall_CarriesTheFrameInsideTheHorizon_UntilCleared()
        {
            SetFloat("spin", 0.6f);
            double a = _hole.Spin;
            var x0 = new Vector3d(8, 0, 1.5);
            var fall = new FreeFall(x0, ObserverFrame.Zamo(x0, a), a, 500);
            while (fall.Radius > 1.5) fall.Advance(0.05); // r₊ = 1.8
            _observer.SetFreeFall(fall.Position, fall.Velocity, fall.Time);

            Assert.IsTrue(_observer.TryGetFrame(_hole, out var f));
            Assert.AreEqual(0, (f.Position - fall.Position).Length);
            Assert.AreEqual(-1, KerrSchild.Dot(f.U, fall.Velocity, f.Position, a), 1e-9);
            Assert.AreEqual(fall.Time, _observer.CoordinateTime(_hole, 2), "the disk turns on the fall's clock");

            _observer.ClearFreeFall();
            Assert.IsFalse(_observer.TryGetFrame(_hole, out _), "back on the Transform, which sits at the centre");
            Assert.AreEqual(_hole.CoordinateTime(2), _observer.CoordinateTime(_hole, 2));
        }

        void SetFloat(string field, float value)
        {
            var so = new SerializedObject(_hole);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
