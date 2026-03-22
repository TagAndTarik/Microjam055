using UnityEngine;

public class ObjectInViewDisappearBehavior : BaseDisappearBehavior
{
    public GameObject[] ObjectsToDisappear;
    public override void Perform(Plane[] cameraPlanes)
    {
        if(inView)
        {
            if(!GeometryUtility.TestPlanesAABB(cameraPlanes, disappearRenderer.bounds))
            {
                disappearRenderer.gameObject.SetActive(false);
                for(int i = 0; i < ObjectsToDisappear.Length; i++)
                {
                    if(ObjectsToDisappear[i] != null)
                        ObjectsToDisappear[i].SetActive(false);
                }
                ActivateAppearTargets();
                base.SpawnSFX();

            }
        }

        else
        {
            if(GeometryUtility.TestPlanesAABB(cameraPlanes, disappearRenderer.bounds))
            {
                inView = true;
            }


        }
    }


}
