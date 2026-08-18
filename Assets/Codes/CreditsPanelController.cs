using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 製作人員與來源面板控制器
/// 可捲動面板，預留製作人員、使用素材、音樂音效、字體、第三方套件與授權來源
/// </summary>
public class CreditsPanelController : MonoBehaviour
{
    [Header("UI 綁定")]
    public Button backButton;
    public MainMenuController mainMenuController;
    public CanvasGroup panelCanvasGroup;
    public ScrollRect scrollRect;

    private void Awake()
    {
        if (panelCanvasGroup == null) panelCanvasGroup = GetComponent<CanvasGroup>();
    }

    private void Start()
    {
        if (backButton != null) backButton.onClick.AddListener(ClosePanel);
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

        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1.0f; // 頂端開始
        }

        if (backButton != null)
        {
            backButton.Select();
        }
    }

    public void ClosePanel()
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
            mainMenuController.OnSubPanelClosed(3); // 3 代表「製作人員與來源」按鈕索引
        }
    }
}
