using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public Plane[] cameraPlanes { get; private set; }

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
    }

    private void OnTriggerEnter(Collider other)
    {
        
    }
}
