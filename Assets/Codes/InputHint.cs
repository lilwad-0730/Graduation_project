using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 我你他　統一操作提示（畫面下方一條小字）
///
/// 【為什麼要有這支】
///   原本的教學只在第一關（SampleScene）用 TutorialZone 的世界空間文字教
///   AD／空白鍵／Shift，之後的荒原、水下、玻璃館完全沒有任何提示；
///   E（拉桿）和 W／S（水下升降）從頭到尾沒教過。
///   這支把「當下能按什麼」統一成同一種樣子：畫面下方一行骨白字，
///   跟文字卡同字型、同淡入淡出，但不蓋黑幕、不停時間、不打斷遊戲。
///
/// 【誰在用】
///   InputHintLeverProbe：走近拉桿亮「按下 E」，拉下後不再出現。
///   InputHintSwimProbe ：第一次入水亮「W／S 上浮與下潛」。
///   其他地方要用就呼叫 InputHint.Show / Flash / Once。
///
/// 【不用手掛】場景載入時自動生成、自動幫拉桿與玩家補上探針。
///
/// 【會自動讓路】播文字卡、演過場（isCutsceneFrozen）、重生流程中一律淡掉，
///   結束後自己回來。sortingOrder 9000 壓在文字卡（10000）底下。
/// </summary>
[DisallowMultipleComponent]
public class InputHint : MonoBehaviour
{
    // ── 外觀（跟文字卡同一套語彙，只是縮小） ─────────────
    [Header("外觀")]
    public int fontSize = 34;
    public Color textColor = new Color(0.9098f, 0.8784f, 0.8235f, 1f);
    public Color shadowColor = new Color(0.04f, 0.05f, 0.10f, 0.55f);
    public float bottomOffset = 132f;
    public float fadeDuration = 0.35f;
    public float risePixels = 8f;
    public int sortingOrder = 9000;

    [Header("字型（留空＝跟 StoryCardPlayer 借，再不行用 TMP 預設）")]
    public TMP_FontAsset fontAsset;

    // ── 狀態 ───────────────────────────────────────────
    private class Entry
    {
        public string key;
        public string text;
        public float remaining;   // <0 ＝不自動收；只在「完全看得見」時才倒數
    }

    private static InputHint _instance;
    public static InputHint Instance { get { EnsureInstance(); return _instance; } }

    private static readonly HashSet<string> _shownOnce = new HashSet<string>();
    private readonly List<Entry> _stack = new List<Entry>();

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _holder;
    private TextMeshProUGUI _label;
    private TextMeshProUGUI _shadow;
    private bool _built;
    private float _alpha;
    private string _current = "";

    // ══════════════════════════════════════════
    // 開機：自己生出來，不用任何人在場景裡掛
    // ══════════════════════════════════════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
        AttachProbes();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        InputHint found = Object.FindFirstObjectByType<InputHint>();
        if (found != null) { _instance = found; return; }
        GameObject go = new GameObject("InputHint");
        _instance = go.AddComponent<InputHint>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Object.Destroy(gameObject);
            return;
        }
        _instance = this;
        Object.DontDestroyOnLoad(gameObject);
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 換場景：上一關殘留的提示一律清掉，再幫新場景補探針
        _stack.Clear();
        _pmCache = null;
        _pmNextLookup = 0f;
        AttachProbes();
    }

    /// <summary>幫場景裡的拉桿與玩家自動補上探針，組員之後再加拉桿也不用手掛。</summary>
    private static void AttachProbes()
    {
        LeverSystem[] levers = Object.FindObjectsByType<LeverSystem>(FindObjectsSortMode.None);
        for (int i = 0; i < levers.Length; i++)
        {
            if (levers[i] == null) continue;
            if (levers[i].GetComponent<InputHintLeverProbe>() == null)
                levers[i].gameObject.AddComponent<InputHintLeverProbe>();
        }

        PlayerMovement[] players = Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) continue;
            if (players[i].GetComponent<InputHintSwimProbe>() == null)
                players[i].gameObject.AddComponent<InputHintSwimProbe>();
        }
    }

    // ══════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════

    /// <summary>亮一條提示，一直亮到 Hide(key) 為止。同 key 會覆蓋不會疊。</summary>
    public static void Show(string key, string text)
    {
        Push(key, text, -1f);
    }

    /// <summary>亮一條提示，seconds 秒後自己收掉。</summary>
    public static void Flash(string key, string text, float seconds)
    {
        Push(key, text, seconds > 0f ? seconds : -1f);
    }

    /// <summary>整場遊戲只亮這一次（重開遊戲才會再出現）。</summary>
    public static void Once(string id, string text, float seconds)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_shownOnce.Contains(id)) return;
        _shownOnce.Add(id);
        Flash("once:" + id, text, seconds);
    }

    public static void Hide(string key)
    {
        if (_instance == null || string.IsNullOrEmpty(key)) return;
        for (int i = _instance._stack.Count - 1; i >= 0; i--)
            if (_instance._stack[i].key == key) _instance._stack.RemoveAt(i);
    }

    public static void HideAll()
    {
        if (_instance == null) return;
        _instance._stack.Clear();
    }

    private static void Push(string key, string text, float remaining)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return;
        EnsureInstance();
        if (_instance == null) return;
        List<Entry> st = _instance._stack;
        for (int i = st.Count - 1; i >= 0; i--)
            if (st[i].key == key) st.RemoveAt(i);
        Entry e = new Entry();
        e.key = key; e.text = text; e.remaining = remaining;
        st.Add(e);           // 後進的顯示在最上面
    }

    // ══════════════════════════════════════════
    // 每幀：挑要顯示的字、算透明度
    // ══════════════════════════════════════════
    private void Update()
    {
        bool suppressed = IsSuppressed();

        // ★倒數只在「真的看得見」時走：被文字卡或過場蓋住的那幾秒不算，
        //   否則玩家一從過場回來，提示已經自己過期了。
        if (!suppressed && _alpha >= 0.999f && _stack.Count > 0)
        {
            Entry top = _stack[_stack.Count - 1];
            if (top.remaining >= 0f)
            {
                top.remaining -= Time.unscaledDeltaTime;
                if (top.remaining <= 0f) _stack.RemoveAt(_stack.Count - 1);
            }
        }

        string want = _stack.Count > 0 ? _stack[_stack.Count - 1].text : "";
        float target = (!suppressed && !string.IsNullOrEmpty(want)) ? 1f : 0f;

        // 完全看不見的時候才換字，避免字在半透明時跳掉
        if (_alpha <= 0.001f && want != _current)
        {
            _current = want;
            if (!string.IsNullOrEmpty(_current)) Build();
            if (_label != null) _label.text = _current;
            if (_shadow != null) _shadow.text = _current;
        }
        if (string.IsNullOrEmpty(_current)) target = 0f;

        float step = fadeDuration > 0.001f ? Time.unscaledDeltaTime / fadeDuration : 1f;
        _alpha = Mathf.MoveTowards(_alpha, target, step);

        if (!_built) return;
        if (_group != null) _group.alpha = _alpha;
        if (_canvas != null) _canvas.enabled = _alpha > 0.001f;
        if (_holder != null)
        {
            // 淡入時從下方微微浮上來，跟文字卡同一個手勢
            float rise = (1f - _alpha) * risePixels;
            _holder.anchoredPosition = new Vector2(0f, bottomOffset - rise);
        }
    }

    /// <summary>現在提示是不是被壓著（播卡／過場／重生）。探針用來決定要不要現在才亮。</summary>
    public static bool IsBusy
    {
        get { return _instance != null && _instance.IsSuppressed(); }
    }

    private PlayerMovement _pmCache;
    private float _pmNextLookup;

    /// <summary>播文字卡、演過場、重生流程、遊戲暫停中：提示讓路。</summary>
    private bool IsSuppressed()
    {
        // ★遊戲暫停（設定選單 PauseGameWhileActive 或任何 timeScale=0）一律讓路：
        //   提示條 sortingOrder 9000 比設定面板高，不讓路會浮在選單上面
        if (Time.timeScale == 0f) return true;
        if (StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying) return true;
        if (PlayerRespawnSystem.IsAnyRespawning) return true;
        // 玩家快取著用，每秒最多找一次，不要每幀掃場景
        if (_pmCache == null && Time.unscaledTime >= _pmNextLookup)
        {
            _pmCache = Object.FindFirstObjectByType<PlayerMovement>();
            _pmNextLookup = Time.unscaledTime + 1f;
        }
        if (_pmCache != null && _pmCache.isCutsceneFrozen) return true;
        return false;
    }

    // ══════════════════════════════════════════
    // 蓋畫面（第一次要顯示時才蓋，不用白佔資源）
    // ══════════════════════════════════════════
    private void Build()
    {
        if (_built) return;

        GameObject canvasGO = new GameObject("InputHintCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject groupGO = new GameObject("Holder");
        groupGO.transform.SetParent(canvasGO.transform, false);
        _holder = groupGO.AddComponent<RectTransform>();
        _holder.anchorMin = new Vector2(0.5f, 0f);
        _holder.anchorMax = new Vector2(0.5f, 0f);
        _holder.pivot = new Vector2(0.5f, 0f);
        _holder.sizeDelta = new Vector2(1400f, 160f);
        _holder.anchoredPosition = new Vector2(0f, bottomOffset);
        _group = groupGO.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // 影子先蓋（在下層），本體後蓋
        _shadow = MakeLabel(groupGO.transform, "Shadow", shadowColor, new Vector2(2f, -2f));
        _label = MakeLabel(groupGO.transform, "Label", textColor, Vector2.zero);

        _built = true;
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string name, Color color, Vector2 offset)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(offset.x, offset.y);
        rt.offsetMax = new Vector2(offset.x, offset.y);

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = ResolveFont();
        t.color = color;
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.lineSpacing = 12f;
        t.characterSpacing = 2f;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        t.text = "";
        return t;
    }

    /// <summary>
    /// 字型解析順序：Inspector 指定 → 跟 StoryCardPlayer 借（各關都掛了 Msjh_SDF）
    /// → TMP 全域預設（上次已經把 Msjh_SDF 設成全域 fallback，中文不會變豆腐）。
    /// </summary>
    private TMP_FontAsset ResolveFont()
    {
        if (fontAsset != null) return fontAsset;
        if (StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.fontAsset != null)
        {
            fontAsset = StoryCardPlayer.Instance.fontAsset;
            return fontAsset;
        }
        if (TMP_Settings.defaultFontAsset != null)
        {
            fontAsset = TMP_Settings.defaultFontAsset;
            return fontAsset;
        }
        return null;
    }
}
