using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class SettingsPopupButton : MonoBehaviour
{
    [SerializeField] private GameObject settingsPopup;
    [SerializeField] private bool showPopup = true;

    private void OnMouseDown()
    {
        SetPopupVisible(showPopup);
    }

    public void SetPopupVisible(bool isVisible)
    {
        if (settingsPopup != null)
        {
            settingsPopup.SetActive(isVisible);
        }
    }
}
