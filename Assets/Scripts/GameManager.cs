using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Night Sound Management")]
    [SerializeField] private AudioSource ambientNightSound;
    
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
        ambientNightSound.volume = volume;
    }
}
