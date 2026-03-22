using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerManager : MonoBehaviour
{
    public Plane[] cameraPlanes { get; private set; }

    public static PlayerManager PlayerManagerInstance { get; private set; }

    

    private void Awake()
    {
        if(PlayerManagerInstance != null && PlayerManagerInstance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            PlayerManagerInstance = this;

        }

    }
    private void Update()
    {
        cameraPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        
    }

    private void OnTriggerEnter(Collider other)
    {
        
    }

    public void MovePlayer(Vector3 position)
    {
        GetComponent<SimpleFirstPersonController>().enabled = false;
        transform.position = position;
        StartCoroutine(Bruh());
    }

    IEnumerator Bruh()
    {
        yield return new WaitForSeconds(1.0f);
        GetComponent<SimpleFirstPersonController>().enabled = true;
    }

    
}
