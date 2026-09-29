using System.Linq;
using LoogaSoft.Lighting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#pragma warning disable CS0618 // The migration reads the obsolete LoogaTonemapper on purpose.
using LegacyTonemapper = LoogaSoft.Tonemapper.Runtime.LoogaTonemapper;

namespace LoogaSoft.PostProcessing.Editor
{
    /// <summary>
    /// Converts former Looga Tonemapper overrides to Looga Post Processing overrides.
    /// It also adds the Looga Post Processing feature to renderers that used the Looga Lighting feature for tonemapping.
    /// </summary>
    public static class LoogaPostProcessingMigration
    {
        private const string MenuPath = "Tools/LoogaSoft/Migrate Tonemapper To Post Processing";

        [MenuItem(MenuPath)]
        private static void MigrateFromMenu()
        {
            int profiles = MigrateProfiles();
            int renderers = AddFeatureToRenderers();
            Debug.Log("Looga Post Processing migration: " + profiles + " volume profiles and " + renderers +
                " renderers changed.");
        }

        /// <summary>Migrates every volume profile under Assets.</summary>
        /// <returns>The number of changed profiles.</returns>
        public static int MigrateProfiles()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets" }))
            {
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile != null && MigrateProfile(profile)) changed++;
            }
            return changed;
        }

        /// <summary>
        /// Moves the values of a Looga Tonemapper override into a Looga Post Processing override.
        /// Then it removes the Looga Tonemapper override. Existing bloom values stay unchanged.
        /// </summary>
        /// <returns>True when the profile had a Looga Tonemapper override.</returns>
        public static bool MigrateProfile(VolumeProfile profile)
        {
            LegacyTonemapper legacy = profile.components.OfType<LegacyTonemapper>().FirstOrDefault();
            if (legacy == null) return false;

            bool persistent = EditorUtility.IsPersistent(profile);
            if (!profile.TryGet(out LoogaPostProcessing settings))
            {
                settings = profile.Add<LoogaPostProcessing>();
                if (persistent)
                {
                    settings.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(settings, profile);
                }
            }

            settings.active = legacy.active;
            Copy(legacy.tonemapMode, settings.tonemapMode);
            Copy(legacy.preExposure, settings.preExposure);
            Copy(legacy.postExposure, settings.postExposure);
            Copy(legacy.blackPoint, settings.blackPoint);
            Copy(legacy.whitePoint, settings.whitePoint);
            Copy(legacy.contrast, settings.contrast);
            Copy(legacy.saturation, settings.saturation);
            Copy(legacy.sigmoidCurve, settings.sigmoidCurve);
            Copy(legacy.reinhardLimit, settings.reinhardLimit);

            profile.components.Remove(legacy);
            if (persistent) AssetDatabase.RemoveObjectFromAsset(legacy);
            Object.DestroyImmediate(legacy, true);
            profile.isDirty = true;
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(profile);
            if (persistent) AssetDatabase.SaveAssetIfDirty(profile);
            return true;
        }

        /// <summary>
        /// Adds the Looga Post Processing feature to every URP renderer under Assets that has the Looga Lighting feature.
        /// </summary>
        /// <returns>The number of changed renderers.</returns>
        public static int AddFeatureToRenderers()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || !data.rendererFeatures.Any(feature => feature is LoogaLightingFeature)) continue;
                if (data.rendererFeatures.Any(feature => feature is LoogaPostProcessingFeature)) continue;
                AddFeature(data);
                changed++;
            }
            return changed;
        }

        /// <summary>Adds a Looga Post Processing feature to a renderer asset.</summary>
        public static LoogaPostProcessingFeature AddFeature(ScriptableRendererData data)
        {
            var feature = ScriptableObject.CreateInstance<LoogaPostProcessingFeature>();
            feature.name = "Looga Post Processing";
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            // The renderer data keeps the feature list and a map of the feature file identifiers.
            var serialized = new SerializedObject(data);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            return feature;
        }

        private static void Copy<T>(VolumeParameter<T> source, VolumeParameter<T> destination)
        {
            destination.value = source.value;
            destination.overrideState = source.overrideState;
        }
    }
}
