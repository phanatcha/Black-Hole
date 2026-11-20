using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// Adds Kerr lensing to a URP renderer. Runs for Game cameras that carry a <see cref="KerrObserver"/>,
    /// lensing through the first enabled <see cref="BlackHole"/> and its <see cref="AccretionDisk"/>, if any.
    /// </summary>
    public sealed class KerrRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("KerrTrace.compute from this package.")]
        [SerializeField] ComputeShader traceShader;

        KerrLensingPass _pass;

        public override void Create()
        {
            _pass = new KerrLensingPass();
            if (traceShader == null)
                Debug.LogWarning($"{nameof(KerrRendererFeature)} '{name}' has no trace shader; black-hole lensing is off.", this);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (traceShader == null || BlackHole.Active.Count == 0) return;

            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.cameraType != CameraType.Game) return;
            // TryGetComponent: GetComponent allocates a fake-null object in the Editor when nothing is found.
            if (!cameraData.camera.TryGetComponent(out KerrObserver observer) || !observer.isActiveAndEnabled) return;

            // shortcut: only the first enabled hole is drawn; several holes need a sum of metrics or nested regions.
            var hole = BlackHole.Active[0];
            var disk = hole.TryGetComponent(out AccretionDisk d) && d.isActiveAndEnabled ? d : null;
            _pass.Setup(traceShader, hole, observer, disk);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
        }
    }
}
