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
/// 全螢幕邊緣暗角。兩種用法：
///  1) 預設（PulseRingAlpha＝0）：整個暗角隨心跳一縮一縮（水下缺氧用）。
///  2) 兩層（PulseRingAlpha＞0）：底層是「穩穩不動」的深色暗角，可由四個角慢慢往中間擴大（Spread）；
///     心跳只讓外圍一圈「較淺的顏色」（PulseColor）一閃一閃（Pulse 控制閃多少）——黑的不閃，閃的是淡一點的那圈。
/// 畫在 sortingOrder 40：蓋住遊戲畫面，但在一般提示 UI 與文字卡（10000）之下。
/// </summary>
public class HeartbeatVignette : MonoBehaviour
{
    [Range(0f, 1f)] public float Intensity;
    public float Bps = 1f;
    public Color Color = new Color(0.02f, 0.01f, 0.03f, 1f);
    [Tooltip("最濃時的不透明度上限")]
    public float MaxAlpha = 0.45f;

    [Tooltip("心跳起伏的程度：0＝不閃，1＝完整")]
    [Range(0f, 1f)] public float Pulse = 1f;
    [Tooltip("暗角蔓延程度：0＝只有四個角，1＝整圈邊緣")]
    [Range(0f, 1f)] public float Spread = 1f;
    [Tooltip("兩層模式：閃動外圈的顏色（較淺）")]
    public Color PulseColor = new Color(0.5f, 0.08f, 0.1f, 1f);
    [Tooltip("兩層模式：外圈閃動最亮時的不透明度；0＝不用兩層模式")]
    public float PulseRingAlpha = 0f;

    [Tooltip("底層黑邊在距離多遠時就完全不透明（1＝只在最角落才全黑；越小黑得越實）")]
    [Range(0.6f, 1f)] public float SolidAt = 1f;

    private const int N = 64;
    private float _builtSolid = -1f;
    private Image _img, _ring;
    private RectTransform _rt, _ringRt;
    private Texture2D _tex, _ringTex;
    private Sprite _sprite, _ringSprite;
    private Color32[] _buf;
    private float _builtSpread = -1f;
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

    private Image MakeLayer(string name, out RectTransform rt)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(transform, false);
        Image img = g.AddComponent<Image>();
        img.raycastTarget = false;
        rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    private void Build()
    {
        _img = MakeLayer("暗角", out _rt);
        _tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.filterMode = FilterMode.Bilinear;
        _buf = new Color32[N * N];
        _sprite = Sprite.Create(_tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        _img.sprite = _sprite;
        _img.color = new Color(Color.r, Color.g, Color.b, 0f);
        RebuildBase(Spread);

        // 外圈（閃動用，較淺的顏色；形狀固定）
        _ring = MakeLayer("閃動外圈", out _ringRt);
        _ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _ringTex.wrapMode = TextureWrapMode.Clamp;
        _ringTex.filterMode = FilterMode.Bilinear;
        Color32[] rb = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float rho = Rho(x, y);
                byte a = (byte)(255f * HeartbeatPulse.Smooth(0.64f, 0.97f, rho));   // 閃動在外圈（角落到邊緣一小段）
                rb[y * N + x] = new Color32(255, 255, 255, a);
            }
        _ringTex.SetPixels32(rb);
        _ringTex.Apply();
        _ringSprite = Sprite.Create(_ringTex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        _ring.sprite = _ringSprite;
        _ring.color = new Color(PulseColor.r, PulseColor.g, PulseColor.b, 0f);
    }

    /// <summary>距畫面中心的正規化距離：邊緣正中＝0.707、四個角＝1。</summary>
    private static float Rho(int x, int y)
    {
        float u = (x / (float)(N - 1) - 0.5f) * 2f;
        float v = (y / (float)(N - 1) - 0.5f) * 2f;
        return Mathf.Sqrt(u * u + v * v) / 1.414f;
    }

    // 底層暗角：從 inner（開始變暗的距離）到四個角完全不透明。Spread 0→inner 0.86（只有角落）、1→0.45（整圈邊緣）
    private void RebuildBase(float spread)
    {
        float inner = Mathf.Lerp(0.86f, 0.45f, Mathf.Clamp01(spread));
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float a = HeartbeatPulse.Smooth(inner, Mathf.Max(inner + 0.05f, SolidAt), Rho(x, y));
                _buf[y * N + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
        _tex.SetPixels32(_buf);
        _tex.Apply();
        _builtSpread = spread;
        _builtSolid = SolidAt;
    }

    private void Update()
    {
        if (_img == null) return;
        float pulse = _clock.Tick(Time.unscaledDeltaTime, Bps);
        float k = Mathf.Clamp01(Intensity);
        bool twoLayer = PulseRingAlpha > 0.001f;

        // 蔓延程度改變才重畫底圖（每 0.02 一格，64×64 很便宜）
        float q = Mathf.Round(Mathf.Clamp01(Spread) * 50f) / 50f;
        if (!Mathf.Approximately(q, _builtSpread) || !Mathf.Approximately(SolidAt, _builtSolid)) RebuildBase(q);

        float a, s;
        if (twoLayer)
        {
            a = k * MaxAlpha;                         // 底層：穩穩的，不閃
            s = 1f;
        }
        else
        {
            a = k * MaxAlpha * (1f - 0.28f * Pulse + 0.28f * Pulse * pulse);
            s = 1.07f - 0.05f * pulse * k * Pulse;    // 每跳一下，暗角往中間縮一點點
        }
        _img.color = new Color(Color.r, Color.g, Color.b, a);
        _rt.localScale = new Vector3(s, s, 1f);
        _img.enabled = a > 0.002f;

        if (_ring != null)
        {
            float ra = twoLayer ? PulseRingAlpha * k * Pulse * pulse : 0f;
            _ring.color = new Color(PulseColor.r, PulseColor.g, PulseColor.b, ra);
            float rs = 1.07f - 0.05f * pulse;
            _ringRt.localScale = new Vector3(rs, rs, 1f);
            _ring.enabled = ra > 0.002f;
        }
    }

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
        if (_sprite != null) Destroy(_sprite);
        if (_ringTex != null) Destroy(_ringTex);
        if (_ringSprite != null) Destroy(_ringSprite);
    }
}
