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
        const float Frame = 1f / 60, SettleSeconds = 3f; // 3 s is 20 smoothing time constants: within 1e-8 of target
        // Speed is read back from float positions tens of units out, so compare it relatively.
        const float VelocityTolerance = 1e-4f;

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
            AssertVelocity(Vector3.forward * BaseSpeed);
        }

        [Test]
        public void Shift_MultipliesSpeed()
        {
            Press(_keyboard.leftShiftKey);
            Press(_keyboard.eKey);
            AssertVelocity(Vector3.up * BaseSpeed * BoostMultiplier);
        }

        [Test]
        public void SwitchingDToA_EasesThroughStopInsteadOfSnapping()
        {
            Press(_keyboard.dKey);
            AssertVelocity(Vector3.right * BaseSpeed);

            Release(_keyboard.dKey);
            Press(_keyboard.aKey);
            var before = _go.transform.position;
            _fly.Tick(Frame);
            float firstFrameVelocity = (_go.transform.position.x - before.x) / Frame;
            Assert.That(firstFrameVelocity, Is.GreaterThan(0), "one frame after the switch the camera should still drift right");

            AssertVelocity(Vector3.left * BaseSpeed);
        }

        void AssertVelocity(Vector3 expected) =>
            Assert.That(Vector3.Distance(SettledVelocity(), expected), Is.LessThan(VelocityTolerance * expected.magnitude));

        // Ticks at 60 fps until the eased velocity has caught up, then measures it over one frame.
        Vector3 SettledVelocity()
        {
            for (float t = 0; t < SettleSeconds; t += Frame) _fly.Tick(Frame);
            var before = _go.transform.position;
            _fly.Tick(Frame);
            return (_go.transform.position - before) / Frame;
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
