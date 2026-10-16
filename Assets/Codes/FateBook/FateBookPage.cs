using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★1016 命運之書・書頁播放器（24 章提案 → 25 章廢墟頁規格 → 26 章研究落下來的版面）。
///
/// 素材（Resources/FateBook/&lt;頁&gt;/）：
///   page.png          上墨整頁 3840×2160（紙色底、九格、D 是貼在頁上的照片）
///   sketch.png        鉛筆整頁 3840×2160（同一個版面，每格是鉛筆稿）
///   page_layout.json  版面：每格的位置（設計座標 1920×1080、y 往下）、上墨順序、哪格永遠鉛筆（H）、鏡頭停點、刻痕位置
///   三個檔由 文件/1015命運之書/廢墟頁/assemble_book_page.py 從 Codex 交的 ink_out／sketch_out 拼出來；Codex 圖還沒來時用構圖底占位。
///
/// 畫法：整頁先顯示鉛筆稿（sketch），已上墨的格用 page 的那一塊蓋在上面（RawImage 裁 uv）；「上墨」＝那一塊從左到右抹開 1.2 秒。
/// 鏡頭：和重生漫畫 D 頁一樣，1920×1080 的設計框等比塞進螢幕，頁面在框裡移動、縮放（zoom 1＝整頁）。
///
/// 四個流程（都用真實時間 unscaled，和重生黑幕一致）：
///   PlayEnter       進場：黑 → 整頁鉛筆淡入 → 推到進場那格、上墨 → 任意鍵（或到時）→ 收
///   PlayFirstDeath  第一次被狼群追擊而死：黑裡翻回這頁（鉛筆＋已上墨的格）→ 命運那格（A）上墨 → 鏡頭推進 A → 既有的 D 連續頁在 A 裡面播（書中書）→ 拉回整頁 → 闔上
///   PlayDeathAgain  之後每次死：鏡頭停在 A，格角多一道刻痕 → 收（≤ 3 秒）
///   PlayEnd         結束：整頁 → 推到結束那格（I）、上墨 → 翻頁 → 下一關
/// 狀態（哪幾格上了墨、死幾次）在 FateBookState；這裡只畫。
/// </summary>
public static class FateBookPage
{
    public const string ResourcesRoot = "FateBook/";
    public const float DesignW = 1920f, DesignH = 1080f;

    /// <summary>進場／結束頁自己開的 Canvas 層級：在重生黑幕（999）、重生漫畫（1200）之上，文字卡（10000）之下。</summary>
    public static int SortingOrder = 1300;

    // 節奏（秒）
    public const float FadeIn = 0.6f, FadeOut = 0.6f, CamMove = 0.9f, InkWipe = 1.2f;
    public const float EnterMaxHold = 2.4f;     // 上墨之後最多等這麼久（任意鍵可提早收）；整個進場頁 ≤ 4 秒
    public const float DeathPageHold = 2.0f, DeathInkHold = 0.3f, DeathBackHold = 0.8f;
    public const float AgainStroke = 0.45f, AgainHold = 1.4f;
    public const float EndHold = 0.5f, PageTurn = 0.55f;

    /// <summary>第一次死亡：書頁在 D 連續頁前後多花的時間（給 FirstWolfDeathStory 拉長黑幕）。</summary>
    public static float FirstDeathOverhead { get { return FadeIn + DeathPageHold + CamMove + InkWipe + DeathInkHold + CamMove + CamMove + DeathBackHold + FadeOut + 0.3f; } }
    /// <summary>之後每次死亡：刻痕那一下的總長。</summary>
    public static float DeathAgainDuration { get { return 0.4f + AgainStroke + AgainHold + 0.5f; } }

    // ── 版面資料（JsonUtility：全部用陣列，沒有字典）──────────────
    [System.Serializable] public class Panel { public string key; public float x, y, w, h; public string trigger; public bool neverInk; public bool photo; public float pad; public bool enabled = true; }
    [System.Serializable] public class Stop { public string name; public float cx, cy, zoom = 1f, hold; }
    [System.Serializable] public class Tally { public string panel; public float x, y, dx = 10f, len = 20f; public int max = 12; }
    [System.Serializable] public class RuinsCfg
    {
        public float landBelowY = -60f, landSettleSeconds = 0.6f, photoPastMeters = 8f;
        public string photoPastWaypoint = "P13", pushWaypointStart = "P13", pushWaypointEnd = "P14", nextSceneName = "desert";
        public float[] pushFractions = new float[] { 0.33f, 0.66f, 0.97f };
    }
    [System.Serializable] public class Layout
    {
        public string id = "ruins", layout, title;
        public int designW = 1920, designH = 1080;
        public string paper = "#2C2936", pencil = "#C4C0CE", frame = "#968CA4";
        public Panel[] panels;
        public string[] inkOrder;
        public string enterPanel = "B", firstDeathPanel = "A", endPanel = "I";
        public Tally tally;
        public Stop[] stops;
        public RuinsCfg ruins;
        public bool enabled = true;

        public Panel Find(string key)
        {
            if (panels == null) return null;
            foreach (Panel p in panels) if (p != null && p.key == key) return p;
            return null;
        }
        public Stop FindStop(string name, float cx = 960f, float cy = 540f, float zoom = 1f, float hold = 0.6f)
        {
            if (stops != null) foreach (Stop s in stops) if (s != null && s.name == name) return s;
            Stop d = new Stop(); d.name = name; d.cx = cx; d.cy = cy; d.zoom = zoom; d.hold = hold; return d;
        }
        public bool PanelEnabled(string key)
        {
            Panel p = Find(key);
            return p != null && p.enabled;
        }
    }

    /// <summary>一頁的素材：上墨整頁、鉛筆整頁、版面。</summary>
    public class Set
    {
        public string folder;
        public Texture2D page, sketch;
        public Layout layout;

        public string Id { get { return layout != null && !string.IsNullOrEmpty(layout.id) ? layout.id : folder; } }

        /// <summary>讀 Resources/FateBook/&lt;pageId&gt;/。缺 page 或版面就回 null（呼叫端照原本流程）。sketch 缺了用 page 代替（只是沒有鉛筆感）。</summary>
        public static Set Load(string pageId)
        {
            if (!FateBookState.Enabled || string.IsNullOrEmpty(pageId)) return null;
            string f = ResourcesRoot + pageId;
            Set s = new Set();
            s.folder = pageId;
            s.page = Resources.Load<Texture2D>(f + "/page");
            if (s.page == null) return null;
            TextAsset ta = Resources.Load<TextAsset>(f + "/page_layout");
            if (ta == null) { Resources.UnloadAsset(s.page); return null; }
            try { s.layout = JsonUtility.FromJson<Layout>(ta.text); }
            catch (System.Exception e) { Debug.LogWarning("[命運之書] " + f + "/page_layout.json 讀不懂：" + e.Message); s.layout = null; }
            Resources.UnloadAsset(ta);
            if (s.layout == null || s.layout.panels == null || s.layout.panels.Length == 0 || !s.layout.enabled) { Resources.UnloadAsset(s.page); return null; }
            if (string.IsNullOrEmpty(s.layout.id)) s.layout.id = pageId;
            s.sketch = Resources.Load<Texture2D>(f + "/sketch");
            if (s.sketch == null) s.sketch = s.page;
            if (s.page.width < 3000) Debug.LogWarning("[命運之書] " + f + "/page.png 只有 " + s.page.width + "×" + s.page.height + "，推近會糊（RespawnStoryImport 會把 Resources/FateBook 的圖設成原尺寸，Project 視窗右鍵 Reimport）");
            return s;
        }

        public void Unload()
        {
            if (sketch != null && sketch != page) Resources.UnloadAsset(sketch);
            if (page != null) Resources.UnloadAsset(page);
            sketch = null; page = null;
        }
    }

    // ── 畫面 ─────────────────────────────────────────────────
    class InkLayer { public RectTransform holder; public CanvasGroup group; public RawImage img; public Panel panel; public float fullW; }

    class View
    {
        public GameObject run; public CanvasGroup group; public RectTransform frame, page;
        public Set set;
        public readonly Dictionary<string, InkLayer> inks = new Dictionary<string, InkLayer>();
        public RectTransform tallyRoot;
        public readonly List<Image> strokes = new List<Image>();
        public Color paper, pencil;
        public Image black;   // 自己開的流程（進場、結束、再死）用：先把遊戲畫面遮黑，頁再淡進來；第一次死亡在重生黑幕上，不用
    }

    static Color Hex(string s, Color fallback)
    {
        Color c;
        return (!string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString(s, out c)) ? c : fallback;
    }

    static Rect PanelRect(Panel p, float pad)
    {
        float x = Mathf.Max(0f, p.x - pad), y = Mathf.Max(0f, p.y - pad);
        float x1 = Mathf.Min(DesignW, p.x + p.w + pad), y1 = Mathf.Min(DesignH, p.y + p.h + pad);
        return new Rect(x, y, x1 - x, y1 - y);
    }

    /// <summary>建整頁：黑底、設計框、頁面（鉛筆整頁＋已上墨的格＋刻痕）。</summary>
    static View Build(Transform canvasRoot, Set set, bool startHidden, float blackAlpha = -1f)
    {
        View v = new View();
        v.set = set;
        Layout L = set.layout;
        v.paper = Hex(L.paper, RespawnComicPlayer.Paper);
        v.pencil = Hex(L.pencil, new Color(0.77f, 0.75f, 0.81f, 1f));
        if (blackAlpha >= 0f)
        {
            v.black = RespawnComicPlayer.NewImage(canvasRoot, "[FateBook Black]", new Color(0f, 0f, 0f, blackAlpha));
            RespawnComicPlayer.Stretch(v.black.rectTransform);
        }
        v.frame = RespawnComicPlayer.MakeDesignFrame(canvasRoot, out v.group, out v.run);
        v.run.name = "[FateBook " + set.Id + "]";

        GameObject pg = new GameObject("Page", typeof(RectTransform));
        v.page = pg.GetComponent<RectTransform>();
        v.page.SetParent(v.frame, false);
        v.page.anchorMin = v.page.anchorMax = v.page.pivot = new Vector2(0.5f, 0.5f);
        v.page.sizeDelta = new Vector2(DesignW, DesignH);

        Image paper = RespawnComicPlayer.NewImage(v.page, "Paper", v.paper);
        RespawnComicPlayer.Place(paper.rectTransform, -DesignW, -DesignH, DesignW * 3f, DesignH * 3f);   // 比頁大：鏡頭推到邊也不露底

        RawImage sk = RespawnComicPlayer.NewRaw(v.page, "Sketch (鉛筆整頁)");
        sk.texture = set.sketch;
        RespawnComicPlayer.Place(sk.rectTransform, 0f, 0f, DesignW, DesignH);

        // 每一格的上墨層（裁 page 的那一塊；RectMask2D 當抹開的遮罩）
        foreach (Panel p in L.panels)
        {
            if (p == null || string.IsNullOrEmpty(p.key)) continue;
            Rect r = PanelRect(p, p.pad);
            GameObject h = new GameObject("Ink " + p.key, typeof(RectTransform), typeof(RectMask2D), typeof(CanvasGroup));
            RectTransform hr = h.GetComponent<RectTransform>();
            hr.SetParent(v.page, false);
            hr.anchorMin = hr.anchorMax = new Vector2(0f, 1f);
            hr.pivot = new Vector2(0f, 1f);
            hr.anchoredPosition = new Vector2(r.x, -r.y);
            hr.sizeDelta = new Vector2(r.width, r.height);
            RawImage im = RespawnComicPlayer.NewRaw(hr, "page crop");
            im.texture = set.page;
            RectTransform ir = im.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 1f);
            ir.pivot = new Vector2(0f, 1f);
            ir.anchoredPosition = Vector2.zero;
            ir.sizeDelta = new Vector2(r.width, r.height);
            im.uvRect = new Rect(r.x / DesignW, 1f - (r.y + r.height) / DesignH, r.width / DesignW, r.height / DesignH);
            InkLayer il = new InkLayer();
            il.holder = hr; il.group = h.GetComponent<CanvasGroup>(); il.img = im; il.panel = p; il.fullW = r.width;
            il.group.blocksRaycasts = false; il.group.interactable = false;
            bool on = !p.neverInk && FateBookState.IsInked(set.Id, p.key);
            il.group.alpha = on ? 1f : 0f;
            if (!on) hr.sizeDelta = new Vector2(0f, r.height);
            v.inks[p.key] = il;
        }

        // 刻痕（第一次死之後，每死一次一道；畫在命運那格的格角）
        if (L.tally != null && !string.IsNullOrEmpty(L.tally.panel))
        {
            GameObject tr = new GameObject("Tally", typeof(RectTransform));
            v.tallyRoot = tr.GetComponent<RectTransform>();
            v.tallyRoot.SetParent(v.page, false);
            v.tallyRoot.anchorMin = v.tallyRoot.anchorMax = new Vector2(0f, 1f);
            v.tallyRoot.pivot = new Vector2(0f, 1f);
            v.tallyRoot.anchoredPosition = new Vector2(L.tally.x, -L.tally.y);
            v.tallyRoot.sizeDelta = new Vector2(10f, 10f);
            int n = Mathf.Clamp(FateBookState.Deaths(set.Id) - 1, 0, Mathf.Max(0, L.tally.max));
            for (int i = 0; i < n; i++) AddStroke(v, i, 1f);
        }

        ApplyCam(v.page, 960f, 540f, 1f);
        v.group.alpha = startHidden ? 0f : 1f;
        return v;
    }

    /// <summary>一道刻痕：細細的一筆，微微斜，像用鉛筆在格角劃的（第 5 道劃斜的，像正字／tally）。</summary>
    static Image AddStroke(View v, int i, float alpha)
    {
        Layout L = v.set.layout;
        Image im = RespawnComicPlayer.NewImage(v.tallyRoot, "stroke " + (i + 1), v.pencil);
        RectTransform r = im.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        bool cross = (i % 5) == 4;
        int group = i / 5, idx = i % 5;
        float x = group * (L.tally.dx * 5f + 10f) + (cross ? L.tally.dx * 1.5f : idx * L.tally.dx);
        r.anchoredPosition = new Vector2(x, 0f);
        r.sizeDelta = cross ? new Vector2(2.2f, L.tally.dx * 4.6f) : new Vector2(2.4f, L.tally.len);
        r.localEulerAngles = new Vector3(0f, 0f, cross ? 62f : Random.Range(-8f, 8f));
        Color c = v.pencil; c.a = alpha; im.color = c;
        v.strokes.Add(im);
        return im;
    }

    /// <summary>鏡頭：頁面在設計框裡移動、縮放。(cx, cy)＝要放到畫面中央的頁面座標（設計座標、y 往下）；zoom 1＝整頁。</summary>
    public static void ApplyCam(RectTransform page, float cx, float cy, float zoom)
    {
        page.localScale = new Vector3(zoom, zoom, 1f);
        page.anchoredPosition = new Vector2((DesignW * 0.5f - cx) * zoom, -(DesignH * 0.5f - cy) * zoom);
    }

    static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

    static IEnumerator MoveCam(RectTransform page, Vector3 from, Vector3 to, float dur)
    {
        float t = 0f; dur = Mathf.Max(0.01f, dur);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float e = Smooth(t / dur);
            float z = Mathf.Exp(Mathf.Lerp(Mathf.Log(from.z), Mathf.Log(to.z), e));
            ApplyCam(page, Mathf.Lerp(from.x, to.x, e), Mathf.Lerp(from.y, to.y, e), z);
            yield return null;
        }
        ApplyCam(page, to.x, to.y, to.z);
    }

    static IEnumerator Fade(CanvasGroup g, float from, float to, float dur)
    {
        float t = 0f; dur = Mathf.Max(0.01f, dur);
        while (t < dur) { t += Time.unscaledDeltaTime; g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)); yield return null; }
        g.alpha = to;
    }

    static IEnumerator Wait(float dur, bool anyKey = false, float minBeforeKey = 0.4f)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            if (anyKey && t > minBeforeKey && Input.anyKeyDown) yield break;
            yield return null;
        }
    }

    /// <summary>那一格上墨：從左到右抹開（遮罩寬度 0 → 滿），同時淡入。記進 FateBookState。</summary>
    static IEnumerator Reveal(View v, string key, float dur)
    {
        InkLayer il;
        if (!v.inks.TryGetValue(key, out il) || il.panel.neverInk) yield break;
        FateBookState.Ink(v.set.Id, key, "翻開書頁");
        float t = 0f; dur = Mathf.Max(0.05f, dur);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float e = Smooth(t / dur);
            il.holder.sizeDelta = new Vector2(il.fullW * Mathf.Clamp01(e * 1.08f), il.holder.sizeDelta.y);
            il.group.alpha = Mathf.Clamp01(e * 1.6f);
            yield return null;
        }
        il.holder.sizeDelta = new Vector2(il.fullW, il.holder.sizeDelta.y);
        il.group.alpha = 1f;
    }

    static Vector3 Cam(Stop s) { return new Vector3(s.cx, s.cy, Mathf.Max(0.2f, s.zoom)); }

    static void Destroy(View v)
    {
        if (v == null) return;
        if (v.run != null) Object.Destroy(v.run);
        if (v.black != null) Object.Destroy(v.black.gameObject);
    }

    static IEnumerator FadeBlack(Image im, float from, float to, float dur)
    {
        if (im == null) yield break;
        float t = 0f; dur = Mathf.Max(0.01f, dur);
        while (t < dur) { t += Time.unscaledDeltaTime; im.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.Clamp01(t / dur))); yield return null; }
        im.color = new Color(0f, 0f, 0f, to);
    }

    // ── 自己的 Canvas（進場、結束、再死；第一次死亡用 FirstWolfDeathStory 的 Canvas）──────
    static Canvas _canvas;

    public static Transform OwnCanvasRoot()
    {
        if (_canvas == null)
        {
            GameObject root = new GameObject("[FateBook Canvas]");
            Object.DontDestroyOnLoad(root);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignW, DesignH);
            scaler.matchWidthOrHeight = 0.5f;
        }
        _canvas.gameObject.SetActive(true);
        return _canvas.transform;
    }

    public static void HideOwnCanvas()
    {
        if (_canvas != null && _canvas.transform.childCount == 0) _canvas.gameObject.SetActive(false);
    }

    // ── 1 進場 ───────────────────────────────────────────────
    /// <summary>進場：整頁鉛筆淡入 → 推到進場那格、上墨 → 任意鍵或到時 → 收。回傳前把 FateBookState 的進場記成播過。</summary>
    public static IEnumerator PlayEnter(Transform canvasRoot, Set set, bool allowAnyKey = true)
    {
        Layout L = set.layout;
        string key = L.enterPanel;
        FateBookState.MarkEnterShown(set.Id);
        View v = Build(canvasRoot, set, true, 0f);
        yield return FadeBlack(v.black, 0f, 1f, 0.4f);   // 遊戲畫面先暗下來，書才翻開
        Stop s0 = L.FindStop("enter_page", 960f, 540f, 1f, 0.6f);
        Panel p = L.Find(key);
        Stop s1 = L.FindStop("enter_panel", p != null ? p.x + p.w * 0.5f : 960f, p != null ? p.y + p.h * 0.5f : 540f, 2.2f, 1.6f);
        ApplyCam(v.page, s0.cx, s0.cy, s0.zoom);
        yield return Fade(v.group, 0f, 1f, FadeIn);
        yield return Wait(s0.hold);
        yield return MoveCam(v.page, Cam(s0), Cam(s1), CamMove);
        if (p != null && p.enabled) yield return Reveal(v, key, InkWipe);
        yield return Wait(Mathf.Max(s1.hold, EnterMaxHold), allowAnyKey);
        yield return Fade(v.group, 1f, 0f, FadeOut);
        yield return FadeBlack(v.black, 1f, 0f, 0.5f);    // 闔上書，遊戲畫面回來
        Destroy(v);
    }

    // ── 2 第一次死亡（書中書）────────────────────────────────
    /// <summary>
    /// 黑裡翻回這頁 → 命運那格上墨 → 鏡頭推進那格 → 在那格裡播 inner（既有的 D 連續頁：inner 會在給它的 root 上自己建黑底與 1920×1080 的框，等比塞進格子）
    /// → inner 播完 → 拉回整頁 → 淡回黑。
    /// </summary>
    public static IEnumerator PlayFirstDeath(Transform canvasRoot, Set set, System.Func<Transform, IEnumerator> inner)
    {
        Layout L = set.layout;
        string key = L.firstDeathPanel;
        Panel p = L.Find(key);
        View v = Build(canvasRoot, set, true);
        Stop sPage = L.FindStop("death_page", 960f, 540f, 1f, DeathPageHold);
        float cx = p != null ? p.x + p.w * 0.5f : 960f, cy = p != null ? p.y + p.h * 0.5f : 540f;
        Stop sPanel = L.FindStop("death_panel", cx, cy, 1.6f, 1.5f);
        Stop sInner = L.FindStop("death_inner", cx, cy, p != null ? DesignW / Mathf.Max(1f, p.w) : 2.2f, 0f);
        ApplyCam(v.page, sPage.cx, sPage.cy, sPage.zoom);
        yield return Fade(v.group, 0f, 1f, FadeIn);
        yield return Wait(sPage.hold);
        yield return MoveCam(v.page, Cam(sPage), Cam(sPanel), CamMove);
        if (p != null && p.enabled) yield return Reveal(v, key, InkWipe);
        yield return Wait(DeathInkHold);

        if (inner != null && p != null)
        {
            // 格子裡的視窗：inner 在這上面建自己的黑底與設計框（RespawnComicFit 會等比塞進來）
            GameObject w = new GameObject("Inner (" + key + ")", typeof(RectTransform), typeof(RectMask2D));
            RectTransform wr = w.GetComponent<RectTransform>();
            wr.SetParent(v.page, false);
            wr.anchorMin = wr.anchorMax = new Vector2(0f, 1f);
            wr.pivot = new Vector2(0f, 1f);
            wr.anchoredPosition = new Vector2(p.x, -p.y);
            wr.sizeDelta = new Vector2(p.w, p.h);
            yield return MoveCam(v.page, Cam(sPanel), Cam(sInner), CamMove);
            yield return inner(wr);
            if (w != null) Object.Destroy(w);
            yield return MoveCam(v.page, Cam(sInner), Cam(sPage), CamMove);
        }
        else yield return MoveCam(v.page, Cam(sPanel), Cam(sPage), CamMove);
        yield return Wait(DeathBackHold);
        yield return Fade(v.group, 1f, 0f, FadeOut);
        Destroy(v);
    }

    // ── 3 之後每次死：刻痕 ───────────────────────────────────
    /// <summary>鏡頭停在命運那格，格角多一道刻痕（FateBookState.AddDeath 要先呼叫；這裡畫最新的那一道）。</summary>
    public static IEnumerator PlayDeathAgain(Transform canvasRoot, Set set)
    {
        Layout L = set.layout;
        Panel p = L.Find(L.firstDeathPanel);
        int deaths = FateBookState.Deaths(set.Id);
        View v = Build(canvasRoot, set, true);
        // Build 已經畫了 deaths-1 道；最新那一道這裡動畫畫上去
        int newIdx = deaths - 2;   // 第 2 次死＝第 0 道
        if (v.tallyRoot != null && newIdx >= 0 && newIdx < L.tally.max && v.strokes.Count > newIdx) { Object.Destroy(v.strokes[newIdx].gameObject); v.strokes.RemoveAt(newIdx); }
        Stop s = L.FindStop("again_panel", p != null ? p.x + p.w * 0.5f : 960f, p != null ? p.y + p.h * 0.5f : 540f, 1.6f, 2.5f);
        ApplyCam(v.page, s.cx, s.cy, s.zoom);
        yield return Fade(v.group, 0f, 1f, 0.4f);
        if (v.tallyRoot != null && newIdx >= 0 && newIdx < L.tally.max)
        {
            Image st = AddStroke(v, newIdx, 0f);
            RectTransform r = st.rectTransform;
            Vector2 full = r.sizeDelta;
            float t = 0f;
            while (t < AgainStroke)
            {
                t += Time.unscaledDeltaTime;
                float e = Smooth(t / AgainStroke);
                r.sizeDelta = new Vector2(full.x, full.y * e);
                Color c = v.pencil; c.a = Mathf.Clamp01(e * 2f); st.color = c;
                yield return null;
            }
            r.sizeDelta = full;
        }
        yield return Wait(Mathf.Max(s.hold - AgainStroke - 0.4f, AgainHold));
        yield return Fade(v.group, 1f, 0f, 0.5f);
        Destroy(v);
    }

    // ── 4 結束：翻頁 ─────────────────────────────────────────
    /// <summary>整頁 → 推到結束那格、上墨 → 翻頁（頁面橫向壓扁）→ 黑。</summary>
    public static IEnumerator PlayEnd(Transform canvasRoot, Set set, bool allowAnyKey = true)
    {
        Layout L = set.layout;
        string key = L.endPanel;
        Panel p = L.Find(key);
        FateBookState.MarkEndShown(set.Id);
        View v = Build(canvasRoot, set, true, 1f);        // 從黑裡來（風暴轉場的黑幕接著）
        Stop s0 = L.FindStop("end_page", 960f, 540f, 1f, 0.6f);
        Stop s1 = L.FindStop("end_panel", p != null ? p.x + p.w * 0.5f : 960f, p != null ? p.y + p.h * 0.5f : 540f, 1.8f, 1.5f);
        ApplyCam(v.page, s0.cx, s0.cy, s0.zoom);
        yield return Fade(v.group, 0f, 1f, FadeIn);
        yield return Wait(s0.hold);
        yield return MoveCam(v.page, Cam(s0), Cam(s1), CamMove);
        if (p != null && p.enabled) yield return Reveal(v, key, InkWipe);
        yield return Wait(Mathf.Max(s1.hold, EndHold), allowAnyKey);
        yield return MoveCam(v.page, Cam(s1), Cam(s0), CamMove * 0.8f);
        // 翻頁：整頁往左翻過去（x 方向壓扁、右緣往左），底下是黑
        float t = 0f;
        Vector3 scale0 = v.page.localScale;
        Vector2 pos0 = v.page.anchoredPosition;
        while (t < PageTurn)
        {
            t += Time.unscaledDeltaTime;
            float e = Smooth(t / PageTurn);
            v.page.localScale = new Vector3(scale0.x * (1f - e), scale0.y * (1f - 0.08f * e), 1f);
            v.page.anchoredPosition = new Vector2(pos0.x - DesignW * 0.5f * e, pos0.y);
            v.group.alpha = 1f - Mathf.Clamp01((e - 0.7f) / 0.3f);
            yield return null;
        }
        v.group.alpha = 0f;
        yield return Wait(0.2f);
        yield return FadeBlack(v.black, 1f, 0f, 0.7f);    // 翻過去了：下一關的畫面亮起來
        Destroy(v);
    }
}
