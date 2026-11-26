using System;
using SilkySeal.Kerr.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SilkySeal.Kerr.Controls
{
    /// <summary>
    /// The plunge, after NASA SVS 14576 (Schnittman &amp; Powell 2024). G lets go: thrust stops and the camera falls freely
    /// from its current motion. 1 and 2 jump 30 M out along the line of sight for a scripted fall that circles the hole and
    /// then plunges (1) or slingshots back out (2). R puts the camera back where it was. Mouse look keeps working; a corner
    /// readout sets the camera's clock against the far-away one. The fall ends at the inner horizon (near the singularity
    /// at a = 0), where known physics stops, and fades to black.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KerrObserver))]
    public sealed class PlungeCamera : MonoBehaviour
    {
        const double PathStartRadius = 30;
        const float FadeSeconds = 2;

        [Tooltip("Close to the hole the fall is slowed so an orbit at your radius would take 1 / this many seconds; " +
                 "far out your clock runs at the hole's speed of light.")]
        [SerializeField, Min(1e-3f)] float orbitsPerSecond = 0.1f;

        readonly InputAction _letGo = Key("LetGo", "g");
        readonly InputAction _plunge = Key("Plunge", "1");
        readonly InputAction _nearMiss = Key("NearMiss", "2");
        readonly InputAction _reset = Key("Reset", "r");

        KerrObserver _observer;
        FlyCamera _fly;
        FreeFall _fall;
        double _startTime;
        float _fade;
        Vector3 _homePosition;
        Quaternion _homeRotation;
        string _readout, _ending;
        GUIStyle _style;

        // Edit mode runs no Awake for this component, so the tests' calls find their neighbours on first use.
        KerrObserver Observer => _observer != null ? _observer : _observer = GetComponent<KerrObserver>();
        FlyCamera Fly => _fly != null ? _fly : _fly = GetComponent<FlyCamera>();
        static BlackHole Hole => BlackHole.Active.Count > 0 ? BlackHole.Active[0] : null;

        void OnEnable()
        {
            _letGo.Enable();
            _plunge.Enable();
            _nearMiss.Enable();
            _reset.Enable();
        }

        void OnDisable()
        {
            ResetFall();
            _letGo.Disable();
            _plunge.Disable();
            _nearMiss.Disable();
            _reset.Disable();
        }

        void OnDestroy()
        {
            _letGo.Dispose();
            _plunge.Dispose();
            _nearMiss.Dispose();
            _reset.Dispose();
        }

        void Update()
        {
            if (_reset.WasPressedThisFrame()) ResetFall();
            else if (_plunge.WasPressedThisFrame()) StartPath(-1);
            else if (_nearMiss.WasPressedThisFrame()) StartPath(1);
            else if (_letGo.WasPressedThisFrame()) LetGo();
            Tick(Time.deltaTime);
        }

        /// <summary>Starts falling with the camera's current 4-velocity. Nothing happens inside r₊ or while falling.</summary>
        internal void LetGo()
        {
            var hole = Hole;
            if (hole == null || _fall != null || !Observer.TryGetFrame(hole, out var f)) return;
            Begin(new FreeFall(f.Position, f.U, hole.Spin, hole.CoordinateTime(Time.timeAsDouble)), transform.rotation);
        }

        /// <summary>A zoom-whirl from 30 M out on the line of sight: side −1 plunges, +1 slingshots out.</summary>
        internal void StartPath(int side)
        {
            var hole = Hole;
            if (hole == null) return;
            var direction = hole.WorldToKerrSchild(transform.position);
            var start = (direction.Length > 0 ? direction.Normalized : new Vector3d(0, -1, 0)) * PathStartRadius;
            double time = _fall != null ? _fall.Time : hole.CoordinateTime(Time.timeAsDouble);
            var holeTransform = hole.transform;
            var look = Quaternion.LookRotation(holeTransform.position - hole.KerrSchildToWorld(start), holeTransform.up);
            Begin(FreeFall.Whirl(start, hole.Spin, side, time), look);
        }

        /// <summary>Ends any fall and puts the camera back where the first fall began.</summary>
        internal void ResetFall()
        {
            if (_fall == null) return;
            _fall = null;
            Observer.ClearFreeFall();
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            if (Fly != null) Fly.MovementEnabled = true;
        }

        internal void Tick(float dt)
        {
            if (_fall == null) return;
            var hole = Hole;
            if (hole == null) { ResetFall(); return; }
            if (!_fall.Ended) _fall.Advance(dt * ProperTimePerSecond(_fall.Radius, hole.SpeedOfLight / hole.UnitsPerM, orbitsPerSecond));
            _fade = _fall.Ended ? _fade + dt : 0;
            Follow(hole);
        }

        /// <summary>
        /// Proper time, M, per second of play at radius r: speedOfLight (M per second) far out; close in, slowed so an
        /// orbit at r, 2π r^{3/2}, takes 1 / orbitsPerSecond seconds. A fall then crosses each factor of e in r in about
        /// the same time, so the last 2 M, which take 4M/3, still play for seconds.
        /// </summary>
        internal static double ProperTimePerSecond(double r, double speedOfLight, double orbitsPerSecond) =>
            Math.Min(speedOfLight, orbitsPerSecond * 2 * Math.PI * r * Math.Sqrt(r));

        void Begin(FreeFall fall, Quaternion look)
        {
            if (_fall == null)
            {
                _homePosition = transform.position;
                _homeRotation = transform.rotation;
            }
            _fall = fall;
            _startTime = fall.Time;
            _fade = 0;
            transform.rotation = look;
            if (Fly != null) Fly.MovementEnabled = false;
            Follow(Hole);
        }

        void Follow(BlackHole hole)
        {
            transform.position = hole.KerrSchildToWorld(_fall.Position);
            Observer.SetFreeFall(_fall.Position, _fall.Velocity, _fall.Time);

            double a = hole.Spin, r = _fall.Radius, outer = KerrSchild.HorizonRadius(a), inner = KerrSchild.InnerHorizonRadius(a);
            string depth = r > outer
                ? $"horizon         r − r₊ = {r - outer:F3} M"
                : $"inside          r₊ − r = {outer - r:F3} M, r − r₋ = {r - inner:F3} M";
            // shortcut: a new string every frame, ~200 B of garbage; cache it per change of digits if GC ever shows.
            _readout = $"your clock      τ = {_fall.ProperTime:F2} M\nfar-away clock  t = {_fall.Time - _startTime:F2} M (Kerr–Schild)\n{depth}";
            _ending = a > 0 ? "Inner horizon: known physics stops here." : "Singularity: known physics stops here.";
        }

        void OnGUI()
        {
            if (_fall == null) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            if (_fade > 0)
            {
                var color = GUI.color;
                GUI.color = new Color(0, 0, 0, Mathf.Clamp01(_fade / FadeSeconds));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = color;
                GUI.Label(new Rect(16, Screen.height * 0.5f, Screen.width - 32, 40), _ending, _style);
            }
            GUI.Label(new Rect(16, 16, 640, 80), _readout, _style);
        }

        static InputAction Key(string name, string key) => new InputAction(name, InputActionType.Button, "<Keyboard>/" + key);
    }
}
