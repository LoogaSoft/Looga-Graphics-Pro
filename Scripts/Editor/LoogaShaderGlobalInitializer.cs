using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting.Editor
{
    [InitializeOnLoad]
    internal static class LoogaShaderGlobalInitializer
    {
        static LoogaShaderGlobalInitializer()
        {
            LoogaIndirectLightingController.EnsureGlobalsAreValid();
            EditorApplication.delayCall += LoogaIndirectLightingController.EnsureGlobalsAreValid;
            RenderPipelineManager.beginCameraRendering += EnsureGlobalsBeforeCameraRendering;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseGlobalResources;
            EditorApplication.quitting += ReleaseGlobalResources;
        }

        private static void EnsureGlobalsBeforeCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            LoogaIndirectLightingController.EnsureGlobalsAreValid();
        }

        private static void ReleaseGlobalResources()
        {
            RenderPipelineManager.beginCameraRendering -= EnsureGlobalsBeforeCameraRendering;
            LoogaIndirectLightingController.ReleaseFallbackResources();
        }
    }
}
