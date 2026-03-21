using UnityEngine;

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

    public void SetLightSetEnabled(int lightIndex, bool isEnabled)
    {
        if (lightIndex < 0 || lightIndex >= lightSets.Length)
        {
            Debug.LogError("Invalid light index: " + lightIndex);
            return;
        }

        GameObject lightSet = lightSets[lightIndex];
        if (lightSet != null)
            lightSet.SetActive(isEnabled);
    }

    public void SetAllLightsEnabled(bool isEnabled)
    {
        for (int i = 0; i < lightSets.Length; i++)
        {
            GameObject lightSet = lightSets[i];
            if (lightSet != null)
                lightSet.SetActive(isEnabled);
        }
    }

    public void TurnOffAllLights()
    {
        SetAllLightsEnabled(false);
    }



}
