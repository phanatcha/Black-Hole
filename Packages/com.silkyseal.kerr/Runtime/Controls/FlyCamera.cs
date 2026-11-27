using UnityEngine;
using UnityEngine.InputSystem;

namespace SilkySeal.Kerr.Controls
{
    /// <summary>
    /// Free-flight camera: WASD to move, Q/E down/up, hold the right mouse button to look,
    /// Shift for a speed boost, scroll to change the base speed. Velocity eases toward the keys, so switching
    /// A to D glides through a stop instead of snapping. It only moves the Transform;
    /// KerrObserver reads the motion from there, so controls and physics stay independent. Turn MovementEnabled off while
    /// something else carries the camera, such as a free fall; looking around still works.
    /// Actions are built in code so the package does not depend on a project's input asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlyCamera : MonoBehaviour
    {
        const float MaxPitchDegrees = 89f;

        [Tooltip("Units per second without boost.")]
        [SerializeField, Min(0)] float baseSpeed = 5f;
        [SerializeField, Min(1)] float boostMultiplier = 5f;
        [Tooltip("Degrees of turn per pixel of mouse movement.")]
        [SerializeField, Min(0)] float lookDegreesPerPixel = 0.1f;
        [Tooltip("Base speed is multiplied or divided by this per scroll notch.")]
        [SerializeField, Range(1.01f, 2f)] float scrollSpeedStep = 1.2f;
        [Tooltip("Seconds for velocity to cover ~63% of the way to the keys' target. 0 snaps instantly.")]
        [SerializeField, Min(0)] float moveSmoothing = 0.15f;

        readonly InputAction _move = Composite2D("Move", "w", "s", "a", "d");
        readonly InputAction _vertical = Composite1D("Vertical", "q", "e");
        readonly InputAction _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
        readonly InputAction _lookHold = new InputAction("LookHold", InputActionType.Button, "<Mouse>/rightButton");
        readonly InputAction _boost = new InputAction("Boost", InputActionType.Button, "<Keyboard>/shift");
        readonly InputAction _scroll = new InputAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y");

        float _yaw, _pitch;
        Vector3 _velocity;

        bool _movementEnabled = true;

        /// <summary>Off: the keys stop moving the camera. Setting it either way picks up the look from the current rotation.</summary>
        internal bool MovementEnabled
        {
            get => _movementEnabled;
            set
            {
                _movementEnabled = value;
                _velocity = Vector3.zero;
                ReadLook();
            }
        }

        void OnEnable() => SetInputEnabled(true);
        void OnDisable() => SetInputEnabled(false);

        void OnDestroy()
        {
            _move.Dispose();
            _vertical.Dispose();
            _look.Dispose();
            _lookHold.Dispose();
            _boost.Dispose();
            _scroll.Dispose();
        }

        void Update() => Tick(Time.deltaTime);

        internal void SetInputEnabled(bool enabled)
        {
            _velocity = Vector3.zero;
            if (enabled) ReadLook();
            SetEnabled(_move, enabled);
            SetEnabled(_vertical, enabled);
            SetEnabled(_look, enabled);
            SetEnabled(_lookHold, enabled);
            SetEnabled(_boost, enabled);
            SetEnabled(_scroll, enabled);
        }

        internal void Tick(float dt)
        {
            float scroll = _scroll.ReadValue<float>();
            // Sign only: notch size differs by platform (1 on macOS, 120 on Windows).
            if (scroll != 0) baseSpeed *= Mathf.Pow(scrollSpeedStep, Mathf.Sign(scroll));

            var t = transform;
            if (_lookHold.IsPressed())
            {
                var delta = _look.ReadValue<Vector2>();
                _yaw += delta.x * lookDegreesPerPixel;
                _pitch = Mathf.Clamp(_pitch - delta.y * lookDegreesPerPixel, -MaxPitchDegrees, MaxPitchDegrees);
                t.rotation = Quaternion.Euler(_pitch, _yaw, 0);
            }

            if (!_movementEnabled) return;
            var move = _move.ReadValue<Vector2>();
            var direction = t.right * move.x + t.forward * move.y + t.up * _vertical.ReadValue<float>();
            var target = direction * (baseSpeed * (_boost.IsPressed() ? boostMultiplier : 1f));
            // Exponential approach: the same feel at any frame rate, unlike a per-frame lerp factor.
            _velocity = moveSmoothing > 0 ? Vector3.Lerp(_velocity, target, 1 - Mathf.Exp(-dt / moveSmoothing)) : target;
            t.position += _velocity * dt;
        }

        void ReadLook()
        {
            var euler = transform.eulerAngles;
            _yaw = euler.y;
            // eulerAngles reports pitch in [0, 360); fold it back to [−180, 180) before clamping.
            _pitch = Mathf.Clamp(Mathf.DeltaAngle(0, euler.x), -MaxPitchDegrees, MaxPitchDegrees);
        }

        static void SetEnabled(InputAction action, bool enabled)
        {
            if (enabled) action.Enable(); else action.Disable();
        }

        static InputAction Composite2D(string name, string up, string down, string left, string right)
        {
            var action = new InputAction(name, InputActionType.Value);
            action.AddCompositeBinding("2DVector")
                  .With("Up", "<Keyboard>/" + up).With("Down", "<Keyboard>/" + down)
                  .With("Left", "<Keyboard>/" + left).With("Right", "<Keyboard>/" + right);
            return action;
        }

        static InputAction Composite1D(string name, string negative, string positive)
        {
            var action = new InputAction(name, InputActionType.Value);
            action.AddCompositeBinding("1DAxis")
                  .With("Negative", "<Keyboard>/" + negative).With("Positive", "<Keyboard>/" + positive);
            return action;
        }
    }
}
