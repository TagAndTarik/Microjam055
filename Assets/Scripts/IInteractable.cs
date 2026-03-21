using UnityEngine;

public interface IInteractable
{
    void SetFocused(bool focused);
    void Interact(Transform interactor);
}
