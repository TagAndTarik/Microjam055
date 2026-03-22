using UnityEngine;

public class OutOfViewDisappearBehavior : BaseDisappearBehavior
{
    public override void Perform(Plane[] cameraPlanes)
    {
        if (!GeometryUtility.TestPlanesAABB(cameraPlanes, disappearRenderer.bounds))
        {
            disappearRenderer.gameObject.SetActive(false);
            ObjectToAppear.SetActive(true);
        }
    }
}
