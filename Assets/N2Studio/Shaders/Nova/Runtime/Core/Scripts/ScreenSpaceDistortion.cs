// --------------------------------------------------------------
// Copyright 2025 CyberAgent, Inc.
// --------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nova.Runtime.Core.Scripts
{
    [Serializable]
    public sealed class ScreenSpaceDistortion : ScriptableRendererFeature
    {
        private const string DistortionLightMode = "DistortedUvBuffer";

        [SerializeField] private bool _applyToSceneView = true;
        [SerializeField] [HideInInspector] private Shader _applyDistortionShader;
        private ApplyDistortionPass _applyDistortionPass;

        private DistortedUvBufferPass _distortedUvBufferPass;

#if !UNITY_6000_4_OR_NEWER
        private RTHandle _distortedUvBufferRTHandle;
#endif

        public override void Create()
        {
            _applyDistortionShader = Shader.Find("Hidden/Nova/Particles/ApplyDistortion");
            if (_applyDistortionShader == null) return;

            _distortedUvBufferPass = new DistortedUvBufferPass(DistortionLightMode);
            _applyDistortionPass = new ApplyDistortionPass(_applyToSceneView, _applyDistortionShader);
        }

        private bool IsPostProcessingAllowed()
        {
            return UniversalRenderPipelineDebugDisplaySettings.Instance.IsPostProcessingAllowed;
        }

#if !UNITY_6000_4_OR_NEWER
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // PostProcess may interfere with drawing process when DebugDisplay is enabled.
            if (_applyDistortionShader == null
                || renderingData.cameraData.cameraType == CameraType.Reflection
                || renderingData.cameraData.cameraType == CameraType.Preview
                || !IsPostProcessingAllowed())
                return;

            var distortedUvBufferFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf)
                ? RenderTextureFormat.RGHalf
                : RenderTextureFormat.DefaultHDR;

#if UNITY_2023_3_OR_NEWER
            if (GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode)
#endif
            {
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.colorFormat = distortedUvBufferFormat;
#pragma warning disable CS0618 // This method will be removed in a future release. Please use ReAllocateHandleIfNeeded instead. #from(2023.3)
                RenderingUtils.ReAllocateIfNeeded(ref _distortedUvBufferRTHandle, desc);
#pragma warning restore CS0618
                _distortedUvBufferPass.Setup(_distortedUvBufferRTHandle);
                _applyDistortionPass.Setup(_distortedUvBufferRTHandle);
            }

            renderer.EnqueuePass(_distortedUvBufferPass);
            renderer.EnqueuePass(_applyDistortionPass);
        }
#else
        // On Unity 6000.4+ the Render Graph path (RecordRenderGraph) is used instead,
        // but AddRenderPasses is still abstract on ScriptableRendererFeature, so we
        // must provide a no-op override.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) { }
#endif
    }
}
