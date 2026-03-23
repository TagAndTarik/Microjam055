using System.Collections;
using UnityEngine;

public class DoorController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private AudioSource doorAudioSource;

    [Header("Motion")]
    [SerializeField] private float openAngle = 95f;
    [SerializeField] private float openDuration = 0.45f;
    [SerializeField] private bool openAwayFromInteractor = true;

    [Header("Audio")]
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.14f;
    [SerializeField, Min(0.1f)] private float minDistance = 1f;
    [SerializeField, Min(0.1f)] private float maxDistance = 8f;

    private Coroutine animationRoutine;
    private Quaternion closedRotation;
    private float lastOpenAngle;

    private void Awake()
    {
        if (doorPivot == null)
            doorPivot = FindChildRecursive(transform, "Exterior_Door");

        if (doorPivot == null)
            doorPivot = FindChildRecursive(transform, "Interior_Door");

        if (doorPivot == null)
        {
            Debug.LogError($"DoorController on {name} could not find a door pivot.", this);
            enabled = false;
            return;
        }

        EnsureAudioSource();
        closedRotation = doorPivot.localRotation;
    }

    public void Toggle(Transform interactor)
    {
        if (!enabled)
            return;

        float currentAngle = GetCurrentAngle();
        bool isFullyOpen = Mathf.Abs(Mathf.Abs(currentAngle) - Mathf.Abs(lastOpenAngle == 0f ? openAngle : lastOpenAngle)) <= 1f;
        float targetAngle = isFullyOpen ? 0f : GetOpenAngle(interactor);

        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        PlayDoorSound(targetAngle);
        animationRoutine = StartCoroutine(AnimateDoor(targetAngle));
    }

    private IEnumerator AnimateDoor(float targetAngle)
    {
        float elapsed = 0f;
        Quaternion startRotation = doorPivot.localRotation;
        Quaternion targetRotation = closedRotation * Quaternion.Euler(0f, targetAngle, 0f);

        if (!Mathf.Approximately(targetAngle, 0f))
            lastOpenAngle = targetAngle;

        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, openDuration));
            float easedT = t * t * (3f - 2f * t);
            doorPivot.localRotation = Quaternion.Slerp(startRotation, targetRotation, easedT);
            yield return null;
        }

        doorPivot.localRotation = targetRotation;
        animationRoutine = null;
    }

    private float GetOpenAngle(Transform interactor)
    {
        float signedAngle = Mathf.Abs(openAngle);

        if (!openAwayFromInteractor || interactor == null)
            return signedAngle;

        Vector3 localInteractorPosition = doorPivot.InverseTransformPoint(interactor.position);
        return localInteractorPosition.z >= 0f ? signedAngle : -signedAngle;
    }

    private float GetCurrentAngle()
    {
        Quaternion relativeRotation = Quaternion.Inverse(closedRotation) * doorPivot.localRotation;
        return Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.y);
    }

    private void EnsureAudioSource()
    {
        if (doorAudioSource == null)
            doorAudioSource = GetComponent<AudioSource>();

        if (doorAudioSource == null)
            doorAudioSource = gameObject.AddComponent<AudioSource>();

        doorAudioSource.playOnAwake = false;
        doorAudioSource.spatialBlend = 1f;
        doorAudioSource.dopplerLevel = 0f;
        doorAudioSource.minDistance = Mathf.Max(0.1f, minDistance);
        doorAudioSource.maxDistance = Mathf.Max(doorAudioSource.minDistance, maxDistance);
        doorAudioSource.loop = false;
    }

    private void PlayDoorSound(float targetAngle)
    {
        if (doorAudioSource == null)
            return;

        AudioClip clipToPlay = Mathf.Approximately(targetAngle, 0f) ? closeSound : openSound;
        if (clipToPlay == null || soundVolume <= 0f)
            return;

        doorAudioSource.PlayOneShot(clipToPlay, soundVolume);
    }

    private static Transform FindChildRecursive(Transform root, string containsName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);

            if (child.name.Contains(containsName))
                return child;

            Transform nestedChild = FindChildRecursive(child, containsName);
            if (nestedChild != null)
                return nestedChild;
        }

        return null;
    }
}
