using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Night Sound Management")]
    [SerializeField] private AudioSource ambientNightSound;
    [SerializeField, Min(0f)] private float ambientNightFadeDuration = 1.75f;

    private Coroutine ambientNightFadeRoutine;
    
    public static GameManager GameManagerInstance; //static instance of the game manager, so that it can be accessed from other scripts

    private void Awake()
    {
        if(GameManagerInstance == null)
        {
            GameManagerInstance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    public void ChangeAmbientNightVolume(float volume)
    {
        if (ambientNightSound == null)
            return;

        float targetVolume = Mathf.Clamp01(volume);
        if (!gameObject.activeInHierarchy || ambientNightFadeDuration <= 0f)
        {
            ambientNightSound.volume = targetVolume;
            return;
        }

        if (ambientNightFadeRoutine != null)
            StopCoroutine(ambientNightFadeRoutine);

        ambientNightFadeRoutine = StartCoroutine(FadeAmbientNightVolume(targetVolume));
    }

    private System.Collections.IEnumerator FadeAmbientNightVolume(float targetVolume)
    {
        float startingVolume = ambientNightSound.volume;
        float elapsed = 0f;

        while (elapsed < ambientNightFadeDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / ambientNightFadeDuration);
            ambientNightSound.volume = Mathf.Lerp(startingVolume, targetVolume, progress);
            yield return null;
        }

        ambientNightSound.volume = targetVolume;
        ambientNightFadeRoutine = null;
    }
}
