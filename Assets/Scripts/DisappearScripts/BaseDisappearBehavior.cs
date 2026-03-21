using UnityEngine;

public abstract class BaseDisappearBehavior : MonoBehaviour
{
    public bool initiated;
    public bool inView;
    public Renderer disappearRenderer;
    public GameObject ObjectToAppear;
    public PickupInteractable pickupInteractable;


    public abstract void PerformDisappear(Plane[] cameraPlanes);

    private void Start()
    {
        pickupInteractable.MakeObjectDisappear += StartDisappearing;
        initiated = false;

    }

    private void Update()
    {
        if (initiated)
        {
            PerformDisappear(PlayerManager.PlayerManagerInstance.cameraPlanes);
        }
    }
    public void StartDisappearing()
    {
        initiated = true;
    }
}
