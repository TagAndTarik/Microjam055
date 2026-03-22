using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class DoorLeafInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private const int DefaultHoverMessageFontSize = 18;
    private const string DefaultHoverMessage = "Open door";

    [SerializeField] private DoorController doorController;
    [SerializeField] private InteractableOutline outline;
    [SerializeField, TextArea] private string hoverMessage = DefaultHoverMessage;
    [SerializeField] private Font hoverMessageFont;
    [SerializeField] private int hoverMessageFontSize = DefaultHoverMessageFontSize;
    [SerializeField] private Color hoverMessageColor = DefaultHoverMessageColor;

    private void Awake()
    {
        if (doorController == null)
            doorController = GetComponentInParent<DoorController>();

        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;
    }

    private void OnValidate()
    {
        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (outline == null)
            outline = GetComponent<InteractableOutline>();
    }

    public void Configure(DoorController controller, string message)
    {
        if (controller != null)
            doorController = controller;

        if (!string.IsNullOrWhiteSpace(message))
            hoverMessage = message.Trim();
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

    public string GetHoverMessage()
    {
        return string.IsNullOrWhiteSpace(hoverMessage) ? DefaultHoverMessage : hoverMessage;
    }

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused);
    }

    public void Interact(Transform interactor)
    {
        if (doorController == null)
            return;

        doorController.Toggle(interactor);
    }

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
