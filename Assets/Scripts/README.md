# Assets/Scripts Overview

## Files

- `SimpleFirstPersonController.cs`: Handles player movement, mouse look, the on-screen crosshair, and interactable detection/activation.
- `IInteractable.cs`: Defines the small interface interactable objects implement so the player can focus and use them.
- `DoorHandleInteractable.cs`: Turns a door handle into an interactable, plays the handle-turn animation, and forwards interaction to the door controller.
- `DoorController.cs`: Opens and closes the door mesh, including choosing the swing direction based on where the player is standing.
- `HeldItemSocket.cs`: Creates the camera-space hold anchor and overlay camera used to present held objects in the lower-right of the screen.
- `InteractableOutline.cs`: Creates and toggles the outline mesh copies used to visually highlight the currently focused interactable.
- `InteractableOutline.shader`: Renders the subtle white silhouette outline used by `InteractableOutline.cs`.
- `PickupInteractable.cs`: Makes an object reusable as a pickup, disables its collisions while held, and presents it in the held-item camera.

## Notes

- Interactables can now expose per-object hover prompt text, font, font size, and color, which the player controller shows below the crosshair while that object is focused.


