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
    [Header("Held Lamp Motion")]
    [SerializeField] private bool enableHeldLampJostle = true;
    [SerializeField] private float heldLampJostleFrequency = 2.2f;
    [SerializeField] private float heldLampJostleAmplitude = 0.02f;
    [SerializeField] private float heldLampJostleSmoothing = 10f;
    [SerializeField] private float minimumJostleSpeed = 0.1f;

    private Camera holdCamera;
    private Transform holdAnchor;
    private PickupInteractable heldItem;
    private int heldItemLayer = -1;
    private RenderTexture holdRenderTexture;
    private Vector3 currentJostleOffset;
    private Vector3 lastSocketPosition;
    private bool hasLastSocketPosition;
    private float heldLampJostleCycle;

    public bool IsHoldingItem => heldItem != null;
    public PickupInteractable HeldItem => heldItem;

    private void Awake()
    {
        EnsureSetup();
    }

    private void LateUpdate()
    {
        EnsureHoldRenderTarget();
        UpdateAnchorTransform();
    }

    private void OnDestroy()
    {
        ReleaseHoldRenderTarget();
        SetMainCameraOverlayTexture(null);
    }

    private void OnValidate()
    {
        holdDistance = Mathf.Max(0.05f, holdDistance);
        heldLampJostleFrequency = Mathf.Max(0f, heldLampJostleFrequency);
        heldLampJostleAmplitude = Mathf.Max(0f, heldLampJostleAmplitude);
        heldLampJostleSmoothing = Mathf.Max(0.01f, heldLampJostleSmoothing);
        minimumJostleSpeed = Mathf.Max(0f, minimumJostleSpeed);

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
        currentJostleOffset = Vector3.zero;
        heldLampJostleCycle = 0f;
        hasLastSocketPosition = false;
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
        currentJostleOffset = Vector3.zero;
        heldLampJostleCycle = 0f;
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
        EnsureHoldRenderTarget();

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
        holdCamera.nearClipPlane = 0.01f;
        holdCamera.farClipPlane = Mathf.Max(2f, holdDistance + 2f);
        holdCamera.depth = playerCamera.depth + 1f;
        holdCamera.allowHDR = playerCamera.allowHDR;
        holdCamera.allowMSAA = false;
        holdCamera.orthographic = playerCamera.orthographic;
        holdCamera.orthographicSize = playerCamera.orthographicSize;
        holdCamera.fieldOfView = playerCamera.fieldOfView;
        holdCamera.backgroundColor = Color.clear;
        holdCamera.clearFlags = CanCompositeHeldCamera() ? CameraClearFlags.SolidColor : CameraClearFlags.Depth;

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
        holdAnchor.localPosition += CalculateHeldLampJostleOffset();
        holdAnchor.localRotation = Quaternion.identity;
        holdAnchor.localScale = Vector3.one;
    }

    private void EnsureHoldRenderTarget()
    {
        if (holdCamera == null || playerCamera == null)
            return;

        if (!CanCompositeHeldCamera())
        {
            holdCamera.targetTexture = null;
            holdCamera.clearFlags = CameraClearFlags.Depth;
            SetMainCameraOverlayTexture(null);
            ReleaseHoldRenderTarget();
            return;
        }

        int targetWidth = Mathf.Max(1, playerCamera.pixelWidth > 0 ? playerCamera.pixelWidth : Screen.width);
        int targetHeight = Mathf.Max(1, playerCamera.pixelHeight > 0 ? playerCamera.pixelHeight : Screen.height);

        if (holdRenderTexture == null ||
            holdRenderTexture.width != targetWidth ||
            holdRenderTexture.height != targetHeight)
        {
            ReleaseHoldRenderTarget();

            holdRenderTexture = new RenderTexture(targetWidth, targetHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "Held Item Overlay RT",
                filterMode = FilterMode.Point,
                useMipMap = false,
                autoGenerateMips = false
            };
            holdRenderTexture.Create();
        }

        holdCamera.clearFlags = CameraClearFlags.SolidColor;
        holdCamera.backgroundColor = Color.clear;
        holdCamera.targetTexture = holdRenderTexture;
        SetMainCameraOverlayTexture(holdRenderTexture);
    }

    private Vector3 CalculateHeldLampJostleOffset()
    {
        Vector3 targetOffset = Vector3.zero;
        float smoothing = 1f - Mathf.Exp(-heldLampJostleSmoothing * Time.deltaTime);

        if (enableHeldLampJostle && IsHeldLamp())
        {
            float speed = GetSocketHorizontalSpeed();
            if (speed > minimumJostleSpeed)
            {
                float speedRatio = Mathf.Clamp01(speed / 5f);
                heldLampJostleCycle += Time.deltaTime * heldLampJostleFrequency * Mathf.Lerp(0.85f, 1.8f, speedRatio);

                float bobPhase = heldLampJostleCycle * Mathf.PI * 2f;
                targetOffset = new Vector3(
                    0f,
                    Mathf.Sin(bobPhase) * heldLampJostleAmplitude * speedRatio,
                    0f);
            }
        }

        currentJostleOffset = Vector3.Lerp(currentJostleOffset, targetOffset, smoothing);
        return currentJostleOffset;
    }

    private float GetSocketHorizontalSpeed()
    {
        Vector3 currentPosition = transform.position;
        if (!hasLastSocketPosition)
        {
            lastSocketPosition = currentPosition;
            hasLastSocketPosition = true;
            return 0f;
        }

        Vector3 delta = currentPosition - lastSocketPosition;
        lastSocketPosition = currentPosition;
        delta.y = 0f;

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        return delta.magnitude / deltaTime;
    }

    private bool IsHeldLamp()
    {
        if (heldItem == null)
            return false;

        return heldItem.GetComponent<WarmLightInteractable>() != null ||
               heldItem.GetComponentInParent<WarmLightInteractable>() != null ||
               heldItem.GetComponentInChildren<WarmLightInteractable>(true) != null;
    }

    private bool CanCompositeHeldCamera()
    {
        ScreenWaveEffect effect = playerCamera != null ? playerCamera.GetComponent<ScreenWaveEffect>() : null;
        return effect != null && effect.material != null;
    }

    private void SetMainCameraOverlayTexture(Texture overlayTexture)
    {
        if (playerCamera == null)
            return;

        ScreenWaveEffect effect = playerCamera.GetComponent<ScreenWaveEffect>();
        if (effect != null)
            effect.SetOverlayTexture(overlayTexture);
    }

    private void ReleaseHoldRenderTarget()
    {
        if (holdRenderTexture == null)
            return;

        holdRenderTexture.Release();
        Destroy(holdRenderTexture);
        holdRenderTexture = null;
    }
}
