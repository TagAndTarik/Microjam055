using System.Collections;
using UnityEngine;

public class DungeonTiming : MonoBehaviour
{
    public float MaxTimeInDungeon = 60f;

    public float t;
    bool startedTimer = false;
    bool completedTimer = false;
    public Animator screenFadeAnimator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        t = 0;
    }

    // Update is called once per frame
    void Update()
    {
        if (startedTimer && !completedTimer)
        {
            t += Time.deltaTime;    
            if( t > MaxTimeInDungeon )
            {
                startedTimer = false;
                completedTimer = true;
                t = 0;
                DoSomething();
            }
        }
    }

    private void DoSomething()
    {
        Debug.Log("Did Something after 60 seconds");
        //PlayerManager.PlayerManagerInstance.MovePlayer(HouseManager.HouseManagerInstance.gameObject.GetComponent<HouseInputTesting>().newSequenceLocation.position);
        StartCoroutine(MovePlayerRoutine());
    }
    public void StartTimer()
    {
        startedTimer = true;
        Debug.Log("HI!!!!");

    }

    IEnumerator MovePlayerRoutine()
    {
        screenFadeAnimator.Play("MakeBlack");
        yield return new WaitForSeconds(1.1f);
        PlayerManager.PlayerManagerInstance.MovePlayer(HouseManager.HouseManagerInstance.gameObject.GetComponent<HouseInputTesting>().newSequenceLocation.position);
        yield return new WaitForSeconds(1.0f);
        screenFadeAnimator.Play("RemoveBlack");
        
    }
}
