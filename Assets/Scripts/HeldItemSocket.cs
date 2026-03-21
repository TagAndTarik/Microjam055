using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class HeldItemSocket : MonoBehaviour
{
    private const string HoldAnchorName = "Held Item Anchor";
    private const string HoldCameraName = "Held Item Camera";
    private const string HeldItemLayerName = "HeldItem";

    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Presentation")]
    [SerializeField, Range(0.55f, 0.98f)] private float viewportX = 0.78f;
    [SerializeField, Range(0.02f, 0.45f)] private float viewportY = 0.22f;
    [SerializeField] private float holdDistance = 0.55f;

    private Camera holdCamera;
    private Transform holdAnchor;
    private PickupInteractable heldItem;
    private int heldItemLayer = -1;

    public bool IsHoldingItem => heldItem != null;
    public PickupInteractable HeldItem => heldItem;

    private void Awake()
    {
        EnsureSetup();
    }

    private void LateUpdate()
    {
        UpdateAnchorTransform();
    }

    private void OnValidate()
    {
        holdDistance = Mathf.Max(0.05f, holdDistance);

        if (playerCamera == null)
            playerCamera = FindPlayerCamera();
    }

    public static HeldItemSocket GetOrCreate(Transform interactor)
    {
        if (interactor == null)
            return null;

        HeldItemSocket socket = interactor.GetComponentInParent<HeldItemSocket>();
        if (socket != null)
            return socket;

        SimpleFirstPersonController controller = interactor.GetComponentInParent<SimpleFirstPersonController>();
        GameObject host = controller != null ? controller.gameObject : interactor.root.gameObject;

        socket = host.GetComponent<HeldItemSocket>();
        if (socket == null)
            socket = host.AddComponent<HeldItemSocket>();

        return socket;
    }

    public bool TryHold(PickupInteractable item)
    {
        if (item == null || heldItem != null)
            return false;

        if (!EnsureSetup())
            return false;

        if (!item.AttachToSocket(holdAnchor, heldItemLayer))
            return false;

        heldItem = item;
        return true;
    }

    public bool TryPlaceHeldItem(Transform targetTransform)
    {
        if (heldItem == null || targetTransform == null)
            return false;

        PickupInteractable itemToPlace = heldItem;
        if (!itemToPlace.PlaceAt(targetTransform))
            return false;

        heldItem = null;
        return true;
    }

    private bool EnsureSetup()
    {
        if (playerCamera == null)
            playerCamera = FindPlayerCamera();

        if (playerCamera == null)
        {
            Debug.LogWarning("HeldItemSocket could not find a player camera.", this);
            return false;
        }

        heldItemLayer = LayerMask.NameToLayer(HeldItemLayerName);
        if (heldItemLayer < 0)
        {
            Debug.LogWarning($"Layer '{HeldItemLayerName}' is missing. Add it in Project Settings > Tags and Layers.", this);
            return false;
        }

        EnsureHoldCamera();
        EnsureHoldAnchor();

        playerCamera.cullingMask &= ~(1 << heldItemLayer);
        holdCamera.cullingMask = 1 << heldItemLayer;

        UpdateAnchorTransform();
        return true;
    }

    private Camera FindPlayerCamera()
    {
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        Camera fallback = null;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null || candidate.name == HoldCameraName)
                continue;

            if (candidate.CompareTag("MainCamera"))
                return candidate;

            if (fallback == null)
                fallback = candidate;
        }

        return fallback;
    }

    private void EnsureHoldCamera()
    {
        if (holdCamera == null)
        {
            Transform existing = playerCamera.transform.Find(HoldCameraName);
            if (existing != null)
                holdCamera = existing.GetComponent<Camera>();
        }

        if (holdCamera == null)
        {
            GameObject holdCameraObject = new GameObject(HoldCameraName, typeof(Camera));
            holdCameraObject.transform.SetParent(playerCamera.transform, false);
            holdCamera = holdCameraObject.GetComponent<Camera>();
        }

        holdCamera.transform.localPosition = Vector3.zero;
        holdCamera.transform.localRotation = Quaternion.identity;
        holdCamera.transform.localScale = Vector3.one;

        holdCamera.enabled = true;
        holdCamera.clearFlags = CameraClearFlags.Depth;
        holdCamera.nearClipPlane = 0.01f;
        holdCamera.farClipPlane = Mathf.Max(2f, holdDistance + 2f);
        holdCamera.depth = playerCamera.depth + 1f;
        holdCamera.allowHDR = playerCamera.allowHDR;
        holdCamera.allowMSAA = false;
        holdCamera.orthographic = playerCamera.orthographic;
        holdCamera.orthographicSize = playerCamera.orthographicSize;
        holdCamera.fieldOfView = playerCamera.fieldOfView;
        holdCamera.backgroundColor = Color.clear;

        ConfigureUniversalCameraStackIfAvailable();
    }

    private void ConfigureUniversalCameraStackIfAvailable()
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset)
            return;

        UniversalAdditionalCameraData baseCameraData = playerCamera.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData holdCameraData = holdCamera.GetUniversalAdditionalCameraData();

        if (baseCameraData == null || holdCameraData == null)
            return;

        if (baseCameraData.scriptableRenderer == null)
        {
            Debug.LogWarning("HeldItemSocket could not access the active URP renderer for camera stacking.", this);
            return;
        }

        holdCameraData.renderType = CameraRenderType.Overlay;

        List<Camera> cameraStack = baseCameraData.cameraStack;
        if (cameraStack == null)
            return;

        if (!cameraStack.Contains(holdCamera))
        {
            cameraStack.Add(holdCamera);
        }
    }

    private void EnsureHoldAnchor()
    {
        Transform anchorParent = holdCamera != null ? holdCamera.transform : playerCamera.transform;

        if (holdAnchor == null)
        {
            Transform existing = anchorParent.Find(HoldAnchorName);
            if (existing != null)
                holdAnchor = existing;
        }

        if (holdAnchor == null)
        {
            GameObject anchorObject = new GameObject(HoldAnchorName);
            holdAnchor = anchorObject.transform;
        }

        holdAnchor.SetParent(anchorParent, false);
    }

    private void UpdateAnchorTransform()
    {
        if (playerCamera == null || holdAnchor == null)
            return;

        float distance = Mathf.Max(0.05f, holdDistance);

        if (holdCamera != null)
        {
            holdCamera.orthographic = playerCamera.orthographic;
            holdCamera.orthographicSize = playerCamera.orthographicSize;
            holdCamera.fieldOfView = playerCamera.fieldOfView;
            holdCamera.farClipPlane = Mathf.Max(2f, distance + 2f);
        }

        float halfHeight;
        float halfWidth;

        if (playerCamera.orthographic)
        {
            halfHeight = playerCamera.orthographicSize;
            halfWidth = halfHeight * playerCamera.aspect;
        }
        else
        {
            halfHeight = Mathf.Tan(playerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            halfWidth = halfHeight * playerCamera.aspect;
        }

        holdAnchor.localPosition = new Vector3(
            Mathf.Lerp(-halfWidth, halfWidth, viewportX),
            Mathf.Lerp(-halfHeight, halfHeight, viewportY),
            distance);
        holdAnchor.localRotation = Quaternion.identity;
        holdAnchor.localScale = Vector3.one;
    }
}
