using UnityEngine;

public class OutOfViewDisappearBehavior : BaseDisappearBehavior
{
    public GameObject[] objectsToDisappear;
    public override void Perform(Plane[] cameraPlanes)
    {
        if (!GeometryUtility.TestPlanesAABB(cameraPlanes, disappearRenderer.bounds))
        {
            disappearRenderer.gameObject.SetActive(false);
            for(int i = 0; i < objectsToDisappear.Length; i++)
            {
                if(objectsToDisappear[i] != null)
                    objectsToDisappear[i].SetActive(false);
            }
            ActivateAppearTargets();
            base.SpawnSFX();
        }
    }
}
