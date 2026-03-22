using UnityEngine;

public abstract class BaseDisappearBehavior : MonoBehaviour
{
    public bool initiated;
    public bool inView;
    public Renderer disappearRenderer;
    public GameObject ObjectToAppear;
    public PickupInteractable pickupInteractable;


    public abstract void Perform(Plane[] cameraPlanes);

    private void Start()
    {
        pickupInteractable.MakeObjectDisappear += StartDisappearing;
        initiated = false;

    }

    private void Update()
    {
        if (initiated)
        {
            Perform(PlayerManager.PlayerManagerInstance.cameraPlanes);
        }
    }
    public virtual void StartDisappearing()
    {
        initiated = true;
    }
}
