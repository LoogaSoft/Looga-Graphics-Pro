using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using LoogaSoft.Tonemapper.Runtime;
using LoogaSoft.Lighting.Editor;

namespace LoogaSoft.Tonemapper.Editor
{
    [CustomEditor(typeof(LoogaTonemapper))]
    public sealed class LoogaTonemapperEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_TonemapMode;
        SerializedDataParameter m_PreExposure;
        SerializedDataParameter m_PostExposure;
        
        SerializedDataParameter m_BlackPoint;
        SerializedDataParameter m_WhitePoint;
        SerializedDataParameter m_Contrast;
        SerializedDataParameter m_Saturation;
        
        SerializedDataParameter m_SigmoidCurve;
        SerializedDataParameter m_ReinhardLimit;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<LoogaTonemapper>(serializedObject);

            m_TonemapMode = Unpack(o.Find(x => x.tonemapMode));
            m_PreExposure = Unpack(o.Find(x => x.preExposure));
            m_PostExposure = Unpack(o.Find(x => x.postExposure));
            
            m_BlackPoint = Unpack(o.Find(x => x.blackPoint));
            m_WhitePoint = Unpack(o.Find(x => x.whitePoint));
            m_Contrast = Unpack(o.Find(x => x.contrast));
            m_Saturation = Unpack(o.Find(x => x.saturation));
            
            m_SigmoidCurve = Unpack(o.Find(x => x.sigmoidCurve));
            m_ReinhardLimit = Unpack(o.Find(x => x.reinhardLimit));
        }

        public override void OnInspectorGUI()
        {
            LoogaEditorBase.DrawLoogaSoftHeader();
            DrawUnityTonemapperWarning();

            PropertyField(m_TonemapMode);
            
            EditorGUILayout.Space();
            PropertyField(m_PreExposure);
            PropertyField(m_PostExposure);

            EditorGUILayout.Space();
            PropertyField(m_BlackPoint);
            PropertyField(m_WhitePoint);
            PropertyField(m_Contrast);
            PropertyField(m_Saturation);

            var currentMode = (LoogaTonemapMode)m_TonemapMode.value.enumValueIndex;

            if (currentMode == LoogaTonemapMode.Sigmoid || currentMode == LoogaTonemapMode.ReinhardExtended)
            {
                EditorGUILayout.Space();
                if (currentMode == LoogaTonemapMode.Sigmoid) PropertyField(m_SigmoidCurve);
                else if (currentMode == LoogaTonemapMode.ReinhardExtended) PropertyField(m_ReinhardLimit);
            }
        }

        private void DrawUnityTonemapperWarning()
        {
            if (!HasActiveUnityTonemapper())
                return;

            EditorGUILayout.HelpBox(
                "Unity Tonemapping is also active. Looga runs after URP post-processing, so both curves will be applied. " +
                "Set the Unity Tonemapping Mode to None for predictable Looga tonemapper results.",
                MessageType.Warning);
            EditorGUILayout.Space(2f);
        }

        private bool HasActiveUnityTonemapper()
        {
            string profilePath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(profilePath))
                return false;

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            return profile != null &&
                   profile.TryGet(out Tonemapping profileTonemapping) &&
                   profileTonemapping.active &&
                   profileTonemapping.mode.overrideState &&
                   profileTonemapping.mode.value != TonemappingMode.None;
        }
    }
}
