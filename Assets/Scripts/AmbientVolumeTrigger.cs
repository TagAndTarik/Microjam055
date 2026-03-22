using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class AmbientVolumeTrigger : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float targetVolume = 0.15f;
    [SerializeField] private bool triggerOnlyOnce = true;

    private bool hasTriggered;

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered && triggerOnlyOnce)
            return;

        if (other == null || other.GetComponentInParent<PlayerManager>() == null)
            return;

        if (GameManager.GameManagerInstance == null)
            return;

        GameManager.GameManagerInstance.ChangeAmbientNightVolume(targetVolume);

        if (triggerOnlyOnce)
            hasTriggered = true;
    }
}
