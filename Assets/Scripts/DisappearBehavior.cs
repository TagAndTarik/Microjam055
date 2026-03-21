using UnityEngine;

public class DisappearBehavior : MonoBehaviour
{
    public bool inView;

    public Renderer disappearRenderer;
    public void PerformDisappear(Plane[] cameraPlanes)
    {
        if(inView)
        {
            if(!GeometryUtility.TestPlanesAABB(cameraPlanes, disappearRenderer.bounds))
            {
                disappearRenderer.gameObject.SetActive(false);
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
