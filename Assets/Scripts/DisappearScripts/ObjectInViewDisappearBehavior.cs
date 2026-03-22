using UnityEngine;

public class ObjectInViewDisappearBehavior : BaseDisappearBehavior
{
    public override void Perform(Plane[] cameraPlanes)
    {
        if(inView)
        {
            if(!GeometryUtility.TestPlanesAABB(cameraPlanes, disappearRenderer.bounds))
            {
                disappearRenderer.gameObject.SetActive(false);
                ObjectToAppear.SetActive(true);
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
