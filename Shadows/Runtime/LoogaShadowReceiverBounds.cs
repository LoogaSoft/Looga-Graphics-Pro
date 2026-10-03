using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Shadows
{
    /// <summary>
    /// Measures the nearest visible receiver of each camera on the GPU and reads it back
    /// asynchronously, so clipmap allocation follows where shadows are actually seen.
    /// Results arrive one or more frames late. A stale value only moves resolution between
    /// levels; coverage is always kept by the coarser levels.
    /// </summary>
    internal sealed class LoogaShadowReceiverBounds : IDisposable
    {
        private const uint EmptyDepthBits = 0x7F7FFFFF;
        private const int ReduceGroupSize = 16;
        private const string PassName = "Looga Shadows Receiver Depth Bounds";

        // A camera keeps its radius step until the ideal step leaves it by this much,
        // so the texel size does not flip back and forth between two steps.
        private const float RadiusStepHysteresis = 0.25f;

        private static readonly int DepthTextureId = Shader.PropertyToID("_LoogaReceiverDepthTexture");
        private static readonly int BoundsBufferId = Shader.PropertyToID("_LoogaReceiverBounds");
        private static readonly int DepthParamsId = Shader.PropertyToID("_LoogaReceiverDepthParams");

        private sealed class CameraState
        {
            public GraphicsBuffer Buffer;
            public Action<AsyncGPUReadbackRequest> OnReadback;
            public CameraType CameraType;
            public bool ReadbackPending;
            public bool HasDepth;
            public float NearestReceiverDepth;
            public int RadiusStep = int.MinValue;

            // How the last rendered frame turned a receiver depth into an allocation.
            // A readback that would not change it needs no extra frame.
            public bool HasAllocation;
            public float RadiusPerDepth;
            public float MinimumRadius;
            public float MaximumStep;
            public float FitStartDistance;
        }

        private sealed class PassData
        {
            public ComputeShader Shader;
            public int ClearKernel;
            public int ReduceKernel;
            public TextureHandle Depth;
            public GraphicsBuffer Buffer;
            public Vector4 DepthParams;
            public CameraState State;
        }

        private readonly Dictionary<Camera, CameraState> _states = new();
        private readonly List<Camera> _destroyedCameras = new();
        private readonly ProfilingSampler _profilingSampler = new(PassName);
        private ComputeShader _shader;
        private int _clearKernel;
        private int _reduceKernel;

        private bool IsSupported =>
            _shader != null &&
            SystemInfo.supportsComputeShaders &&
            SystemInfo.supportsAsyncGPUReadback;

        public void SetShader(ComputeShader shader)
        {
            if (_shader == shader)
                return;

            _shader = shader;
            if (_shader == null)
                return;

            _clearKernel = _shader.FindKernel("ClearBounds");
            _reduceKernel = _shader.FindKernel("ReduceBounds");
        }

        public bool TryGetNearestReceiverDepth(Camera camera, out float depth)
        {
            depth = 0f;
            if (!_states.TryGetValue(camera, out CameraState state) || !state.HasDepth)
                return false;

            depth = state.NearestReceiverDepth;
            return true;
        }

        // Half-octave steps above the minimum radius: step k is minimumRadius * 2^(k / 2).
        public static float GetRadiusStep(float idealRadius, float minimumRadius, float maximumStep)
        {
            return Mathf.Min(
                2f * Mathf.Log(Mathf.Max(idealRadius / minimumRadius, 1f), 2f),
                maximumStep);
        }

        public int SelectRadiusStep(Camera camera, float idealStep)
        {
            CameraState state = GetOrCreateState(camera);
            int step = KeepsRadiusStep(state.RadiusStep, idealStep)
                ? state.RadiusStep
                : Mathf.FloorToInt(idealStep);
            state.RadiusStep = step;
            return step;
        }

        // Records how this frame allocated the clipmaps from the measured depth, so a later
        // readback can tell whether it would change anything.
        public void RecordAllocation(
            Camera camera,
            float radiusPerDepth,
            float minimumRadius,
            float maximumStep,
            float fitStartDistance)
        {
            CameraState state = GetOrCreateState(camera);
            state.HasAllocation = true;
            state.RadiusPerDepth = radiusPerDepth;
            state.MinimumRadius = minimumRadius;
            state.MaximumStep = maximumStep;
            state.FitStartDistance = fitStartDistance;
        }

        private static bool KeepsRadiusStep(int step, float idealStep)
        {
            return step != int.MinValue &&
                idealStep >= step - RadiusStepHysteresis &&
                idealStep < step + 1f + RadiusStepHysteresis;
        }

        public void RecordPass(
            RenderGraph renderGraph,
            UniversalCameraData cameraData,
            TextureHandle cameraDepth)
        {
            Camera camera = cameraData.camera;
            // Orthographic footprints are known on the CPU. Single-pass XR depth is a
            // texture array, which this reduction does not read.
            if (!IsSupported || camera == null || camera.orthographic || cameraData.xr.enabled)
                return;

            RemoveDestroyedCameras();
            CameraState state = GetOrCreateState(camera);
            state.CameraType = camera.cameraType;
            if (state.ReadbackPending)
                return;

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            using IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass(
                PassName,
                out PassData passData,
                _profilingSampler);
            passData.Shader = _shader;
            passData.ClearKernel = _clearKernel;
            passData.ReduceKernel = _reduceKernel;
            passData.Depth = cameraDepth;
            passData.Buffer = state.Buffer;
            passData.DepthParams = new Vector4(
                projection.m22,
                projection.m23,
                descriptor.width,
                descriptor.height);
            passData.State = state;
            builder.UseTexture(cameraDepth, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.SetComputeBufferParam(data.Shader, data.ClearKernel, BoundsBufferId, data.Buffer);
                cmd.DispatchCompute(data.Shader, data.ClearKernel, 1, 1, 1);
                cmd.SetComputeTextureParam(data.Shader, data.ReduceKernel, DepthTextureId, data.Depth);
                cmd.SetComputeBufferParam(data.Shader, data.ReduceKernel, BoundsBufferId, data.Buffer);
                cmd.SetComputeVectorParam(data.Shader, DepthParamsId, data.DepthParams);
                cmd.DispatchCompute(
                    data.Shader,
                    data.ReduceKernel,
                    Mathf.CeilToInt(data.DepthParams.z / ReduceGroupSize),
                    Mathf.CeilToInt(data.DepthParams.w / ReduceGroupSize),
                    1);
                data.State.ReadbackPending = true;
                cmd.RequestAsyncReadback(data.Buffer, data.State.OnReadback);
            });
        }

        public void Dispose()
        {
            foreach (CameraState state in _states.Values)
                state.Buffer?.Release();

            _states.Clear();
        }

        private CameraState GetOrCreateState(Camera camera)
        {
            if (_states.TryGetValue(camera, out CameraState state))
                return state;

            state = new CameraState
            {
                Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint))
                {
                    name = PassName
                }
            };
            CameraState capturedState = state;
            state.OnReadback = request => OnReadback(capturedState, request);
            _states.Add(camera, state);
            return state;
        }

        private static void OnReadback(CameraState state, AsyncGPUReadbackRequest request)
        {
            state.ReadbackPending = false;
            if (request.hasError)
                return;

            uint depthBits = request.GetData<uint>()[0];
            if (depthBits == EmptyDepthBits)
                return;

            float depth = BitConverter.Int32BitsToSingle(unchecked((int)depthBits));
            state.NearestReceiverDepth = depth;
            state.HasDepth = true;

#if UNITY_EDITOR
            // Edit-mode views render on demand. When the measurement would change what the
            // last frame showed, request one more frame of that camera's view only.
            if (!Application.isPlaying && ChangesAllocation(state, depth))
            {
                if (state.CameraType == CameraType.SceneView)
                    UnityEditor.SceneView.RepaintAll();
                else
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
#endif
        }

        // Moving away from receivers only leaves some resolution unused, so it needs no
        // frame. A new radius step, or a receiver in front of the fitted slices, does.
        private static bool ChangesAllocation(CameraState state, float depth)
        {
            if (!state.HasAllocation)
                return true;

            float idealStep = GetRadiusStep(
                depth * state.RadiusPerDepth,
                state.MinimumRadius,
                state.MaximumStep);
            return !KeepsRadiusStep(state.RadiusStep, idealStep) ||
                depth < state.FitStartDistance;
        }

        private void RemoveDestroyedCameras()
        {
            foreach (KeyValuePair<Camera, CameraState> entry in _states)
            {
                if (entry.Key == null && !entry.Value.ReadbackPending)
                    _destroyedCameras.Add(entry.Key);
            }

            foreach (Camera camera in _destroyedCameras)
            {
                _states[camera].Buffer?.Release();
                _states.Remove(camera);
            }

            _destroyedCameras.Clear();
        }
    }
}
