using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerTesting : MonoBehaviour
{
    public Transform newSequenceLocation;
    public InputSystem_Actions actions;
    private InputAction movePlayer;

    private void Awake()
    {
        actions = new InputSystem_Actions();

    }

    private void MoveThisPlayer(InputAction.CallbackContext context)
    {
        Debug.Log("Moving Player");
        transform.position = newSequenceLocation.position;
    }

    private void OnEnable()
    {
        movePlayer = actions.Player.Move;
        movePlayer.Enable();
        movePlayer.performed += MoveThisPlayer;
    }

    private void OnDisable()
    {
        movePlayer.Disable();
        
    }
}
