using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

/// <summary>
/// 按鈕「破碎失真介面」效果
/// 包含預設低亮度灰白、選取時高亮 + 暗紅斷裂線段/微弱外框光 + 0.15~0.3s 俐落錯位動畫
/// 保證繁體中文字體 100% 可讀性
/// </summary>
public class MenuGlitchEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("UI 元件引用")]
    public TMP_Text buttonText;
    public Image outlineBorder;
    public Image redAccentLine;
    public RectTransform textRectTransform;

    [Header("色彩設定")]
    public Color normalTextColor = new Color(0.7f, 0.72f, 0.75f, 0.85f);
    public Color selectedTextColor = new Color(1.0f, 0.98f, 0.95f, 1.0f);
    public Color normalBorderColor = new Color(0.3f, 0.35f, 0.4f, 0.3f);
    public Color selectedBorderColor = new Color(0.85f, 0.2f, 0.2f, 0.85f); // 暗紅警示光線

    [Header("選取錯位動畫設定")]
    public float transitionDuration = 0.2f;
    public float glitchOffsetAmount = 6.0f;

    private bool _isSelected = false;
    private Coroutine _glitchCoroutine;
    private Vector2 _originalTextPos;
    private MainMenuController _mainMenuController;

    private void Awake()
    {
        if (textRectTransform != null)
        {
            _originalTextPos = textRectTransform.anchoredPosition;
        }
        _mainMenuController = GetComponentInParent<MainMenuController>();
        SetState(false, true);
    }

    public void SetSelectedState(bool selected, bool instant = false)
    {
        if (_isSelected == selected && !instant) return;
        _isSelected = selected;
        SetState(selected, instant);
    }

    private void SetState(bool selected, bool instant)
    {
        if (buttonText != null)
        {
            buttonText.color = selected ? selectedTextColor : normalTextColor;
        }

        if (outlineBorder != null)
        {
            outlineBorder.color = selected ? selectedBorderColor : normalBorderColor;
        }

        if (redAccentLine != null)
        {
            redAccentLine.gameObject.SetActive(selected);
        }

        if (selected && !instant)
        {
            if (_glitchCoroutine != null) StopCoroutine(_glitchCoroutine);
            _glitchCoroutine = StartCoroutine(PlayGlitchAnim());
        }
        else
        {
            if (textRectTransform != null)
            {
                textRectTransform.anchoredPosition = _originalTextPos;
            }
        }
    }

    private IEnumerator PlayGlitchAnim()
    {
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / transitionDuration;

            // 產生兩次輕微的平移錯位 (Subtle offset jitter)
            if (t < 0.4f && textRectTransform != null)
            {
                float offsetX = (Random.value - 0.5f) * glitchOffsetAmount * 2.0f;
                textRectTransform.anchoredPosition = _originalTextPos + new Vector2(offsetX, 0f);
            }
            else if (textRectTransform != null)
            {
                textRectTransform.anchoredPosition = _originalTextPos;
            }
            yield return null;
        }
        if (textRectTransform != null)
        {
            textRectTransform.anchoredPosition = _originalTextPos;
        }
    }

    // 事件系統介面實作 (Mouse, Keyboard, Gamepad 統一路由)
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_mainMenuController != null)
        {
            _mainMenuController.OnButtonHoverOrSelect(gameObject);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 移出時若無 Focus 則自動恢復
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (_mainMenuController != null)
        {
            _mainMenuController.OnButtonHoverOrSelect(gameObject);
        }
    }

    public void OnDeselect(BaseEventData eventData)
    {
    }
}
