using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace SilkySeal.Kerr.Rendering
{
    /// <summary>
    /// Traces one Kerr null geodesic per pixel into an RGBA16F texture, then blits it over the camera colour.
    /// GPU cost O(pixels × steps), steps ≤ <see cref="GpuMaxSteps"/>. CPU cost O(1) per camera, no GC.
    /// </summary>
    sealed class KerrLensingPass : ScriptableRenderPass, IDisposable
    {
        // GPU tolerance, step cap and far radius are the values GpuParityTests checks against the double reference.
        const float GpuTolerance = 1e-5f;
        const int GpuMaxSteps = 4096;
        const float MinFarRadius = 5000f;
        // A camera beyond MinFarRadius would see every ray "escape" at once; keep the sphere well outside it.
        const float FarRadiusPerCameraRadius = 2f;
        const int ThreadGroupSize = 8;
        const string KernelName = "CSRender";

        static readonly int s_CamPos = Shader.PropertyToID("_CamPos");
        static readonly int s_CamU = Shader.PropertyToID("_CamU");
        static readonly int s_CamRight = Shader.PropertyToID("_CamRight");
        static readonly int s_CamUp = Shader.PropertyToID("_CamUp");
        static readonly int s_CamForward = Shader.PropertyToID("_CamForward");
        static readonly int s_Spin = Shader.PropertyToID("_Spin");
        static readonly int s_TanHalfFovY = Shader.PropertyToID("_TanHalfFovY");
        static readonly int s_Aspect = Shader.PropertyToID("_Aspect");
        static readonly int s_FarRadius = Shader.PropertyToID("_FarRadius");
        static readonly int s_Tolerance = Shader.PropertyToID("_Tolerance");
        static readonly int s_MaxSteps = Shader.PropertyToID("_MaxSteps");
        static readonly int s_Size = Shader.PropertyToID("_Size");
        static readonly int s_Output = Shader.PropertyToID("_Output");
        static readonly int s_Sky = Shader.PropertyToID("_Sky");
        static readonly int s_UseSky = Shader.PropertyToID("_UseSky");
        static readonly int s_Blackout = Shader.PropertyToID("_Blackout");
        static readonly int s_HoleToWorld = Shader.PropertyToID("_HoleToWorld");
        static readonly int s_SkyLuminance = Shader.PropertyToID("_SkyLuminance");
        static readonly int s_Exposure = Shader.PropertyToID("_Exposure");
        static readonly int s_DiskEnabled = Shader.PropertyToID("_DiskEnabled");
        static readonly int s_DiskShifts = Shader.PropertyToID("_DiskShifts");
        static readonly int s_DiskOpacity = Shader.PropertyToID("_DiskOpacity");
        static readonly int s_DiskRange = Shader.PropertyToID("_DiskRange");
        static readonly int s_DiskFlux = Shader.PropertyToID("_DiskFlux");
        static readonly int s_Blackbody = Shader.PropertyToID("_Blackbody");
        static readonly int s_BlackbodyRange = Shader.PropertyToID("_BlackbodyRange");

        // Transient per-recording data; every field is written in Fill before the pass executes.
        sealed class PassData
        {
            public ComputeShader shader;
            public int kernel;
            public TextureHandle output, sky, diskFlux, blackbody;
            public Vector4 camPos, camU, camRight, camUp, camForward, diskRange;
            public Matrix4x4 holeToWorld;
            public float spin, tanHalfFovY, aspect, farRadius, useSky, skyLuminance, exposure, diskOpacity;
            public int blackout, diskEnabled, diskShifts;
            public readonly int[] size = new int[2];
        }

        ComputeShader _shader;
        int _kernel;
        BlackHole _hole;
        KerrObserver _observer;
        RTHandle _skyHandle, _diskFluxHandle, _blackbodyHandle;
        Texture2D _blackbody;

        public KerrLensingPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            // The compute output is blitted onto camera colour, which must be a texture, not the back buffer.
            requiresIntermediateTexture = true;
        }

        /// <summary>Called by the feature each frame before EnqueuePass.</summary>
        public void Setup(ComputeShader shader, BlackHole hole, KerrObserver observer)
        {
            if (_shader != shader)
            {
                _shader = shader;
                _kernel = shader.FindKernel(KernelName);
            }
            _hole = hole;
            _observer = observer;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            var desc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
            desc.name = "_KerrLensed";
            desc.format = GraphicsFormat.R16G16B16A16_SFloat;
            desc.enableRandomWrite = true;
            desc.msaaSamples = MSAASamples.None;
            desc.depthBufferBits = DepthBits.None;
            desc.clearBuffer = false;
            var output = renderGraph.CreateTexture(desc);
            var sky = renderGraph.ImportTexture(Wrap(ref _skyHandle, _hole.Sky != null ? _hole.Sky : CoreUtils.blackCubeTexture));
            if (_blackbody == null) _blackbody = DiskTables.BakeBlackbody();
            var blackbody = renderGraph.ImportTexture(Wrap(ref _blackbodyHandle, _blackbody));
            var diskFlux = renderGraph.ImportTexture(Wrap(ref _diskFluxHandle, Texture2D.blackTexture));

            using (var builder = renderGraph.AddComputePass<PassData>("Kerr Trace", out var data))
            {
                Fill(data, cameraData.camera, desc.width, desc.height);
                data.output = output;
                data.sky = sky;
                data.blackbody = blackbody;
                data.diskFlux = diskFlux;
                builder.UseTexture(output, AccessFlags.Write);
                builder.UseTexture(sky, AccessFlags.Read);
                builder.UseTexture(blackbody, AccessFlags.Read);
                builder.UseTexture(diskFlux, AccessFlags.Read);
                builder.SetRenderFunc(static (PassData d, ComputeGraphContext ctx) => Execute(d, ctx));
            }

            renderGraph.AddBlitPass(output, resourceData.activeColorTexture, Vector2.one, Vector2.zero, passName: "Kerr Composite");
        }

        void Fill(PassData d, Camera camera, int width, int height)
        {
            d.shader = _shader;
            d.kernel = _kernel;
            d.size[0] = width;
            d.size[1] = height;
            d.aspect = (float)width / height;
            d.tanHalfFovY = Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad);
            d.spin = (float)_hole.Spin;
            d.useSky = _hole.Sky != null ? 1f : 0f;
            d.holeToWorld = _hole.transform.localToWorldMatrix;

            bool hasFrame = _observer.TryGetFrame(_hole, out var f);
            d.blackout = hasFrame ? 0 : 1;
            d.camPos = ToFloat(f.Position);
            d.camU = ToFloat(f.U);
            d.camRight = ToFloat(f.Right);
            d.camUp = ToFloat(f.Up);
            d.camForward = ToFloat(f.Forward);
            d.farRadius = Mathf.Max(MinFarRadius, FarRadiusPerCameraRadius * (float)f.Position.Length);

            d.skyLuminance = 1;
            d.exposure = 1;
            d.diskEnabled = 0;
            d.diskShifts = 0;
            d.diskOpacity = 1;
            d.diskRange = Vector4.zero;
        }

        static void Execute(PassData d, ComputeGraphContext ctx)
        {
            var cmd = ctx.cmd;
            cmd.SetComputeVectorParam(d.shader, s_CamPos, d.camPos);
            cmd.SetComputeVectorParam(d.shader, s_CamU, d.camU);
            cmd.SetComputeVectorParam(d.shader, s_CamRight, d.camRight);
            cmd.SetComputeVectorParam(d.shader, s_CamUp, d.camUp);
            cmd.SetComputeVectorParam(d.shader, s_CamForward, d.camForward);
            cmd.SetComputeFloatParam(d.shader, s_Spin, d.spin);
            cmd.SetComputeFloatParam(d.shader, s_TanHalfFovY, d.tanHalfFovY);
            cmd.SetComputeFloatParam(d.shader, s_Aspect, d.aspect);
            cmd.SetComputeFloatParam(d.shader, s_FarRadius, d.farRadius);
            cmd.SetComputeFloatParam(d.shader, s_Tolerance, GpuTolerance);
            cmd.SetComputeIntParam(d.shader, s_MaxSteps, GpuMaxSteps);
            cmd.SetComputeIntParams(d.shader, s_Size, d.size);
            cmd.SetComputeFloatParam(d.shader, s_UseSky, d.useSky);
            cmd.SetComputeIntParam(d.shader, s_Blackout, d.blackout);
            cmd.SetComputeMatrixParam(d.shader, s_HoleToWorld, d.holeToWorld);
            cmd.SetComputeTextureParam(d.shader, d.kernel, s_Output, d.output);
            cmd.SetComputeTextureParam(d.shader, d.kernel, s_Sky, d.sky);
            cmd.SetComputeFloatParam(d.shader, s_SkyLuminance, d.skyLuminance);
            cmd.SetComputeFloatParam(d.shader, s_Exposure, d.exposure);
            cmd.SetComputeIntParam(d.shader, s_DiskEnabled, d.diskEnabled);
            cmd.SetComputeIntParam(d.shader, s_DiskShifts, d.diskShifts);
            cmd.SetComputeFloatParam(d.shader, s_DiskOpacity, d.diskOpacity);
            cmd.SetComputeVectorParam(d.shader, s_DiskRange, d.diskRange);
            cmd.SetComputeVectorParam(d.shader, s_BlackbodyRange, DiskTables.BlackbodyRange);
            cmd.SetComputeTextureParam(d.shader, d.kernel, s_DiskFlux, d.diskFlux);
            cmd.SetComputeTextureParam(d.shader, d.kernel, s_Blackbody, d.blackbody);
            cmd.DispatchCompute(d.shader, d.kernel, (d.size[0] + ThreadGroupSize - 1) / ThreadGroupSize,
                                (d.size[1] + ThreadGroupSize - 1) / ThreadGroupSize, 1);
        }

        // Render graph only binds TextureHandles in compute passes, so plain textures go through an RTHandle,
        // re-wrapped only when the source changes. Placeholders (black cube, black 2D) keep unused slots bound.
        static RTHandle Wrap(ref RTHandle handle, Texture source)
        {
            if (handle == null || handle.externalTexture != source)
            {
                handle?.Release();
                handle = RTHandles.Alloc(source);
            }
            return handle;
        }

        // HLSL side stores 4-vectors as (x, y, z, t).
        static Vector4 ToFloat(Vector4d v) => new Vector4((float)v.X, (float)v.Y, (float)v.Z, (float)v.T);
        static Vector4 ToFloat(Vector3d v) => new Vector4((float)v.X, (float)v.Y, (float)v.Z, 0);

        public void Dispose()
        {
            _skyHandle?.Release();
            _diskFluxHandle?.Release();
            _blackbodyHandle?.Release();
            _skyHandle = _diskFluxHandle = _blackbodyHandle = null;
            CoreUtils.Destroy(_blackbody);
            _blackbody = null;
        }
    }
}
