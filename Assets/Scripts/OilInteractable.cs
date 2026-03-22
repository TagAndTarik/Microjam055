using UnityEngine;

[DisallowMultipleComponent]
public class OilInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private const int DefaultHoverMessageFontSize = 18;
    private const float DefaultPostInteractMessageDuration = 2f;

    [SerializeField] private InteractableOutline outline;
    [SerializeField, TextArea] private string hoverMessage = "Refill lamp";
    [SerializeField, TextArea] private string postInteractMessage = "The lamp burns brighter.";
    [SerializeField] private float postInteractMessageDuration = 2f;
    [SerializeField] private Font hoverMessageFont;
    [SerializeField] private int hoverMessageFontSize = 18;
    [SerializeField] private Color hoverMessageColor = new Color(1f, 1f, 1f, 0.95f);

    [Header("Progression")]
    [SerializeField] private bool revealAfterBedInteraction = true;
    [SerializeField] private bool requireLampHeld = true;

    private Collider[] colliders;
    private Renderer[] renderers;
    private bool[] originalColliderStates;
    private bool[] originalRendererStates;
    private bool isRevealed;
    private SimpleFirstPersonController cachedController;

    private void Awake()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (postInteractMessageDuration <= 0f)
            postInteractMessageDuration = DefaultPostInteractMessageDuration;

        CacheComponents();
        CaptureOriginalState();

        isRevealed = !revealAfterBedInteraction || LightsOffInteractable.HasTriggeredAnyBedInteraction;
        ApplyRevealState(isRevealed);
    }

    private void Update()
    {
        if (isRevealed || !revealAfterBedInteraction || !LightsOffInteractable.HasTriggeredAnyBedInteraction)
            return;

        isRevealed = true;
        ApplyRevealState(true);
    }

    private void OnValidate()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (postInteractMessageDuration <= 0f)
            postInteractMessageDuration = DefaultPostInteractMessageDuration;
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
        return CanInteract() ? hoverMessage : string.Empty;
    }

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused && CanInteract());
    }

    public void Interact(Transform interactor)
    {
        if (!CanInteract())
            return;

        SimpleFirstPersonController controller = ResolvePlayerController(interactor);
        if (controller == null || !controller.ResetLampBrightnessTimer())
            return;

        if (!string.IsNullOrWhiteSpace(postInteractMessage))
            controller.ShowPlayerMessage(postInteractMessage, postInteractMessageDuration);

        outline?.SetOutlined(false);
        gameObject.SetActive(false);
    }

    private void CacheComponents()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void CaptureOriginalState()
    {
        originalColliderStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
            originalColliderStates[i] = colliders[i].enabled;

        originalRendererStates = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalRendererStates[i] = renderers[i].enabled;
    }

    private void ApplyRevealState(bool revealed)
    {
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = revealed && originalColliderStates[i];

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = revealed && originalRendererStates[i];

        if (!revealed)
            outline?.SetOutlined(false);
    }

    private bool CanInteract()
    {
        if (!isRevealed)
            return false;

        if (!requireLampHeld)
            return true;

        SimpleFirstPersonController controller = ResolvePlayerController();
        return controller != null && controller.IsLampHeld();
    }

    private SimpleFirstPersonController ResolvePlayerController(Transform interactor = null)
    {
        if (interactor != null)
        {
            cachedController = interactor.GetComponentInParent<SimpleFirstPersonController>();
            if (cachedController != null)
                return cachedController;
        }

        if (cachedController == null)
            cachedController = FindObjectOfType<SimpleFirstPersonController>();

        return cachedController;
    }

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
