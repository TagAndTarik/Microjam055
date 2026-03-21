using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class PickupInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private const int DefaultHoverMessageFontSize = 18;

    [SerializeField] private InteractableOutline outline;
    [SerializeField, TextArea] private string hoverMessage;
    [SerializeField] private Font hoverMessageFont;
    [SerializeField] private int hoverMessageFontSize = 18;
    [SerializeField] private Color hoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Vector3 heldEulerAngles = new Vector3(12f, -30f, 0f);
    [SerializeField] private Vector3 heldPositionOffset = Vector3.zero;
    [SerializeField] private float heldMaxSize = 0.45f;
    [SerializeField] private bool disableShadowsWhileHeld = true;
    [SerializeField] private string placementId;

    [Header("Disappear Behavior")]
    public Action MakeObjectDisappear;

    private Collider[] colliders;
    private Rigidbody[] rigidbodies;
    private Renderer[] renderers;
    private Transform[] hierarchyTransforms;
    private bool isHeld;
    private bool[] originalColliderStates;
    private bool[] originalKinematicStates;
    private bool[] originalGravityStates;
    private ShadowCastingMode[] originalShadowCastingModes;
    private bool[] originalReceiveShadowStates;
    private int[] originalLayers;
    private Vector3 originalLocalScale;

    public Vector3 OriginalLocalScale => originalLocalScale == Vector3.zero ? transform.localScale : originalLocalScale;

    private void Awake()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        CacheComponents();

        if (colliders.Length == 0)
            Debug.LogWarning($"{name} has a PickupInteractable but no collider in its hierarchy, so it cannot be interacted with.", this);

        if (HasStaticObjectsInHierarchy())
            Debug.LogWarning($"{name} is marked static in its hierarchy. Pickup objects should not use static batching flags.", this);

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        CaptureOriginalState();
    }

    private void OnValidate()
    {
        heldMaxSize = Mathf.Max(0.05f, heldMaxSize);

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (outline == null)
            outline = GetComponent<InteractableOutline>();
    }

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused && !isHeld);
    }

    public string GetHoverMessage()
    {
        return isHeld ? string.Empty : hoverMessage;
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
        if (isHeld)
            return;

        HeldItemSocket socket = HeldItemSocket.GetOrCreate(interactor);
        if (socket == null)
        {
            return;

        }
        MakeObjectDisappear?.Invoke();
        //PlayerManager.PlayerManagerInstance._disappearComponent = DisappearObject;
        if (socket.TryHold(this))
            outline?.SetOutlined(false);
    }

    public bool PlaceAt(Transform targetTransform)
    {
        if (!isHeld || targetTransform == null)
            return false;

        CacheComponents();

        transform.SetParent(targetTransform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = originalLocalScale;

        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = originalColliderStates[i];

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody body = rigidbodies[i];
            body.isKinematic = originalKinematicStates[i];
            body.useGravity = originalGravityStates[i];
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            targetRenderer.shadowCastingMode = originalShadowCastingModes[i];
            targetRenderer.receiveShadows = originalReceiveShadowStates[i];
        }

        for (int i = 0; i < hierarchyTransforms.Length; i++)
            hierarchyTransforms[i].gameObject.layer = originalLayers[i];

        isHeld = false;
        outline?.SetOutlined(false);
        return true;
    }

    public bool MatchesPlacementId(string candidatePlacementId)
    {
        if (string.IsNullOrWhiteSpace(placementId) || string.IsNullOrWhiteSpace(candidatePlacementId))
            return false;

        return string.Equals(
            placementId.Trim(),
            candidatePlacementId.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    public bool AttachToSocket(Transform holdAnchor, int heldItemLayer)
    {
        if (isHeld || holdAnchor == null || heldItemLayer < 0)
            return false;

        CacheComponents();
        CaptureOriginalState();

        Vector3 originalWorldScale = transform.lossyScale;
        Bounds originalBounds = CalculateWorldBounds();
        float heldScaleFactor = CalculateHeldScaleFactor(originalBounds);

        isHeld = true;

        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody body = rigidbodies[i];
            body.isKinematic = true;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (disableShadowsWhileHeld)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer targetRenderer = renderers[i];
                targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
                targetRenderer.receiveShadows = false;
            }
        }

        for (int i = 0; i < hierarchyTransforms.Length; i++)
            hierarchyTransforms[i].gameObject.layer = heldItemLayer;

        transform.SetParent(holdAnchor, false);
        transform.localRotation = Quaternion.Euler(heldEulerAngles);
        transform.localScale = originalWorldScale * heldScaleFactor;
        transform.localPosition = Vector3.zero;

        // Center the rendered bounds after the hold rotation and scale are applied.
        Bounds heldBounds = CalculateWorldBounds();
        if (heldBounds.size.sqrMagnitude > 0f)
        {
            Vector3 centeredOffset = holdAnchor.InverseTransformPoint(heldBounds.center);
            transform.localPosition = heldPositionOffset - centeredOffset;
        }
        else
        {
            transform.localPosition = heldPositionOffset;
        }

        return true;
    }

    private void CacheComponents()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        hierarchyTransforms = GetComponentsInChildren<Transform>(true);

        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        List<Renderer> filteredRenderers = new List<Renderer>(allRenderers.Length);

        for (int i = 0; i < allRenderers.Length; i++)
        {
            Renderer candidate = allRenderers[i];
            if (candidate == null || candidate.gameObject.hideFlags != HideFlags.None)
                continue;

            filteredRenderers.Add(candidate);
        }

        renderers = filteredRenderers.ToArray();
    }

    private void CaptureOriginalState()
    {
        if (isHeld)
            return;

        CacheComponents();

        if (originalColliderStates != null &&
            originalColliderStates.Length == colliders.Length &&
            originalKinematicStates != null &&
            originalKinematicStates.Length == rigidbodies.Length &&
            originalGravityStates != null &&
            originalGravityStates.Length == rigidbodies.Length &&
            originalShadowCastingModes != null &&
            originalShadowCastingModes.Length == renderers.Length &&
            originalReceiveShadowStates != null &&
            originalReceiveShadowStates.Length == renderers.Length &&
            originalLayers != null &&
            originalLayers.Length == hierarchyTransforms.Length)
        {
            return;
        }

        originalLocalScale = transform.localScale;

        originalColliderStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
            originalColliderStates[i] = colliders[i].enabled;

        originalKinematicStates = new bool[rigidbodies.Length];
        originalGravityStates = new bool[rigidbodies.Length];
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            originalKinematicStates[i] = rigidbodies[i].isKinematic;
            originalGravityStates[i] = rigidbodies[i].useGravity;
        }

        originalShadowCastingModes = new ShadowCastingMode[renderers.Length];
        originalReceiveShadowStates = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            originalShadowCastingModes[i] = renderers[i].shadowCastingMode;
            originalReceiveShadowStates[i] = renderers[i].receiveShadows;
        }

        originalLayers = new int[hierarchyTransforms.Length];
        for (int i = 0; i < hierarchyTransforms.Length; i++)
            originalLayers[i] = hierarchyTransforms[i].gameObject.layer;
    }

    private Bounds CalculateWorldBounds()
    {
        bool foundBounds = false;
        Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null || !targetRenderer.enabled || !targetRenderer.gameObject.activeInHierarchy)
                continue;

            if (!foundBounds)
            {
                combinedBounds = targetRenderer.bounds;
                foundBounds = true;
                continue;
            }

            combinedBounds.Encapsulate(targetRenderer.bounds);
        }

        return combinedBounds;
    }

    private float CalculateHeldScaleFactor(Bounds bounds)
    {
        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension <= 0.0001f)
            return 1f;

        return Mathf.Clamp(heldMaxSize / largestDimension, 0.1f, 4f);
    }

    private bool HasStaticObjectsInHierarchy()
    {
        for (int i = 0; i < hierarchyTransforms.Length; i++)
        {
            if (hierarchyTransforms[i].gameObject.isStatic)
                return true;
        }

        return false;
    }

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
