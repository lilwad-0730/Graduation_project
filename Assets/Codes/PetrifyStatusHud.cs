using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1024 抗敵能力狀態提示：荒原按住 S 變成石頭的那段時間，畫面左上角亮出來，讓玩家知道「這是她的能力」。
///
/// 樣子（跟遊戲其他提示同一套：骨白字、深藍陰影、手寫感的字型、線條畫）：
///   ・左邊一顆「手繪線條的石頭」（骨白描邊、微微抖動的筆觸、幾道裂痕、左上一小段高光，內部淡淡沙色）
///   ・右邊兩行字：「抗敵能力」＋「抗風・擋鳥　放開 S 解除」
///   ・底下不是牌子，是一片由左往右淡掉的深藍暈影＋一條淡淡的骨白細線，像繪本頁邊的註記
///   被風吹成石頭的被動石化則顯示「石化中／被風吹成石頭」。
/// 字型跟文字卡同一個（StoryCardPlayer 的字型，找不到才用 TMP 預設）。淡入淡出，石頭輕輕呼吸，不搶畫面。
///
/// 只在有 PlayerPetrification、而且這個場景允許石化（荒原）時自己掛上；場景不動。開關：PetrifyStatusHud.Enabled。
/// </summary>
[DisallowMultipleComponent]
public class PetrifyStatusHud : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("文字")]
    public string braceTitle = "抗敵能力";
    public string braceSub = "抗風・擋鳥　放開 S 解除";
    public string passiveTitle = "石化中";
    public string passiveSub = "被風吹成石頭";

    [Header("位置與大小（1920×1080 設計座標）")]
    public Vector2 margin = new Vector2(40f, 34f);
    public Vector2 size = new Vector2(520f, 104f);
    public float fadeSeconds = 0.22f;

    [Header("顏色（跟 InputHint 同一套）")]
    public Color boneColor = new Color(0.9098f, 0.8784f, 0.8235f, 1f);
    public Color shadowColor = new Color(0.04f, 0.05f, 0.10f, 0.55f);
    public Color backingColor = new Color(0.04f, 0.05f, 0.10f, 0.5f);

    private PlayerPetrification _pet;
    private float _nextLookup;
    private CanvasGroup _group;
    private RectTransform _icon;
    private TextMeshProUGUI _title, _sub;
    private float _alpha;
    private bool _fontReady;
    private static Sprite _backingSprite, _stoneSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedStatic;
        SceneManager.sceneLoaded += OnSceneLoadedStatic;
        TryInstall();
    }

    private static void OnSceneLoadedStatic(Scene s, LoadSceneMode m) { TryInstall(); }

    private static void TryInstall()
    {
        if (!Enabled) return;
        PlayerPetrification pet = Object.FindAnyObjectByType<PlayerPetrification>();
        if (pet == null || !pet.IsPetrifyAllowedInCurrentScene()) return;
        if (Object.FindAnyObjectByType<PetrifyStatusHud>() != null) return;
        new GameObject("PetrifyStatusHud (自動生成)").AddComponent<PetrifyStatusHud>();
    }

    private void Awake() { Build(); }

    private void Update()
    {
        if (_group == null) return;
        if (_pet == null && Time.unscaledTime >= _nextLookup)
        {
            _nextLookup = Time.unscaledTime + 0.5f;
            _pet = Object.FindAnyObjectByType<PlayerPetrification>();
        }

        bool bracing = _pet != null && _pet.isPetrified && _pet.IsBracing;
        bool passive = _pet != null && _pet.isPetrified && !_pet.IsBracing;
        bool show = Enabled && (bracing || passive);
        if (show)
        {
            if (!_fontReady) ApplyFont();
            _title.text = bracing ? braceTitle : passiveTitle;
            _sub.text = bracing ? braceSub : passiveSub;
        }

        float target = show ? 1f : 0f;
        _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.02f, fadeSeconds));
        _group.alpha = _alpha;
        if (_icon != null && _alpha > 0.01f)
        {
            float pulse = 1f + 0.045f * Mathf.Sin(Time.unscaledTime * 2.6f);   // 石頭輕輕呼吸
            _icon.localScale = new Vector3(pulse, pulse, 1f);
        }
    }

    /// <summary>字型跟文字卡同一個；文字卡還沒出現就先用 TMP 預設，等它出現再換。</summary>
    private void ApplyFont()
    {
        TMP_FontAsset f = null;
        if (StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.fontAsset != null) { f = StoryCardPlayer.Instance.fontAsset; _fontReady = true; }
        else if (TMP_Settings.defaultFontAsset != null) f = TMP_Settings.defaultFontAsset;
        if (f == null) return;
        _title.font = f;
        _sub.font = f;
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;   // 蓋過遊戲 UI，但在文字卡（9000）、轉場黑幕下面
        CanvasScaler cs = canvasGo.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight = 0.5f;

        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        _group = panel.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;
        RectTransform prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 1f);
        prt.anchoredPosition = new Vector2(margin.x, -margin.y);
        prt.sizeDelta = size;

        // 底：由左往右淡掉的深藍暈影＋底邊一條細線（像繪本頁邊的註記，不是方塊牌子）
        Image backing = panel.AddComponent<Image>();
        backing.sprite = GetBackingSprite(backingColor, boneColor);
        backing.color = Color.white;
        backing.raycastTarget = false;

        GameObject iconGo = new GameObject("StoneIcon", typeof(RectTransform));
        iconGo.transform.SetParent(panel.transform, false);
        _icon = (RectTransform)iconGo.transform;
        _icon.anchorMin = _icon.anchorMax = _icon.pivot = new Vector2(0f, 0.5f);
        _icon.anchoredPosition = new Vector2(26f, 4f);
        _icon.sizeDelta = new Vector2(68f, 68f);
        Image icon = iconGo.AddComponent<Image>();
        icon.sprite = GetStoneSprite(boneColor);
        icon.raycastTarget = false;

        _title = MakeText(panel.transform, "Title", 38, boneColor, new Vector2(112f, -16f), new Vector2(size.x - 130f, 48f));
        _sub = MakeText(panel.transform, "Sub", 25, new Color(boneColor.r, boneColor.g, boneColor.b, 0.68f), new Vector2(112f, -62f), new Vector2(size.x - 130f, 34f));
        ApplyFont();
    }

    private TextMeshProUGUI MakeText(Transform parent, string name, float fontSize, Color color, Vector2 pos, Vector2 sz)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.raycastTarget = false;
        Shadow sh = go.AddComponent<Shadow>();
        sh.effectColor = shadowColor;
        sh.effectDistance = new Vector2(2f, -2f);
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sz;
        return t;
    }

    /// <summary>左濃右淡的深藍暈影，上下邊緣柔化；底邊一條由左往右淡掉的骨白細線。</summary>
    private static Sprite GetBackingSprite(Color back, Color line)
    {
        if (_backingSprite != null) return _backingSprite;
        const int w = 256, h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PetrifyHudBacking" };
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float vy = (y + 0.5f) / h;
            float vert = Mathf.SmoothStep(0f, 1f, Mathf.Min(vy, 1f - vy) / 0.28f);   // 上下邊緣柔化
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float horiz = 1f - Mathf.SmoothStep(0.15f, 1f, u);                    // 左濃右淡
                float a = back.a * horiz * vert;
                Color c = new Color(back.r, back.g, back.b, a);
                // 底邊細線（距離底邊 4～6 像素）
                float lineA = (y >= 5 && y <= 6) ? 0.55f * (1f - Mathf.SmoothStep(0.05f, 0.9f, u)) : 0f;
                if (lineA > 0f)
                {
                    float outA = lineA + c.a * (1f - lineA);
                    Color mix = (new Color(line.r, line.g, line.b) * lineA + new Color(c.r, c.g, c.b) * c.a * (1f - lineA)) / Mathf.Max(0.001f, outA);
                    c = new Color(mix.r, mix.g, mix.b, outA);
                }
                px[y * w + x] = c;
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _backingSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return _backingSprite;
    }

    /// <summary>
    /// 程式畫的「手繪線條石頭」：略扁、輪廓帶不規則抖動的描邊（骨白）、內部淡淡沙色、兩道裂痕、左上一小段高光弧線。
    /// 畫在 128×128，筆觸粗細有一點變化，看起來像鉛筆／墨線勾出來的，跟遊戲裡的線條畫提示同一種感覺。
    /// </summary>
    private static Sprite GetStoneSprite(Color bone)
    {
        if (_stoneSprite != null) return _stoneSprite;
        const int n = 128;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PetrifyHudStone" };
        Color[] px = new Color[n * n];
        Color sand = new Color(0.78f, 0.62f, 0.45f, 1f);   // 荒原的沙色，淡淡打底
        Vector2 center = new Vector2(n * 0.5f, n * 0.47f);

        // 裂痕（用中心座標，單位像素）
        Vector2[][] cracks =
        {
            new[] { new Vector2(-6f, 26f), new Vector2(-2f, 12f), new Vector2(-10f, 2f), new Vector2(-4f, -12f) },
            new[] { new Vector2(14f, 18f), new Vector2(22f, 6f), new Vector2(18f, -6f) },
        };

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                float ang = Mathf.Atan2(p.y, p.x);
                // 輪廓半徑：不規則的石頭形（略扁，下寬上窄一點）
                float rBase = 46f * (1f + 0.07f * Mathf.Sin(3f * ang + 1.0f) + 0.045f * Mathf.Sin(5f * ang + 2.3f) + 0.03f * Mathf.Sin(9f * ang));
                Vector2 q = new Vector2(p.x / 1.0f, p.y / (p.y > 0f ? 0.78f : 0.86f));
                float d = q.magnitude;
                float edgeDist = Mathf.Abs(d - rBase);
                float width = 2.6f + 0.9f * Mathf.Sin(2f * ang + 0.7f);   // 筆觸粗細微變
                float stroke = Mathf.Clamp01(width * 0.5f + 0.8f - edgeDist);

                float fill = d < rBase ? 0.22f * Mathf.Clamp01((rBase - d) / 3f) : 0f;   // 內部淡淡沙色
                float lineA = stroke;

                // 裂痕
                foreach (Vector2[] c in cracks)
                {
                    for (int i = 0; i < c.Length - 1; i++)
                    {
                        float sd = DistToSegment(p, c[i], c[i + 1]);
                        lineA = Mathf.Max(lineA, Mathf.Clamp01(1.6f - sd) * 0.85f);
                    }
                }

                // 左上高光弧線（在輪廓內側）
                float hd = Mathf.Abs(d - (rBase - 9f));
                float hAng = Mathf.DeltaAngle(ang * Mathf.Rad2Deg, 135f);
                if (Mathf.Abs(hAng) < 30f) lineA = Mathf.Max(lineA, Mathf.Clamp01(1.5f - hd) * 0.6f * (1f - Mathf.Abs(hAng) / 30f));

                float a = Mathf.Clamp01(lineA + fill * (1f - lineA));
                Color col = Color.Lerp(sand, new Color(bone.r, bone.g, bone.b), Mathf.Clamp01(lineA / Mathf.Max(0.001f, lineA + fill)));
                px[y * n + x] = new Color(col.r, col.g, col.b, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        _stoneSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return _stoneSprite;
    }

    private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }
}
