using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1019 棉花堡（CottonCastle）→ 墜落＋廢墟（SampleScene）的換場景轉場。
///
/// 為什麼要它：棉花堡獨立成一個場景之後，跳崖不再是同場景的傳送，而是換場景。
/// 這支把「白霧淡出 → 非同步載入 → 到達後白霧散去＋文字卡 M1」串成一條有時間控制的流程，
/// 而且協程掛在自己的 DontDestroyOnLoad 物件上，換場景時不會跟著被銷毀。
///
/// 時間線（UnscaledTime）：
///   t0  玩家碰到跳崖觸發區 → 白霧淡出（fadeOut，預設 0.25 秒；畫面已被白霧蓋住）
///   t1  開始 LoadSceneAsync（白霧蓋著，玩家看不到載入）
///   t2  場景載入完成、等 2 幀讓 Awake／Start 跑完（新場景玩家一出生就在墜落通道裡，會自己開始固定速度墜落）
///   t3  停頓 hold 秒 → 白霧散去（fadeIn）；同一刻文字卡 M1 開始（Overlay：疊在墜落畫面上）；卡片期間鎖住玩家輸入（跟原本的 StoryCardTrigger 一樣）
/// 全程把每一段花的秒數印在 Console（【跳崖換場景】），要調速度時照 log 看哪一段最慢。
///
/// 為什麼預設不做「背景預載」：LoadSceneAsync(allowSceneActivation=false) 掛著沒放行的時候，
/// 之後任何別的場景載入（例如暫停→回主選單）會被它擋住。這個風險在關卡中途不值得冒，
/// 載入時間靠「棉花堡拆掉後 SampleScene 變小」＋白霧遮住來吃。想試預載：SkyDiveTeleportTrigger.preloadScene 打勾。
/// </summary>
public class CliffSceneLoader : MonoBehaviour
{
    private static CliffSceneLoader _instance;
    private static AsyncOperation _preOp;
    private static string _preName = "";
    private static bool _busy;

    /// <summary>正在換場景（其他觸發器不要再接）。</summary>
    public static bool IsBusy { get { return _busy; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _preOp = null;
        _preName = "";
        _busy = false;
    }

    private static CliffSceneLoader Ensure()
    {
        if (_instance != null) return _instance;
        GameObject go = new GameObject("[CliffSceneLoader]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CliffSceneLoader>();
        return _instance;
    }

    /// <summary>開始背景預載（只在 preloadScene 打勾時用；見類別說明的風險）。</summary>
    public static void Preload(string sceneName)
    {
        if (_preOp != null || _busy || string.IsNullOrWhiteSpace(sceneName)) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName)) return;
        _preName = sceneName.Trim();
        _preOp = SceneManager.LoadSceneAsync(_preName);
        if (_preOp != null) _preOp.allowSceneActivation = false;
        Debug.Log($"【跳崖換場景】開始背景預載 [{_preName}]");
    }

    /// <summary>放行預載（要離開時呼叫，避免擋住別的場景載入）。</summary>
    public static void ReleasePreload()
    {
        if (_preOp != null) { _preOp.allowSceneActivation = true; _preOp = null; }
    }

    /// <summary>開始換場景。回傳 false＝沒開始（已經在換、或場景沒加進 Build Settings）。</summary>
    public static bool Begin(string sceneName, Color mistColor, float fadeOut, float hold, float fadeIn, string arrivalCardId, AudioClip arrivalSfx = null, float arrivalSfxVolume = 0.9f)
    {
        if (_busy) return false;
        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName.Trim()))
        {
            Debug.LogError($"【跳崖換場景錯誤】場景 '{sceneName}' 不在 Build Settings 裡（File ▸ Build Profiles ▸ Scene List）！");
            return false;
        }
        _busy = true;
        CliffSceneLoader loader = Ensure();
        loader.StartCoroutine(loader.Run(sceneName.Trim(), mistColor, fadeOut, hold, fadeIn, arrivalCardId, arrivalSfx, arrivalSfxVolume));
        return true;
    }

    // ── 白霧畫布（DontDestroyOnLoad，跨場景不會斷）──
    private Canvas _canvas;
    private Image _mist;

    private void EnsureOverlay()
    {
        if (_canvas != null) return;
        GameObject c = new GameObject("CliffMistCanvas");
        c.transform.SetParent(transform, false);
        _canvas = c.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9999;   // 文字卡是 10000，要壓在白霧上面
        c.AddComponent<CanvasScaler>();
        GameObject im = new GameObject("Mist");
        im.transform.SetParent(c.transform, false);
        _mist = im.AddComponent<Image>();
        _mist.raycastTarget = false;
        RectTransform rt = _mist.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
    }

    private void SetMist(Color col, float a)
    {
        _mist.color = new Color(col.r, col.g, col.b, Mathf.Clamp01(a));
    }

    private IEnumerator Run(string sceneName, Color mist, float fadeOut, float hold, float fadeIn, string cardId, AudioClip sfx, float sfxVolume)
    {
        float t0 = Time.unscaledTime;
        EnsureOverlay();
        _canvas.enabled = true;
        SetMist(mist, 0f);

        // 1. 白霧淡出
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeOut);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            SetMist(mist, t / dur);
            yield return null;
        }
        SetMist(mist, 1f);
        float tMist = Time.unscaledTime;

        // 2. 載入（有預載就直接放行）
        AsyncOperation op = null;
        if (_preOp != null && _preName == sceneName) { op = _preOp; _preOp = null; op.allowSceneActivation = true; }
        else
        {
            ReleasePreload();
            op = SceneManager.LoadSceneAsync(sceneName);
        }
        if (op == null)
        {
            Debug.LogError("【跳崖換場景錯誤】LoadSceneAsync 回傳 null，放棄換場景。");
            SetMist(mist, 0f);
            _canvas.enabled = false;
            _busy = false;
            yield break;
        }
        while (!op.isDone) yield return null;
        float tLoaded = Time.unscaledTime;

        // 3. 等新場景的 Awake／Start 跑完
        yield return null;
        yield return null;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        // 4. 白霧散去，同時文字卡 M1（Overlay）疊在墜落畫面上；跳崖的呼嘯聲也在這一刻響
        //    （AudioManager 一換場景就會停掉所有 SFX，所以要等到新場景載入完才放，不能在觸發當下放）
        if (sfx != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(sfx, sfxVolume);
            else AudioSource.PlayClipAtPoint(sfx, Camera.main != null ? Camera.main.transform.position : Vector3.zero, AudioManager.ScaleSfx(sfxVolume));
        }
        Coroutine card = null;
        if (!string.IsNullOrEmpty(cardId) && StoryCardPlayer.Instance != null)
        {
            if (pm != null) pm.isCutsceneFrozen = true;
            card = StoryCardPlayer.Instance.PlayDetached(cardId, true, true);
        }
        t = 0f;
        dur = Mathf.Max(0.01f, fadeIn);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            SetMist(mist, 1f - t / dur);
            yield return null;
        }
        SetMist(mist, 0f);
        _canvas.enabled = false;
        float tDone = Time.unscaledTime;
        Debug.Log($"【跳崖換場景】→ {sceneName}：白霧淡出 {tMist - t0:F2}s ／ 載入 {tLoaded - tMist:F2}s ／ 白霧散去 {tDone - tLoaded:F2}s ／ 合計 {tDone - t0:F2}s（文字卡 {(card != null ? cardId : "無")}）");

        if (card != null)
        {
            yield return card;
            if (pm != null) pm.isCutsceneFrozen = false;
        }
        _busy = false;
    }
}
