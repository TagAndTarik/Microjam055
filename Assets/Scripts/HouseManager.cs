using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;

public class HouseManager : MonoBehaviour
{

    [SerializeField]
    private GameObject[] lightSets;
    
    public static HouseManager HouseManagerInstance; //static instance of the house manager, so that it can be accessed from other scripts

    private void Awake()
    {
        if(HouseManagerInstance == null)
        {
            HouseManagerInstance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void FlickerLightSet(int lightIndex)
    {
        if(lightIndex >= 0 && lightIndex < lightSets.Length)
        {
            lightSets[lightIndex].SetActive(!lightSets[lightIndex].activeSelf);
        }
        else
        {
            Debug.LogError("Invalid light index: " + lightIndex);
        }
    }



}
