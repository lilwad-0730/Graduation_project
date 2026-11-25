using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1125 玻璃館「按鍵線條畫動畫」的共用畫面（燭火 CandleLineKeyHint、鏡箱 CrateLineKeyHint 都用它）。
/// 一個實例＝一個提示：按鍵圖（up／down 兩張）＋三個往下流的小箭頭＋後面一圈暖色柔光，全是世界座標的 SpriteRenderer。
/// 只管「怎麼畫」：什麼時候出現、放在哪裡由呼叫的人決定（UpdatePresence 決定淡入淡出，Draw 擺位置＋播動畫）。
///
/// 動畫一圈 cycleSeconds：按鍵靜止 → 三個小箭頭由上往下一個接一個流進按鍵 → 按鍵換成「按下去」那張（小壓扁、柔光閃一下）→ 彈回。
/// 時間用 unscaledTime（不受子彈時間影響），面向鏡頭。
/// 素材：Assets/Resources/Hints/{keyboardAsset}_up／_down、{padAsset}_up／_down、CandleKey_chevron（共用）。
/// </summary>
public class LineKeyHintView
{
    public struct Style
    {
        public float keyWidth;          // 按鍵圖寬度（公尺）
        public float chevronWidth;      // 三個小箭頭的寬度（公尺）
        public float cycleSeconds, pressAt, pressSeconds;
        public Vector2 pressSquash;
        public float bobAmplitude;
        public Color glowColor;
        public float glowIdle, glowPress, glowScale;
        public int sortingOrder;
        /// <summary>★1125 廢墟：按鍵圖與小箭頭要染成的線色（深色線配淺色白天背景）。useTint＝false（預設）＝不染，維持原本的奶白色。</summary>
        public bool useTint;
        public Color tint;
    }

    private static Texture2D _glowTex;
    private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();

    private readonly Transform _parent;
    private readonly string _rootName, _kbAsset, _padAsset, _logTag;

    private Transform _root;
    private SpriteRenderer _glow, _key;
    private SpriteRenderer[] _chevrons;
    private Sprite _up, _down, _chev;
    private float _keyH, _chevH;
    private bool _built, _builtPad;

    /// <summary>0＝完全看不到、1＝完全出現。</summary>
    public float Presence;
    /// <summary>動畫相位偏移（每個提示不同步）。</summary>
    public float Phase;

    public LineKeyHintView(Transform parent, string rootName, string keyboardAsset, string padAsset, string logTag, float phase)
    {
        _parent = parent;
        _rootName = rootName;
        _kbAsset = keyboardAsset;
        _padAsset = padAsset;
        _logTag = logTag;
        Phase = phase;
    }

    /// <summary>淡入淡出：回傳「現在還看得到」。want＝目標出現度（0～1）；hide＝這一刻要很快收起來（用 hideSeconds），否則用 fadeSeconds。</summary>
    public bool UpdatePresence(float want, bool hide, float fadeSeconds, float hideSeconds)
    {
        float sec = want < Presence && hide ? hideSeconds : fadeSeconds;
        Presence = Mathf.MoveTowards(Presence, want, Time.unscaledDeltaTime / Mathf.Max(0.02f, sec));
        if (Presence <= 0.002f)
        {
            SetAll();
            return false;
        }
        return true;
    }

    /// <summary>擺位置＋播一格動畫（要先呼叫過 UpdatePresence 且回傳 true）。</summary>
    public void Draw(Style s, bool pad, Vector3 anchor, Camera cam)
    {
        if (!_built || pad != _builtPad) Build(s, pad);
        if (_up == null) { SetAll(); return; }

        // 位置：錨點上方、微微漂浮；面向鏡頭
        float now = Time.unscaledTime + Phase;
        float bob = Mathf.Sin(now * 2.1f) * s.bobAmplitude;
        _root.position = anchor + new Vector3(0f, bob, 0f);
        if (cam != null) _root.rotation = cam.transform.rotation;

        // ── 動畫：小箭頭一個接一個往下流進按鍵 → 按鍵按下去 → 彈回 ──
        float cyc = Mathf.Max(0.3f, s.cycleSeconds);
        float t = now % cyc;
        float spacing = _chevH * 1.55f;
        float drift = _chevH * 0.55f;
        float baseY = _keyH + _chevH * 0.35f;
        for (int k = 0; k < 3; k++)
        {
            // k＝0 最上面、k＝2 最靠近按鍵；上面的先亮，像一股往下流的力
            float u = (t - 0.04f - k * 0.17f) / 0.34f;
            SpriteRenderer sr = _chevrons[k];
            if (u > 0f && u < 1f)
            {
                float a = Mathf.Sin(u * Mathf.PI);
                float y = baseY + (2 - k) * spacing + drift * (1f - u);
                sr.transform.localPosition = new Vector3(0f, y, 0f);
                Color cc = s.useTint ? s.tint : Color.white;
                sr.color = new Color(cc.r, cc.g, cc.b, a * Presence);
                sr.enabled = true;
            }
            else sr.enabled = false;
        }

        float pu = (t - s.pressAt) / Mathf.Max(0.05f, s.pressSeconds);
        bool pressed = pu >= 0f && pu < 1f;
        _key.sprite = pressed && _down != null ? _down : _up;
        _key.transform.localScale = pressed ? new Vector3(s.pressSquash.x, s.pressSquash.y, 1f) : Vector3.one;
        Color baseC = s.useTint ? s.tint : Color.white;
        _key.color = new Color(baseC.r, baseC.g, baseC.b, Presence);
        _key.enabled = true;

        // 柔光：平時淡淡的、按下去那一下亮一閃再慢慢退
        float sinceP = t - s.pressAt;
        float pulse = sinceP >= 0f ? Mathf.Exp(-sinceP * 7f) : 0f;
        float ga = Presence * Mathf.Lerp(s.glowIdle, s.glowPress, pulse);
        _glow.color = new Color(s.glowColor.r, s.glowColor.g, s.glowColor.b, ga);
        _glow.enabled = ga > 0.002f;
    }

    /// <summary>全部關掉（看不到時）。</summary>
    public void SetAll()
    {
        if (_root == null) return;
        _key.enabled = false;
        _glow.enabled = false;
        foreach (SpriteRenderer sr in _chevrons) if (sr != null) sr.enabled = false;
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null;
        _built = false;
    }

    // ═══════════════════════════ 素材 ═══════════════════════════

    private Texture2D Tex(string name)
    {
        Texture2D t;
        if (TexCache.TryGetValue(name, out t) && t != null) return t;
        t = Resources.Load<Texture2D>("Hints/" + name);
        if (t == null) Debug.LogWarning(_logTag + "找不到 Assets/Resources/Hints/" + name + ".png");
        TexCache[name] = t;
        return t;
    }

    /// <summary>底部置中的 Sprite：寬度 widthMeters 公尺（上下兩張同尺寸的畫布，換圖不會跳）。</summary>
    private static Sprite MakeSprite(Texture2D t, float widthMeters)
    {
        if (t == null) return null;
        float ppu = t.width / Mathf.Max(0.05f, widthMeters);
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0f), ppu);
    }

    /// <summary>放射狀柔光：程式產生（跟 LineHintSign 同一種）。</summary>
    private static Sprite GlowSprite()
    {
        if (_glowTex == null)
        {
            const int N = 128;
            _glowTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            _glowTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);
                    _glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            _glowTex.Apply();
        }
        return Sprite.Create(_glowTex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 128);
    }

    private void Build(Style s, bool pad)
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        _built = true;
        _builtPad = pad;

        string k = pad ? _padAsset : _kbAsset;
        Texture2D tu = Tex(k + "_up"), td = Tex(k + "_down"), tc = Tex("CandleKey_chevron");
        _up = MakeSprite(tu, s.keyWidth);
        _down = MakeSprite(td, s.keyWidth);
        _chev = MakeSprite(tc, s.chevronWidth);
        _keyH = _up != null ? _up.bounds.size.y : s.keyWidth;
        _chevH = _chev != null ? _chev.bounds.size.y : 0.2f;

        GameObject root = new GameObject(_rootName);
        root.transform.SetParent(_parent, false);
        _root = root.transform;

        // 柔光：中心在按鍵中央
        GameObject g = new GameObject("Glow");
        g.transform.SetParent(_root, false);
        g.transform.localPosition = new Vector3(0f, _keyH * 0.5f, 0.01f);
        g.transform.localScale = Vector3.one * (s.keyWidth * s.glowScale);
        _glow = g.AddComponent<SpriteRenderer>();
        _glow.sprite = GlowSprite();
        _glow.sortingOrder = s.sortingOrder - 1;
        _glow.color = new Color(s.glowColor.r, s.glowColor.g, s.glowColor.b, 0f);

        GameObject kg = new GameObject("Key");
        kg.transform.SetParent(_root, false);
        _key = kg.AddComponent<SpriteRenderer>();
        _key.sprite = _up;
        _key.sortingOrder = s.sortingOrder;
        _key.color = new Color(1f, 1f, 1f, 0f);

        _chevrons = new SpriteRenderer[3];
        for (int i = 0; i < 3; i++)
        {
            GameObject cg = new GameObject("Chevron" + i);
            cg.transform.SetParent(_root, false);
            SpriteRenderer sr = cg.AddComponent<SpriteRenderer>();
            sr.sprite = _chev;
            sr.sortingOrder = s.sortingOrder;
            sr.color = new Color(1f, 1f, 1f, 0f);
            _chevrons[i] = sr;
        }
    }
}
