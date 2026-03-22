using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

[InitializeOnLoad]
internal static class BuiltInAmbientOcclusionSetup
{
    private const string PlayerPrefabPath = "Assets/Resources/Player.prefab";
    private const string ProfilePath = "Assets/Settings/BuiltInAmbientOcclusionProfile.asset";
    private const string PostProcessResourcesPath = "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset";

    static BuiltInAmbientOcclusionSetup()
    {
        EditorApplication.delayCall += EnsureSetup;
    }

    [MenuItem("Tools/Rendering/Ensure Built-in Ambient Occlusion Setup")]
    private static void EnsureSetupMenu()
    {
        EnsureSetup();
    }

    private static void EnsureSetup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || Application.isPlaying)
        {
            EditorApplication.delayCall += EnsureSetup;
            return;
        }

        PostProcessResources resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(PostProcessResourcesPath);
        if (resources == null)
            return;

        bool changed = false;
        PostProcessProfile profile = EnsureProfile(ref changed);
        if (profile == null)
            return;

        changed |= EnsurePlayerPrefab(profile, resources);

        if (!changed)
            return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Built-in ambient occlusion setup updated.");
    }

    private static PostProcessProfile EnsureProfile(ref bool changed)
    {
        PostProcessProfile profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            profile.name = "BuiltInAmbientOcclusionProfile";
            AssetDatabase.CreateAsset(profile, ProfilePath);
            changed = true;
        }

        AmbientOcclusion ambientOcclusion = profile.GetSetting<AmbientOcclusion>();
        if (ambientOcclusion == null)
        {
            ambientOcclusion = profile.AddSettings<AmbientOcclusion>();
            ambientOcclusion.name = nameof(AmbientOcclusion);
            ambientOcclusion.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(ambientOcclusion, profile);
            changed = true;
        }

        bool localChanged = false;
        localChanged |= SetValue(ref ambientOcclusion.active, true);
        localChanged |= SetParameter(ambientOcclusion.enabled, true);
        localChanged |= SetParameter(ambientOcclusion.mode, AmbientOcclusionMode.ScalableAmbientObscurance);
        localChanged |= SetParameter(ambientOcclusion.intensity, 1f);
        localChanged |= SetParameter(ambientOcclusion.color, Color.black);
        localChanged |= SetParameter(ambientOcclusion.ambientOnly, false);
        localChanged |= SetParameter(ambientOcclusion.radius, 0.35f);
        localChanged |= SetParameter(ambientOcclusion.quality, AmbientOcclusionQuality.Medium);
        localChanged |= SetParameter(ambientOcclusion.thicknessModifier, 1f);
        localChanged |= SetParameter(ambientOcclusion.zBias, 0.0001f);

        if (localChanged)
        {
            EditorUtility.SetDirty(ambientOcclusion);
            EditorUtility.SetDirty(profile);
            changed = true;
        }

        return profile;
    }

    private static bool EnsurePlayerPrefab(PostProcessProfile profile, PostProcessResources resources)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        bool changed = false;

        try
        {
            GameObject playerRoot = prefabRoot;
            Camera playerCamera = prefabRoot.GetComponentInChildren<Camera>(true);
            if (playerRoot == null || playerCamera == null)
                return false;

            PostProcessLayer layer = playerCamera.GetComponent<PostProcessLayer>();
            if (layer == null)
            {
                layer = playerCamera.gameObject.AddComponent<PostProcessLayer>();
                changed = true;
            }

            layer.Init(resources);

            changed |= SetValue(ref layer.volumeTrigger, playerCamera.transform);
            changed |= SetValue(ref layer.volumeLayer, (LayerMask)(1 << 0));
            changed |= SetValue(ref layer.stopNaNPropagation, true);
            changed |= SetValue(ref layer.finalBlitToCameraTarget, false);
            changed |= SetValue(ref layer.antialiasingMode, PostProcessLayer.Antialiasing.None);

            SerializedObject layerObject = new SerializedObject(layer);
            SerializedProperty resourcesProperty = layerObject.FindProperty("m_Resources");
            if (resourcesProperty != null && resourcesProperty.objectReferenceValue != resources)
            {
                resourcesProperty.objectReferenceValue = resources;
                layerObject.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            PostProcessVolume volume = playerRoot.GetComponent<PostProcessVolume>();
            if (volume == null)
            {
                volume = playerRoot.AddComponent<PostProcessVolume>();
                changed = true;
            }

            changed |= SetValue(ref volume.sharedProfile, profile);
            changed |= SetValue(ref volume.isGlobal, true);
            changed |= SetValue(ref volume.blendDistance, 0f);
            changed |= SetValue(ref volume.weight, 1f);
            changed |= SetValue(ref volume.priority, 0f);

            if (changed)
            {
                EditorUtility.SetDirty(layer);
                EditorUtility.SetDirty(volume);
                EditorUtility.SetDirty(playerRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        return changed;
    }

    private static bool SetParameter<T>(ParameterOverride<T> parameter, T value, bool overrideState = true)
    {
        bool changed = parameter.overrideState != overrideState ||
                       !EqualityComparer<T>.Default.Equals(parameter.value, value);
        if (!changed)
            return false;

        parameter.overrideState = overrideState;
        parameter.value = value;
        return true;
    }

    private static bool SetValue<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        return true;
    }
}
