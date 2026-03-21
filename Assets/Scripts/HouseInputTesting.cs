using UnityEngine;
using UnityEngine.InputSystem;

public class HouseInputTesting : MonoBehaviour
{
    public InputSystem_Actions inputActions;
    private InputAction _lightOne;
    private InputAction _lightTwo;
    private InputAction _lightThree;
    private void Awake()
    {
        inputActions = new InputSystem_Actions();
    }

    
    private void FlickerLightSetOne(InputAction.CallbackContext ctx)
    {
        HouseManager.HouseManagerInstance.FlickerLightSet(0);
    }

    private void FlickerLightSetTwo(InputAction.CallbackContext ctx)
    {
        HouseManager.HouseManagerInstance.FlickerLightSet(1);
    }

    private void FlickerLightSetThree(InputAction.CallbackContext ctx)
    {
        HouseManager.HouseManagerInstance.FlickerLightSet(2);
    }

    private void OnEnable()
    {
        _lightOne = inputActions.Player.LightOne;
        _lightOne.Enable();
        _lightTwo = inputActions.Player.LightTwo;
        _lightTwo.Enable();
        _lightThree = inputActions.Player.LightThree;
        _lightThree.Enable();

        _lightOne.performed += FlickerLightSetOne;
        _lightTwo.performed += FlickerLightSetTwo;
        _lightThree.performed += FlickerLightSetThree;
    }

    private void OnDisable()
    {
        _lightOne.Disable();
        _lightTwo.Disable();
        _lightThree.Disable();
    }




}
