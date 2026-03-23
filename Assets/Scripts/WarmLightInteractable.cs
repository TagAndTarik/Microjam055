using UnityEngine;

[DisallowMultipleComponent]
public class WarmLightInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private static readonly Color DefaultLightColor = new Color(1f, 0.73f, 0.48f, 1f);
    private const int DefaultHoverMessageFontSize = 18;
    private const float DefaultPickupMessageDuration = 4.5f;

    [SerializeField] private InteractableOutline outline;
    [SerializeField] private Light targetLight;
    [SerializeField, TextArea] private string hoverMessage = "Light lamp";
    [SerializeField] private Font hoverMessageFont;
    [SerializeField] private int hoverMessageFontSize = 18;
    [SerializeField] private Color hoverMessageColor = new Color(1f, 1f, 1f, 0.95f);

    [Header("Light")]
    [SerializeField] private Vector3 lightLocalPosition = new Vector3(0f, 0.7f, 0f);
    [SerializeField] private Color lightColor = new Color(1f, 0.73f, 0.48f, 1f);
    [SerializeField, Min(0f)] private float lightIntensity = 1.6f;
    [SerializeField, Min(0f)] private float lightRange = 4.5f;
    [SerializeField] private bool castShadows = true;

    [Header("Progression")]
    [SerializeField] private bool requireBedInteraction;
    [SerializeField] private bool enablePickupAfterLighting = true;
    [SerializeField] private PickupInteractable pickupInteractable;

    [Header("Pickup Message")]
    [SerializeField, TextArea] private string postPickupMessage = "This lamp is going to run out of oil fast. I better start looking for more.";
    [SerializeField] private float postPickupMessageDuration = 4.5f;
    [SerializeField] private bool showPickupMessageOnlyOnce = true;

    [Header("Lamp Glass")]
    [SerializeField] private string glassPartName = "Cube.004";
    [SerializeField] private Material unlitGlassMaterial;

    private bool isLit;
    private bool hasShownPickupMessage;
    private Renderer glassPartRenderer;
    private Material[] litGlassPartMaterials;

    private void Awake()
    {
        EnsureLampSwingPhysics();

        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (postPickupMessageDuration <= 0f)
            postPickupMessageDuration = DefaultPickupMessageDuration;

        pickupInteractable = ResolvePickupInteractable();
        targetLight = ResolveTargetLight();
        isLit = targetLight != null && targetLight.enabled;
        CaptureLitGlassPartMaterials();
        ApplyLampGlassState();
        UpdatePickupAvailability();
    }

    private void OnValidate()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (postPickupMessageDuration <= 0f)
            postPickupMessageDuration = DefaultPickupMessageDuration;

        if (pickupInteractable == null)
            pickupInteractable = GetComponent<PickupInteractable>();
    }

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused && !isLit && CanLightLamp());
    }

    public string GetHoverMessage()
    {
        return isLit || !CanLightLamp() ? string.Empty : hoverMessage;
    }

    public Font GetHoverMessageFont()
    {
        return hoverMessageFont;
    }

    public int GetHoverMessageFontSize()
    {
        return hoverMessageFontSize > 0 ? hoverMessageFontSize : DefaultHoverMessageFontSize;
    }

    public Color GetHoverMessageColor()
    {
        return IsUnsetColor(hoverMessageColor) ? DefaultHoverMessageColor : hoverMessageColor;
    }

    public void Interact(Transform interactor)
    {
        if (isLit || !CanLightLamp())
            return;

        targetLight = ResolveTargetLight();
        if (targetLight == null)
            targetLight = CreateTargetLight();

        if (targetLight == null)
            return;

        ConfigureLight(targetLight);
        targetLight.enabled = true;
        isLit = true;
        ApplyLampGlassState();
        outline?.SetOutlined(false);
        EnablePickupIfConfigured();
    }

    public void TryShowPickupMessage(Transform interactor)
    {
        if (string.IsNullOrWhiteSpace(postPickupMessage))
            return;

        if (showPickupMessageOnlyOnce && hasShownPickupMessage)
            return;

        SimpleFirstPersonController controller = ResolvePlayerController(interactor);
        if (controller == null)
            return;

        controller.ShowPlayerMessage(postPickupMessage, postPickupMessageDuration);
        hasShownPickupMessage = true;
    }

    private Light ResolveTargetLight()
    {
        if (targetLight != null)
            return targetLight;

        targetLight = GetComponentInChildren<Light>(true);
        return targetLight;
    }

    private PickupInteractable ResolvePickupInteractable()
    {
        if (pickupInteractable != null)
            return pickupInteractable;

        pickupInteractable = GetComponent<PickupInteractable>();
        return pickupInteractable;
    }

    private Renderer ResolveGlassPartRenderer()
    {
        if (glassPartRenderer != null)
            return glassPartRenderer;

        if (string.IsNullOrWhiteSpace(glassPartName))
            return null;

        Transform glassPartTransform = FindDescendantByName(transform, glassPartName.Trim());
        if (glassPartTransform == null)
            return null;

        glassPartRenderer = glassPartTransform.GetComponent<Renderer>();
        return glassPartRenderer;
    }

    private void CaptureLitGlassPartMaterials()
    {
        Renderer targetRenderer = ResolveGlassPartRenderer();
        if (targetRenderer == null)
            return;

        Material[] currentMaterials = targetRenderer.sharedMaterials;
        if (currentMaterials == null || currentMaterials.Length == 0)
            return;

        litGlassPartMaterials = (Material[])currentMaterials.Clone();
    }

    private void ApplyLampGlassState()
    {
        Renderer targetRenderer = ResolveGlassPartRenderer();
        if (targetRenderer == null)
            return;

        if (isLit)
        {
            if (litGlassPartMaterials != null && litGlassPartMaterials.Length > 0)
                targetRenderer.sharedMaterials = (Material[])litGlassPartMaterials.Clone();

            return;
        }

        if (unlitGlassMaterial == null)
            return;

        int materialCount = Mathf.Max(1, targetRenderer.sharedMaterials.Length);
        Material[] glassMaterials = new Material[materialCount];
        for (int i = 0; i < glassMaterials.Length; i++)
            glassMaterials[i] = unlitGlassMaterial;

        targetRenderer.sharedMaterials = glassMaterials;
    }

    private Light CreateTargetLight()
    {
        GameObject lightObject = new GameObject("Warm Lamp Light");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = lightLocalPosition;
        lightObject.transform.localRotation = Quaternion.identity;

        Light createdLight = lightObject.AddComponent<Light>();
        createdLight.enabled = false;
        return createdLight;
    }

    private void ConfigureLight(Light lightToConfigure)
    {
        lightToConfigure.type = LightType.Point;
        lightToConfigure.color = IsUnsetColor(lightColor) ? DefaultLightColor : lightColor;
        lightToConfigure.intensity = Mathf.Max(0f, lightIntensity);
        lightToConfigure.range = Mathf.Max(0f, lightRange);
        lightToConfigure.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        lightToConfigure.renderMode = LightRenderMode.Auto;
        lightToConfigure.bounceIntensity = 1f;
    }

    private bool CanLightLamp()
    {
        return !requireBedInteraction || LightsOffInteractable.HasTriggeredAnyBedInteraction;
    }

    private void UpdatePickupAvailability()
    {
        PickupInteractable pickup = ResolvePickupInteractable();
        if (pickup == null)
            return;

        if (enablePickupAfterLighting && isLit)
        {
            pickup.enabled = true;
            enabled = false;
            return;
        }

        pickup.enabled = false;
    }

    private void EnablePickupIfConfigured()
    {
        PickupInteractable pickup = ResolvePickupInteractable();
        if (!enablePickupAfterLighting || pickup == null)
            return;

        pickup.enabled = true;
        enabled = false;
    }

    private void EnsureLampSwingPhysics()
    {
        if (GetComponent<HeldLampPlanePhysics>() != null)
            return;

        if (FindDescendantByName(transform, "Plane.005") == null ||
            FindDescendantByName(transform, "hinge") == null)
        {
            return;
        }

        gameObject.AddComponent<HeldLampPlanePhysics>();
    }

    private static SimpleFirstPersonController ResolvePlayerController(Transform interactor)
    {
        return interactor != null
            ? interactor.GetComponentInParent<SimpleFirstPersonController>()
            : FindObjectOfType<SimpleFirstPersonController>();
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, targetName, System.StringComparison.Ordinal))
                return child;

            Transform descendant = FindDescendantByName(child, targetName);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
