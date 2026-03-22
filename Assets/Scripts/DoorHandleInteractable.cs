using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DoorHandleInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private const int DefaultHoverMessageFontSize = 18;

    [SerializeField] private DoorController doorController;
    [SerializeField] private InteractableOutline outline;
    [SerializeField, TextArea] private string hoverMessage;
    [SerializeField] private Font hoverMessageFont;
    [SerializeField] private int hoverMessageFontSize = 18;
    [SerializeField] private Color hoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private float turnAngle = 30f;
    [SerializeField] private float turnDuration = 0.12f;
    [SerializeField] private Vector3 localTurnAxis = Vector3.forward;

    private Coroutine turnRoutine;
    private Quaternion restingRotation;
    public DungeonTiming timingScript;

    private void Awake()
    {
        restingRotation = transform.localRotation;

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

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused);
    }

    public string GetHoverMessage()
    {
        return hoverMessage;
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
        if (doorController == null)
            return;

        if (turnRoutine != null)
            StopCoroutine(turnRoutine);

        if(timingScript != null)
        {
            timingScript.StartTimer();
        }
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

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
