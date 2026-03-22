using UnityEngine;

[DisallowMultipleComponent]
public class HeldLampPlanePhysics : MonoBehaviour
{
    private const string DefaultSwingingPartName = "Plane.005";
    private const string DefaultHingeName = "hinge";

    [Header("References")]
    [SerializeField] private string swingingPartName = DefaultSwingingPartName;
    [SerializeField] private string hingeName = DefaultHingeName;

    [Header("Swing")]
    [SerializeField] private float swingStrength = 4f;
    [SerializeField] private float returnToRestStrength = 28f;
    [SerializeField] private float swingDamping = 12f;
    [SerializeField] private float bobLiftStrength = 1.6f;
    [SerializeField] private float maxSwingAngle = 55f;

    private Transform swingingPart;
    private Transform hinge;
    private Transform swingParent;
    private Transform turnReference;
    private Transform motionReference;
    private Vector3 pivotLocalPoint;
    private Vector3 hingeAxisLocal = Vector3.right;
    private Vector3 baseLocalOffset;
    private Quaternion baseLocalRotation;
    private float lastTurnYaw;
    private Vector3 lastMotionReferencePosition;
    private Vector3 lastMotionReferenceVelocity;
    private float swingAngle;
    private float swingVelocity;
    private bool hasTurnSample;
    private bool hasMotionSample;
    private bool hasCachedRestPose;
    private bool missingReferenceWarningLogged;

    private void Awake()
    {
        ResolveReferences();
        CacheRestPose(forceRefresh: true);
        ApplySwingPose();
    }

    private void OnEnable()
    {
        hasTurnSample = false;
        hasMotionSample = false;
    }

    private void OnValidate()
    {
        swingStrength = Mathf.Max(0f, swingStrength);
        returnToRestStrength = Mathf.Max(0f, returnToRestStrength);
        swingDamping = Mathf.Max(0f, swingDamping);
        bobLiftStrength = Mathf.Max(0f, bobLiftStrength);
        maxSwingAngle = Mathf.Clamp(maxSwingAngle, 0f, 180f);
    }

    private void LateUpdate()
    {
        if (!ResolveReferences())
            return;

        if (!hasCachedRestPose)
            CacheRestPose(forceRefresh: true);

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            ApplySwingPose();
            return;
        }

        UpdateTurnReference();
        UpdateMotionReference();

        float leftTurnDelta = GetLeftTurnDelta();
        float bobTargetAngle = GetBobTargetAngle(deltaTime);
        swingVelocity += leftTurnDelta * swingStrength;
        swingVelocity += ((bobTargetAngle - swingAngle) * returnToRestStrength) * deltaTime;
        swingVelocity *= Mathf.Exp(-swingDamping * deltaTime);
        swingAngle = Mathf.Clamp(swingAngle + swingVelocity * deltaTime, 0f, maxSwingAngle);

        if (swingAngle <= 0.001f && swingVelocity < 0f)
            swingVelocity = 0f;

        if (swingAngle >= maxSwingAngle - 0.001f && swingVelocity > 0f)
            swingVelocity = 0f;

        ApplySwingPose();
    }

    private bool ResolveReferences()
    {
        if (swingingPart == null)
            swingingPart = FindDescendantByName(transform, swingingPartName);

        if (hinge == null)
            hinge = FindDescendantByName(transform, hingeName);

        if (swingingPart == null || hinge == null)
        {
            if (!missingReferenceWarningLogged)
            {
                Debug.LogWarning(
                    $"HeldLampPlanePhysics on {name} could not find '{swingingPartName}' or '{hingeName}'.",
                    this);
                missingReferenceWarningLogged = true;
            }

            return false;
        }

        missingReferenceWarningLogged = false;
        Transform nextSwingParent = swingingPart.parent;
        if (nextSwingParent != swingParent)
        {
            swingParent = nextSwingParent;
            hasCachedRestPose = false;
        }

        return swingParent != null;
    }

    private void CacheRestPose(bool forceRefresh)
    {
        if (!forceRefresh && hasCachedRestPose)
            return;

        if (swingingPart == null || hinge == null || swingParent == null)
            return;

        pivotLocalPoint = swingParent.InverseTransformPoint(hinge.position);
        hingeAxisLocal = Vector3.right;
        baseLocalOffset = swingingPart.localPosition - pivotLocalPoint;
        baseLocalRotation = swingingPart.localRotation;
        hasCachedRestPose = true;
    }

    private void ApplySwingPose()
    {
        if (swingingPart == null || swingParent == null)
            return;

        Quaternion localSwingRotation = Quaternion.AngleAxis(swingAngle, hingeAxisLocal);
        swingingPart.localPosition = pivotLocalPoint + (localSwingRotation * baseLocalOffset);
        swingingPart.localRotation = localSwingRotation * baseLocalRotation;
    }

    private void UpdateTurnReference()
    {
        Transform nextTurnReference = ResolveTurnReference();
        if (nextTurnReference == turnReference)
            return;

        turnReference = nextTurnReference;
        hasTurnSample = false;
    }

    private void UpdateMotionReference()
    {
        Transform nextMotionReference = ResolveMotionReference();
        if (nextMotionReference == motionReference)
            return;

        motionReference = nextMotionReference;
        hasMotionSample = false;
    }

    private float GetLeftTurnDelta()
    {
        if (turnReference == null)
            return 0f;

        float currentYaw = turnReference.eulerAngles.y;
        if (!hasTurnSample)
        {
            lastTurnYaw = currentYaw;
            hasTurnSample = true;
            return 0f;
        }

        float yawDelta = Mathf.DeltaAngle(lastTurnYaw, currentYaw);
        lastTurnYaw = currentYaw;
        return Mathf.Max(0f, -yawDelta);
    }

    private Transform ResolveTurnReference()
    {
        SimpleFirstPersonController controller = GetComponentInParent<SimpleFirstPersonController>();
        if (controller != null)
            return controller.transform;

        HeldItemSocket heldItemSocket = GetComponentInParent<HeldItemSocket>();
        if (heldItemSocket != null)
            return heldItemSocket.transform;

        CharacterController characterController = GetComponentInParent<CharacterController>();
        if (characterController != null)
            return characterController.transform;

        return transform;
    }

    private Transform ResolveMotionReference()
    {
        HeldItemSocket heldItemSocket = GetComponentInParent<HeldItemSocket>();
        if (heldItemSocket != null)
            return transform;

        return null;
    }

    private float GetBobTargetAngle(float deltaTime)
    {
        if (motionReference == null || bobLiftStrength <= 0f)
            return 0f;

        Vector3 referencePosition = motionReference.position;
        Vector3 referenceVelocity = hasMotionSample ? (referencePosition - lastMotionReferencePosition) / deltaTime : Vector3.zero;
        Vector3 referenceAcceleration = hasMotionSample ? (referenceVelocity - lastMotionReferenceVelocity) / deltaTime : Vector3.zero;

        lastMotionReferencePosition = referencePosition;
        lastMotionReferenceVelocity = referenceVelocity;
        hasMotionSample = true;

        Vector3 gravity = Physics.gravity.sqrMagnitude > 0.0001f ? Physics.gravity.normalized : Vector3.down;
        float accelerationAgainstGravity = Vector3.Dot(referenceAcceleration, -gravity);
        return Mathf.Clamp(accelerationAgainstGravity * bobLiftStrength, 0f, maxSwingAngle);
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        string trimmedName = targetName.Trim();
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, trimmedName, System.StringComparison.Ordinal))
                return child;

            Transform descendant = FindDescendantByName(child, trimmedName);
            if (descendant != null)
                return descendant;
        }

        return null;
    }
}
