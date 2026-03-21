using UnityEngine;

public class DisappearBehavior : MonoBehaviour
{
    public bool inView;

    public Renderer disappearRenderer;
    public GameObject ObjectToAppear;
    public void PerformDisappear(Plane[] cameraPlanes)
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
