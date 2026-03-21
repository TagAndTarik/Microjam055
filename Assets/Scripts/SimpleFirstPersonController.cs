using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class SimpleFirstPersonController : MonoBehaviour
{
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

    private CharacterController controller;
    private IInteractable currentInteractable;
    private float verticalVelocity;
    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera != null)
            playerCamera.nearClipPlane = Mathf.Max(0.01f, cameraNearClipPlane);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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

        IInteractable nextInteractable = null;
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Collide))
            nextInteractable = FindInteractable(hit.collider);

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
}
