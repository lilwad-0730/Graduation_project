using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ★1017 M：「如果是文字的話，請透明化，玩家在經過時後不會阻擋視野，玩家經過之後恢復原狀；整個過程做漸隱漸顯」
///
/// 字跟她（推石時也算巨石）在畫面上疊在一起時，字漸漸變透明；她離開後再漸漸回來。
///
/// 【管哪些字】
///   ・場景裡的世界文字（TextMeshPro 3D，例如棉花堡的教學字）：她碰到那段字，整段一起變淡。
///   ・文字卡 M1～M4（StoryCardPlayer 疊在演出上的那種）：只有壓到她的那幾個字變淡，其他字照常。
///     鏡頭一直跟著她、她在畫面正中間，整張變淡的話從頭到尾都看不到字。
///     黑幕卡、日誌紙卡看不到她，不處理。
///   ・畫面下方的操作提示（InputHint）：同文字卡，壓到她的那幾個字變淡。
///
/// 【不改的東西】字一個都不改、場景不動、別人的程式不動：只在畫面要畫之前把那幾個字的透明度乘上去。
/// 【開關】TextSeeThrough.Enabled（全部）；Inspector 上三類各一個開關。
/// </summary>
[DisallowMultipleComponent]
public class TextSeeThrough : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("管哪些字")]
    public bool worldTexts = true;
    public bool storyCards = true;
    public bool inputHints = true;
    [Tooltip("她推著巨石時，巨石也算她的一部分（風暴 M2 的字後面就是她和巨石）")]
    public bool includeBoulder = true;

    [Header("透明度")]
    [Range(0f, 1f), Tooltip("世界文字（教學字）她經過時淡到多少")]
    public float worldTextMinAlpha = 0.15f;
    [Range(0f, 1f), Tooltip("文字卡、操作提示壓到她的字淡到多少（太淡就讀不到，留一點）")]
    public float cardMinAlpha = 0.3f;
    [Tooltip("漸隱、漸顯各要幾秒")]
    public float fadeSeconds = 0.35f;
    [Tooltip("她的範圍往外多算幾個像素（1080 基準），字的邊緣才不會貼著她")]
    public float marginPx = 10f;
    [Tooltip("文字卡逐字變淡時，離她多遠開始變淡（1080 基準像素）。0＝只有疊到的字")]
    public float featherPx = 24f;

    private static TextSeeThrough _instance;

    private class Target
    {
        public TMP_Text text;
        public bool perGlyph;          // true＝逐字（文字卡、提示）；false＝整段（世界文字）
        public bool isUI;
        public Image cardCurtain;      // 文字卡的幕：幾乎全黑時（黑幕卡）不處理
        public CanvasGroup cardGroup;  // 文字卡的字的群組透明度
        public float whole = 1f;       // 整段模式目前的倍率
        public float[] glyph = new float[0];
        public bool touched;           // 上一幀有改過頂點透明度
    }

    private readonly List<Target> _targets = new List<Target>();
    private readonly List<Rect> _avoid = new List<Rect>(4);
    private float _nextScan;
    private PlayerMovement _pm;
    private float _nextPmLookup;
    private readonly List<Renderer> _tmpRenderers = new List<Renderer>(16);

    // ══════════════════════════════════════════
    // 開機：自己生出來，跨場景常駐
    // ══════════════════════════════════════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        GameObject go = new GameObject("TextSeeThrough (自動生成)");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<TextSeeThrough>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; RestoreAll(); }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _pm = null;
        _nextScan = 0f;
    }

    // ══════════════════════════════════════════
    // 找字
    // ══════════════════════════════════════════
    private void Scan()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
            if (_targets[i].text == null) _targets.RemoveAt(i);

        if (worldTexts)
        {
            TextMeshPro[] all = Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++) Register(all[i], false, false, null, null);
        }

        if (storyCards)
        {
            StoryCardPlayer scp = Object.FindAnyObjectByType<StoryCardPlayer>();
            if (scp != null)
            {
                Transform label = FindDeep(scp.transform, "StoryCardText");
                Transform curtain = FindDeep(scp.transform, "Curtain");
                if (label != null)
                {
                    TMP_Text t = label.GetComponent<TMP_Text>();
                    if (t != null)
                        Register(t, true, true, curtain != null ? curtain.GetComponent<Image>() : null, label.GetComponent<CanvasGroup>());
                }
            }
        }

        if (inputHints)
        {
            InputHint ih = Object.FindAnyObjectByType<InputHint>();
            if (ih != null)
            {
                TextMeshProUGUI[] labels = ih.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < labels.Length; i++) Register(labels[i], true, true, null, null);
            }
        }
    }

    private void Register(TMP_Text t, bool perGlyph, bool isUI, Image curtain, CanvasGroup group)
    {
        if (t == null) return;
        for (int i = 0; i < _targets.Count; i++) if (_targets[i].text == t) return;
        Target tg = new Target();
        tg.text = t; tg.perGlyph = perGlyph; tg.isUI = isUI; tg.cardCurtain = curtain; tg.cardGroup = group;
        _targets.Add(tg);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    // ══════════════════════════════════════════
    // 每幀：在畫面要畫之前改透明度
    // ══════════════════════════════════════════
    private void LateUpdate()
    {
        if (!Enabled) { RestoreAll(); return; }

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.5f;
            Scan();
        }

        Camera cam = Camera.main;
        BuildAvoidRects(cam);

        float k = Screen.height / 1080f;
        float margin = marginPx * k;
        float feather = Mathf.Max(0.001f, featherPx * k);
        float step = fadeSeconds > 0.001f ? Time.unscaledDeltaTime / fadeSeconds : 1f;

        for (int i = 0; i < _targets.Count; i++)
        {
            Target t = _targets[i];
            if (t.text == null) continue;
            if (t.perGlyph) UpdatePerGlyph(t, cam, margin, feather, step);
            else UpdateWhole(t, cam, margin, step);
        }
    }

    // ── 她（和巨石）在畫面上的範圍 ──────────────────
    private void BuildAvoidRects(Camera cam)
    {
        _avoid.Clear();
        if (cam == null) return;
        if (_pm == null && Time.unscaledTime >= _nextPmLookup)
        {
            _pm = Object.FindAnyObjectByType<PlayerMovement>();
            _nextPmLookup = Time.unscaledTime + 1f;
        }
        // 硬撐（石化）時 PlayerMovement 會被關掉，所以看物件是不是還在場上，不看元件開關
        if (_pm != null && _pm.gameObject.activeInHierarchy)
        {
            Rect r;
            if (TryScreenRectOfBody(_pm.transform, cam, out r)) _avoid.Add(r);
        }
        if (includeBoulder)
        {
            RollingRockVisual rock = RollingRockVisual.ActivelyPushed;
            if (rock != null)
            {
                Rect r;
                if (TryScreenRectOfRenderers(rock.transform, cam, out r)) _avoid.Add(r);
            }
        }
    }

    private bool TryScreenRectOfBody(Transform root, Camera cam, out Rect rect)
    {
        rect = new Rect();
        bool has = false;
        Bounds b = new Bounds();
        foreach (Collider c in root.GetComponents<Collider>())
        {
            if (c == null || !c.enabled || c.isTrigger) continue;
            if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
        }
        SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        for (int i = 0; i < smrs.Length; i++)
        {
            if (smrs[i] == null || !smrs[i].enabled) continue;
            Bounds sb = smrs[i].bounds;
            if (sb.size.magnitude > 12f) continue;   // 光暈之類的大東西不算
            if (!has) { b = sb; has = true; } else b.Encapsulate(sb);
        }
        if (!has) return false;
        return BoundsToScreen(b, cam, out rect);
    }

    private bool TryScreenRectOfRenderers(Transform root, Camera cam, out Rect rect)
    {
        rect = new Rect();
        _tmpRenderers.Clear();
        root.GetComponentsInChildren(false, _tmpRenderers);
        bool has = false;
        Bounds b = new Bounds();
        for (int i = 0; i < _tmpRenderers.Count; i++)
        {
            Renderer r = _tmpRenderers[i];
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.bounds.size.magnitude > 30f) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return false;
        return BoundsToScreen(b, cam, out rect);
    }

    private static readonly Vector3[] _corners = new Vector3[8];
    private static bool BoundsToScreen(Bounds b, Camera cam, out Rect rect)
    {
        Vector3 mn = b.min, mx = b.max;
        _corners[0] = new Vector3(mn.x, mn.y, mn.z); _corners[1] = new Vector3(mx.x, mn.y, mn.z);
        _corners[2] = new Vector3(mn.x, mx.y, mn.z); _corners[3] = new Vector3(mx.x, mx.y, mn.z);
        _corners[4] = new Vector3(mn.x, mn.y, mx.z); _corners[5] = new Vector3(mx.x, mn.y, mx.z);
        _corners[6] = new Vector3(mn.x, mx.y, mx.z); _corners[7] = new Vector3(mx.x, mx.y, mx.z);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        int ok = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector3 s = cam.WorldToScreenPoint(_corners[i]);
            if (s.z <= 0f) continue;
            ok++;
            x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
        }
        rect = ok > 0 ? Rect.MinMaxRect(x0, y0, x1, y1) : new Rect();
        return ok > 0;
    }

    private static float RectDistance(Rect a, Rect b)
    {
        float dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
        float dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // ── 字在畫面上的位置 ──────────────────────────
    private static Vector2 ToScreen(TMP_Text text, bool isUI, Camera cam, Vector3 local)
    {
        Vector3 w = text.transform.TransformPoint(local);
        if (isUI)
        {
            Canvas c = text.canvas;
            if (c == null || c.renderMode == RenderMode.ScreenSpaceOverlay) return new Vector2(w.x, w.y);
            Camera uiCam = c.worldCamera != null ? c.worldCamera : cam;
            return RectTransformUtility.WorldToScreenPoint(uiCam, w);
        }
        if (cam == null) return new Vector2(-99999f, -99999f);
        Vector3 s = cam.WorldToScreenPoint(w);
        if (s.z <= 0f) return new Vector2(-99999f, -99999f);
        return new Vector2(s.x, s.y);
    }

    // ── 整段（世界文字）──────────────────────────
    private void UpdateWhole(Target t, Camera cam, float margin, float step)
    {
        TMP_Text text = t.text;
        bool visible = text.isActiveAndEnabled && text.color.a > 0.01f && cam != null;
        float target = 1f;
        if (visible && _avoid.Count > 0 && text.textInfo != null && text.textInfo.characterCount > 0)
        {
            Bounds lb = text.textBounds;
            Vector2 a = ToScreen(text, false, cam, new Vector3(lb.min.x, lb.min.y, 0f));
            Vector2 b = ToScreen(text, false, cam, new Vector3(lb.max.x, lb.max.y, 0f));
            Vector2 c = ToScreen(text, false, cam, new Vector3(lb.min.x, lb.max.y, 0f));
            Vector2 d = ToScreen(text, false, cam, new Vector3(lb.max.x, lb.min.y, 0f));
            Rect tr = Rect.MinMaxRect(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)),
                                      Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)), Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
            for (int i = 0; i < _avoid.Count; i++)
                if (RectDistance(tr, _avoid[i]) <= margin) { target = worldTextMinAlpha; break; }
        }
        t.whole = Mathf.MoveTowards(t.whole, target, step * (1f - worldTextMinAlpha));

        if (!visible) { if (t.touched) Restore(t); t.whole = 1f; return; }
        if (t.whole >= 0.999f) { if (t.touched) Restore(t); return; }

        text.ForceMeshUpdate();
        MultiplyAlpha(text, null, t.whole);
        t.touched = true;
    }

    // ── 逐字（文字卡、提示）──────────────────────
    private void UpdatePerGlyph(Target t, Camera cam, float margin, float feather, float step)
    {
        TMP_Text text = t.text;
        bool visible = text.isActiveAndEnabled && (text.canvas == null || text.canvas.enabled);
        if (visible && t.cardGroup != null && t.cardGroup.alpha < 0.01f) visible = false;
        // 黑幕卡（幕幾乎全黑）或紙卡：看不到她，不處理
        if (visible && t.cardCurtain != null && t.cardCurtain.color.a > 0.9f) visible = false;

        TMP_TextInfo ti = text.textInfo;
        int n = (ti != null) ? ti.characterCount : 0;
        if (t.glyph.Length != n)
        {
            float[] g = new float[n];
            for (int i = 0; i < n; i++) g[i] = 1f;
            t.glyph = g;
        }

        bool any = false;
        for (int i = 0; i < n; i++)
        {
            float target = 1f;
            TMP_CharacterInfo ci = ti.characterInfo[i];
            if (visible && ci.isVisible && _avoid.Count > 0)
            {
                Vector2 p0 = ToScreen(text, t.isUI, cam, ci.bottomLeft);
                Vector2 p1 = ToScreen(text, t.isUI, cam, ci.topRight);
                Rect gr = Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.y, p1.y), Mathf.Max(p0.x, p1.x), Mathf.Max(p0.y, p1.y));
                float best = float.MaxValue;
                for (int r = 0; r < _avoid.Count; r++) best = Mathf.Min(best, RectDistance(gr, _avoid[r]) - margin);
                float u = feather > 0.001f ? Mathf.Clamp01(best / feather) : (best > 0f ? 1f : 0f);
                target = Mathf.Lerp(cardMinAlpha, 1f, u * u * (3f - 2f * u));
            }
            t.glyph[i] = Mathf.MoveTowards(t.glyph[i], target, step * (1f - cardMinAlpha));
            if (t.glyph[i] < 0.999f) any = true;
        }

        if (!any) { if (t.touched) Restore(t); return; }

        text.ForceMeshUpdate();
        // ForceMeshUpdate 之後字數一樣才套（換頁的那一幀先不套）
        if (text.textInfo.characterCount != t.glyph.Length) { t.touched = true; return; }
        MultiplyAlpha(text, t.glyph, 1f);
        t.touched = true;
    }

    private static void MultiplyAlpha(TMP_Text text, float[] perGlyph, float whole)
    {
        TMP_TextInfo ti = text.textInfo;
        int n = ti.characterCount;
        for (int i = 0; i < n; i++)
        {
            TMP_CharacterInfo ci = ti.characterInfo[i];
            if (!ci.isVisible) continue;
            float f = perGlyph != null ? (i < perGlyph.Length ? perGlyph[i] : 1f) : whole;
            if (f >= 0.999f) continue;
            int mi = ci.materialReferenceIndex;
            int vi = ci.vertexIndex;
            if (mi < 0 || mi >= ti.meshInfo.Length) continue;
            Color32[] cols = ti.meshInfo[mi].colors32;
            if (cols == null || vi + 3 >= cols.Length) continue;
            for (int k = 0; k < 4; k++)
            {
                Color32 c = cols[vi + k];
                c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * f), 0, 255);
                cols[vi + k] = c;
            }
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private static void Restore(Target t)
    {
        t.touched = false;
        if (t.text != null && t.text.isActiveAndEnabled) t.text.ForceMeshUpdate();
    }

    private void RestoreAll()
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            Target t = _targets[i];
            if (t.text == null || !t.touched) continue;
            t.whole = 1f;
            for (int g = 0; g < t.glyph.Length; g++) t.glyph[g] = 1f;
            Restore(t);
        }
    }
}
