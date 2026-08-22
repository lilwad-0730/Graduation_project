using UnityEngine;

/// <summary>
/// Handles clicks on the sprite-based quit button.
/// Opens the existing confirmation panel when available.
/// </summary>
public sealed class QuitGameOnClick : MonoBehaviour
{
    [SerializeField] private QuitPanelController quitPanel;

    private void OnMouseDown()
    {
        if (quitPanel != null)
        {
            quitPanel.OpenPanel();
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}