using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class SimpleFirstPersonController : MonoBehaviour
{
    private const string CrosshairCanvasName = "Runtime Crosshair";
    private const string HorizontalCrosshairName = "Horizontal";
    private const string VerticalCrosshairName = "Vertical";

    [Header("References")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Camera playerCamera;

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

    [Header("Crosshair")]
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private float crosshairSize = 12f;
    [SerializeField] private float crosshairThickness = 2f;
    [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);

    private CharacterController controller;
    private IInteractable currentInteractable;
    private Canvas crosshairCanvas;
    private Image horizontalCrosshair;
    private Image verticalCrosshair;
    private float verticalVelocity;
    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera != null)
            playerCamera.nearClipPlane = Mathf.Max(0.01f, cameraNearClipPlane);

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

        ApplyCrosshairStyle();
        SetCrosshairVisible(showCrosshair);
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

        if (!ReferenceEquals(nextInteractable, currentInteractable))
        {
            currentInteractable?.SetFocused(false);
            currentInteractable = nextInteractable;
            currentInteractable?.SetFocused(true);
        }

        if (currentInteractable != null &&
            Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            currentInteractable.Interact(transform);
        }
    }

    private void ClearCurrentInteractable()
    {
        currentInteractable?.SetFocused(false);
        currentInteractable = null;
    }

    private static IInteractable FindInteractable(Collider hitCollider)
    {
        MonoBehaviour[] behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IInteractable interactable)
                return interactable;
        }

        return null;
    }

    private IInteractable FindInteractableTarget(Ray ray)
    {
        IInteractable overlappingInteractable = FindOverlappingInteractable(ray);
        if (overlappingInteractable != null)
            return overlappingInteractable;

        RaycastHit[] hits = Physics.RaycastAll(ray, interactDistance, interactMask, QueryTriggerInteraction.Collide);

        if (hits.Length == 0 && interactCastRadius > 0f)
        {
            hits = Physics.SphereCastAll(ray, interactCastRadius, interactDistance, interactMask, QueryTriggerInteraction.Collide);
        }

        if (hits.Length == 0)
            return null;

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            IInteractable interactable = FindInteractable(hits[i].collider);
            if (interactable != null)
                return interactable;
        }

        return null;
    }

    private IInteractable FindOverlappingInteractable(Ray ray)
    {
        if (interactCastRadius <= 0f)
            return null;

        Collider[] colliders = Physics.OverlapSphere(ray.origin, interactCastRadius, interactMask, QueryTriggerInteraction.Collide);
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

            float score = alignment / sqrDistance;
            if (score > bestScore)
            {
                bestScore = score;
                bestInteractable = interactable;
            }
        }

        return bestInteractable;
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
    }

    private void ApplyCrosshairStyle()
    {
        if (crosshairCanvas == null || horizontalCrosshair == null || verticalCrosshair == null)
            return;

        RectTransform horizontalRect = (RectTransform)horizontalCrosshair.transform;
        horizontalRect.sizeDelta = new Vector2(crosshairSize, crosshairThickness);
        horizontalRect.anchoredPosition = Vector2.zero;

        RectTransform verticalRect = (RectTransform)verticalCrosshair.transform;
        verticalRect.sizeDelta = new Vector2(crosshairThickness, crosshairSize);
        verticalRect.anchoredPosition = Vector2.zero;

        horizontalCrosshair.color = crosshairColor;
        verticalCrosshair.color = crosshairColor;
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
}
