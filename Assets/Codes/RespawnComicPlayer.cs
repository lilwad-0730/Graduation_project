using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★1003 重生漫畫的四個版本（B–E）。A 頁面逐格在 FirstWolfDeathStory 裡。
/// 這裡只負責「在一個 Canvas 底下把漫畫播出來」；什麼時候播、黑幕拉多長，由 FirstWolfDeathStory 決定。
///
///   B 導覽鏡頭：直式漫畫頁，鏡頭整頁 → 格 1 → 格 2 → 格 3 → 拉回整頁（comiXology Guided View、inFAMOUS 的做法）
///   C 直式條漫（1004 再設計）：七拍往下捲——光被夾住 → 同格反覆（光熄）→ 無框・滾 → 蝕（巨石蓋滿成黑）→ 塵成星（一點光重新亮）
///     → 醒（貼地：爪印走遠、壓痕）→ 高格・坡（山頂光球在等、滿坡沒到頂的壓痕、她又把手放上去）。逐段緩動＋光點層（ComicGlow）
///   D 定格分格（1004 再設計）：被咬滿那一刻的實機畫面定格成格 1，接四格不直述的手繪——格 2 插入格・光熄（亮→熄）、
///     格 3 縱長・滾（狼被甩出格框，她不入畫，格底是蝕）、格 4 黑（一點光亮起）、格 5 坡（光球在等、她又把手放上去）。
///     PlayDPage（連續世界版，現行，1004 深夜）：照 M 定的七拍——攻擊（實機定格＝一張照片）、倒下、往崖邊滾、落下狼四散、山腳（她的光沿著巨石的路下來）、
///     再推、狼又跟在身後——畫在同一張完整的頁面圖上，而且每一列是同一片連續的風景（多聯畫）；鏡頭只跟著會動的東西走（往左 → 往下 → 往右），
///     van Wijk 平移縮放路徑＋梯形速度（起停各三分之一）＋臨界阻尼彈簧、暗角，最後拉回整頁；可關掉鏡頭改用聚光
///   E 繪本翻頁：團隊繪本頁 廢-12 → 黑頁 → 廢-8，翻頁（Journey 壁畫、繪本的做法）
///
/// 全部在 1920×1080 的設計座標裡排，依螢幕等比縮放置中（其他比例會留黑邊），時間用 unscaled time。
/// 素材是 2048×1024 的 POT 貼圖，內容照指定長寬比置中（Centered()；Python 端 content_size() 同一個算法）。
/// </summary>
public static class RespawnComicPlayer
{
    public const float DesignW = 1920f, DesignH = 1080f;
    public static readonly Color Paper = new Color(12f / 255f, 10f / 255f, 24f / 255f, 1f);
    public static readonly Color Bone = new Color(232f / 255f, 225f / 255f, 210f / 255f, 1f);

    // ── 素材 ─────────────────────────────────────────────
    public static Texture2D Tex(string path)
    {
        Texture2D t = Resources.Load<Texture2D>(path);
        if (t != null) t.wrapMode = TextureWrapMode.Clamp;   // 預設 Repeat 會在邊緣混到另一側，拼接處出線
        return t;
    }

    /// <summary>2048×1024 貼圖裡、置中、指定長寬比的最大內容區做成 Sprite。</summary>
    public static Sprite Centered(Texture2D tex, float aspect)
    {
        if (tex == null) return null;
        float w = tex.width, h = tex.height, cw, ch;
        if (aspect >= w / h) { cw = w; ch = Mathf.Round(w / aspect); }
        else { ch = h; cw = Mathf.Round(h * aspect); }
        Rect r = new Rect(Mathf.Floor((w - cw) * 0.5f), Mathf.Floor((h - ch) * 0.5f), cw, ch);
        return Sprite.Create(tex, r, new Vector2(0.5f, 0.5f), 100f);
    }

    public static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
    }

    public static float Sum(float[] a)
    {
        float s = 0f;
        if (a != null) for (int i = 0; i < a.Length; i++) s += Mathf.Max(0f, a[i]);
        return s;
    }

    static float At(float[] a, int i, float fallback)
    {
        return (a != null && i < a.Length && a[i] >= 0f) ? a[i] : fallback;
    }

    // ── 版面 ─────────────────────────────────────────────
    public static Image NewImage(Transform parent, string name, Color c)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image im = go.GetComponent<Image>();
        im.color = c;
        im.raycastTarget = false;
        return im;
    }

    public static RawImage NewRaw(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        RawImage im = go.GetComponent<RawImage>();
        im.raycastTarget = false;
        return im;
    }

    public static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>以父物件左上角為原點（y 往下）放一個矩形；pivot 在中心，方便旋轉。</summary>
    public static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
    }

    /// <summary>建一層：全螢幕黑底＋置中的 1920×1080 設計框（等比縮放、超出裁掉）。整層用 CanvasGroup 淡出。</summary>
    public static RectTransform MakeDesignFrame(Transform canvasRoot, out CanvasGroup group, out GameObject runRoot)
    {
        runRoot = new GameObject("[RespawnComic]", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rrt = runRoot.GetComponent<RectTransform>();
        rrt.SetParent(canvasRoot, false);
        Stretch(rrt);
        group = runRoot.GetComponent<CanvasGroup>();
        group.alpha = 1f; group.blocksRaycasts = false; group.interactable = false;
        Stretch(NewImage(rrt, "Backdrop", Color.black).rectTransform);
        GameObject fr = new GameObject("Design 1920x1080", typeof(RectTransform), typeof(RectMask2D), typeof(RespawnComicFit));
        RectTransform frt = fr.GetComponent<RectTransform>();
        frt.SetParent(rrt, false);
        frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0.5f);
        frt.sizeDelta = new Vector2(DesignW, DesignH);
        fr.GetComponent<RespawnComicFit>().Fit();
        return frt;
    }

    static IEnumerator FadeOut(CanvasGroup g, float dur)
    {
        float t = 0f;
        dur = Mathf.Max(0.01f, dur);
        while (t < dur) { t += Time.unscaledDeltaTime; g.alpha = 1f - Mathf.Clamp01(t / dur); yield return null; }
        g.alpha = 0f;
    }

    static IEnumerator Wait(float dur)
    {
        float t = 0f;
        while (t < dur) { t += Time.unscaledDeltaTime; yield return null; }
    }

    // ── B 導覽鏡頭 ─────────────────────────────────────────
    public static readonly Vector2 BPage = new Vector2(1080f, 1920f);
    public static readonly Rect[] BRects = { new Rect(60, 90, 960, 540), new Rect(0, 680, 1080, 680), new Rect(60, 1410, 960, 460) };   // x, y（由上往下）, w, h

    /// <summary>times：整頁停、移到格1、停、移到格2、停、移到格3、停、拉回整頁、停、淡出（10 個）</summary>
    public static float DurationB(float[] times) { return Sum(times); }

    public static IEnumerator PlayB(Transform canvasRoot, Texture2D[] tex, float[] times)
    {
        CanvasGroup g; GameObject run;
        RectTransform frame = MakeDesignFrame(canvasRoot, out g, out run);
        Image pageImg = NewImage(frame, "Page", Paper);
        RectTransform page = pageImg.rectTransform;
        page.anchorMin = page.anchorMax = page.pivot = new Vector2(0.5f, 0.5f);
        page.sizeDelta = BPage;
        for (int i = 0; i < BRects.Length && i < tex.Length; i++)
        {
            Rect r = BRects[i];
            Image im = NewImage(page, "Panel " + (i + 1), Color.white);
            im.sprite = Centered(tex[i], r.width / r.height);
            RectTransform rt = im.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
        }

        float sPage; Vector2 cPage; FitB(-1, out sPage, out cPage);
        float s = sPage; Vector2 c = cPage;
        Apply(page, s, c);
        yield return HoldB(page, s, c, At(times, 0, 0.6f), 1f);
        int[] order = { 0, 1, 2, -1 };
        for (int k = 0; k < order.Length; k++)
        {
            float s1; Vector2 c1; FitB(order[k], out s1, out c1);
            yield return MoveB(page, s, c, s1, c1, At(times, 1 + k * 2, 0.9f));
            s = s1; c = c1;
            float hold = At(times, 2 + k * 2, 2.0f);
            yield return HoldB(page, s, c, hold, order[k] >= 0 ? 1.04f : 1f);
            if (order[k] >= 0) s *= 1.04f;
        }
        yield return FadeOut(g, At(times, 9, 0.6f));
        Object.Destroy(run);
    }

    static void FitB(int target, out float s, out Vector2 center)
    {
        Rect r = target < 0 ? new Rect(0, 0, BPage.x, BPage.y) : BRects[target];
        float fill = target < 0 ? 0.94f : 0.97f;
        s = Mathf.Min(DesignW / r.width, DesignH / r.height) * fill;
        center = new Vector2(r.x + r.width * 0.5f - BPage.x * 0.5f, BPage.y * 0.5f - (r.y + r.height * 0.5f));   // 頁面中心為原點、y 往上
    }

    static void Apply(RectTransform page, float s, Vector2 c)
    {
        page.localScale = new Vector3(s, s, 1f);
        page.anchoredPosition = -c * s;
    }

    static IEnumerator MoveB(RectTransform page, float s0, Vector2 c0, float s1, Vector2 c1, float dur)
    {
        float t = 0f;
        dur = Mathf.Max(0.01f, dur);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float e = Ease(t / dur);
            float s = Mathf.Exp(Mathf.Lerp(Mathf.Log(s0), Mathf.Log(s1), e));
            Apply(page, s, Vector2.Lerp(c0, c1, e));
            yield return null;
        }
        Apply(page, s1, c1);
    }

    static IEnumerator HoldB(RectTransform page, float s, Vector2 c, float dur, float push)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            Apply(page, s * Mathf.Lerp(1f, push, Mathf.Clamp01(t / Mathf.Max(0.01f, dur))), c);
            yield return null;
        }
    }

    // ── C 直式條漫 ─────────────────────────────────────────
    public const float StripContentW = 1820f, StripTileH = 1024f;

    // 1004 再設計的預設節奏（條漫 1820×8192、8 塊）。FirstWolfDeathStory 的欄位預設值、舊場景數值的升級都用這一組。
    //   停：0 光被夾住｜1 同格反覆｜2 蝕｜3 塵成星・一點光｜4 醒｜5 山頂光球｜6 坡底・她又把手放上去
    public static float[] C2Tops() { return new float[] { -8f, 600f, 2420f, 3700f, 4668f, 5600f, 7168f }; }
    public static float[] C2Moves() { return new float[] { 0.7f, 1.3f, 1.8f, 1.0f, 0.9f, 2.4f }; }
    public static float[] C2Holds() { return new float[] { 1.6f, 1.2f, 0.4f, 1.3f, 1.4f, 0.6f, 2.0f }; }
    /// <summary>0 慢進慢出、1 加速（滾下來，撞上「蝕」才停）、2 等速、3 減速</summary>
    public static int[] C2Eases() { return new int[] { 0, 1, 0, 0, 0, 0 }; }
    public static ComicGlow[] C2Glows()
    {
        return new ComicGlow[]
        {
            new ComicGlow(new Vector2(821f, 501f), 190f, -1, 0),    // 第 1 拍：她胸前被夾住的光（呼吸；離開就熄）
            new ComicGlow(new Vector2(440f, 4380f), 160f, 3, 99),   // 第 5 拍：黑裡重新亮起的那一點
            new ComicGlow(new Vector2(910f, 5974f), 230f, 5, 99),   // 第 7 拍：山頂的光球在等
            new ComicGlow(new Vector2(794f, 7870f), 170f, 6, 99),   // 第 7 拍：坡底她胸前的光（和第 1 拍同一個姿勢）
        };
    }

    public static float DurationC(float[] moves, float[] holds, float fade) { return Sum(moves) + Sum(holds) + Mathf.Max(0f, fade); }

    /// <summary>0 慢進慢出、1 加速、2 等速、3 減速</summary>
    public static float EaseBy(int code, float u)
    {
        u = Mathf.Clamp01(u);
        switch (code)
        {
            case 1: return Mathf.Pow(u, 2.2f);
            case 2: return u;
            case 3: return 1f - Mathf.Pow(1f - u, 2.2f);
            default: return Ease(u);
        }
    }

    /// <summary>1003 舊版呼叫方式（沒有逐段緩動、沒有光點）</summary>
    public static IEnumerator PlayC(Transform canvasRoot, Texture2D[] tiles, float[] tops, float[] moves, float[] holds, float fade)
    {
        return PlayC(canvasRoot, tiles, tops, moves, holds, fade, null, null, 0f, 2.6f);
    }

    /// <summary>tops：每一停的視窗上緣（條漫像素，條寬 1820）；moves：停與停之間捲動秒數；holds：每一停停留秒數；
    /// eases：每段捲動的緩動（null＝舊版：第一段慢進慢出，之後偏等速）；glows：疊在條漫上的光點（跟著捲，依停亮／熄、會呼吸）</summary>
    public static IEnumerator PlayC(Transform canvasRoot, Texture2D[] tiles, float[] tops, float[] moves, float[] holds, float fade,
                                    int[] eases, ComicGlow[] glows, float pulse, float pulsePeriod)
    {
        CanvasGroup g; GameObject run;
        RectTransform frame = MakeDesignFrame(canvasRoot, out g, out run);
        float k = DesignW / StripContentW;
        float tileH = StripTileH * k;
        GameObject sgo = new GameObject("Strip", typeof(RectTransform));
        RectTransform strip = sgo.GetComponent<RectTransform>();
        strip.SetParent(frame, false);
        strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(DesignW, tileH * tiles.Length);
        for (int i = 0; i < tiles.Length; i++)
        {
            Image im = NewImage(strip, "Tile " + i, Color.white);
            im.sprite = Centered(tiles[i], StripContentW / StripTileH);
            RectTransform rt = im.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -i * tileH);
            rt.sizeDelta = new Vector2(DesignW, tileH + 1f);   // 多 1 單位，避免接縫
        }
        GlowState gs = new GlowState(strip, k, glows, pulse, pulsePeriod);

        int n = tops != null ? tops.Length : 0;
        if (n == 0) { tops = new float[] { 0f }; n = 1; }
        strip.anchoredPosition = new Vector2(0f, tops[0] * k);
        yield return HoldC(At(holds, 0, 2.4f), 0f, gs);
        for (int j = 1; j < n; j++)
        {
            float a = tops[j - 1], b = tops[j];
            float dur = Mathf.Max(0.01f, At(moves, j - 1, 1.0f));
            bool legacy = eases == null;
            int code = (!legacy && j - 1 < eases.Length) ? eases[j - 1] : 0;
            float t = 0f;
            while (t < dur)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                float u = Mathf.Clamp01(t / dur);
                float e = legacy ? (j == 1 ? Ease(u) : 0.15f * u + 0.85f * Ease(u)) : EaseBy(code, u);
                strip.anchoredPosition = new Vector2(0f, Mathf.Lerp(a, b, e) * k);
                gs.Step(j - 0.5f, dt);
                yield return null;
            }
            strip.anchoredPosition = new Vector2(0f, b * k);
            yield return HoldC(At(holds, j, 2.8f), j, gs);
        }
        // 淡出（光點跟著整層一起淡）
        float f = 0f, fd = Mathf.Max(0.01f, fade);
        while (f < fd)
        {
            float dt = Time.unscaledDeltaTime;
            f += dt;
            g.alpha = 1f - Mathf.Clamp01(f / fd);
            gs.Step(n - 1, dt);
            yield return null;
        }
        g.alpha = 0f;
        Object.Destroy(run);
    }

    static IEnumerator HoldC(float dur, float stop, GlowState gs)
    {
        float t = 0f;
        gs.Step(stop, 0f);
        while (t < dur)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            gs.Step(stop, dt);
            yield return null;
        }
    }

    /// <summary>條漫上的光點：依「目前停在第幾停」淡入／淡出，平常微微呼吸。</summary>
    class GlowState
    {
        readonly Image[] img;
        readonly float[] a;
        readonly ComicGlow[] g;
        readonly float pulse, period;
        float clock;

        public GlowState(RectTransform strip, float k, ComicGlow[] glows, float pulse, float period)
        {
            this.pulse = Mathf.Clamp01(pulse);
            this.period = Mathf.Max(0.1f, period);
            if (glows == null || glows.Length == 0) return;
            g = glows;
            img = new Image[glows.Length];
            a = new float[glows.Length];
            for (int i = 0; i < glows.Length; i++)
            {
                Image im = NewImage(strip, "Glow " + i, Color.clear);
                im.sprite = GlowSprite();
                RectTransform rt = im.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((glows[i].pos.x - StripContentW * 0.5f) * k, -glows[i].pos.y * k);
                float d = Mathf.Max(4f, glows[i].radius * 2f * k);
                rt.sizeDelta = new Vector2(d, d);
                img[i] = im;
                a[i] = (glows[i].fromStop <= 0 && 0 <= glows[i].toStop) ? 1f : 0f;
            }
        }

        public void Step(float cur, float dt)
        {
            if (img == null) return;
            clock += dt;
            float kk = 1f - Mathf.Exp(-2.2f * dt);
            for (int i = 0; i < img.Length; i++)
            {
                float target = (g[i].fromStop <= cur && cur <= g[i].toStop) ? 1f : 0f;
                a[i] += (target - a[i]) * kk;
                float p = 1f - pulse * 0.5f + pulse * 0.5f * Mathf.Sin(2f * Mathf.PI * clock / period + i * 1.3f);
                Color c = g[i].color;
                c.a *= Mathf.Clamp01(a[i] * p) * 0.85f;
                img[i].color = c;
            }
        }
    }

    static Sprite _glow;
    static Sprite GlowSprite()
    {
        if (_glow != null) return _glow;
        const int S = 128;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float v = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(255 * v));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        _glow = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _glow;
    }

    // ── D 定格分格 ─────────────────────────────────────────
    public static readonly Rect DP1 = new Rect(60, 60, 1040, 585);
    public static readonly Rect DP2 = new Rect(1160, 60, 700, 585);
    public static readonly Rect DP3 = new Rect(60, 705, 1800, 315);

    /// <summary>times：閃、轉紫、縮進格 1、停、格 2 進、停、格 3 進、停、淡出（9 個）</summary>
    public static float DurationD(float[] times) { return Sum(times); }

    public static IEnumerator PlayD(Transform canvasRoot, Texture2D capture, Texture2D p2, Texture2D p3, Texture2D halftone, float[] times, Color tint, float halftoneAlpha)
    {
        CanvasGroup g; GameObject run;
        RectTransform frame = MakeDesignFrame(canvasRoot, out g, out run);
        Image paper = NewImage(frame, "Paper", new Color(Paper.r, Paper.g, Paper.b, 0f));
        Stretch(paper.rectTransform);

        Image border = NewImage(frame, "Panel 1 (定格)", Bone);
        RectTransform b = border.rectTransform;
        Place(b, 0, 0, DesignW, DesignH);
        RawImage cap = NewRaw(b, "Capture");
        Stretch(cap.rectTransform, 8f);
        cap.texture = capture != null ? capture : Texture2D.blackTexture;
        RawImage ht = NewRaw(cap.rectTransform, "Halftone");
        Stretch(ht.rectTransform);
        if (halftone != null) { halftone.wrapMode = TextureWrapMode.Repeat; ht.texture = halftone; }
        ht.color = new Color(1f, 1f, 1f, 0f);

        Image i2 = NewImage(frame, "Panel 2", new Color(1f, 1f, 1f, 0f));
        i2.sprite = Centered(p2, DP2.width / DP2.height);
        Place(i2.rectTransform, DP2.x, DP2.y, DP2.width, DP2.height);
        Image i3 = NewImage(frame, "Panel 3", new Color(1f, 1f, 1f, 0f));
        i3.sprite = Centered(p3, DP3.width / DP3.height);
        Place(i3.rectTransform, DP3.x, DP3.y, DP3.width, DP3.height);

        Image flash = NewImage(frame, "Flash", new Color(1f, 0.98f, 0.94f, 0.55f));
        Stretch(flash.rectTransform);

        // 1 閃（定格的那一下）
        float t = 0f, dur = Mathf.Max(0.01f, At(times, 0, 0.12f));
        while (t < dur) { t += Time.unscaledDeltaTime; flash.color = new Color(1f, 0.98f, 0.94f, 0.55f * (1f - t / dur)); yield return null; }
        flash.color = Color.clear;
        // 2 轉紫、上網點
        t = 0f; dur = Mathf.Max(0.01f, At(times, 1, 0.35f));
        while (t < dur)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / dur);
            cap.color = Color.Lerp(Color.white, tint, u);
            ht.color = new Color(1f, 1f, 1f, halftoneAlpha * u);
            Tile(ht, b.sizeDelta);
            yield return null;
        }
        // 3 縮進格 1、轉 −2°
        t = 0f; dur = Mathf.Max(0.01f, At(times, 2, 0.6f));
        while (t < dur)
        {
            t += Time.unscaledDeltaTime; float e = Ease(t / dur);
            float x = Mathf.Lerp(0f, DP1.x, e), y = Mathf.Lerp(0f, DP1.y, e);
            float w = Mathf.Lerp(DesignW, DP1.width, e), h = Mathf.Lerp(DesignH, DP1.height, e);
            Place(b, x, y, w, h);
            b.localEulerAngles = new Vector3(0f, 0f, -2f * e);
            paper.color = new Color(Paper.r, Paper.g, Paper.b, e);
            Tile(ht, b.sizeDelta);
            yield return null;
        }
        Place(b, DP1.x, DP1.y, DP1.width, DP1.height);
        b.localEulerAngles = new Vector3(0f, 0f, -2f);
        paper.color = Paper;
        Tile(ht, b.sizeDelta);
        yield return Wait(At(times, 3, 1.6f));
        // 5 格 2 從右邊滑進
        yield return SlideIn(i2, DP2, new Vector2(60f, 0f), At(times, 4, 0.3f));
        yield return Wait(At(times, 5, 3.0f));
        // 7 格 3 從下面浮上
        yield return SlideIn(i3, DP3, new Vector2(0f, 40f), At(times, 6, 0.3f));
        yield return Wait(At(times, 7, 3.2f));
        yield return FadeOut(g, At(times, 8, 0.6f));
        Object.Destroy(run);
    }

    // ── D 定格分格・連續世界版（1004 深夜）：一張完整的頁面圖＋鏡頭偏移逐漸讀出來 ─────────────────
    static readonly Color GlowWarm = new Color(1f, 0.77f, 0.5f, 1f);
    // M：「圖片是完整的，是利用鏡頭的偏移逐漸顯示漫畫內容」「深入研究，再規劃漫畫格的，鏡頭偏移時候內容自然」。研究見 16 章：
    //   ・每一列是同一片連續的夜山（多聯畫／手卷）：坡線、天空在格縫兩邊接得上、比例一樣；上坡永遠往右（和實機定格同向）
    //   ・鏡頭只跟著會動的東西走：巨石往左（上列）→ 往下（左欄）→ 她推著巨石往右（下列）；方向只在故事轉彎的地方轉兩次
    //   ・格縫 12 px 墨色＋1.5 px 淡線（不再用骨白粗框——亮的粗線橫掃過畫面會跳）；只有格 1 是一張「照片」（白邊、轉 -1.5°）
    //   ・速度：畫面上的東西每秒最多移動約 0.6 個畫面寬（平滑追視約 30°/s 的上限內）；起步、停下各佔移動時間約三分之一（梯形速度）
    //   ・她回到巨石那一段不移鏡頭：鏡頭停在山腳，她的光沿著巨石滾下來的路一盞一盞亮下來（她動、鏡頭不動）
    // 停點與停點之間走 van Wijk & Nuij（2003）平移＋縮放路徑；鏡頭本身用臨界阻尼彈簧跟著走；停的時候往下一個停點靠過去一點；
    // 四周一圈暗角，最後拉回整頁時散開。cameraMoves 關掉：鏡頭只從實機畫面拉遠成整頁，之後不動，改由一盞聚光照同一條路徑。
    const float DPageEmber = 0.18f;                                           // 光被咬到只剩的那一點火（不歸零）
    public const float DPageFlash = 0.12f, DPageTint = 0.30f, DPageFade = 0.6f;
    const float DPageSmooth = 0.30f;                                          // 彈簧的 smoothTime（秒）
    const float DPageLean = 0.04f;                                            // 停留時往下一個停點靠過去的比例
    const float DPageVignette = 0.60f;                                         // 暗角最深處（畫面中央 62% 不暗，往邊緣漸暗）
    const float DPageRho = 1.41421356f;                                       // van Wijk 的 ρ（d3 預設；長距離移動中途約拉遠 15–20%）
    const float DPageAcc = 1f / 3f;                                           // 梯形速度：起步、停下各佔移動時間的三分之一

    /// <summary>光點的變化：哪一盞、到哪個停點之後幾秒開始（可負）、花幾秒、亮到多少（同一盞依序套用）</summary>
    public struct DGlowKey
    {
        public int glow, shot; public float delay, dur, to;
        public DGlowKey(int glow, int shot, float delay, float dur, float to) { this.glow = glow; this.shot = shot; this.delay = delay; this.dur = dur; this.to = to; }
    }

    /// <summary>一種頁面版面：格 1 的位置、鏡頭停點、節奏、光點。版面跟著頁面圖走（page_layout.json 的 layout 欄位，沒有就看光點名字）</summary>
    public class DPageSpec
    {
        public string id, label;
        public Rect p1; public float p1Rot, p1Border;                         // 格 1（實機定格）的位置、轉角、白邊
        public Vector2[] c; public float[] z, roll, rho; public bool[] via;   // 停點：中心（設計座標，y 往下）、縮放（1＝整頁）、側傾、進到這一點用的 ρ、經過點
        public float[] holds, moves;                                         // 預設節奏（停留秒、到下一個的移動秒）
        public bool trapezoid;                                               // true：梯形速度；false：Smoother（1004 夜的完整頁）
        public int shotS2, shotS4, vigShot;                                  // 倒下（照片變暗）、落地震、從哪個停點拉回整頁（暗角散開）
        public string[] glowNames; public Vector2[] glowPos; public float[] glowR, glowInit, glowPhase;
        public DGlowKey[] keys;
        public int Count { get { return c.Length; } }
    }

    static float[] Rhos(int n, int viaFrom = -1)
    {
        float[] r = new float[n];
        for (int i = 0; i < n; i++) r[i] = DPageRho;
        if (viaFrom >= 0) { r[viaFrom] = 1f; r[viaFrom + 1] = 1f; }
        return r;
    }

    /// <summary>連續世界版（1004 深夜，現行）：每一列是同一片連續的夜山；照片 → 往左格 2、格 3 → 往下格 4、格 5 → 往右格 6、格 7 → 整頁</summary>
    public static readonly DPageSpec DSpecContinuous = new DPageSpec
    {
        id = "continuous_1004", label = "連續世界（1004 深夜，9 停）",
        p1 = new Rect(1172, 32, 716, 408), p1Rot = -1.5f, p1Border = 6f,      // 6 px 白邊 → 裡面剛好 16:9
        c = new Vector2[] { new Vector2(1530f, 236f), new Vector2(1452f, 245f), new Vector2(945f, 255f), new Vector2(430f, 272f), new Vector2(290f, 612f),
                            new Vector2(300f, 895f), new Vector2(860f, 880f), new Vector2(1490f, 748f), new Vector2(960f, 540f) },
        z = new float[] { 1080f / (408f - 12f), 2.15f, 2.45f, 2.4f, 2.4f, 2.3f, 2.45f, 1.85f, 1f },
        roll = new float[] { 1.5f, 0f, 0f, 0f, -1.5f, 0f, 0f, 0f, 0f },
        via = new bool[9], rho = Rhos(9),
        holds = new float[] { 0.00f, 0.40f, 1.10f, 0.45f, 0.60f, 2.40f, 1.00f, 1.25f, 1.10f },
        moves = new float[] { 0.80f, 1.50f, 1.50f, 1.10f, 1.00f, 1.75f, 1.85f, 1.70f, 0.00f },
        trapezoid = true, shotS2 = 2, shotS4 = 4, vigShot = 7,
        glowNames = new string[] { "P2light", "P5a", "P5b", "P5c", "P6her", "P7her", "P7orb" },
        glowPos = new Vector2[] { new Vector2(934.2f, 235.7f), new Vector2(214.0f, 788.0f), new Vector2(191.5f, 878.0f), new Vector2(146.4f, 950.3f),
                                  new Vector2(859.2f, 903.2f), new Vector2(1475.5f, 689.0f), new Vector2(1815.0f, 528.0f) },
        glowR = new float[] { 100f, 34f, 46f, 90f, 110f, 70f, 110f },
        glowInit = new float[] { 1f, 0f, 0f, 0f, 1f, 1f, 1f },
        glowPhase = new float[] { 0.4f, 0f, 1.0f, 2.0f, 2.6f, 3.7f, 1.3f },
        keys = new DGlowKey[] {
            new DGlowKey(0, 2, 0.15f, 0.90f, DPageEmber),                                                 // 她倒下：光剩一點火
            new DGlowKey(1, 5, 0.80f, 0.50f, 0.60f), new DGlowKey(1, 5, 1.75f, 0.70f, 0.32f),             // 崖上：她起身
            new DGlowKey(2, 5, 1.20f, 0.45f, 0.80f), new DGlowKey(2, 5, 2.05f, 0.60f, 0.50f),             // 沿著巨石滾下來的路走下來
            new DGlowKey(3, 5, 1.60f, 0.55f, 1.00f) },                                                    // 回到巨石旁：光照出她
    };

    /// <summary>完整頁版（1004 夜）：Codex 10/04 交付的七格就是這個版面。格 1 → 往左格 2、格 3 → 往下格 4、格 5 → 經過點 → 格 6 頂 → 格 6 底 → 格 7 → 整頁</summary>
    public static readonly DPageSpec DSpecFullPage = new DPageSpec
    {
        id = "fullpage_1004", label = "完整頁（1004 夜，12 停）",
        p1 = new Rect(1116, 44, 760, 428), p1Rot = -1.5f, p1Border = 8f,
        c = new Vector2[] { new Vector2(1496f, 258f), new Vector2(1484f, 262f), new Vector2(892f, 262f), new Vector2(372f, 252f), new Vector2(336f, 650f),
                            new Vector2(356f, 930f), new Vector2(548f, 566f), new Vector2(900f, 588f), new Vector2(888f, 872f), new Vector2(1336f, 836f),
                            new Vector2(1536f, 712f), new Vector2(960f, 540f) },
        z = new float[] { 1080f / (428f - 16f), 2.12f, 2.50f, 2.18f, 2.42f, 2.58f, 2.24f, 2.50f, 2.32f, 2.12f, 1.92f, 1f },
        roll = new float[] { 1.5f, 0f, 0f, 0.6f, -2.2f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
        via = new bool[] { false, false, false, false, false, false, true, false, false, false, false, false }, rho = Rhos(12, 6),
        holds = new float[] { 0f, 0.45f, 1.05f, 0.70f, 0.55f, 0.70f, 0f, 0.25f, 0.80f, 0.30f, 0.65f, 1.10f },
        moves = new float[] { 0.80f, 0.95f, 1.00f, 0.85f, 0.85f, 0.64f, 0.60f, 1.05f, 0.90f, 0.85f, 1.25f, 0f },
        trapezoid = false, shotS2 = 2, shotS4 = 4, vigShot = 10,
        glowNames = new string[] { "P2light", "P3her", "P5her", "P6a", "P6b", "P6c", "P7her", "P7orb" },
        glowPos = new Vector2[] { new Vector2(782.4f, 323.0f), new Vector2(579.1f, 189.4f), new Vector2(611.5f, 851.4f), new Vector2(937.4f, 588.0f),
                                  new Vector2(923.7f, 738.3f), new Vector2(878.5f, 942.2f), new Vector2(1506.2f, 722.5f), new Vector2(1769.6f, 558.3f) },
        glowR = new float[] { 110f, 40f, 36f, 40f, 70f, 120f, 60f, 110f },
        glowInit = new float[] { 1f, DPageEmber, DPageEmber, 0.30f, 0.60f, 0.35f, 1f, 1f },
        glowPhase = new float[] { 0.4f, 0f, 1.0f, 2.0f, 2.6f, 3.1f, 3.7f, 1.3f },
        keys = new DGlowKey[] {
            new DGlowKey(0, 2, 0.15f, 0.90f, DPageEmber),     // 她倒下：光剩一點火
            new DGlowKey(2, 5, 0f, 0.70f, 0.40f),             // 格 5：坡上那一點火亮一些
            new DGlowKey(5, 8, -0.35f, 0.80f, 1f) },          // 格 6：回到巨石旁，光亮回來
    };

    /// <summary>現行版面（連續世界）的停點數與預設節奏；頁面圖若是完整頁版，FirstWolfDeathStory 會改用 DPageSet.spec 的預設</summary>
    public static int DPageShotCount { get { return DSpecContinuous.Count; } }
    /// <summary>每個停點停多久（S0 實機滿版、S1 格1 照片、S2 格2 倒下、S3 格3 往崖邊滾、S4 格4 落下狼散、S5 格5 山腳・她回來、S6 格6 再推、S7 格7 狼又跟著、S8 整頁）</summary>
    public static float[] DPageHolds() { return (float[])DSpecContinuous.holds.Clone(); }
    /// <summary>從每個停點移到下一個要幾秒（最後一個是 0）；預設值讓畫面上的東西每秒最多移動約 0.6 個畫面寬</summary>
    public static float[] DPageMoves() { return (float[])DSpecContinuous.moves.Clone(); }
    public static float DurationDPage(float[] holds, float[] moves, float speed)
    {
        float sp = Mathf.Max(0.25f, speed);
        return DPageFlash + DPageTint + (Sum(holds) + Sum(moves)) / sp + DPageFade;
    }

    [System.Serializable] public class DPageGlow { public string name; public float x, y, r; }
    [System.Serializable] public class DPageLayout { public string layout; public DPageGlow[] glows; }

    /// <summary>D 完整頁版的素材：page（整頁）＋halftone；同資料夾的 page_layout.json 決定版面（layout 欄位；沒有就看光點名字）與光點位置</summary>
    public class DPageSet
    {
        public Texture2D page, halftone;
        public DPageSpec spec = DSpecContinuous;
        public Vector2[] glowPos;
        public float[] glowR;

        public static DPageSet Load(string folder)
        {
            DPageSet s = new DPageSet();
            s.page = Tex(folder + "/page");
            if (s.page == null) return null;
            s.halftone = Tex(folder + "/halftone");
            DPageLayout L = null;
            TextAsset ta = Resources.Load<TextAsset>(folder + "/page_layout");
            if (ta != null)
            {
                try { L = JsonUtility.FromJson<DPageLayout>(ta.text); } catch (System.Exception) { L = null; }
                Resources.UnloadAsset(ta);
            }
            s.spec = SpecOf(L);
            s.glowPos = (Vector2[])s.spec.glowPos.Clone();
            s.glowR = (float[])s.spec.glowR.Clone();
            if (L != null && L.glows != null)
                foreach (DPageGlow gl in L.glows)
                {
                    int i = System.Array.IndexOf(s.spec.glowNames, gl.name);
                    if (i >= 0 && gl.r > 0f) { s.glowPos[i] = new Vector2(gl.x, gl.y); s.glowR[i] = gl.r; }
                }
            return s;
        }

        /// <summary>layout 欄位優先；沒有就看光點名字（P3her、P5her、P6a–c 只有完整頁版有）；都沒有＝現行的連續世界版</summary>
        static DPageSpec SpecOf(DPageLayout L)
        {
            if (L == null) return DSpecContinuous;
            if (L.layout == DSpecContinuous.id) return DSpecContinuous;
            if (L.layout == DSpecFullPage.id) return DSpecFullPage;
            if (L.glows != null)
                foreach (DPageGlow gl in L.glows)
                    if (System.Array.IndexOf(DSpecFullPage.glowNames, gl.name) >= 0 && System.Array.IndexOf(DSpecContinuous.glowNames, gl.name) < 0) return DSpecFullPage;
            return DSpecContinuous;
        }

        public Texture2D[] All() { return new Texture2D[] { page, halftone }; }
    }

    struct DCam
    {
        public Vector2 c; public float z, rot;
        public DCam(float x, float y, float z, float rot) { c = new Vector2(x, y); this.z = z; this.rot = rot; }
    }

    /// <summary>鏡頭中心 c（設計座標，y 往下）放到畫面正中，縮放 z、整頁轉 rot 度</summary>
    static void ApplyCam(RectTransform page, DCam k, Vector2 shake)
    {
        page.localScale = new Vector3(k.z, k.z, 1f);
        page.localEulerAngles = new Vector3(0f, 0f, k.rot);
        Vector2 d = new Vector2(k.c.x - DesignW * 0.5f, -(k.c.y - DesignH * 0.5f)) * k.z;
        float r = k.rot * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
        page.anchoredPosition = -new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs) + shake;
    }

    static float Pulse(float clock, float period, float phase, float amp)
    {
        return 1f - amp * 0.5f + amp * 0.5f * Mathf.Sin(2f * Mathf.PI * clock / Mathf.Max(0.1f, period) + phase);
    }

    static float Smoother(float u) { u = Mathf.Clamp01(u); return u * u * u * (u * (u * 6f - 15f) + 10f); }

    /// <summary>梯形速度（起停用半個餘弦）：前 a 加速、中間等速、後 a 減速；最高速＝平均的 1/(1−a)。動畫 pan 的 slow-in／slow-out 約佔三分之一</summary>
    static float Trap(float u)
    {
        u = Mathf.Clamp01(u);
        const float a = DPageAcc;
        if (u > 1f - a) return 1f - Trap(1f - u);
        float vm = 1f / (1f - a);
        if (u < a) return vm * (u * 0.5f - a / (2f * Mathf.PI) * Mathf.Sin(Mathf.PI * u / a));
        return vm * (a * 0.5f + (u - a));
    }

    /// <summary>只有加速段（接經過點用）：前 a 加速、之後等速</summary>
    static float TrapIn(float u)
    {
        u = Mathf.Clamp01(u);
        const float a = DPageAcc;
        float vm = 1f / (1f - a * 0.5f);
        if (u < a) return vm * (u * 0.5f - a / (2f * Mathf.PI) * Mathf.Sin(Mathf.PI * u / a));
        return vm * (a * 0.5f + (u - a));
    }

    /// <summary>van Wijk & Nuij 2003 的平移＋縮放路徑（和 d3.interpolateZoom 同式）；u＝中心、w＝看得到的寬（設計像素）</summary>
    struct ZoomPath
    {
        double ux0, uy0, w0, dx, dy, d1, r0, S, rho;
        bool pureZoom;

        public ZoomPath(Vector2 c0, double w0, Vector2 c1, double w1, double rho)
        {
            ux0 = c0.x; uy0 = c0.y; this.w0 = w0; this.rho = rho;
            dx = c1.x - c0.x; dy = c1.y - c0.y;
            double d2 = dx * dx + dy * dy, rho2 = rho * rho, rho4 = rho2 * rho2;
            pureZoom = d2 < 1e-6;
            if (pureZoom) { d1 = 0; r0 = 0; S = System.Math.Log(w1 / w0) / rho; return; }
            d1 = System.Math.Sqrt(d2);
            double b0 = (w1 * w1 - w0 * w0 + rho4 * d2) / (2 * w0 * rho2 * d1);
            double b1 = (w1 * w1 - w0 * w0 - rho4 * d2) / (2 * w1 * rho2 * d1);
            r0 = System.Math.Log(System.Math.Sqrt(b0 * b0 + 1) - b0);
            double r1 = System.Math.Log(System.Math.Sqrt(b1 * b1 + 1) - b1);
            S = (r1 - r0) / rho;
        }

        /// <summary>t 0–1 → (中心 x, 中心 y, 寬)</summary>
        public Vector3 At(double t)
        {
            if (pureZoom) return new Vector3((float)(ux0 + t * dx), (float)(uy0 + t * dy), (float)(w0 * System.Math.Exp(rho * t * S)));
            double s = t * S, cr0 = System.Math.Cosh(r0);
            double u = w0 / (rho * rho * d1) * (cr0 * System.Math.Tanh(rho * s + r0) - System.Math.Sinh(r0));
            return new Vector3((float)(ux0 + u * dx), (float)(uy0 + u * dy), (float)(w0 * cr0 / System.Math.Cosh(rho * s + r0)));
        }
    }

    /// <summary>鏡頭要去的地方（目標），隨時間：停點停留時往下一個停點靠過去一點；兩停點之間走 ZoomPath（梯形速度）</summary>
    class DTrack
    {
        readonly System.Collections.Generic.List<float> t0 = new System.Collections.Generic.List<float>(), t1 = new System.Collections.Generic.List<float>();
        readonly System.Collections.Generic.List<int> kind = new System.Collections.Generic.List<int>();       // 0 停、1–4 移（緩動）
        readonly System.Collections.Generic.List<Vector4> a = new System.Collections.Generic.List<Vector4>(), b = new System.Collections.Generic.List<Vector4>();   // x, y, w, roll
        readonly System.Collections.Generic.List<ZoomPath> zp = new System.Collections.Generic.List<ZoomPath>();
        readonly DPageSpec spec;
        public readonly float[] arrive, leave;
        public float end;

        public DTrack(DPageSpec spec, float start, float[] holds, float[] moves, float speed)
        {
            this.spec = spec;
            int n = spec.Count;
            arrive = new float[n]; leave = new float[n];
            float t = start, sp = Mathf.Max(0.25f, speed);
            Vector4 cur = Shot(0);
            for (int i = 0; i < n; i++)
            {
                Vector4 s = Shot(i);
                arrive[i] = t;
                if (i > 0 && spec.via[i]) cur = s;                            // 經過點：不停
                else if (i > 0)
                {
                    float hold = RespawnComicPlayer.At(holds, i, 0.6f) / sp;
                    Vector4 lean;
                    if (i + 1 < n) { Vector4 nx = Shot(i + 1); lean = new Vector4(s.x + (nx.x - s.x) * DPageLean, s.y + (nx.y - s.y) * DPageLean, s.z * (1f - 0.015f), s.w); }
                    else lean = new Vector4(s.x + 8f, s.y + 3f, s.z * (1f - 0.02f), s.w);
                    Add(t, t + hold, 0, s, lean, default(ZoomPath));
                    t += hold; cur = lean;
                }
                else cur = s;
                leave[i] = t;
                if (i + 1 < n)
                {
                    Vector4 nx = Shot(i + 1);
                    float mv = RespawnComicPlayer.At(moves, i, 0.9f) / sp;
                    int ease = (spec.via[i] && spec.via[i + 1]) ? 4 : spec.via[i] ? 3 : spec.via[i + 1] ? 2 : 1;   // 1 起停都緩、2 只加速、3 只減速、4 等速
                    Add(t, t + mv, ease, cur, nx, new ZoomPath(new Vector2(cur.x, cur.y), cur.z, new Vector2(nx.x, nx.y), nx.z, spec.rho[i + 1]));
                    t += mv;
                }
            }
            end = t;
        }

        Vector4 Shot(int i) { return new Vector4(spec.c[i].x, spec.c[i].y, DesignW / spec.z[i], spec.roll[i]); }

        void Add(float ta, float tb, int k, Vector4 va, Vector4 vb, ZoomPath z) { t0.Add(ta); t1.Add(tb); kind.Add(k); a.Add(va); b.Add(vb); zp.Add(z); }

        float Ease(int k, float u)
        {
            if (spec.trapezoid) return k == 2 ? TrapIn(u) : k == 3 ? 1f - TrapIn(1f - u) : k == 4 ? u : Trap(u);   // 梯形（起停各 1/3）
            return k == 2 ? u * u : k == 3 ? 1f - (1f - u) * (1f - u) : k == 4 ? u : Smoother(u);                  // 1004 夜的完整頁
        }

        public Vector4 At(float t)
        {
            if (t0.Count == 0 || t <= t0[0]) return Shot(0);
            for (int i = 0; i < t0.Count; i++)
            {
                if (t > t1[i] && i < t0.Count - 1) continue;
                float u = Mathf.Clamp01((t - t0[i]) / Mathf.Max(1e-5f, t1[i] - t0[i]));
                if (kind[i] == 0) return Vector4.Lerp(a[i], b[i], u);
                float e = Ease(kind[i], u);
                Vector3 p = zp[i].At(e);
                return new Vector4(p.x, p.y, p.z, Mathf.Lerp(a[i].w, b[i].w, e));
            }
            return b[t0.Count - 1];
        }
    }

    /// <summary>臨界阻尼彈簧（精確解；和 Unity SmoothDamp 同一個式子），在 x、y、log 寬、側傾上各跑一個</summary>
    struct DSpring
    {
        Vector4 x, v;
        readonly float omega;
        public DSpring(Vector4 x0, float smoothTime) { x = x0; v = Vector4.zero; omega = 2f / Mathf.Max(0.01f, smoothTime); }

        public Vector4 Step(Vector4 target, float dt)
        {
            float ex = Mathf.Exp(-omega * dt);
            Vector4 d = x - target;
            Vector4 tmp = (v + omega * d) * dt;
            v = (v - omega * tmp) * ex;
            x = target + (d + tmp) * ex;
            return x;
        }
    }

    static Vector4 LogW(Vector4 q) { return new Vector4(q.x, q.y, Mathf.Log(Mathf.Max(1f, q.z)), q.w); }
    static DCam CamOf(Vector4 s) { return new DCam(s.x, s.y, DesignW / Mathf.Exp(s.z), s.w); }

    static Image GlowOn(RectTransform layer, Rect layerRect, Vector2 p, float r)
    {
        Image im = NewImage(layer, "Glow", Color.clear);
        im.sprite = GlowSprite();
        Place(im.rectTransform, p.x - r - layerRect.x, p.y - r - layerRect.y, 2f * r, 2f * r);
        return im;
    }

    static IEnumerator Step(float dur, System.Action<float, float> tick)
    {
        if (dur <= 0f) { tick(1f, 0f); yield break; }
        float t = 0f;
        while (t < dur)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            tick(Mathf.Clamp01(t / dur), dt);
            yield return null;
        }
    }

    // 暗角（鏡頭版，蓋滿畫面）與聚光（不動鏡頭版，跟著停點走）的貼圖
    static Sprite _vig, _spot;
    static Sprite VignetteSprite()
    {
        if (_vig != null) return _vig;
        const int W = 256, H = 144;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false); t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float nx = ((x + 0.5f) / W - 0.5f) / 0.5f, ny = ((y + 0.5f) / H - 0.5f) / 0.5f;
                float rr = Mathf.Sqrt(nx * nx + ny * ny);
                float m = Mathf.Pow(Mathf.Clamp01((rr - 0.62f) / 0.5f), 1.4f);
                px[y * W + x] = new Color32(0, 0, 0, (byte)(255 * m));
            }
        t.SetPixels32(px); t.Apply(false, true);
        _vig = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        return _vig;
    }

    /// <summary>中間透明的一個洞、外面暗；貼圖涵蓋 ±6 個半徑（d＝1 在半徑處）</summary>
    static Sprite SpotSprite()
    {
        if (_spot != null) return _spot;
        const int S = 256;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false); t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = ((x + 0.5f) / S - 0.5f) * 12f, dy = ((y + 0.5f) / S - 0.5f) * 12f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float m = Mathf.Pow(Mathf.Clamp01((d - 0.55f) / 0.55f), 1.3f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(255 * m));
            }
        t.SetPixels32(px); t.Apply(false, true);
        _spot = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _spot;
    }

    public static IEnumerator PlayDPage(Transform canvasRoot, Texture2D capture, DPageSet a, float[] holds, float[] moves, float speed,
                                        bool cameraMoves, Color tint, float halftoneAlpha, float pulse = 0.25f, float pulsePeriod = 2.6f)
    {
        CanvasGroup g; GameObject run;
        RectTransform frame = MakeDesignFrame(canvasRoot, out g, out run);
        GameObject pg = new GameObject("Page (鏡頭)", typeof(RectTransform));
        RectTransform page = pg.GetComponent<RectTransform>();
        page.SetParent(frame, false);
        page.anchorMin = page.anchorMax = page.pivot = new Vector2(0.5f, 0.5f);
        page.sizeDelta = new Vector2(DesignW, DesignH);
        Image paper = NewImage(page, "Paper", Paper);
        Place(paper.rectTransform, -DesignW, -DesignH, DesignW * 3f, DesignH * 3f);   // 比頁大：鏡頭推到邊也不露底

        // 完整頁面（七格都在上面）
        Image pageImg = NewImage(page, "Page image (完整七格)", Color.white);
        pageImg.sprite = Sprite.Create(a.page, new Rect(0, 0, a.page.width, a.page.height), new Vector2(0.5f, 0.5f), 100f);
        Place(pageImg.rectTransform, 0f, 0f, DesignW, DesignH);

        // 格 1：實機定格蓋在頁面圖的格 1 上（頁面圖那一格是備用畫）
        Image border = NewImage(page, "Panel 1 (實機定格)", Bone);
        RectTransform b = border.rectTransform;
        DPageSpec spec = a.spec ?? DSpecContinuous;
        Place(b, spec.p1.x, spec.p1.y, spec.p1.width, spec.p1.height);
        b.localEulerAngles = new Vector3(0f, 0f, spec.p1Rot);
        RawImage cap = NewRaw(b, "Capture");
        Stretch(cap.rectTransform, spec.p1Border);
        if (capture != null) cap.texture = capture;
        else border.gameObject.SetActive(false);                          // 沒截到就用頁面圖上的備用畫
        RawImage ht = NewRaw(cap.rectTransform, "Halftone");
        Stretch(ht.rectTransform);
        if (a.halftone != null) { a.halftone.wrapMode = TextureWrapMode.Repeat; ht.texture = a.halftone; }
        ht.color = new Color(1f, 1f, 1f, 0f);
        Tile(ht, b.sizeDelta);

        // 光點（掛在頁面上，跟著鏡頭走）
        int ng = spec.glowNames.Length;
        Image[] glows = new Image[ng];
        float[] lvl = new float[ng];
        for (int i = 0; i < ng; i++) glows[i] = GlowOn(pageImg.rectTransform, new Rect(0, 0, DesignW, DesignH), a.glowPos[i], a.glowR[i]);

        // 暗角（鏡頭版）／聚光（不動鏡頭版）、閃
        Image vig = NewImage(frame, "Vignette", Color.white);
        vig.sprite = VignetteSprite(); Stretch(vig.rectTransform);
        Image spot = NewImage(frame, "Spotlight", new Color(Paper.r, Paper.g, Paper.b, 0f));
        spot.sprite = SpotSprite();
        RectTransform srt = spot.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f); srt.pivot = new Vector2(0.5f, 0.5f);
        Image flash = NewImage(frame, "Flash", new Color(1f, 0.98f, 0.94f, 0.4f));
        Stretch(flash.rectTransform);

        float sp = Mathf.Max(0.25f, speed);
        float start = DPageFlash + DPageTint;
        DTrack track = new DTrack(spec, start, holds, moves, sp);
        DSpring spring = new DSpring(LogW(track.At(0f)), DPageSmooth);
        DCam K0 = CamOf(LogW(track.At(0f)));
        DCam KP = new DCam(DesignW * 0.5f, DesignH * 0.5f, 1f, 0f);
        Color dimTint = new Color(tint.r * 0.78f, tint.g * 0.78f, tint.b * 0.78f, 1f);
        float total = track.end + DPageFade;
        float p2dim = track.arrive[spec.shotS2] + spec.keys[0].delay / sp, p2len = spec.keys[0].dur / sp, hit = track.arrive[spec.shotS4];
        float vigOut0 = track.leave[spec.vigShot], vigOutLen = Mathf.Max(0.1f, At(moves, spec.vigShot, spec.moves[spec.vigShot]) / sp * 0.9f);
        float t = 0f;
        DCam cam = K0;
        ApplyCam(page, cam, Vector2.zero);
        while (t < total)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            Vector4 s4 = spring.Step(LogW(track.At(t)), dt);
            DCam follow = CamOf(s4);
            if (cameraMoves) cam = follow;
            else cam = LerpCamD(K0, KP, Smoother((t - start) / 0.9f));
            // 閃、轉紫
            flash.color = new Color(1f, 0.98f, 0.94f, t < DPageFlash ? 0.4f * (1f - t / DPageFlash) : 0f);
            float tu = Mathf.Clamp01((t - DPageFlash) / DPageTint);
            float k2 = Smoother((t - p2dim) / p2len);
            cap.color = Color.Lerp(Color.Lerp(Color.white, tint, tu), dimTint, k2);
            ht.color = new Color(1f, 1f, 1f, halftoneAlpha * tu);
            // 光：起始亮度＋依序套用每一個關鍵
            for (int i = 0; i < ng; i++) lvl[i] = spec.glowInit[i];
            foreach (DGlowKey gk in spec.keys)
            {
                float u = Smoother((t - (track.arrive[gk.shot] + gk.delay / sp)) / Mathf.Max(1e-3f, gk.dur / sp));
                lvl[gk.glow] += (gk.to - lvl[gk.glow]) * u;
            }
            for (int i = 0; i < ng; i++) SetGlow(glows[i], lvl[i] * Pulse(t, pulsePeriod, spec.glowPhase[i], pulse));
            // 落地震
            float th = t - hit, amp = (cameraMoves && th >= 0f && th < 0.25f) ? 6f * (1f - th / 0.25f) : 0f;
            Vector2 shake = amp > 0f ? new Vector2(Random.Range(-amp, amp), Random.Range(-amp, amp)) : Vector2.zero;
            ApplyCam(page, cam, shake);
            // 暗角／聚光：讀格時在；拉回整頁時散開
            float open = Smoother((t - vigOut0) / vigOutLen);
            vig.color = new Color(1f, 1f, 1f, cameraMoves ? DPageVignette * (1f - open) : 0f);
            if (!cameraMoves && t > start + 0.5f)
            {
                float rad = 0.62f * DesignW / follow.z;                   // 停點看得到的寬的 0.62 倍（整頁 z＝1 時就是設計像素）
                Vector2 c = follow.c - cam.c;                                // 不動鏡頭在整頁：頁面座標＝畫面座標
                srt.anchoredPosition = new Vector2(DesignW * 0.5f + c.x, -(DesignH * 0.5f + c.y));
                srt.sizeDelta = new Vector2(12f * rad, 12f * rad / 1.35f);
                spot.color = new Color(Paper.r, Paper.g, Paper.b, 0.62f * (1f - open));
            }
            else spot.color = new Color(Paper.r, Paper.g, Paper.b, 0f);
            // 淡出
            g.alpha = 1f - Mathf.Clamp01((t - track.end) / DPageFade);
            yield return null;
        }
        g.alpha = 0f;
        Object.Destroy(run);
    }

    static DCam LerpCamD(DCam a, DCam b, float e)
    {
        DCam k;
        k.c = Vector2.Lerp(a.c, b.c, e);
        k.z = Mathf.Exp(Mathf.Lerp(Mathf.Log(a.z), Mathf.Log(b.z), e));
        k.rot = Mathf.Lerp(a.rot, b.rot, e);
        return k;
    }

    static void SetGlow(Image im, float a)
    {
        Color c = GlowWarm; c.a = Mathf.Clamp01(a) * 0.85f; im.color = c;
    }

    static void SetA(Image im, float a)
    {
        Color c = im.color; c.a = Mathf.Clamp01(a); im.color = c;
    }

    static void Tile(RawImage ht, Vector2 size)
    {
        if (ht.texture == null) return;
        ht.uvRect = new Rect(0f, 0f, size.x / ht.texture.width, size.y / ht.texture.height);
    }

    static IEnumerator SlideIn(Image im, Rect r, Vector2 from, float dur)
    {
        float t = 0f;
        dur = Mathf.Max(0.01f, dur);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime; float e = Ease(t / dur);
            Place(im.rectTransform, r.x + from.x * (1f - e), r.y + from.y * (1f - e), r.width, r.height);
            im.color = new Color(1f, 1f, 1f, e);
            yield return null;
        }
        Place(im.rectTransform, r.x, r.y, r.width, r.height);
        im.color = Color.white;
    }

    // ── E 繪本翻頁 ─────────────────────────────────────────
    /// <summary>times：頁 1 停、翻、頁 2 停、翻、頁 3 停、淡出（6 個）</summary>
    public static float DurationE(float[] times) { return Sum(times); }

    public static IEnumerator PlayE(Transform canvasRoot, Texture2D[] pages, float[] times)
    {
        CanvasGroup g; GameObject run;
        RectTransform frame = MakeDesignFrame(canvasRoot, out g, out run);
        Sprite[] sp = new Sprite[pages.Length];
        for (int i = 0; i < pages.Length; i++) sp[i] = Centered(pages[i], 16f / 9f);

        Image under = NewImage(frame, "Under", Color.white);
        Place(under.rectTransform, 0, 0, DesignW, DesignH);
        Image top = NewImage(frame, "Top", Color.white);
        RectTransform trt = top.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 0.5f);
        trt.pivot = new Vector2(0f, 0.5f);   // 以左邊（書背）為軸
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(DesignW, DesignH);
        Image shadow = NewImage(frame, "Shadow", Color.white);
        shadow.sprite = ShadowSprite();
        RectTransform srt = shadow.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
        srt.pivot = new Vector2(0f, 0.5f);
        srt.sizeDelta = new Vector2(70f, DesignH);
        shadow.color = Color.clear;

        top.sprite = sp[0];
        under.sprite = sp.Length > 1 ? sp[1] : sp[0];
        yield return Wait(At(times, 0, 3.2f));
        for (int p = 1; p < sp.Length; p++)
        {
            under.sprite = sp[p];
            float t = 0f, dur = Mathf.Max(0.01f, At(times, p * 2 - 1, 0.7f));
            while (t < dur)
            {
                t += Time.unscaledDeltaTime; float e = Ease(t / dur);
                trt.localScale = new Vector3(Mathf.Max(0.0001f, 1f - e), 1f, 1f);
                top.color = Color.Lerp(Color.white, new Color(0.5f, 0.5f, 0.5f, 1f), e);
                under.color = Color.Lerp(new Color(0.55f, 0.55f, 0.55f, 1f), Color.white, e);
                srt.anchoredPosition = new Vector2(DesignW * (1f - e), 0f);
                shadow.color = new Color(0f, 0f, 0f, 0.6f * (1f - e));
                yield return null;
            }
            top.sprite = sp[p];
            top.color = Color.white;
            trt.localScale = Vector3.one;
            under.color = Color.white;
            shadow.color = Color.clear;
            yield return Wait(At(times, p * 2, 2.0f));
        }
        yield return FadeOut(g, At(times, 5, 0.6f));
        Object.Destroy(run);
    }

    static Sprite _shadow;
    static Sprite ShadowSprite()
    {
        if (_shadow != null) return _shadow;
        Texture2D t = new Texture2D(64, 4, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[64 * 4];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 64; x++)
                px[y * 64 + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Pow(1f - x / 63f, 1.6f)));
        t.SetPixels32(px);
        t.Apply(false, true);
        _shadow = Sprite.Create(t, new Rect(0, 0, 64, 4), new Vector2(0f, 0.5f), 100f);
        return _shadow;
    }
}

/// <summary>C 直式條漫上的一個光點（條漫像素座標：條寬 1820、y 往下）。到第 fromStop 停時淡入，離開第 toStop 停後淡出；-1＝一開始就亮。</summary>
[System.Serializable]
public struct ComicGlow
{
    [Tooltip("條漫像素座標（條寬 1820，y 往下）")] public Vector2 pos;
    [Tooltip("半徑（條漫像素）")] public float radius;
    public Color color;
    [Tooltip("到第幾停開始亮（-1＝一開始就亮）")] public int fromStop;
    [Tooltip("離開第幾停後熄（99＝一直亮到最後）")] public int toStop;

    public ComicGlow(Vector2 pos, float radius, int fromStop, int toStop)
    {
        this.pos = pos; this.radius = radius; this.fromStop = fromStop; this.toStop = toStop;
        color = new Color(1f, 0.77f, 0.5f, 1f);   // 和條漫裡她的光同一個暖色
    }
}

/// <summary>讓 1920×1080 的設計框等比塞進目前的 Canvas（每幀檢查，換解析度也跟著變）。由 RespawnComicPlayer 動態掛上。</summary>
public class RespawnComicFit : MonoBehaviour
{
    public void Fit()
    {
        RectTransform rt = (RectTransform)transform;
        RectTransform parent = rt.parent as RectTransform;
        if (parent == null) return;
        Vector2 size = parent.rect.size;
        if (size.x < 1f || size.y < 1f) size = new Vector2(RespawnComicPlayer.DesignW, RespawnComicPlayer.DesignH);
        float s = Mathf.Min(size.x / RespawnComicPlayer.DesignW, size.y / RespawnComicPlayer.DesignH);
        rt.localScale = new Vector3(s, s, 1f);
    }

    private void LateUpdate() { Fit(); }
}
