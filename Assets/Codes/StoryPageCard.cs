using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★1023 場景裡翻幾頁繪本（不換場景）。
///
/// 給「停下來看幾頁書」用：荒原拿到光球那一刻翻 039（光把她罩住）；玻璃館鏡牆碎掉之後翻 060、061、鏡-1、062、鏡-2～4、067
/// （她的光被鏡子留住 → 裂縫劃過鏡中的她 → 鏡中的她變黑長大 → 跟她同形狀的巨大黑影從碎鏡出來 → 追她）。
/// 畫法跟開場（OpeningSequence）同一套：夜空色黑底 → 第一頁淡入、慢慢推近 → ★1023c 頁與頁之間「交叉淡入淡出」（不再先黑一下）
/// → 最後一頁淡出。任意鍵提早換下一頁（每頁至少 0.6 秒）。
/// 播的時候世界暫停（timeScale 0，鳥、風、黑影全停），她定住；播完一起放開。
///
/// 頁名＝Resources 底下的路徑，不含副檔名；可以在後面加「|停幾秒|推近多少|交叉淡化幾秒」：
///   "Pages/062_鏡-1|2.4|0"＝停 2.4 秒、不推近（同一個鏡頭的連續畫面用 0，淡入淡出時看起來像動畫）。
///   沒寫的用預設（停 holdSeconds 秒、推近 5%、交叉淡化 0.6 秒）。找不到的頁會跳過，不會卡住流程。
///
/// 用法（協程裡）：yield return StoryPageCard.PlayRoutine(new[]{"Pages/039_荒-9"}, 5f);
/// </summary>
public class StoryPageCard : MonoBehaviour
{
    public const float FadeIn = 0.8f;
    public const float FadeOut = 0.8f;
    public const float Zoom = 0.05f;
    public const float CrossFade = 0.6f;
    public const float MinSecondsBeforeSkip = 0.6f;
    public const int CanvasSortingOrder = 10001;    // 跟開場同層：蓋過轉場特效 9999 與文字卡 10000

    /// <summary>正在播（其他系統可以用來擋輸入）。</summary>
    public static bool IsPlaying { get; private set; }

    private struct Spec
    {
        public string path;
        public float hold;
        public float zoom;
        public float xfade;
        public Texture2D tex;
    }

    private Canvas _canvas;
    private Image _bg;
    private readonly RawImage[] _img = new RawImage[2];
    private readonly AspectRatioFitter[] _fit = new AspectRatioFitter[2];
    private readonly float[] _age = new float[2];      // 這一層的頁面出現了多久（推近用）
    private readonly float[] _life = new float[2];     // 這一層的頁面預計顯示多久
    private readonly float[] _zoom = new float[2];
    private PlayerMovement _pm;
    private bool _froze;
    private float _prevTimeScale = 1f;
    private bool _pausedWorld;

    /// <summary>給協程 yield 用。pages＝Resources 路徑（不含副檔名），可加「|停幾秒|推近|交叉淡化秒數」。</summary>
    public static IEnumerator PlayRoutine(string[] pages, float holdSeconds = 5f, bool freezePlayer = true, bool pauseWorld = true)
    {
        if (pages == null || pages.Length == 0) yield break;
        if (IsPlaying) yield break;   // 已經有一段在播：不疊加
        GameObject go = new GameObject("[StoryPageCard]");
        StoryPageCard card = go.AddComponent<StoryPageCard>();
        yield return card.Run(pages, holdSeconds, freezePlayer, pauseWorld);
    }

    private static Spec Parse(string s, float defHold)
    {
        Spec sp = new Spec { path = s, hold = defHold, zoom = Zoom, xfade = CrossFade };
        if (string.IsNullOrEmpty(s)) return sp;
        string[] parts = s.Split('|');
        sp.path = parts[0].Trim();
        float v;
        if (parts.Length > 1 && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0f) sp.hold = v;
        if (parts.Length > 2 && float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0f) sp.zoom = v;
        if (parts.Length > 3 && float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0f) sp.xfade = v;
        return sp;
    }

    private IEnumerator Run(string[] pages, float hold, bool freezePlayer, bool pauseWorld)
    {
        IsPlaying = true;

        // 先把頁面都找好（找不到的跳過）
        List<Spec> list = new List<Spec>();
        foreach (string raw in pages)
        {
            Spec sp = Parse(raw, hold);
            sp.tex = string.IsNullOrEmpty(sp.path) ? null : Resources.Load<Texture2D>(sp.path);
            if (sp.tex == null) { Debug.LogWarning("【翻頁卡】找不到 Resources/" + sp.path + "，跳過這一頁。"); continue; }
            list.Add(sp);
        }
        if (list.Count == 0) { IsPlaying = false; Destroy(gameObject); yield break; }

        Build();
        if (freezePlayer)
        {
            _pm = FindAnyObjectByType<PlayerMovement>();
            if (_pm != null)
            {
                _pm.isCutsceneFrozen = true;
                Rigidbody prb = _pm.GetComponent<Rigidbody>();
                if (prb != null && !prb.isKinematic) prb.linearVelocity = Vector3.zero;
                _froze = true;
            }
        }
        if (pauseWorld)
        {
            _prevTimeScale = Time.timeScale;
            if (_prevTimeScale <= 0f) _prevTimeScale = 1f;
            Time.timeScale = 0f;
            _pausedWorld = true;
        }

        // 黑底淡入（看得到她停下來，再暗下去）
        yield return FadeGraphic(_bg, 0f, 1f, 0.45f);

        int cur = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Spec sp = list[i];
            bool first = i == 0;
            float inDur = first ? FadeIn : Mathf.Max(0.01f, sp.xfade);
            float outDur = i == list.Count - 1 ? FadeOut : Mathf.Max(0.01f, list[i + 1].xfade);
            int other = 1 - cur;

            RawImage img = _img[cur];
            img.texture = sp.tex;
            _fit[cur].aspectRatio = (float)sp.tex.width / Mathf.Max(1, sp.tex.height);
            img.rectTransform.SetAsLastSibling();       // 新的一頁疊在上面
            SetAlpha(img, 0f);
            _age[cur] = 0f; _life[cur] = inDur + sp.hold + outDur; _zoom[cur] = sp.zoom;
            ApplyZoom(cur);
            Debug.Log("【翻頁卡】★1023 翻 " + sp.path + "（停 " + sp.hold.ToString("F1") + " 秒，" + (first ? "淡入" : "交叉淡化 " + inDur.ToString("F1") + " 秒") + "，任意鍵可提早翻）");

            // 淡入（第一頁從黑底；之後跟上一頁交叉淡化）
            float t = 0f;
            while (t < inDur)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                float k = Mathf.Clamp01(t / inDur);
                SetAlpha(img, k);       // 新的一頁疊在上面淡入；舊的一頁留在下面不動，淡完才收掉（不會中間變暗）
                Age(dt);
                yield return null;
            }
            SetAlpha(img, 1f);
            if (!first) SetAlpha(_img[other], 0f);

            // 停留；任意鍵提早換下一頁
            float h = 0f;
            while (h < sp.hold)
            {
                float dt = Time.unscaledDeltaTime;
                h += dt;
                Age(dt);
#if ENABLE_LEGACY_INPUT_MANAGER
                if (inDur + h >= MinSecondsBeforeSkip && (Input.anyKeyDown || Input.GetMouseButtonDown(0))) break;
#endif
                yield return null;
            }
            cur = other;
        }

        // 最後一頁淡出，再收黑底
        int last = 1 - cur;
        float o = 0f;
        while (o < FadeOut)
        {
            float dt = Time.unscaledDeltaTime;
            o += dt;
            SetAlpha(_img[last], 1f - Mathf.Clamp01(o / FadeOut));
            Age(dt);
            yield return null;
        }
        SetAlpha(_img[last], 0f);
        yield return FadeGraphic(_bg, 1f, 0f, 0.6f);
        Finish();
    }

    private void Age(float dt)
    {
        for (int k = 0; k < 2; k++)
        {
            if (_img[k] == null || _img[k].color.a <= 0f) continue;
            _age[k] += dt;
            ApplyZoom(k);
        }
    }

    private void ApplyZoom(int k)
    {
        float z = 1f + _zoom[k] * Mathf.Clamp01(_life[k] > 0f ? _age[k] / _life[k] : 0f);
        _img[k].rectTransform.localScale = new Vector3(z, z, 1f);
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject bgGo = new GameObject("Black");
        bgGo.transform.SetParent(canvasGo.transform, false);
        _bg = bgGo.AddComponent<Image>();
        _bg.color = new Color(0.0510f, 0.0667f, 0.1569f, 0f);   // 夜空色，跟文字卡、開場同色
        _bg.raycastTarget = true;
        Stretch(_bg.rectTransform);

        for (int k = 0; k < 2; k++)
        {
            GameObject imgGo = new GameObject("Page" + k);
            imgGo.transform.SetParent(canvasGo.transform, false);
            RawImage ri = imgGo.AddComponent<RawImage>();
            ri.raycastTarget = false;
            ri.color = new Color(1f, 1f, 1f, 0f);
            Stretch(ri.rectTransform);
            AspectRatioFitter f = imgGo.AddComponent<AspectRatioFitter>();
            f.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            f.aspectRatio = 16f / 9f;
            _img[k] = ri;
            _fit[k] = f;
        }
    }

    private void Finish()
    {
        if (_pausedWorld) { Time.timeScale = _prevTimeScale; _pausedWorld = false; }
        if (_froze && _pm != null) _pm.isCutsceneFrozen = false;
        _froze = false;
        IsPlaying = false;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // 保險：場景被換掉、物件被砍，也要把時間與她放開
        if (_pausedWorld) { Time.timeScale = _prevTimeScale; _pausedWorld = false; }
        if (_froze && _pm != null) _pm.isCutsceneFrozen = false;
        if (IsPlaying) IsPlaying = false;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        Color c = g.color; c.a = a; g.color = c;
    }

    private static IEnumerator FadeGraphic(Graphic g, float from, float to, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(g, Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)));
            yield return null;
        }
        SetAlpha(g, to);
    }
}
