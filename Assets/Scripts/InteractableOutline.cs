using System.Collections.Generic;
using UnityEngine;

public class InteractableOutline : MonoBehaviour
{
    [SerializeField] private MeshRenderer[] targetRenderers;
    private float outlineScale = 1.03f;

    private static readonly Color OutlineColor = new Color(1f, 1f, 1f, .2f);
    private static Material sharedOutlineMaterial;
    private readonly List<GameObject> outlineObjects = new List<GameObject>();

    private void Awake()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<MeshRenderer>(true);

        EnsureOutlineObjects();
        SetOutlined(false);
    }

    public void SetOutlined(bool outlined)
    {
        for (int i = 0; i < outlineObjects.Count; i++)
            outlineObjects[i].SetActive(outlined);
    }

    private void EnsureOutlineObjects()
    {
        if (outlineObjects.Count > 0)
            return;

        Material outlineMaterial = GetOutlineMaterial();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            MeshRenderer sourceRenderer = targetRenderers[i];
            if (sourceRenderer == null)
                continue;

            MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                continue;

            GameObject outlineObject = new($"{sourceRenderer.gameObject.name}_Outline");
            outlineObject.hideFlags = HideFlags.HideInHierarchy;
            outlineObject.transform.SetParent(sourceRenderer.transform, false);
            outlineObject.transform.localPosition = Vector3.zero;
            outlineObject.transform.localRotation = Quaternion.identity;
            outlineObject.transform.localScale = Vector3.one * outlineScale;
            outlineObject.layer = sourceRenderer.gameObject.layer;

            MeshFilter outlineFilter = outlineObject.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMaterial;
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            outlineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            outlineRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            outlineRenderer.allowOcclusionWhenDynamic = false;

            outlineObjects.Add(outlineObject);
        }
    }

    private static Material GetOutlineMaterial()
    {
        if (sharedOutlineMaterial != null)
            return sharedOutlineMaterial;

        Shader shader = Shader.Find("Custom/InteractableOutline");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        sharedOutlineMaterial = new Material(shader)
        {
            name = "InteractableOutlineMaterial",
            enableInstancing = true
        };

        if (sharedOutlineMaterial.HasProperty("_BaseColor"))
            sharedOutlineMaterial.SetColor("_BaseColor", OutlineColor);

        if (sharedOutlineMaterial.HasProperty("_Color"))
            sharedOutlineMaterial.SetColor("_Color", OutlineColor);

        if (sharedOutlineMaterial.HasProperty("_Glossiness"))
            sharedOutlineMaterial.SetFloat("_Glossiness", 0f);

        if (sharedOutlineMaterial.HasProperty("_Metallic"))
            sharedOutlineMaterial.SetFloat("_Metallic", 0f);

        if (sharedOutlineMaterial.HasProperty("_EmissionColor"))
        {
            sharedOutlineMaterial.EnableKeyword("_EMISSION");
            sharedOutlineMaterial.SetColor("_EmissionColor", OutlineColor);
            sharedOutlineMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        return sharedOutlineMaterial;
    }

    public static Material GetSharedOutlineMaterial()
    {
        return GetOutlineMaterial();
    }
}
