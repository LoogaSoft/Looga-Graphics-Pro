using System.Linq;
using LoogaSoft.Lighting.Editor;
using LoogaSoft.PostProcessing;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.PostProcessing.Editor
{
    [CustomEditor(typeof(LoogaPostProcessing))]
    public sealed class LoogaPostProcessingEditor : VolumeComponentEditor
    {
        private const string SectionPrefix = "LoogaSoft.PostProcessing.";

        private SerializedDataParameter _tonemapMode;
        private SerializedDataParameter _preExposure;
        private SerializedDataParameter _postExposure;
        private SerializedDataParameter _blackPoint;
        private SerializedDataParameter _whitePoint;
        private SerializedDataParameter _contrast;
        private SerializedDataParameter _saturation;
        private SerializedDataParameter _sigmoidCurve;
        private SerializedDataParameter _reinhardLimit;

        private SerializedDataParameter _bloomIntensity;
        private SerializedDataParameter _bloomThreshold;
        private SerializedDataParameter _bloomKnee;
        private SerializedDataParameter _bloomScatter;
        private SerializedDataParameter _bloomTint;
        private SerializedDataParameter _bloomLevels;
        private SerializedDataParameter _bloomMode;
        private SerializedDataParameter _bloomFFTSize;
        private SerializedDataParameter _bloomKernelSize;
        private SerializedDataParameter _bloomKernelTexture;
        private SerializedDataParameter _lensDirtTexture;
        private SerializedDataParameter _lensDirtIntensity;

        private bool _rendererHasFeature;

        #region Built-in
        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<LoogaPostProcessing>(serializedObject);
            _tonemapMode = Unpack(fetcher.Find(settings => settings.tonemapMode));
            _preExposure = Unpack(fetcher.Find(settings => settings.preExposure));
            _postExposure = Unpack(fetcher.Find(settings => settings.postExposure));
            _blackPoint = Unpack(fetcher.Find(settings => settings.blackPoint));
            _whitePoint = Unpack(fetcher.Find(settings => settings.whitePoint));
            _contrast = Unpack(fetcher.Find(settings => settings.contrast));
            _saturation = Unpack(fetcher.Find(settings => settings.saturation));
            _sigmoidCurve = Unpack(fetcher.Find(settings => settings.sigmoidCurve));
            _reinhardLimit = Unpack(fetcher.Find(settings => settings.reinhardLimit));

            _bloomIntensity = Unpack(fetcher.Find(settings => settings.bloomIntensity));
            _bloomThreshold = Unpack(fetcher.Find(settings => settings.bloomThreshold));
            _bloomKnee = Unpack(fetcher.Find(settings => settings.bloomKnee));
            _bloomScatter = Unpack(fetcher.Find(settings => settings.bloomScatter));
            _bloomTint = Unpack(fetcher.Find(settings => settings.bloomTint));
            _bloomLevels = Unpack(fetcher.Find(settings => settings.bloomLevels));
            _bloomMode = Unpack(fetcher.Find(settings => settings.bloomMode));
            _bloomFFTSize = Unpack(fetcher.Find(settings => settings.bloomFFTSize));
            _bloomKernelSize = Unpack(fetcher.Find(settings => settings.bloomKernelSize));
            _bloomKernelTexture = Unpack(fetcher.Find(settings => settings.bloomKernelTexture));
            _lensDirtTexture = Unpack(fetcher.Find(settings => settings.lensDirtTexture));
            _lensDirtIntensity = Unpack(fetcher.Find(settings => settings.lensDirtIntensity));

            _rendererHasFeature = ActiveRenderersHaveFeature();
        }

        public override void OnInspectorGUI()
        {
            LoogaEditorBase.DrawLoogaSoftHeader();
            DrawWarnings();
            LoogaEditorBase.DrawSection("Bloom", SectionPrefix + "Bloom", true, DrawBloom);
            LoogaEditorBase.DrawSection("Tonemapping", SectionPrefix + "Tonemapping", true, DrawTonemapping);
        }
        #endregion

        #region Sections
        private void DrawBloom()
        {
            PropertyField(_bloomIntensity);
            PropertyField(_bloomThreshold);
            if (_bloomThreshold.value.floatValue > 0f)
            {
                PropertyField(_bloomKnee);
            }
            PropertyField(_bloomTint);

            EditorGUILayout.Space();
            PropertyField(_bloomMode);
            if ((LoogaBloomMode)_bloomMode.value.intValue == LoogaBloomMode.FFTConvolution)
            {
                PropertyField(_bloomFFTSize);
                PropertyField(_bloomKernelSize);
                PropertyField(_bloomKernelTexture);
            }
            else
            {
                PropertyField(_bloomScatter);
                PropertyField(_bloomLevels);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lens Dirt", EditorStyles.boldLabel);
            PropertyField(_lensDirtTexture);
            PropertyField(_lensDirtIntensity);
        }

        private void DrawTonemapping()
        {
            PropertyField(_tonemapMode);
            EditorGUILayout.HelpBox("Override the mode and choose a curve to enable tonemapping. None leaves the image " +
                "unchanged.", MessageType.Info);

            EditorGUILayout.Space();
            PropertyField(_preExposure);
            PropertyField(_postExposure);

            EditorGUILayout.Space();
            PropertyField(_blackPoint);
            PropertyField(_whitePoint);
            PropertyField(_contrast);
            PropertyField(_saturation);

            var mode = (LoogaTonemapMode)_tonemapMode.value.intValue;
            if (mode == LoogaTonemapMode.Sigmoid)
            {
                EditorGUILayout.Space();
                PropertyField(_sigmoidCurve);
            }
            else if (mode == LoogaTonemapMode.ReinhardExtended)
            {
                EditorGUILayout.Space();
                PropertyField(_reinhardLimit);
            }
        }
        #endregion

        #region Warnings
        private void DrawWarnings()
        {
            if (!_rendererHasFeature)
            {
                EditorGUILayout.HelpBox("No renderer of the active URP asset has the Looga Post Processing feature. " +
                    "Add it to the renderer, or run Tools > LoogaSoft > Migrate Tonemapper To Post Processing.",
                    MessageType.Warning);
            }

            VolumeProfile profile = GetProfile();
            if (profile == null) return;
            var settings = (LoogaPostProcessing)target;

            if (settings.IsTonemapActive() && profile.TryGet(out Tonemapping unityTonemapping) &&
                unityTonemapping.active && unityTonemapping.mode.overrideState &&
                unityTonemapping.mode.value != TonemappingMode.None)
            {
                EditorGUILayout.HelpBox("Unity Tonemapping is also active. Looga tonemapping runs after URP " +
                    "post-processing, so both curves apply. Set the Unity Tonemapping Mode to None.", MessageType.Warning);
            }

            if (settings.IsBloomActive() && profile.TryGet(out Bloom unityBloom) && unityBloom.active &&
                unityBloom.intensity.overrideState && unityBloom.intensity.value > 0f)
            {
                EditorGUILayout.HelpBox("Unity Bloom is also active, so both blooms apply. Disable the Unity Bloom " +
                    "override or set its intensity to 0.", MessageType.Warning);
            }
        }

        private VolumeProfile GetProfile()
        {
            string path = AssetDatabase.GetAssetPath(target);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        }

        private static bool ActiveRenderersHaveFeature()
        {
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset) return true;
            SerializedProperty renderers = new SerializedObject(asset).FindProperty("m_RendererDataList");
            if (renderers == null) return true;
            for (int index = 0; index < renderers.arraySize; index++)
            {
                if (renderers.GetArrayElementAtIndex(index).objectReferenceValue is ScriptableRendererData data &&
                    data.rendererFeatures.Any(feature => feature is LoogaPostProcessingFeature))
                {
                    return true;
                }
            }
            return false;
        }
        #endregion
    }
}
