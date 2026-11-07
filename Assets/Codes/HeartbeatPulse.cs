using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★1107 全遊戲共用的「心跳語言」：咚—咚（一強一弱），各關只改快慢、顏色與強度，節奏語言一致。
///   用到的地方：水下力竭紅框、水下快沒氧氣的暗角、廢墟狼靠近的暗角、荒原下沉轉場的暗角。
/// </summary>
public static class HeartbeatPulse
{
    /// <summary>phase 0～1 的一個心跳週期 → 0～1 的強度（0.05 附近一大跳、0.30 附近一小跳）。</summary>
    public static float Curve(float phase)
    {
        phase -= Mathf.Floor(phase);
        float b1 = Mathf.Exp(-Mathf.Pow((phase - 0.05f) * 9f, 2f));
        float b2 = 0.6f * Mathf.Exp(-Mathf.Pow((phase - 0.30f) * 9f, 2f));
        return Mathf.Clamp01(b1 + b2);
    }

    /// <summary>標準 smoothstep：x 從 edge0 走到 edge1 時由 0 平滑升到 1（edge0 &gt; edge1 時反過來）。注意 Mathf.SmoothStep 不是這個意思。</summary>
    public static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>固定速度的心跳（速度不會變時用）。</summary>
    public static float Beat(float time, float beatsPerSecond)
    {
        return Curve(time * beatsPerSecond);
    }

    /// <summary>緊張度 0～1 → 每秒幾下。calm＝平靜時，panic＝最緊張時。</summary>
    public static float Bps(float intensity01, float calm = 0.9f, float panic = 2.2f)
    {
        return Mathf.Lerp(calm, panic, Mathf.Clamp01(intensity01));
    }
}

/// <summary>會變速的心跳計時器：速度改變時節奏不會「跳拍」。</summary>
public sealed class HeartbeatClock
{
    private float _phase;
    public float Phase { get { return _phase; } }

    public float Tick(float dt, float beatsPerSecond)
    {
        _phase += dt * beatsPerSecond;
        _phase -= Mathf.Floor(_phase);
        return HeartbeatPulse.Curve(_phase);
    }
}

/// <summary>
/// 全螢幕邊緣暗角，隨心跳一縮一縮。其他效果只要設定 Intensity（0～1）、Bps、Color 就行。
/// 畫在 sortingOrder 40：蓋住遊戲畫面，但在一般提示 UI 與文字卡（10000）之下。
/// </summary>
public class HeartbeatVignette : MonoBehaviour
{
    [Range(0f, 1f)] public float Intensity;
    public float Bps = 1f;
    public Color Color = new Color(0.02f, 0.01f, 0.03f, 1f);
    [Tooltip("最濃時的不透明度上限")]
    public float MaxAlpha = 0.45f;

    private Image _img;
    private RectTransform _rt;
    private Texture2D _tex;
    private Sprite _sprite;
    private readonly HeartbeatClock _clock = new HeartbeatClock();

    public static HeartbeatVignette Create(Transform parent, string name, int sortingOrder = 40)
    {
        GameObject go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        Canvas cv = go.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = sortingOrder;
        HeartbeatVignette hv = go.AddComponent<HeartbeatVignette>();
        hv.Build();
        return hv;
    }

    private void Build()
    {
        GameObject g = new GameObject("暗角");
        g.transform.SetParent(transform, false);
        _img = g.AddComponent<Image>();
        _img.raycastTarget = false;
        _rt = _img.rectTransform;
        _rt.anchorMin = Vector2.zero; _rt.anchorMax = Vector2.one;
        _rt.offsetMin = Vector2.zero; _rt.offsetMax = Vector2.zero;

        const int n = 128;
        _tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x / (float)(n - 1) - 0.5f) * 2f;
                float v = (y / (float)(n - 1) - 0.5f) * 2f;
                float r = Mathf.Sqrt(u * u + v * v) / 1.414f;
                float a = Mathf.SmoothStep(0.42f, 1f, r);
                _tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _tex.Apply();
        _sprite = Sprite.Create(_tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        _img.sprite = _sprite;
        _img.color = new Color(Color.r, Color.g, Color.b, 0f);
    }

    private void Update()
    {
        if (_img == null) return;
        float pulse = _clock.Tick(Time.unscaledDeltaTime, Bps);
        float k = Mathf.Clamp01(Intensity);
        float a = k * MaxAlpha * (0.55f + 0.45f * pulse);
        _img.color = new Color(Color.r, Color.g, Color.b, a);
        float s = 1.07f - 0.05f * pulse * k;           // 每跳一下，暗角往中間縮一點點
        _rt.localScale = new Vector3(s, s, 1f);
        _img.enabled = a > 0.002f;
    }

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
        if (_sprite != null) Destroy(_sprite);
    }
}
