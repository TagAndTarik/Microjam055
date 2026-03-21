using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    [Header("Ambient Sound Volumes")]
    [SerializeField] private float outsideSoundVolume = 1.0f;
    [SerializeField] private float insideSoundVolume = 0.35f;

    

    

    public bool inHouse { get; private set; } = false;
    public Plane[] cameraPlanes { get; private set; }
    private Collider _previousActivatedTrigger;
    public DisappearBehavior _disappearComponent;

    private void Start()
    {
        inHouse = false;
    }

    private void Update()
    {
        cameraPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        if(_disappearComponent != null)
        {
            _disappearComponent.PerformDisappear(cameraPlanes);
        }
    }

    /*"if the previous activated trigger was the outside one, and player now triggered inside one, player has just crossed the boundary and is now inside"

   "if the previous activated trigger was the inside one, and player now triggered outside one, player has just crossed the boundary and is now outside"

   "if the last activated trigger is the same as the current one, player hasn't crossed any boundary so inside/outside status hasn't changed. */
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Wacky"))
        {
            _disappearComponent = other.GetComponentInChildren<DisappearBehavior>();
        }
        if(_previousActivatedTrigger == null)
        {
            return;
        }
        else if(_previousActivatedTrigger.CompareTag("OutsideBox") && other.CompareTag("InsideBox"))
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
        if (other.CompareTag("Wacky"))
        {
            _disappearComponent = null;
        }

        else if(other.CompareTag("InsideBox") || other.CompareTag("OutsideBox"))
            _previousActivatedTrigger = other;
    }
}
