using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 離開遊戲確認彈窗控制器
/// 詢問玩家是否確定離開，提供「確定」與「取消」
/// </summary>
public class QuitPanelController : MonoBehaviour
{
    [Header("UI 綁定")]
    public Button confirmButton;
    public Button cancelButton;
    public MainMenuController mainMenuController;
    public CanvasGroup panelCanvasGroup;

    private void Awake()
    {
        if (panelCanvasGroup == null) panelCanvasGroup = GetComponent<CanvasGroup>();
    }

    private void Start()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmQuit);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelQuit);
    }

    public void OpenPanel()
    {
        gameObject.SetActive(true);
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        if (cancelButton != null)
        {
            cancelButton.Select();
        }
    }

    private void OnConfirmQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnCancelQuit()
    {
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);

        if (mainMenuController != null)
        {
            mainMenuController.OnSubPanelClosed(2); // 2 代表「離開」按鈕索引
        }
    }
}
