using NUnit.Framework;
using SilkySeal.Kerr.Controls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SilkySeal.Kerr.Tests
{
    public class FlyCameraTests : InputTestFixture
    {
        // Serialized defaults of FlyCamera.
        const float BaseSpeed = 5f, BoostMultiplier = 5f, LookDegreesPerPixel = 0.1f;
        const float Tolerance = 1e-4f;

        Keyboard _keyboard;
        Mouse _mouse;
        GameObject _go;
        FlyCamera _fly;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _go = new GameObject("Fly");
            _fly = _go.AddComponent<FlyCamera>();
            _fly.SetInputEnabled(true);
        }

        public override void TearDown()
        {
            _fly.SetInputEnabled(false);
            Object.DestroyImmediate(_go);
            base.TearDown();
        }

        [Test]
        public void W_MovesForwardAtBaseSpeed()
        {
            Press(_keyboard.wKey);
            _fly.Tick(1f);
            Assert.That(Vector3.Distance(_go.transform.position, Vector3.forward * BaseSpeed), Is.LessThan(Tolerance));
        }

        [Test]
        public void Shift_MultipliesSpeed()
        {
            Press(_keyboard.leftShiftKey);
            Press(_keyboard.eKey);
            _fly.Tick(1f);
            Assert.That(Vector3.Distance(_go.transform.position, Vector3.up * BaseSpeed * BoostMultiplier), Is.LessThan(Tolerance));
        }

        [Test]
        public void MouseDelta_TurnsOnlyWhileRightButtonHeld()
        {
            Set(_mouse.delta, new Vector2(100, 0));
            _fly.Tick(0.016f);
            Assert.That(Quaternion.Angle(_go.transform.rotation, Quaternion.identity), Is.LessThan(Tolerance));

            Press(_mouse.rightButton);
            Set(_mouse.delta, new Vector2(100, 0));
            _fly.Tick(0.016f);
            Assert.AreEqual(100 * LookDegreesPerPixel, _go.transform.eulerAngles.y, Tolerance);
        }
    }
}
