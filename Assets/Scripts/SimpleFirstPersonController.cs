using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class SimpleFirstPersonController : MonoBehaviour
{
    private const string CrosshairCanvasName = "Runtime Crosshair";
    private const string HorizontalCrosshairName = "Horizontal";
    private const string VerticalCrosshairName = "Vertical";
    private const string HoverPromptName = "Hover Prompt";
    private const string PlayerMessageName = "Player Message";
    private const int DefaultHoverPromptFontSize = 18;
    private const int DefaultPlayerMessageFontSize = 22;
    private static readonly Color DefaultHoverPromptColor = new Color(1f, 1f, 1f, 0.95f);
    private static readonly Color DefaultPlayerMessageColor = new Color(1f, 1f, 1f, 0.96f);

    [Header("References")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Camera playerCamera;
    private PlayerManager playerManagerScript;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float maxLookAngle = 80f;
    [SerializeField] private float cameraNearClipPlane = 0.03f;

    [Header("Interaction")]
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private LayerMask interactMask = ~0;
    [SerializeField] private float interactCastRadius = 0.08f;
    [SerializeField] private bool logInteractDebug = true;

    [Header("Crosshair")]
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private float crosshairSize = 12f;
    [SerializeField] private float crosshairThickness = 2f;
    [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);

    [Header("Hover Prompt")]
    [SerializeField] private bool showHoverPrompt = true;
    [SerializeField] private float hoverPromptOffsetY = -28f;
    [SerializeField] private Vector2 hoverPromptSize = new Vector2(320f, 44f);

    [Header("Player Message")]
    [SerializeField] private bool showPlayerMessages = true;
    [SerializeField] private float defaultPlayerMessageDuration = 4.5f;
    [SerializeField] private float playerMessageOffsetY = -74f;
    [SerializeField] private Vector2 playerMessageSize = new Vector2(440f, 72f);
    [SerializeField] private int playerMessageFontSize = DefaultPlayerMessageFontSize;
    [SerializeField] private Color playerMessageColor = DefaultPlayerMessageColor;

    private CharacterController controller;
    private IInteractable currentInteractable;
    private Canvas crosshairCanvas;
    private Image horizontalCrosshair;
    private Image verticalCrosshair;
    private Text hoverPrompt;
    private Text playerMessage;
    private Font defaultHoverPromptFont;
    private readonly RaycastHit[] interactHitBuffer = new RaycastHit[16];
    private float verticalVelocity;
    private float pitch;
    private float playerMessageHideTime;
    private float defaultFarClipPlane = 1000f;
    private string activePlayerMessage = string.Empty;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera != null)
        {
            playerCamera.nearClipPlane = Mathf.Max(0.01f, cameraNearClipPlane);
            defaultFarClipPlane = Mathf.Max(playerCamera.nearClipPlane + 0.01f, playerCamera.farClipPlane);
        }
        playerManagerScript = GetComponent<PlayerManager>();    
        EnsureCrosshair();
        ApplyCrosshairStyle();
        SetCrosshairVisible(showCrosshair);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnEnable()
    {
        EnsureCrosshair();
        ApplyCrosshairStyle();
        SetCrosshairVisible(showCrosshair);
    }

    private void Update()
    {
        Look();
        UpdateInteraction();
        UpdatePlayerMessage();
        Move();
    }

    private void OnDisable()
    {
        ClearCurrentInteractable();
        SetCrosshairVisible(false);
    }

    private void OnValidate()
    {
        interactDistance = Mathf.Max(0.1f, interactDistance);
        interactCastRadius = Mathf.Max(0f, interactCastRadius);
        crosshairSize = Mathf.Max(1f, crosshairSize);
        crosshairThickness = Mathf.Max(1f, crosshairThickness);
        hoverPromptSize.x = Mathf.Max(1f, hoverPromptSize.x);
        hoverPromptSize.y = Mathf.Max(1f, hoverPromptSize.y);
        playerMessageSize.x = Mathf.Max(1f, playerMessageSize.x);
        playerMessageSize.y = Mathf.Max(1f, playerMessageSize.y);
        playerMessageFontSize = Mathf.Max(1, playerMessageFontSize);
        defaultPlayerMessageDuration = Mathf.Max(0.1f, defaultPlayerMessageDuration);

        ApplyCrosshairStyle();
        SetCrosshairVisible(showCrosshair);
    }

    public void ShowPlayerMessage(string message, float duration = -1f)
    {
        EnsureCrosshair();
        ApplyCrosshairStyle();

        activePlayerMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        playerMessageHideTime = Time.unscaledTime + (duration > 0f ? duration : defaultPlayerMessageDuration);
        RefreshPlayerMessage();
    }

    public void ApplyVisibilityLimit(
        float maxVisibleDistance,
        float fogStartDistance,
        Color fogColor,
        float vignetteIntensity = 0f,
        float vignetteSmoothness = 0.85f)
    {
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        float nearClip = Mathf.Max(0.01f, cameraNearClipPlane);
        float clampedMaxDistance = Mathf.Max(nearClip + 0.01f, maxVisibleDistance);
        float clampedFogStart = Mathf.Clamp(fogStartDistance, 0f, Mathf.Max(0f, clampedMaxDistance - 0.01f));

        if (playerCamera != null)
        {
            playerCamera.nearClipPlane = nearClip;
            // Keep the original far clip so the darkness comes from fog, not from a flat cut plane.
            playerCamera.farClipPlane = Mathf.Max(defaultFarClipPlane, clampedMaxDistance);
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = clampedFogStart;
        RenderSettings.fogEndDistance = clampedMaxDistance;

        ApplyVisibilityVignette(fogColor, vignetteIntensity, vignetteSmoothness);
    }

    private void Look()
    {
        if (Mouse.current == null || cameraPivot == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        pitch -= mouseDelta.y;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        transform.Rotate(Vector3.up * mouseDelta.x);
    }

    private void Move()
    {
        Vector2 input = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed) input.y -= 1f;
            if (Keyboard.current.aKey.isPressed) input.x -= 1f;
            if (Keyboard.current.dKey.isPressed) input.x += 1f;
        }

        input = input.normalized;

        Vector3 move = (transform.right * input.x + transform.forward * input.y) * moveSpeed;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame &&
            controller.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);
    }

    private void UpdateInteraction()
    {
        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        IInteractable nextInteractable = FindInteractableTarget(ray);
        bool interactPressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

        if (!ReferenceEquals(nextInteractable, currentInteractable))
        {
            currentInteractable?.SetFocused(false);
            currentInteractable = nextInteractable;
            currentInteractable?.SetFocused(true);
        }

        UpdateHoverPrompt();

        if (interactPressed)
        {
            LogInteractDebug(ray);
            currentInteractable?.Interact(transform);
            UpdateHoverPrompt();
        }
    }

    private void ClearCurrentInteractable()
    {
        currentInteractable?.SetFocused(false);
        currentInteractable = null;
        UpdateHoverPrompt();
    }

    private static IInteractable FindInteractable(Collider hitCollider)
    {
        MonoBehaviour[] behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] == null || !behaviours[i].isActiveAndEnabled)
                continue;

            if (behaviours[i] is IInteractable interactable)
                return interactable;
        }

        return null;
    }

    private IInteractable FindInteractableTarget(Ray ray)
    {
        if (TryGetDirectInteractable(ray, out IInteractable interactable))
            return interactable;

        return FindOverlappingInteractable(ray);
    }

    private IInteractable FindOverlappingInteractable(Ray ray)
    {
        if (interactCastRadius <= 0f)
            return null;

        float closeProbeDistance = Mathf.Max(interactCastRadius * 3f, 0.2f);
        Vector3 capsuleEnd = ray.origin + ray.direction * closeProbeDistance;
        Collider[] colliders = Physics.OverlapCapsule(ray.origin, capsuleEnd, interactCastRadius, interactMask, QueryTriggerInteraction.Collide);
        IInteractable bestInteractable = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            IInteractable interactable = FindInteractable(collider);

            if (interactable == null)
                continue;

            Vector3 targetPoint = collider.ClosestPoint(ray.origin);
            if ((targetPoint - ray.origin).sqrMagnitude <= 0.0001f)
                targetPoint = collider.bounds.center;

            Vector3 toTarget = targetPoint - ray.origin;
            float sqrDistance = toTarget.sqrMagnitude;

            if (sqrDistance <= 0.0001f || sqrDistance > interactDistance * interactDistance)
                continue;

            float alignment = Vector3.Dot(ray.direction, toTarget.normalized);
            if (alignment <= 0.35f)
                continue;

            if (!HasLineOfSight(ray.origin, targetPoint, interactable))
                continue;

            float forwardDistance = Vector3.Dot(ray.direction, toTarget);
            if (forwardDistance < -interactCastRadius || forwardDistance > closeProbeDistance + interactCastRadius)
                continue;

            float score = alignment / Mathf.Max(0.01f, sqrDistance);
            if (score > bestScore)
            {
                bestScore = score;
                bestInteractable = interactable;
            }
        }

        return bestInteractable;
    }

    private bool TryGetDirectInteractable(Ray ray, out IInteractable interactable)
    {
        interactable = null;

        if (TryGetClosestNonSelfRayHit(ray, interactDistance, out RaycastHit hit))
        {
            interactable = FindInteractable(hit.collider);
            return interactable != null;
        }

        if (interactCastRadius <= 0f)
            return false;

        if (!TryGetClosestNonSelfSphereHit(ray, interactDistance, out hit))
            return false;

        interactable = FindInteractable(hit.collider);
        return interactable != null;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 targetPoint, IInteractable interactable)
    {
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (distance <= 0.0001f)
            return true;

        Ray ray = new Ray(origin, direction / distance);
        if (!TryGetClosestNonSelfRayHit(ray, distance, out RaycastHit hit))
            return true;

        return ReferenceEquals(FindInteractable(hit.collider), interactable);
    }

    private bool TryGetClosestNonSelfRayHit(Ray ray, float maxDistance, out RaycastHit closestHit)
    {
        int hitCount = Physics.RaycastNonAlloc(ray, interactHitBuffer, maxDistance, interactMask, QueryTriggerInteraction.Collide);
        return TryGetClosestNonSelfHit(hitCount, out closestHit);
    }

    private bool TryGetClosestNonSelfSphereHit(Ray ray, float maxDistance, out RaycastHit closestHit)
    {
        int hitCount = Physics.SphereCastNonAlloc(ray, interactCastRadius, interactHitBuffer, maxDistance, interactMask, QueryTriggerInteraction.Collide);
        return TryGetClosestNonSelfHit(hitCount, out closestHit);
    }

    private bool TryGetClosestNonSelfHit(int hitCount, out RaycastHit closestHit)
    {
        closestHit = default;
        float closestDistance = float.PositiveInfinity;
        bool foundHit = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = interactHitBuffer[i];

            if (candidate.collider == null || IsSelfCollider(candidate.collider))
                continue;

            if (candidate.distance >= closestDistance)
                continue;

            closestDistance = candidate.distance;
            closestHit = candidate;
            foundHit = true;
        }

        return foundHit;
    }

    private bool IsSelfCollider(Collider collider)
    {
        return collider != null && collider.transform.IsChildOf(transform);
    }

    private void LogInteractDebug(Ray ray)
    {
        if (!logInteractDebug)
            return;

        string directHitDescription = DescribeRaycastHit(ray, interactCastRadius <= 0f);
        string wideHitDescription = interactCastRadius > 0f
            ? DescribeSphereCastHit(ray)
            : "disabled";

        Debug.Log(
            $"Interact debug. Direct ray: {directHitDescription}. Wide cast: {wideHitDescription}. " +
            $"Focused interactable: {DescribeInteractable(currentInteractable)}.",
            this);
    }

    private string DescribeRaycastHit(Ray ray, bool appendRadiusInfo)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Collide))
            return "no hit";

        string description = DescribeHit(hit);
        if (!appendRadiusInfo)
            return description;

        return $"{description} (sphere cast disabled)";
    }

    private string DescribeSphereCastHit(Ray ray)
    {
        if (!Physics.SphereCast(ray, interactCastRadius, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Collide))
            return "no hit";

        return DescribeHit(hit);
    }

    private static string DescribeHit(RaycastHit hit)
    {
        IInteractable interactable = FindInteractable(hit.collider);
        string layerName = LayerMask.LayerToName(hit.collider.gameObject.layer);
        if (string.IsNullOrEmpty(layerName))
            layerName = hit.collider.gameObject.layer.ToString();

        return $"{hit.collider.name} at {hit.distance:0.00}m " +
               $"(layer {layerName}, trigger {hit.collider.isTrigger}, " +
               $"interactable {DescribeInteractable(interactable)})";
    }

    private static string DescribeInteractable(IInteractable interactable)
    {
        if (interactable == null)
            return "none";

        if (interactable is Component component)
            return component.name;

        return interactable.GetType().Name;
    }

    private void EnsureCrosshair()
    {
        if (!showCrosshair && crosshairCanvas == null)
            return;

        if (crosshairCanvas == null)
        {
            Transform existingCanvas = transform.Find(CrosshairCanvasName);
            if (existingCanvas != null)
                crosshairCanvas = existingCanvas.GetComponent<Canvas>();
        }

        if (crosshairCanvas == null)
        {
            GameObject canvasObject = new GameObject(CrosshairCanvasName, typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);

            crosshairCanvas = canvasObject.GetComponent<Canvas>();
            crosshairCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            crosshairCanvas.sortingOrder = 100;

            RectTransform canvasRect = crosshairCanvas.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
        }

        if (horizontalCrosshair == null)
        {
            Transform existingHorizontal = crosshairCanvas.transform.Find(HorizontalCrosshairName);
            horizontalCrosshair = existingHorizontal != null
                ? existingHorizontal.GetComponent<Image>()
                : CreateCrosshairLine(HorizontalCrosshairName, crosshairCanvas.transform);
        }

        if (verticalCrosshair == null)
        {
            Transform existingVertical = crosshairCanvas.transform.Find(VerticalCrosshairName);
            verticalCrosshair = existingVertical != null
                ? existingVertical.GetComponent<Image>()
                : CreateCrosshairLine(VerticalCrosshairName, crosshairCanvas.transform);
        }

        if (hoverPrompt == null)
        {
            Transform existingPrompt = crosshairCanvas.transform.Find(HoverPromptName);
            hoverPrompt = existingPrompt != null
                ? existingPrompt.GetComponent<Text>()
                : CreateHoverPrompt(HoverPromptName, crosshairCanvas.transform);
        }

        if (playerMessage == null)
        {
            Transform existingPlayerMessage = crosshairCanvas.transform.Find(PlayerMessageName);
            playerMessage = existingPlayerMessage != null
                ? existingPlayerMessage.GetComponent<Text>()
                : CreatePlayerMessage(PlayerMessageName, crosshairCanvas.transform);
        }
    }

    private void ApplyCrosshairStyle()
    {
        if (crosshairCanvas == null || horizontalCrosshair == null || verticalCrosshair == null || hoverPrompt == null || playerMessage == null)
            return;

        RectTransform horizontalRect = (RectTransform)horizontalCrosshair.transform;
        horizontalRect.sizeDelta = new Vector2(crosshairSize, crosshairThickness);
        horizontalRect.anchoredPosition = Vector2.zero;

        RectTransform verticalRect = (RectTransform)verticalCrosshair.transform;
        verticalRect.sizeDelta = new Vector2(crosshairThickness, crosshairSize);
        verticalRect.anchoredPosition = Vector2.zero;

        horizontalCrosshair.color = crosshairColor;
        verticalCrosshair.color = crosshairColor;

        RectTransform promptRect = (RectTransform)hoverPrompt.transform;
        promptRect.sizeDelta = hoverPromptSize;
        promptRect.anchoredPosition = new Vector2(0f, hoverPromptOffsetY);

        RectTransform playerMessageRect = (RectTransform)playerMessage.transform;
        playerMessageRect.sizeDelta = playerMessageSize;
        playerMessageRect.anchoredPosition = new Vector2(0f, playerMessageOffsetY);

        playerMessage.fontSize = Mathf.Max(1, playerMessageFontSize);
        playerMessage.color = playerMessageColor;
    }

    private void SetCrosshairVisible(bool isVisible)
    {
        if (crosshairCanvas != null)
            crosshairCanvas.gameObject.SetActive(isVisible);
    }

    private static Image CreateCrosshairLine(string objectName, Transform parent)
    {
        GameObject lineObject = new GameObject(objectName, typeof(Image));
        lineObject.transform.SetParent(parent, false);

        RectTransform rectTransform = lineObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        Image image = lineObject.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void UpdateHoverPrompt()
    {
        if (hoverPrompt == null)
            return;

        Font font = GetDefaultHoverPromptFont();
        int fontSize = DefaultHoverPromptFontSize;
        Color color = DefaultHoverPromptColor;
        string hoverMessage = string.Empty;

        if (showHoverPrompt && currentInteractable != null)
        {
            hoverMessage = currentInteractable.GetHoverMessage();
            font = currentInteractable.GetHoverMessageFont() ?? font;
            fontSize = Mathf.Max(1, currentInteractable.GetHoverMessageFontSize());
            color = currentInteractable.GetHoverMessageColor();
        }

        hoverPrompt.text = string.IsNullOrWhiteSpace(hoverMessage) ? string.Empty : hoverMessage;
        hoverPrompt.font = font;
        hoverPrompt.fontSize = fontSize;
        hoverPrompt.color = color;
        hoverPrompt.enabled = showHoverPrompt && !string.IsNullOrWhiteSpace(hoverPrompt.text);
    }

    private void UpdatePlayerMessage()
    {
        if (string.IsNullOrEmpty(activePlayerMessage))
            return;

        if (Time.unscaledTime < playerMessageHideTime)
            return;

        activePlayerMessage = string.Empty;
        RefreshPlayerMessage();
    }

    private void RefreshPlayerMessage()
    {
        if (playerMessage == null)
            return;

        playerMessage.text = activePlayerMessage;
        playerMessage.font = GetDefaultHoverPromptFont();
        playerMessage.fontSize = Mathf.Max(1, playerMessageFontSize);
        playerMessage.color = playerMessageColor;
        playerMessage.enabled = showPlayerMessages && !string.IsNullOrWhiteSpace(activePlayerMessage);
    }

    private Text CreateHoverPrompt(string objectName, Transform parent)
    {
        return CreatePromptText(objectName, parent, TextAnchor.UpperCenter);
    }

    private Text CreatePlayerMessage(string objectName, Transform parent)
    {
        return CreatePromptText(objectName, parent, TextAnchor.MiddleCenter);
    }

    private Text CreatePromptText(string objectName, Transform parent, TextAnchor alignment)
    {
        GameObject promptObject = new GameObject(objectName, typeof(Text));
        promptObject.transform.SetParent(parent, false);

        RectTransform rectTransform = promptObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        Text text = promptObject.GetComponent<Text>();
        text.font = GetDefaultHoverPromptFont();
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = string.Empty;
        text.enabled = false;
        return text;
    }

    private Font GetDefaultHoverPromptFont()
    {
        if (defaultHoverPromptFont == null)
            defaultHoverPromptFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return defaultHoverPromptFont;
    }

    private void ApplyVisibilityVignette(Color fogColor, float intensity, float smoothness)
    {
        if (intensity <= 0f)
            return;

        Volume targetVolume = ResolveGlobalVolume();
        if (targetVolume == null)
            return;

        VolumeProfile profile = targetVolume.profile;
        if (profile == null)
            return;

        if (!profile.TryGet(out Vignette vignette))
            vignette = profile.Add<Vignette>(true);

        if (vignette == null)
            return;

        vignette.active = true;
        vignette.color.Override(fogColor);
        vignette.intensity.Override(Mathf.Clamp01(intensity));
        vignette.smoothness.Override(Mathf.Clamp(smoothness, 0.01f, 1f));
        vignette.rounded.Override(true);
    }

    private static Volume ResolveGlobalVolume()
    {
        Volume[] volumes = FindObjectsOfType<Volume>(true);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (volume != null && volume.isGlobal)
                return volume;
        }

        return null;
    }
}
