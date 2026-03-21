using UnityEngine;

public interface IInteractable
{
    string GetHoverMessage();
    void SetFocused(bool focused);
    void Interact(Transform interactor);
}
