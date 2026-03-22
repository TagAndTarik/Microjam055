using System;
using UnityEngine;

public class MakeAppear : BaseDisappearBehavior
{
    public bool completedAnimation;
    public float speed;
    public Transform targetLocation;
    public float timeBeforeMovement = 0.5f;

    private float t;

    public override void Perform(Plane[] cameraPlanes)
    {
        if (!GeometryUtility.TestPlanesAABB(cameraPlanes, ObjectToAppear.GetComponentInChildren<Renderer>().bounds))
        {
            ObjectToAppear.SetActive(true);
            
        }

        else if (!completedAnimation)
        {
            SpawnSFX();
            if (t < timeBeforeMovement)
            {
                t += Time.deltaTime;
                return;
            }
            ObjectToAppear.transform.position = Vector3.MoveTowards(ObjectToAppear.transform.position, targetLocation.position, speed * Time.deltaTime);
            if(ObjectToAppear.transform.position == targetLocation.position)
            {
                completedAnimation = true;
                ObjectToAppear.SetActive(false);
                initiated = false;
            }
        }
    }

    public override void StartDisappearing()
    {
        base.StartDisappearing();
    }

    public override void SpawnSFX()
    {
        if (!madeSFX)
        {
            madeSFX = true;
            GameObject sfxToCreate = Resources.Load<GameObject>("SFXObjects/ScaryHowlSFX");
            Instantiate(sfxToCreate, transform.position, Quaternion.identity);
        }
    }
}
