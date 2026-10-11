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
        if (MainMenuController.OwnsClicks) return;   // ★1011 點擊由 MainMenuController 統一偵測
        Activate();
    }

    /// <summary>★1011 離開遊戲。滑鼠點擊與鍵盤操作（MainMenuController）都走這一個。</summary>
    public void Activate()
    {
        if (quitPanel != null)
        {
            quitPanel.OpenPanel();
            return;
        }

        GameFlow.QuitApplication();
    }
}