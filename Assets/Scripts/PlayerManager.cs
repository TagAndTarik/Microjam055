using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public Plane[] cameraPlanes { get; private set; }
    public DisappearBehavior _disappearComponent;

    public static PlayerManager PlayerManagerInstance { get; private set; }

    private void Awake()
    {
        if(PlayerManagerInstance != null && PlayerManagerInstance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            PlayerManagerInstance = this;
        }
    }
    private void Update()
    {
        cameraPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        if(_disappearComponent != null)
        {
            _disappearComponent.PerformDisappear(cameraPlanes);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Wacky"))
        {
            _disappearComponent = other.GetComponentInChildren<DisappearBehavior>();
        }
    }
}
