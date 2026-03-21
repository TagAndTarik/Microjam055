using UnityEngine;

public class DestroySelf : MonoBehaviour
{
    [Header("Timer")]
    public float time_til_death;

    private float t;

    // Start is called before the first frame update
    void Start()
    {
        t = 0;
    }

    // Update is called once per frame
    void Update()
    {
        t += Time.deltaTime;
        if (t > time_til_death)
        {
            Destroy(gameObject);
        }
    }
}