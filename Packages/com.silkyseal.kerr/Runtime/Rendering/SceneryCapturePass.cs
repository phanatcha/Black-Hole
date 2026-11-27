using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// Runs on <see cref="LensedScene"/>'s capture camera: stores the face it is rendering into the scene's scenery map,
    /// as linear colour before post-processing and distance in M from its depth. One thread per texel.
    /// </summary>
    sealed class SceneryCapturePass : ScriptableRenderPass
    {
        const string KernelName = "CSCaptureScenery";
        const int ThreadGroupSize = 8;

        static readonly int s_Colour = Shader.PropertyToID("_CaptureColour");
        static readonly int s_Depth = Shader.PropertyToID("_CaptureDepth");
        static readonly int s_Out = Shader.PropertyToID("_SceneryOut");
        static readonly int s_Face = Shader.PropertyToID("_CaptureFace");
        static readonly int s_Size = Shader.PropertyToID("_CaptureSize");
        static readonly int s_ZBuffer = Shader.PropertyToID("_CaptureZBuffer");
        static readonly int s_FarDepth = Shader.PropertyToID("_CaptureFarDepth");
        static readonly int s_ToM = Shader.PropertyToID("_CaptureToM");

        // Transient per-recording data; every field is written before the pass executes.
        sealed class PassData
        {
            public ComputeShader shader;
            public int kernel, face, size;
            public TextureHandle colour, depth, target;
            public Vector4 zBuffer;
            public float farDepth, toM;
        }

        ComputeShader _shader;
        int _kernel;
        LensedScene _scene;
        RTHandle _target;

        public SceneryCapturePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        /// <summary>Called by the feature before EnqueuePass, while <paramref name="scene"/> is capturing.</summary>
        public void Setup(ComputeShader shader, LensedScene scene)
        {
            if (_shader != shader)
            {
                _shader = shader;
                _kernel = shader.FindKernel(KernelName);
            }
            _scene = scene;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>().camera;
            var map = _scene.SceneryTarget;
            if (_target == null || _target.externalTexture != map)
            {
                _target?.Release();
                _target = RTHandles.Alloc(map);
            }
            var target = renderGraph.ImportTexture(_target);

            using var builder = renderGraph.AddComputePass<PassData>("Kerr Scenery Capture", out var data);
            data.shader = _shader;
            data.kernel = _kernel;
            data.face = _scene.CaptureFace;
            data.size = map.width;
            data.toM = _scene.CaptureToM;
            // Unity's _ZBufferParams for this camera, and the depth the far plane clears to.
            float n = camera.nearClipPlane, f = camera.farClipPlane;
            bool reversed = SystemInfo.usesReversedZBuffer;
            data.zBuffer = reversed ? new Vector4(f / n - 1, 1, (f / n - 1) / f, 1 / f) : new Vector4(1 - f / n, f / n, (1 - f / n) / f, 1 / n);
            data.farDepth = reversed ? 0 : 1;
            data.colour = resourceData.activeColorTexture;
            data.depth = resourceData.cameraDepthTexture;
            data.target = target;
            builder.UseTexture(data.colour, AccessFlags.Read);
            builder.UseTexture(data.depth, AccessFlags.Read);
            builder.UseTexture(target, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData d, ComputeGraphContext ctx) => Execute(d, ctx));
        }

        static void Execute(PassData d, ComputeGraphContext ctx)
        {
            var cmd = ctx.cmd;
            cmd.SetComputeTextureParam(d.shader, d.kernel, s_Colour, d.colour);
            cmd.SetComputeTextureParam(d.shader, d.kernel, s_Depth, d.depth);
            cmd.SetComputeTextureParam(d.shader, d.kernel, s_Out, d.target);
            cmd.SetComputeIntParam(d.shader, s_Face, d.face);
            cmd.SetComputeIntParam(d.shader, s_Size, d.size);
            cmd.SetComputeVectorParam(d.shader, s_ZBuffer, d.zBuffer);
            cmd.SetComputeFloatParam(d.shader, s_FarDepth, d.farDepth);
            cmd.SetComputeFloatParam(d.shader, s_ToM, d.toM);
            int groups = (d.size + ThreadGroupSize - 1) / ThreadGroupSize;
            cmd.DispatchCompute(d.shader, d.kernel, groups, groups, 1);
        }

        public void Dispose()
        {
            _target?.Release();
            _target = null;
        }
    }
}
