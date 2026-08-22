using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CreditsScrollableTextController : MonoBehaviour
{
    [Header("捲動文字")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform contentTextRect;
    [SerializeField] private TMP_Text contentText;
    [SerializeField] private RectTransform verticalScrollbarRect;

    [Header("Inspector 顯示設定")]
    [SerializeField, Range(300f, 900f), InspectorName("呈現高度")]
    private float displayHeight = 560f;

    private void Awake()
    {
        Configure();
    }

    private void OnEnable()
    {
        Configure();
    }



    private void Configure()
    {
        if (scrollRect == null || viewport == null || contentTextRect == null || contentText == null)
        {
            return;
        }

        contentText.enableWordWrapping = true;
        contentText.overflowMode = TextOverflowModes.Overflow;

        ApplyHeight(displayHeight);

        if (Application.isPlaying)
        {
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    public void ApplyHeight(float height)
    {
        displayHeight = Mathf.Clamp(height, 300f, 900f);

        RectTransform scrollViewRect = scrollRect != null ? scrollRect.transform as RectTransform : null;
        if (scrollViewRect != null)
        {
            scrollViewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, displayHeight);
        }

        if (viewport != null)
        {
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, displayHeight);
        }

        if (verticalScrollbarRect != null)
        {
            verticalScrollbarRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, displayHeight);
        }

        if (contentText != null && contentTextRect != null)
        {
            contentText.ForceMeshUpdate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentTextRect);
        }

        Canvas.ForceUpdateCanvases();

        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyHeight(displayHeight);
    }
#endif
}
