using System;
using NUnit.Framework;
using SilkySeal.Kerr.Controls;
using SilkySeal.Kerr.Rendering;
using UnityEngine;

namespace SilkySeal.Kerr.Tests
{
    public class PlungeCameraTests
    {
        GameObject _holeObject, _cameraObject;
        BlackHole _hole;
        KerrObserver _observer;
        PlungeCamera _plunge;

        [SetUp]
        public void SetUp()
        {
            _holeObject = new GameObject("Hole");
            _hole = _holeObject.AddComponent<BlackHole>();
            _cameraObject = new GameObject("Camera");
            _cameraObject.transform.position = new Vector3(0, 3, -30);
            _cameraObject.transform.LookAt(Vector3.zero);
            _observer = _cameraObject.AddComponent<KerrObserver>();
            _plunge = _cameraObject.AddComponent<PlungeCamera>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_cameraObject);
            UnityEngine.Object.DestroyImmediate(_holeObject);
        }

        // At rest in the scene, the camera lets go with the ZAMO's velocity and starts to fall.
        [Test]
        public void LetGo_FallsFromTheCurrentMotion()
        {
            Assert.IsTrue(_observer.TryGetFrame(_hole, out var before));
            _plunge.LetGo();

            Assert.IsTrue(_observer.IsFalling);
            Assert.IsTrue(_observer.TryGetFrame(_hole, out var start));
            Assert.AreEqual(-1, KerrSchild.Dot(before.U, start.U, start.Position, start.Spin), 1e-9, "same velocity");
            float distance = _cameraObject.transform.position.magnitude;
            _plunge.Tick(1);
            Assert.Less(_cameraObject.transform.position.magnitude, distance);
        }

        [Test]
        public void Reset_PutsTheCameraBack()
        {
            var home = _cameraObject.transform.position;
            var look = _cameraObject.transform.rotation;
            _plunge.StartPath(-1);
            _plunge.Tick(1);
            _plunge.ResetFall();

            Assert.IsFalse(_observer.IsFalling);
            Assert.AreEqual(home, _cameraObject.transform.position);
            Assert.AreEqual(look, _cameraObject.transform.rotation);
        }

        [Test]
        public void Path_Starts30MOutOnTheLineOfSight_FacingTheHole()
        {
            var direction = _cameraObject.transform.position.normalized;
            _plunge.StartPath(1);

            var p = _cameraObject.transform.position;
            Assert.AreEqual(30, p.magnitude, 1e-3);
            Assert.That(Vector3.Distance(direction, p.normalized), Is.LessThan(1e-5f));
            Assert.That(Vector3.Dot(_cameraObject.transform.forward, -direction), Is.GreaterThan(0.9999f));
            Assert.IsTrue(_observer.IsFalling);
        }

        // Far out the camera's clock runs at the hole's speed of light; near it, an orbit at its radius takes 10 s.
        [Test]
        public void Clock_SlowsOnlyNearTheHole()
        {
            Assert.AreEqual(30, PlungeCamera.ProperTimePerSecond(48, 30, 0.1), 1e-12);
            Assert.AreEqual(0.1 * 2 * Math.PI * Math.Pow(2, 1.5), PlungeCamera.ProperTimePerSecond(2, 30, 0.1), 1e-12);
        }
    }
}
