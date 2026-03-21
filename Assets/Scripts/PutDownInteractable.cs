using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class PutDownInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private const int DefaultHoverMessageFontSize = 18;
    private const float MinimumColliderSize = 0.05f;

    [SerializeField] private PickupInteractable previewSource;
    [SerializeField] private string acceptedPlacementId;
    [SerializeField, TextArea] private string hoverMessage = "Put down";
    [SerializeField] private Font hoverMessageFont;
    [SerializeField] private int hoverMessageFontSize = 18;
    [SerializeField] private Color hoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Vector3 interactionColliderPadding = new Vector3(0.08f, 0.08f, 0.08f);
    [SerializeField, Range(0.1f, 1f)] private float previewScale = 0.85f;

    private BoxCollider interactionCollider;
    private Transform previewRoot;
    private bool isFocused;
    private HeldItemSocket cachedSocket;

    private void Awake()
    {
        EnsureInteractionCollider();
        ResizeInteractionCollider();
        EnsurePreviewHierarchy();
        ApplyPreviewScale();
        SetPreviewVisible(false);
        RefreshAvailability();
    }

    private void Update()
    {
        RefreshAvailability();

        if (isFocused)
            RefreshPreviewVisibility();
    }

    private void OnValidate()
    {
        hoverMessageFontSize = Mathf.Max(1, hoverMessageFontSize);
        interactionColliderPadding.x = Mathf.Max(0f, interactionColliderPadding.x);
        interactionColliderPadding.y = Mathf.Max(0f, interactionColliderPadding.y);
        interactionColliderPadding.z = Mathf.Max(0f, interactionColliderPadding.z);
        previewScale = Mathf.Clamp(previewScale, 0.1f, 1f);

        if (previewRoot != null)
            ApplyPreviewScale();
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
        return CanAccept(GetHeldItem()) ? hoverMessage : string.Empty;
    }

    public void SetFocused(bool focused)
    {
        isFocused = focused;
        RefreshPreviewVisibility();
    }

    public void Interact(Transform interactor)
    {
        HeldItemSocket socket = HeldItemSocket.GetOrCreate(interactor);
        if (socket == null)
            return;

        cachedSocket = socket;
        PlayerManager.PlayerManagerInstance._disappearComponent = null;
        PickupInteractable heldItem = socket.HeldItem;
        if (!CanAccept(heldItem))
            return;

        if (!socket.TryPlaceHeldItem(transform))
            return;

        SetPreviewVisible(false);
        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        if (interactionCollider == null)
            return;

        interactionCollider.enabled = CanAccept(GetHeldItem());
    }

    private void RefreshPreviewVisibility()
    {
        SetPreviewVisible(isFocused && CanAccept(GetHeldItem()));
    }

    private PickupInteractable GetHeldItem()
    {
        if (cachedSocket == null)
            cachedSocket = FindObjectOfType<HeldItemSocket>();

        return cachedSocket != null ? cachedSocket.HeldItem : null;
    }

    private bool CanAccept(PickupInteractable heldItem)
    {
        if (heldItem == null)
            return false;

        if (!string.IsNullOrWhiteSpace(acceptedPlacementId))
            return heldItem.MatchesPlacementId(acceptedPlacementId);

        return previewSource != null && ReferenceEquals(heldItem, previewSource);
    }

    private void EnsureInteractionCollider()
    {
        interactionCollider = GetComponent<BoxCollider>();
        interactionCollider.isTrigger = true;
    }

    private void ResizeInteractionCollider()
    {
        if (interactionCollider == null)
            return;

        if (!TryCalculateLocalBounds(out Bounds localBounds))
        {
            interactionCollider.center = Vector3.zero;
            interactionCollider.size = Vector3.one * 0.35f;
            return;
        }

        Vector3 paddedSize = localBounds.size + interactionColliderPadding;
        paddedSize.x = Mathf.Max(MinimumColliderSize, paddedSize.x);
        paddedSize.y = Mathf.Max(MinimumColliderSize, paddedSize.y);
        paddedSize.z = Mathf.Max(MinimumColliderSize, paddedSize.z);

        interactionCollider.center = localBounds.center;
        interactionCollider.size = paddedSize;
    }

    private bool TryCalculateLocalBounds(out Bounds localBounds)
    {
        localBounds = new Bounds(Vector3.zero, Vector3.zero);

        if (previewSource == null)
            return false;

        MeshRenderer[] sourceRenderers = previewSource.GetComponentsInChildren<MeshRenderer>(true);
        if (sourceRenderers.Length == 0)
            return false;

        Matrix4x4 worldToLocal = previewSource.transform.worldToLocalMatrix;
        bool foundBounds = false;

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            MeshRenderer sourceRenderer = sourceRenderers[i];
            if (sourceRenderer == null || sourceRenderer.gameObject.hideFlags != HideFlags.None)
                continue;

            Bounds rendererBounds = sourceRenderer.bounds;
            Vector3 extents = rendererBounds.extents;
            Vector3 center = rendererBounds.center;

            Vector3[] corners =
            {
                center + new Vector3( extents.x,  extents.y,  extents.z),
                center + new Vector3( extents.x,  extents.y, -extents.z),
                center + new Vector3( extents.x, -extents.y,  extents.z),
                center + new Vector3( extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x,  extents.y,  extents.z),
                center + new Vector3(-extents.x,  extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y,  extents.z),
                center + new Vector3(-extents.x, -extents.y, -extents.z)
            };

            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                Vector3 localPoint = worldToLocal.MultiplyPoint3x4(corners[cornerIndex]);

                if (!foundBounds)
                {
                    localBounds = new Bounds(localPoint, Vector3.zero);
                    foundBounds = true;
                    continue;
                }

                localBounds.Encapsulate(localPoint);
            }
        }

        return foundBounds;
    }

    private void EnsurePreviewHierarchy()
    {
        if (previewRoot != null || previewSource == null)
            return;

        GameObject previewObject = new GameObject("Put Down Preview");
        previewObject.hideFlags = HideFlags.HideInHierarchy;
        previewRoot = previewObject.transform;
        previewRoot.SetParent(transform, false);
        previewRoot.localPosition = Vector3.zero;
        previewRoot.localRotation = Quaternion.identity;
        previewRoot.localScale = Vector3.one;

        ApplyPreviewRenderer(previewSource.transform, previewObject);
        CopyPreviewChildren(previewSource.transform, previewRoot);
    }

    private void ApplyPreviewScale()
    {
        if (previewRoot != null)
            previewRoot.localScale = GetPreviewBaseScale() * previewScale;
    }

    private Vector3 GetPreviewBaseScale()
    {
        if (previewSource == null)
            return Vector3.one;

        return previewSource.OriginalLocalScale;
    }

    private void CopyPreviewChildren(Transform sourceParent, Transform previewParent)
    {
        for (int i = 0; i < sourceParent.childCount; i++)
        {
            Transform sourceChild = sourceParent.GetChild(i);
            if (sourceChild.gameObject.hideFlags != HideFlags.None)
                continue;

            GameObject previewChild = new GameObject(sourceChild.name);
            previewChild.hideFlags = HideFlags.HideInHierarchy;

            Transform previewChildTransform = previewChild.transform;
            previewChildTransform.SetParent(previewParent, false);
            previewChildTransform.localPosition = sourceChild.localPosition;
            previewChildTransform.localRotation = sourceChild.localRotation;
            previewChildTransform.localScale = sourceChild.localScale;

            ApplyPreviewRenderer(sourceChild, previewChild);
            CopyPreviewChildren(sourceChild, previewChildTransform);
        }
    }

    private static void ApplyPreviewRenderer(Transform sourceTransform, GameObject previewObject)
    {
        if (sourceTransform.gameObject.hideFlags != HideFlags.None)
            return;

        MeshFilter sourceFilter = sourceTransform.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = sourceTransform.GetComponent<MeshRenderer>();

        if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null)
            return;

        MeshFilter previewFilter = previewObject.AddComponent<MeshFilter>();
        previewFilter.sharedMesh = sourceFilter.sharedMesh;

        MeshRenderer previewRenderer = previewObject.AddComponent<MeshRenderer>();
        previewRenderer.sharedMaterial = InteractableOutline.GetSharedOutlineMaterial();
        previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
        previewRenderer.receiveShadows = false;
        previewRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        previewRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        previewRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        previewRenderer.allowOcclusionWhenDynamic = false;
    }

    private void SetPreviewVisible(bool visible)
    {
        if (previewRoot != null)
            previewRoot.gameObject.SetActive(visible);
    }

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
