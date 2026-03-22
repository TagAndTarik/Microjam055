using UnityEngine;

public abstract class BaseDisappearBehavior : MonoBehaviour
{
    public bool initiated;
    public bool inView;
    private bool madeSFX = false;
    public Renderer disappearRenderer;
    public GameObject ObjectToAppear;
    public PickupInteractable pickupInteractable;
    public OilInteractable oilInteractable;


    public abstract void Perform(Plane[] cameraPlanes);

    private void Start()
    {
        if(pickupInteractable!= null)
        {
            pickupInteractable.MakeObjectDisappear += StartDisappearing;
        }

        else if(oilInteractable != null)
        {
            oilInteractable.MakeObjectDisappear += StartDisappearing;
        }

        else
        {
            Debug.LogError("No event to trigger disappearance assigned to " + gameObject.name);
        }
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

    public void SpawnSFX()
    {
        if (!madeSFX)
        {
            madeSFX = true;
            GameObject sfxToCreate = Resources.Load<GameObject>("SFXObjects/ScaryChimesSFX");
            Instantiate(sfxToCreate, transform.position, Quaternion.identity);
        }
    }
}
