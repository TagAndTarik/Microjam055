# Assets/Scripts Overview

## Files

- `SimpleFirstPersonController.cs`: Handles player movement, mouse look, the on-screen crosshair, and interactable detection/activation.
- `IInteractable.cs`: Defines the small interface interactable objects implement so the player can focus and use them.
- `DoorHandleInteractable.cs`: Turns a door handle into an interactable, plays the handle-turn animation, and forwards interaction to the door controller.
- `DoorController.cs`: Opens and closes the door mesh, including choosing the swing direction based on where the player is standing.
- `InteractableOutline.cs`: Creates and toggles the outline mesh copies used to visually highlight the currently focused interactable.
- `InteractableOutline.shader`: Renders the subtle white silhouette outline used by `InteractableOutline.cs`.

## Notes


