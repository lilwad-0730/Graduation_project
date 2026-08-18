using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 控制主選單背景深邃虛無空間的光影緩慢「呼吸」效果 (4~6 秒循環)
/// 影響背景暗紅光強度、霧氣可見度與漂浮記憶碎片，保持平滑 Ease In Out
/// </summary>
public class MenuBackgroundBreath : MonoBehaviour
{
    [Header("呼吸時間設定 (秒)")]
    [Tooltip("一個完整明暗循環的時間 (預設 5.0 秒)")]
    public float cycleDuration = 5.0f;

    [Header("背景組件綁定")]
    public CanvasGroup backgroundCanvasGroup;
    public Image darkRedWarningGlow;
    public CanvasGroup mistParticlesCanvasGroup;
    public CanvasGroup memoryFragmentsCanvasGroup;

    [Header("透明度與強度範圍")]
    [Range(0f, 1f)] public float minRedGlowAlpha = 0.15f;
    [Range(0f, 1f)] public float maxRedGlowAlpha = 0.45f;

    [Range(0f, 1f)] public float minMistAlpha = 0.2f;
    [Range(0f, 1f)] public float maxMistAlpha = 0.6f;

    [Range(0f, 1f)] public float minFragmentAlpha = 0.3f;
    [Range(0f, 1f)] public float maxFragmentAlpha = 0.75f;

    private float _timer = 0f;

    private void Update()
    {
        if (cycleDuration <= 0f) return;

        _timer += Time.deltaTime;
        float progress = Mathf.Repeat(_timer / cycleDuration, 1.0f);

        // 使用 PingPong + SmoothStep 計算平滑 Ease In Out 曲線 (0 -> 1 -> 0)
        float sinVal = Mathf.Sin(progress * Mathf.PI * 2.0f);
        float normalized = (sinVal + 1.0f) * 0.5f; // 0 ~ 1
        float smoothCurve = Mathf.SmoothStep(0f, 1f, normalized);

        // 套用暗紅警示光呼吸
        if (darkRedWarningGlow != null)
        {
            Color c = darkRedWarningGlow.color;
            c.a = Mathf.Lerp(minRedGlowAlpha, maxRedGlowAlpha, smoothCurve);
            darkRedWarningGlow.color = c;
        }

        // 套用霧氣透明度呼吸
        if (mistParticlesCanvasGroup != null)
        {
            mistParticlesCanvasGroup.alpha = Mathf.Lerp(minMistAlpha, maxMistAlpha, smoothCurve);
        }

        // 套用記憶碎片透明度呼吸
        if (memoryFragmentsCanvasGroup != null)
        {
            memoryFragmentsCanvasGroup.alpha = Mathf.Lerp(minFragmentAlpha, maxFragmentAlpha, smoothCurve);
        }
    }
}
