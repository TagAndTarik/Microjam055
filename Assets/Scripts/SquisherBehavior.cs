using UnityEngine;

public class SquisherBehavior : MonoBehaviour
{
    public bool move = false;

    public Transform targetPos;
    public float speed;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (move)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos.position, speed * Time.deltaTime);
            if(transform.position == targetPos.position && !PlayerManager.PlayerManagerInstance.dead)
            {
                PlayerManager.PlayerManagerInstance.dead = true;
                PlayerManager.PlayerManagerInstance.gameObject.GetComponent<SimpleFirstPersonController>().EndGame();
            }
        }
    }


    private void OnEnable()
    {
        move = true;
    }
}
