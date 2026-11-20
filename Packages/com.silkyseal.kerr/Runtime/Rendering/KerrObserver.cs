using System;
using UnityEngine;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// Put on a camera to see it through a Kerr black hole. Tracks the camera's velocity and
    /// builds its relativistic frame each frame; the lensing pass skips cameras without one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KerrObserver : MonoBehaviour
    {
        public enum ExposureMode
        {
            [Tooltip("Adapts like an eye to the brightest part of the view.")]
            Automatic,
            [Tooltip("Fixed EV100, as a camera set by hand.")]
            Manual,
        }

        // Below r₊ + 0.01 M the ZAMO frame is ill-conditioned; Phase 6 takes over inside with free fall.
        const double HorizonClearance = 0.01;
        // γ = 7 at 0.99c: strong aberration without the tetrad losing precision in float upload.
        const double MaxBeta = 0.99;

        [Header("Exposure")]
        [SerializeField] ExposureMode exposureMode = ExposureMode.Automatic;

        [Tooltip("Manual exposure as EV100. Sunlit snow is about 16; a 4500 K disk surface about 32.")]
        [SerializeField, Range(-10, 60)] float manualEV100 = 30;

        [Tooltip("Stops added on top of either mode. Positive brightens.")]
        [SerializeField, Range(-10, 10)] float exposureCompensation;

        [Tooltip("How fast automatic exposure follows a change in brightness, per second.")]
        [SerializeField, Min(0.01f)] float adaptationSpeed = 2;

        Vector3 _lastPosition;
        Vector3 _velocity;
        bool _hasLastPosition;

        public ExposureMode Exposure => exposureMode;
        public float ManualEV100 => manualEV100;
        public float ExposureCompensation => exposureCompensation;
        public float AdaptationSpeed => adaptationSpeed;

        void OnEnable() => _hasLastPosition = false;

        void LateUpdate()
        {
            var position = transform.position;
            float dt = Time.deltaTime;
            _velocity = _hasLastPosition && dt > 0 ? (position - _lastPosition) / dt : Vector3.zero;
            _lastPosition = position;
            _hasLastPosition = true;
        }

        internal void SetVelocityForTest(Vector3 v) => _velocity = v;

        /// <summary>
        /// Camera tetrad in the hole's Kerr-Schild coordinates. False when the camera is at or inside
        /// r₊ + 0.01 M, where this observer model does not apply; the caller then draws black.
        /// </summary>
        /// <exception cref="ArgumentNullException">hole is null or destroyed.</exception>
        public bool TryGetFrame(BlackHole hole, out CameraFrame frame)
        {
            if (hole == null) throw new ArgumentNullException(nameof(hole));

            frame = default;
            var t = transform;
            var x = hole.WorldToKerrSchild(t.position);
            if (KerrSchild.Radius(x, hole.Spin) <= KerrSchild.HorizonRadius(hole.Spin) + HorizonClearance)
                return false;

            // shortcut: speed is measured against Unity coordinates, not the ZAMO's own rotation; exact for a still camera far out. Phase 6 uses the true 4-velocity.
            double beta = Math.Min(_velocity.magnitude / hole.SpeedOfLight, MaxBeta);
            frame = ObserverFrame.Build(x, hole.Spin, hole.WorldDirToKerrSchild(t.forward), hole.WorldDirToKerrSchild(t.up),
                                        hole.WorldDirToKerrSchild(t.right), hole.WorldDirToKerrSchild(_velocity), beta);
            return true;
        }
    }
}
