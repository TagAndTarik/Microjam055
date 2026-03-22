using UnityEngine;

[DisallowMultipleComponent]
public class WarmLightInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private static readonly Color DefaultLightColor = new Color(1f, 0.73f, 0.48f, 1f);
    private const int DefaultHoverMessageFontSize = 18;

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
    [SerializeField] private bool castShadows;

    private bool isLit;

    private void Awake()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        targetLight = ResolveTargetLight();
        isLit = targetLight != null && targetLight.enabled;
    }

    private void OnValidate()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;
    }

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused && !isLit);
    }

    public string GetHoverMessage()
    {
        return isLit ? string.Empty : hoverMessage;
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
        if (isLit)
            return;

        targetLight = ResolveTargetLight();
        if (targetLight == null)
            targetLight = CreateTargetLight();

        if (targetLight == null)
            return;

        ConfigureLight(targetLight);
        targetLight.enabled = true;
        isLit = true;
        outline?.SetOutlined(false);
    }

    private Light ResolveTargetLight()
    {
        if (targetLight != null)
            return targetLight;

        targetLight = GetComponentInChildren<Light>(true);
        return targetLight;
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

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
