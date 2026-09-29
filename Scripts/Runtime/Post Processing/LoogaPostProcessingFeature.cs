using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LoogaSoft.PostProcessing
{
    /// <summary>
    /// Renders the effects of the Looga Post Processing volume override.
    /// Bloom runs before URP post-processing in linear HDR. Tonemapping runs after URP post-processing.
    /// Each pass runs only when the blended volume settings enable it and the camera has post-processing on.
    /// </summary>
    [DisallowMultipleRendererFeature(FeatureDisplayName)]
    public sealed class LoogaPostProcessingFeature : ScriptableRendererFeature
    {
        private const string FeatureDisplayName = "Looga Post Processing";
        private const string BloomComputeName = "LoogaBloom";
        private const string BloomFFTComputeName = "LoogaBloomFFT";
        private const string BloomCompositeShaderPath = "Hidden/LoogaSoft/BloomComposite";
        private const string TonemapShaderPath = "Hidden/LoogaSoft/Tonemapper";

        // The feature keeps references to its shaders so that player builds include them.
        [SerializeField, HideInInspector] private ComputeShader _bloomCompute;
        [SerializeField, HideInInspector] private ComputeShader _bloomFFTCompute;
        [SerializeField, HideInInspector] private Shader _bloomCompositeShader;
        [SerializeField, HideInInspector] private Shader _tonemapShader;

        private LoogaBloomKernels _bloomKernels;
        private LoogaBloomFFT _bloomFFT;
        private Material _bloomCompositeMaterial;
        private Material _tonemapMaterial;
        private LoogaBloomPass _bloomPass;
        private LoogaTonemapPass _tonemapPass;

        #region Built-in
#if UNITY_EDITOR
        private void OnValidate()
        {
            AssignEditorAssets();
        }
#endif
        #endregion

        public override void Create()
        {
#if UNITY_EDITOR
            AssignEditorAssets();
#endif
            name = FeatureDisplayName;
            _bloomPass ??= new LoogaBloomPass();
            _tonemapPass ??= new LoogaTonemapPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView) return;
            if (!renderingData.cameraData.postProcessEnabled) return;

            LoogaPostProcessing settings = VolumeManager.instance.stack?.GetComponent<LoogaPostProcessing>();
            if (settings == null) return;

            if (settings.IsBloomActive() && EnsureBloomResources())
            {
                _bloomPass ??= new LoogaBloomPass();
                _bloomPass.Setup(_bloomKernels, settings.IsFFTBloom() ? EnsureBloomFFT() : null,
                    _bloomCompositeMaterial, settings);
                renderer.EnqueuePass(_bloomPass);
            }

            if (settings.IsTonemapActive() && EnsureMaterial(ref _tonemapMaterial, ref _tonemapShader, TonemapShaderPath))
            {
                _tonemapPass ??= new LoogaTonemapPass();
                _tonemapPass.Setup(_tonemapMaterial, settings);
                renderer.EnqueuePass(_tonemapPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_bloomCompositeMaterial);
            CoreUtils.Destroy(_tonemapMaterial);
            _bloomCompositeMaterial = null;
            _tonemapMaterial = null;
            _bloomKernels = null;
            _bloomFFT = null;
            _bloomPass?.Dispose();
            _bloomPass = null;
            _tonemapPass = null;
            base.Dispose(disposing);
        }

        #region Resources
        private bool EnsureBloomResources()
        {
            if (_bloomCompute == null || !SystemInfo.supportsComputeShaders) return false;
            if (_bloomKernels == null || _bloomKernels.Compute != _bloomCompute)
            {
                _bloomKernels = new LoogaBloomKernels(_bloomCompute);
            }
            return EnsureMaterial(ref _bloomCompositeMaterial, ref _bloomCompositeShader, BloomCompositeShaderPath);
        }

        // Returns null when the FFT compute shader is missing. The bloom then uses the pyramid.
        private LoogaBloomFFT EnsureBloomFFT()
        {
            if (_bloomFFTCompute == null) return null;
            if (_bloomFFT == null || _bloomFFT.Compute != _bloomFFTCompute)
            {
                _bloomFFT = new LoogaBloomFFT(_bloomFFTCompute);
            }
            return _bloomFFT;
        }

        private static bool EnsureMaterial(ref Material material, ref Shader shader, string shaderPath)
        {
            if (shader == null) shader = Shader.Find(shaderPath);
            if (shader == null) return false;
            if (material != null && material.shader == shader) return true;

            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(shader);
            return material != null;
        }

#if UNITY_EDITOR
        private void AssignEditorAssets()
        {
            bool changed = false;
            if (name != FeatureDisplayName)
            {
                name = FeatureDisplayName;
                changed = true;
            }
            changed |= AssignComputeShader(ref _bloomCompute, BloomComputeName);
            changed |= AssignComputeShader(ref _bloomFFTCompute, BloomFFTComputeName);
            changed |= AssignShader(ref _bloomCompositeShader, BloomCompositeShaderPath);
            changed |= AssignShader(ref _tonemapShader, TonemapShaderPath);
            if (changed) EditorUtility.SetDirty(this);
        }

        private static bool AssignShader(ref Shader shader, string shaderPath)
        {
            if (shader != null) return false;
            shader = Shader.Find(shaderPath);
            return shader != null;
        }

        // The search matches names that contain the file name, so only an exact file name is accepted.
        private static bool AssignComputeShader(ref ComputeShader shader, string fileName)
        {
            if (shader != null) return false;
            foreach (string guid in AssetDatabase.FindAssets(fileName + " t:ComputeShader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != fileName) continue;
                shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                return shader != null;
            }
            return false;
        }
#endif
        #endregion
    }
}
