using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★1023 場景裡翻一兩頁繪本（不換場景）。
///
/// 給「停下來看一頁書」用：荒原拿到光球那一刻翻 039（光把她罩住）、玻璃館鏡牆碎掉之後翻 060～062（她的光被鏡子吸走）。
/// 畫法跟開場（OpeningSequence）同一套：夜空色黑底 → 頁面滿版淡入、慢慢推近、淡出；任意鍵提早翻頁（每頁至少 0.6 秒）。
/// 播的時候世界暫停（timeScale 0，鳥、風、黑影全停），她定住；播完一起放開。
///
/// 用法（協程裡）：yield return StoryPageCard.PlayRoutine(new[]{"Pages/039_荒-9"}, 5f);
/// 頁名＝Resources 底下的路徑，不含副檔名。找不到的頁會跳過，不會卡住流程。
/// </summary>
public class StoryPageCard : MonoBehaviour
{
    public const float FadeIn = 0.8f;
    public const float FadeOut = 0.8f;
    public const float Zoom = 0.05f;
    public const float MinSecondsBeforeSkip = 0.6f;
    public const int CanvasSortingOrder = 10001;    // 跟開場同層：蓋過轉場特效 9999 與文字卡 10000

    /// <summary>正在播（其他系統可以用來擋輸入）。</summary>
    public static bool IsPlaying { get; private set; }

    private Canvas _canvas;
    private Image _bg;
    private RawImage _img;
    private RectTransform _imgRect;
    private AspectRatioFitter _fit;
    private PlayerMovement _pm;
    private bool _froze;
    private float _prevTimeScale = 1f;
    private bool _pausedWorld;

    /// <summary>給協程 yield 用。pages＝Resources 路徑（不含副檔名）。</summary>
    public static IEnumerator PlayRoutine(string[] pages, float holdSeconds = 5f, bool freezePlayer = true, bool pauseWorld = true)
    {
        if (pages == null || pages.Length == 0) yield break;
        if (IsPlaying) yield break;   // 已經有一段在播：不疊加
        GameObject go = new GameObject("[StoryPageCard]");
        StoryPageCard card = go.AddComponent<StoryPageCard>();
        yield return card.Run(pages, holdSeconds, freezePlayer, pauseWorld);
    }

    private IEnumerator Run(string[] pages, float hold, bool freezePlayer, bool pauseWorld)
    {
        IsPlaying = true;
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

        bool skipAll = false;
        for (int i = 0; i < pages.Length && !skipAll; i++)
        {
            Texture2D tex = Resources.Load<Texture2D>(pages[i]);
            if (tex == null)
            {
                Debug.LogWarning("【翻頁卡】找不到 Resources/" + pages[i] + "，跳過這一頁。");
                continue;
            }
            Debug.Log("【翻頁卡】★1023 翻 " + pages[i] + "（停 " + hold.ToString("F1") + " 秒，任意鍵可提早翻）");
            yield return ShowPage(tex, hold, v => skipAll = v);
        }

        yield return FadeGraphic(_bg, 1f, 0f, 0.6f);
        Finish();
    }

    private IEnumerator ShowPage(Texture2D tex, float hold, System.Action<bool> setSkipAll)
    {
        _img.texture = tex;
        _fit.aspectRatio = (float)tex.width / Mathf.Max(1, tex.height);
        SetAlpha(_img, 0f);
        _imgRect.localScale = Vector3.one;

        float total = FadeIn + hold + FadeOut;
        float t = 0f, shown = 0f;
        bool fadingOut = false;
        float fadeOutStart = FadeIn + hold;
        while (t < total)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt; shown += dt;
            float z = 1f + Zoom * Mathf.Clamp01(t / total);
            _imgRect.localScale = new Vector3(z, z, 1f);
            float a;
            if (t < FadeIn) a = t / FadeIn;
            else if (t < fadeOutStart) a = 1f;
            else { a = 1f - (t - fadeOutStart) / FadeOut; fadingOut = true; }
            SetAlpha(_img, Mathf.Clamp01(a));
#if ENABLE_LEGACY_INPUT_MANAGER
            if (shown >= MinSecondsBeforeSkip && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))
            {
                if (fadingOut) { setSkipAll(true); break; }
                fadeOutStart = Mathf.Min(fadeOutStart, t);
                total = fadeOutStart + FadeOut;
            }
#endif
            yield return null;
        }
        SetAlpha(_img, 0f);
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

        GameObject imgGo = new GameObject("Page");
        imgGo.transform.SetParent(canvasGo.transform, false);
        _img = imgGo.AddComponent<RawImage>();
        _img.raycastTarget = false;
        _img.color = new Color(1f, 1f, 1f, 0f);
        _imgRect = _img.rectTransform;
        Stretch(_imgRect);
        _fit = imgGo.AddComponent<AspectRatioFitter>();
        _fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        _fit.aspectRatio = 16f / 9f;
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
