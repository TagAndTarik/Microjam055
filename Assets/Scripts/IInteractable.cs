using UnityEngine;

public interface IInteractable
{
    Font GetHoverMessageFont();
    int GetHoverMessageFontSize();
    Color GetHoverMessageColor();
    string GetHoverMessage();
    void SetFocused(bool focused);
    void Interact(Transform interactor);
}
