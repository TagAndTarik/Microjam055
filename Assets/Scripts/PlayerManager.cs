using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    [Header("Ambient Sound Volumes")]
    [SerializeField] private float outsideSoundVolume = 1.0f;
    [SerializeField] private float insideSoundVolume = 0.35f;

    public bool inHouse { get; private set; } = false;
    private Collider _previousActivatedTrigger;

    private void Start()
    {
        inHouse = false;
    }

     /*"if the previous activated trigger was the outside one, and player now triggered inside one, player has just crossed the boundary and is now inside"

    "if the previous activated trigger was the inside one, and player now triggered outside one, player has just crossed the boundary and is now outside"

    "if the last activated trigger is the same as the current one, player hasn't crossed any boundary so inside/outside status hasn't changed. */
    private void OnTriggerEnter(Collider other)
    {
        if(_previousActivatedTrigger == null)
        {
            return;
        }
        if(_previousActivatedTrigger.CompareTag("OutsideBox") && other.CompareTag("InsideBox"))
        {
            inHouse = true;
            GameManager.GameManagerInstance.ChangeAmbientNightVolume(insideSoundVolume);
        }

        else if(_previousActivatedTrigger.CompareTag("InsideBox") && other.CompareTag("OutsideBox"))
        {
            inHouse = false;
            GameManager.GameManagerInstance.ChangeAmbientNightVolume(outsideSoundVolume);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        _previousActivatedTrigger = other;
    }
}
