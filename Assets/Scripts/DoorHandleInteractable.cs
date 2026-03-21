using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DoorHandleInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private DoorController doorController;
    [SerializeField] private InteractableOutline outline;
    [SerializeField, TextArea] private string hoverMessage;
    [SerializeField] private float turnAngle = 30f;
    [SerializeField] private float turnDuration = 0.12f;
    [SerializeField] private Vector3 localTurnAxis = Vector3.forward;

    private Coroutine turnRoutine;
    private Quaternion restingRotation;

    private void Awake()
    {
        restingRotation = transform.localRotation;

        if (doorController == null)
            doorController = GetComponentInParent<DoorController>();

        if (outline == null)
            outline = GetComponent<InteractableOutline>();
    }

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused);
    }

    public string GetHoverMessage()
    {
        return hoverMessage;
    }

    public void Interact(Transform interactor)
    {
        if (doorController == null)
            return;

        if (turnRoutine != null)
            StopCoroutine(turnRoutine);

        turnRoutine = StartCoroutine(AnimateHandleTurn());
        doorController.Toggle(interactor);
    }

    private IEnumerator AnimateHandleTurn()
    {
        Quaternion turnedRotation = restingRotation * Quaternion.AngleAxis(turnAngle, localTurnAxis.normalized);

        yield return RotateHandle(restingRotation, turnedRotation);
        yield return RotateHandle(turnedRotation, restingRotation);

        turnRoutine = null;
    }

    private IEnumerator RotateHandle(Quaternion startRotation, Quaternion endRotation)
    {
        float elapsed = 0f;

        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, turnDuration));
            transform.localRotation = Quaternion.Slerp(startRotation, endRotation, t);
            yield return null;
        }

        transform.localRotation = endRotation;
    }
}
