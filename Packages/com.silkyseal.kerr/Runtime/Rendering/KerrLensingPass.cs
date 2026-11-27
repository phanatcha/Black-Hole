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
    /// Traces one Kerr null geodesic per pixel, shades sky, stars, disk and hot gas into an RGBA16F texture, then blits it
    /// over the camera colour. GPU cost O(pixels × steps), steps ≤ <see cref="GpuMaxSteps"/>. CPU cost O(1) per camera, no GC,
    /// apart from baking the star map once when the hole's catalogue changes.
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
        const string TraceKernelName = "CSTrace", ShadeKernelName = "CSShade", MeasureKernelName = "CSMeasureExposure";
        const int MeterStride = 4; // KERR_METER_STRIDE in Exposure.hlsl
        // Level-0 texels per cube face edge: 0.2° texels, so two stars merge only when closer than that. 34 MB with mips.
        const int StarMapSize = 512;

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
        static readonly int s_Trace = Shader.PropertyToID("_Trace");
        static readonly int s_Stars = Shader.PropertyToID("_Stars");
        static readonly int s_StarSize = Shader.PropertyToID("_StarSize");
        static readonly int s_StarLevels = Shader.PropertyToID("_StarLevels");
        static readonly int s_Spectrum = Shader.PropertyToID("_Spectrum");
        static readonly int s_SpectrumWeights = Shader.PropertyToID("_SpectrumWeights");
        static readonly int s_Sky = Shader.PropertyToID("_Sky");
        static readonly int s_UseSky = Shader.PropertyToID("_UseSky");
        static readonly int s_Blackout = Shader.PropertyToID("_Blackout");
        static readonly int s_HoleToWorld = Shader.PropertyToID("_HoleToWorld");
        static readonly int s_SkyLuminance = Shader.PropertyToID("_SkyLuminance");
        static readonly int s_AutoExposure = Shader.PropertyToID("_AutoExposure");
        static readonly int s_ManualEV100 = Shader.PropertyToID("_ManualEV100");
        static readonly int s_ExposureCompensation = Shader.PropertyToID("_ExposureCompensation");
        static readonly int s_DeltaTime = Shader.PropertyToID("_DeltaTime");
        static readonly int s_AdaptationSpeed = Shader.PropertyToID("_AdaptationSpeed");
        static readonly int s_MeterCount = Shader.PropertyToID("_MeterCount");
        static readonly int s_MeterSamples = Shader.PropertyToID("_MeterSamples");
        static readonly int s_ExposureState = Shader.PropertyToID("_ExposureState");
        static readonly int s_DiskEnabled = Shader.PropertyToID("_DiskEnabled");
        static readonly int s_DiskShifts = Shader.PropertyToID("_DiskShifts");
        static readonly int s_DiskOpacity = Shader.PropertyToID("_DiskOpacity");
        static readonly int s_DiskRange = Shader.PropertyToID("_DiskRange");
        static readonly int s_DiskFlux = Shader.PropertyToID("_DiskFlux");
        static readonly int s_Blackbody = Shader.PropertyToID("_Blackbody");
        static readonly int s_BlackbodyRange = Shader.PropertyToID("_BlackbodyRange");
        static readonly int s_GasView = Shader.PropertyToID("_GasView");
        static readonly int s_Gas = Shader.PropertyToID("_Gas");
        static readonly int s_GasPowerLaw = Shader.PropertyToID("_GasPowerLaw");
        static readonly int s_GasScale = Shader.PropertyToID("_GasScale");
        static readonly int s_BlueStreams = Shader.PropertyToID("_BlueStreams");
        static readonly int s_CloudTexture = Shader.PropertyToID("_CloudTexture");
        static readonly int s_DiskSwirl = Shader.PropertyToID("_DiskSwirl");
        static readonly int s_GasSwirl = Shader.PropertyToID("_GasSwirl");
        static readonly int s_SwirlTurns = Shader.PropertyToID("_SwirlTurns");

        // Transient per-recording data; every field is written in Fill before the pass executes.
        sealed class PassData
        {
            public ComputeShader shader;
            public int traceKernel, shadeKernel, measureKernel;
            public TextureHandle output, sky, diskFlux, blackbody, stars, spectrum, spectrumWeights, gasPowerLaw;
            public BufferHandle trace, meter, exposureState;
            public Vector4 camPos, camU, camRight, camUp, camForward, diskRange;
            public Matrix4x4 holeToWorld;
            public float spin, tanHalfFovY, aspect, farRadius, useSky, skyLuminance, diskOpacity, gasScale, diskSwirl, cloudTexture, gasSwirl;
            public float manualEV100, exposureCompensation, deltaTime, adaptationSpeed;
            public int blackout, diskEnabled, diskShifts, blueStreams, autoExposure, meterCount, starSize, starLevels, gasView;
            public readonly int[] size = new int[2];
            public readonly Vector4[] gas = new Vector4[GasTables.ParameterCount];
            public readonly Vector4[] swirlTurns = new Vector4[SwirlTables.Size];
        }

        ComputeShader _shader;
        int _traceKernel, _shadeKernel, _measureKernel;
        // shortcut: one adaptation state per renderer, so split-screen observers share it; key it by camera if that matters.
        GraphicsBuffer _exposureState;
        BlackHole _hole;
        KerrObserver _observer;
        AccretionDisk _disk;
        HotGas _gas;
        Vector4 _diskRange;
        RTHandle _skyHandle, _diskFluxHandle, _blackbodyHandle, _starsHandle, _spectrumHandle, _spectrumWeightsHandle, _gasPowerLawHandle;
        Texture2D _blackbody, _spectrumWeights, _gasPowerLaw;
        Texture2DArray _stars, _spectrum;
        TextAsset _starsSource;

        public KerrLensingPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            // The compute output is blitted onto camera colour, which must be a texture, not the back buffer.
            requiresIntermediateTexture = true;
        }

        /// <summary>Called by the feature each frame before EnqueuePass.</summary>
        public void Setup(ComputeShader shader, BlackHole hole, KerrObserver observer, AccretionDisk disk, HotGas gas)
        {
            if (_shader != shader)
            {
                _shader = shader;
                _traceKernel = shader.FindKernel(TraceKernelName);
                _shadeKernel = shader.FindKernel(ShadeKernelName);
                _measureKernel = shader.FindKernel(MeasureKernelName);
            }
            _hole = hole;
            _observer = observer;
            _disk = disk;
            _gas = gas;
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
            if (_exposureState == null)
            {
                _exposureState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 2 * sizeof(float)) { name = "Kerr Exposure" };
                _exposureState.SetData(new Vector2[1]); // contents start undefined; (0, 0) means "snap on first meter"
            }
            var exposureState = renderGraph.ImportBuffer(_exposureState);
            int meterCount = ((desc.width + MeterStride - 1) / MeterStride) * ((desc.height + MeterStride - 1) / MeterStride);
            var meter = renderGraph.CreateBuffer(new BufferDesc(meterCount, sizeof(float)) { name = "Kerr Meter" });
            var fluxTable = _disk != null ? _disk.FluxTable(out _diskRange) : Texture2D.blackTexture;
            var diskFlux = renderGraph.ImportTexture(Wrap(ref _diskFluxHandle, fluxTable));
            var trace = renderGraph.CreateBuffer(new BufferDesc(2 * desc.width * desc.height, 4 * sizeof(float)) { name = "Kerr Trace" });
            var stars = renderGraph.ImportTexture(Wrap(ref _starsHandle, StarTexture()));
            if (_spectrum == null) _spectrum = SkyTables.BakeSpectrum(out _spectrumWeights);
            var spectrum = renderGraph.ImportTexture(Wrap(ref _spectrumHandle, _spectrum));
            var spectrumWeights = renderGraph.ImportTexture(Wrap(ref _spectrumWeightsHandle, _spectrumWeights));
            // Baked even with no gas: the trace kernel declares it, and Metal wants every declared texture bound.
            if (_gasPowerLaw == null) _gasPowerLaw = GasTables.BakePowerLaw();
            var gasPowerLaw = renderGraph.ImportTexture(Wrap(ref _gasPowerLawHandle, _gasPowerLaw));

            using (var builder = renderGraph.AddComputePass<PassData>("Kerr Trace", out var data))
            {
                Fill(data, cameraData.camera, desc.width, desc.height);
                data.output = output;
                data.sky = sky;
                data.blackbody = blackbody;
                data.diskFlux = diskFlux;
                data.stars = stars;
                data.starSize = _stars.width;
                data.starLevels = _stars.mipmapCount;
                data.spectrum = spectrum;
                data.spectrumWeights = spectrumWeights;
                data.gasPowerLaw = gasPowerLaw;
                data.trace = trace;
                data.meter = meter;
                data.meterCount = meterCount;
                data.exposureState = exposureState;
                builder.UseBuffer(trace, AccessFlags.ReadWrite);
                builder.UseBuffer(meter, AccessFlags.ReadWrite);
                builder.UseBuffer(exposureState, AccessFlags.ReadWrite);
                builder.UseTexture(output, AccessFlags.Write);
                builder.UseTexture(sky, AccessFlags.Read);
                builder.UseTexture(blackbody, AccessFlags.Read);
                builder.UseTexture(diskFlux, AccessFlags.Read);
                builder.UseTexture(stars, AccessFlags.Read);
                builder.UseTexture(spectrum, AccessFlags.Read);
                builder.UseTexture(spectrumWeights, AccessFlags.Read);
                builder.UseTexture(gasPowerLaw, AccessFlags.Read);
                builder.SetRenderFunc(static (PassData d, ComputeGraphContext ctx) => Execute(d, ctx));
            }

            renderGraph.AddBlitPass(output, resourceData.activeColorTexture, Vector2.one, Vector2.zero, passName: "Kerr Composite");
        }

        void Fill(PassData d, Camera camera, int width, int height)
        {
            d.shader = _shader;
            d.traceKernel = _traceKernel;
            d.shadeKernel = _shadeKernel;
            d.measureKernel = _measureKernel;
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

            d.skyLuminance = _hole.SkyLuminance;
            d.autoExposure = _observer.Exposure == KerrObserver.ExposureMode.Automatic ? 1 : 0;
            d.manualEV100 = _observer.ManualEV100;
            d.exposureCompensation = _observer.ExposureCompensation;
            d.adaptationSpeed = _observer.AdaptationSpeed;
            d.deltaTime = Time.unscaledDeltaTime; // adaptation is the eye's, not the simulation's
            d.gasView = _gas != null ? (int)_gas.CurrentView : 0;
            if (_gas != null) GasTables.Parameters(_gas.Flow(_hole.Spin), d.gas);
            d.gasScale = _gas != null ? GasTables.WhiteScale(_gas.WhiteKelvin) : 0;
            // A thin disk is dark at 230 GHz, so the radio views draw the gas alone.
            d.diskEnabled = _disk != null && d.gasView < (int)HotGas.View.Radio ? 1 : 0;
            d.diskShifts = _disk != null && _disk.Shifts ? 1 : 0;
            d.diskOpacity = _disk != null ? _disk.Opacity : 0;
            d.diskRange = _diskRange;
            d.blueStreams = _disk != null && _disk.BlueStreams ? 1 : 0;
            d.cloudTexture = _disk != null && _disk.BlueStreams ? _disk.CloudTexture : 0;
            d.diskSwirl = _disk != null ? _disk.Streaks : 0;
            d.gasSwirl = _gas != null ? _gas.Clumps : 0;
            // Scene time stands still outside Play mode; the wall clock keeps the disk turning in the editor.
            double seconds = Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;
            SwirlTables.Turns(_hole.Spin, _observer.CoordinateTime(_hole, seconds), d.swirlTurns);
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
            cmd.SetComputeIntParam(d.shader, s_Blackout, d.blackout);
            cmd.SetComputeMatrixParam(d.shader, s_HoleToWorld, d.holeToWorld);
            cmd.SetComputeIntParam(d.shader, s_DiskEnabled, d.diskEnabled);
            cmd.SetComputeIntParam(d.shader, s_DiskShifts, d.diskShifts);
            cmd.SetComputeFloatParam(d.shader, s_DiskOpacity, d.diskOpacity);
            cmd.SetComputeVectorParam(d.shader, s_DiskRange, d.diskRange);
            cmd.SetComputeIntParam(d.shader, s_BlueStreams, d.blueStreams);
            cmd.SetComputeFloatParam(d.shader, s_CloudTexture, d.cloudTexture);
            cmd.SetComputeFloatParam(d.shader, s_DiskSwirl, d.diskSwirl);
            cmd.SetComputeFloatParam(d.shader, s_GasSwirl, d.gasSwirl);
            cmd.SetComputeVectorArrayParam(d.shader, s_SwirlTurns, d.swirlTurns);
            cmd.SetComputeVectorParam(d.shader, s_BlackbodyRange, DiskTables.BlackbodyRange);
            cmd.SetComputeTextureParam(d.shader, d.traceKernel, s_DiskFlux, d.diskFlux);
            cmd.SetComputeTextureParam(d.shader, d.traceKernel, s_Blackbody, d.blackbody);
            cmd.SetComputeIntParam(d.shader, s_GasView, d.gasView);
            cmd.SetComputeVectorArrayParam(d.shader, s_Gas, d.gas);
            cmd.SetComputeTextureParam(d.shader, d.traceKernel, s_GasPowerLaw, d.gasPowerLaw);
            cmd.SetComputeBufferParam(d.shader, d.traceKernel, s_Trace, d.trace);
            int groupsX = (d.size[0] + ThreadGroupSize - 1) / ThreadGroupSize, groupsY = (d.size[1] + ThreadGroupSize - 1) / ThreadGroupSize;
            cmd.DispatchCompute(d.shader, d.traceKernel, groupsX, groupsY, 1);

            cmd.SetComputeFloatParam(d.shader, s_UseSky, d.useSky);
            cmd.SetComputeFloatParam(d.shader, s_SkyLuminance, d.skyLuminance);
            cmd.SetComputeIntParam(d.shader, s_StarSize, d.starSize);
            cmd.SetComputeIntParam(d.shader, s_StarLevels, d.starLevels);
            cmd.SetComputeIntParam(d.shader, s_AutoExposure, d.autoExposure);
            cmd.SetComputeFloatParam(d.shader, s_GasScale, d.gasScale);
            cmd.SetComputeFloatParam(d.shader, s_ManualEV100, d.manualEV100);
            cmd.SetComputeFloatParam(d.shader, s_ExposureCompensation, d.exposureCompensation);
            cmd.SetComputeBufferParam(d.shader, d.shadeKernel, s_Trace, d.trace);
            cmd.SetComputeBufferParam(d.shader, d.shadeKernel, s_MeterSamples, d.meter);
            cmd.SetComputeBufferParam(d.shader, d.shadeKernel, s_ExposureState, d.exposureState);
            cmd.SetComputeTextureParam(d.shader, d.shadeKernel, s_Sky, d.sky);
            cmd.SetComputeTextureParam(d.shader, d.shadeKernel, s_Stars, d.stars);
            cmd.SetComputeTextureParam(d.shader, d.shadeKernel, s_Spectrum, d.spectrum);
            cmd.SetComputeTextureParam(d.shader, d.shadeKernel, s_SpectrumWeights, d.spectrumWeights);
            cmd.SetComputeTextureParam(d.shader, d.shadeKernel, s_Blackbody, d.blackbody);
            cmd.SetComputeTextureParam(d.shader, d.shadeKernel, s_Output, d.output);
            cmd.DispatchCompute(d.shader, d.shadeKernel, groupsX, groupsY, 1);

            // The false-colour views skip exposure, so there is nothing to meter.
            if (d.autoExposure == 0 || d.gasView >= (int)HotGas.View.Radio) return;
            // Meters this frame, exposes the next: a one-frame lag nobody sees at adaptation speeds of seconds.
            cmd.SetComputeIntParam(d.shader, s_MeterCount, d.meterCount);
            cmd.SetComputeFloatParam(d.shader, s_DeltaTime, d.deltaTime);
            cmd.SetComputeFloatParam(d.shader, s_AdaptationSpeed, d.adaptationSpeed);
            cmd.SetComputeBufferParam(d.shader, d.measureKernel, s_MeterSamples, d.meter);
            cmd.SetComputeBufferParam(d.shader, d.measureKernel, s_ExposureState, d.exposureState);
            cmd.DispatchCompute(d.shader, d.measureKernel, 1, 1, 1);
        }

        // The hole's catalogue as a star map, baked again only when the catalogue changes. No catalogue: an empty map.
        Texture2DArray StarTexture()
        {
            var source = _hole.Stars;
            if (_stars != null && source == _starsSource) return _stars;
            CoreUtils.Destroy(_stars);
            _starsSource = source;
            var map = source != null ? StarMap.Bake(StarCatalogue.Unpack(source.bytes), StarMapSize) : StarMap.Bake(Array.Empty<Star>(), 1);
            _stars = SkyTables.BakeStars(map);
            return _stars;
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
            _exposureState?.Release();
            _exposureState = null;
            _diskFluxHandle?.Release();
            _blackbodyHandle?.Release();
            _starsHandle?.Release();
            _spectrumHandle?.Release();
            _spectrumWeightsHandle?.Release();
            _gasPowerLawHandle?.Release();
            _skyHandle = _diskFluxHandle = _blackbodyHandle = _starsHandle = _spectrumHandle = _spectrumWeightsHandle = _gasPowerLawHandle = null;
            CoreUtils.Destroy(_blackbody);
            CoreUtils.Destroy(_gasPowerLaw);
            _gasPowerLaw = null;
            CoreUtils.Destroy(_stars);
            CoreUtils.Destroy(_spectrum);
            CoreUtils.Destroy(_spectrumWeights);
            _blackbody = _spectrumWeights = null;
            _stars = _spectrum = null;
            _starsSource = null;
        }
    }
}
