using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1016 命運之書・廢墟頁的觸發（25 章第 4 節「上墨時間表」）。不改場景、不改別人的程式：載入有廢墟的場景時自己掛上，每幀看遊戲的狀態。
///
///   進場 B    她落到廢墟（y 低於 landBelowY）、站穩、可以操作 → 翻開這頁：整頁鉛筆 → 推到 B 上墨 → 任意鍵收（≤ 4 秒）。一輪只一次。
///   得到點 2 D 得到點（碎音樂盒、照片）還沒接進遊戲：先用「巨石放下之後，她走過 P13（石門）再往右 photoPastMeters 米」當作拿到照片 → D 靜靜上墨（不顯示）。
///             之後接得到點時，直接呼叫 FateBookState.Ink("ruins", "D") 即可。
///   放巨石 C  巨石被放開（Rigidbody 不再 kinematic）→ C 靜靜上墨。
///   推巨石 E F G  巨石從放下後停的地方（巨石挑戰的 Boulder Spawn；沒有就從 P13）到 P14 的距離走到 1/3、2/3、3/3 → 各上一格（靜靜的）。
///             ★1016 P14 會被 RuinsOrbDirector 往下移（執行時），這裡每幀讀 P14，跟著縮短。
///   第一次死 A、之後每次死的刻痕  在 FirstWolfDeathStory（重生黑幕裡播）。
///   結束 I    風暴轉場之後進荒原：荒原場景一載入、還在黑裡，翻回這頁推到 I 上墨、翻頁，再讓荒原開始（FateBookRuins.OnSceneLoaded）。
///
/// 開關：FateBookState.Enabled（整包）；page_layout.json 的 enabled／每格 enabled。
/// </summary>
[DisallowMultipleComponent]
public class FateBookRuins : MonoBehaviour
{
    public const string PageId = "ruins";

    public static bool LogEvents = true;

    private PlayerMovement _pm;
    private GuidanceLight _orb;
    private Rigidbody _rock;
    private Transform _wpStart, _wpEnd, _wpPhoto;
    private FateBookPage.Set _set;
    private FateBookPage.RuinsCfg _cfg;

    private float _settle;                 // 落地後站穩的秒數
    private bool _busy;                    // 正在翻頁（進場）
    private bool _rockWasReleased;
    private float _rockStartX;             // 巨石放開時的 x（推巨石的起點）
    private float _maxFrac;                // 推到過的最遠比例
    private bool _frozeByUs;

    // ── 自動掛載 ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m != LoadSceneMode.Single) return;
        Install(s);
        TryPlayEndOnArrival(s);
    }

    private static void Install(Scene s)
    {
        if (!FateBookState.Enabled) return;
        if (FindAnyObjectByType<FateBookRuins>() != null) return;
        // 和 RuinsOrbDirector 同一個判斷：有光球、拉桿（巨石）、風暴轉場的場景＝廢墟在這裡
        if (FindAnyObjectByType<GuidanceLight>() == null || FindAnyObjectByType<LeverSystem>() == null || FindAnyObjectByType<StormSceneTransition>() == null) return;
        new GameObject("[命運之書 廢墟]").AddComponent<FateBookRuins>();
    }

    // ── 結束頁：荒原一載入、還在黑裡 ─────────────────────────
    private static void TryPlayEndOnArrival(Scene s)
    {
        if (!FateBookState.Enabled) return;
        if (!FateBookState.AnyInked(PageId) || FateBookState.EndShown(PageId)) return;   // 沒翻過廢墟頁（例如直接測荒原）就不播
        FateBookPage.Set set = FateBookPage.Set.Load(PageId);
        if (set == null) return;
        string next = set.layout.ruins != null && !string.IsNullOrEmpty(set.layout.ruins.nextSceneName) ? set.layout.ruins.nextSceneName : "desert";
        if (!string.Equals(s.name, next, System.StringComparison.OrdinalIgnoreCase)) { set.Unload(); return; }
        if (!set.layout.PanelEnabled(set.layout.endPanel)) { FateBookState.MarkEndShown(PageId); set.Unload(); return; }
        GameObject go = new GameObject("[命運之書 結束頁]");
        go.AddComponent<FateBookEndRunner>().Begin(set);
    }

    /// <summary>荒原開頭：先把她鎖住、頁在黑裡翻完才放開。</summary>
    private class FateBookEndRunner : MonoBehaviour
    {
        public void Begin(FateBookPage.Set set) { StartCoroutine(Run(set)); }

        private IEnumerator Run(FateBookPage.Set set)
        {
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            bool prev = pm != null && pm.isCutsceneFrozen;
            if (pm != null) pm.isCutsceneFrozen = true;
            if (LogEvents) Debug.Log("[命運之書] 進荒原：先翻回廢墟頁，結束那格上墨、翻頁");
            Transform root = FateBookPage.OwnCanvasRoot();
            yield return FateBookPage.PlayEnd(root, set, true);
            FateBookPage.HideOwnCanvas();
            if (pm != null && !PlayerRespawnSystem.IsAnyRespawning) pm.isCutsceneFrozen = prev;
            set.Unload();
            Destroy(gameObject);
        }
    }

    // ── 本體 ─────────────────────────────────────────────────
    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        _orb = FindAnyObjectByType<GuidanceLight>();
        LeverSystem lever = FindAnyObjectByType<LeverSystem>();
        _rock = lever != null ? lever.targetRock : null;
        if (_rock == null)
        {
            RollingRockVisual v = FindAnyObjectByType<RollingRockVisual>();
            if (v != null) _rock = v.GetComponent<Rigidbody>();
        }
        _set = FateBookPage.Set.Load(PageId);
        if (_set == null)
        {
            if (LogEvents) Debug.Log("[命運之書] 沒有 Resources/FateBook/" + PageId + "/（page.png、page_layout.json）或整頁關著：廢墟頁不啟用");
            enabled = false;
            return;
        }
        _cfg = _set.layout.ruins ?? new FateBookPage.RuinsCfg();
        _wpStart = FindWaypoint(_cfg.pushWaypointStart);
        _wpEnd = FindWaypoint(_cfg.pushWaypointEnd);
        _wpPhoto = FindWaypoint(_cfg.photoPastWaypoint);
        PlayerRespawnSystem.OnResettablesReset += OnReset;
        if (LogEvents)
            Debug.Log("[命運之書] 廢墟頁就緒：已上墨 " + FateBookState.InkedCount(PageId) + " 格；進場 " + (FateBookState.EnterShown(PageId) ? "播過" : "待播") +
                      "；推巨石 " + _cfg.pushWaypointStart + (_wpStart != null ? "" : "(找不到)") + " → " + _cfg.pushWaypointEnd + (_wpEnd != null ? "" : "(找不到)"));
    }

    private void OnDestroy()
    {
        PlayerRespawnSystem.OnResettablesReset -= OnReset;
        if (_frozeByUs && _pm != null) _pm.isCutsceneFrozen = false;
        if (_set != null) _set.Unload();
        _set = null;
    }

    private Transform FindWaypoint(string wpName)
    {
        if (_orb == null || _orb.waypoints == null || string.IsNullOrEmpty(wpName)) return null;
        foreach (Transform w in _orb.waypoints) if (w != null && w.name == wpName) return w;
        return null;
    }

    private void OnReset()
    {
        // 重生：巨石放回原位（推巨石失敗時仍是放開的）；推到過的最遠距離留著（墨跡不會退）
        _rockWasReleased = false;
    }

    private void Update()
    {
        if (!FateBookState.Enabled || _pm == null || _set == null) return;
        if (_busy || PlayerRespawnSystem.IsAnyRespawning || StormSceneTransition.IsAnyTransitioning) return;
        StoryCardPlayer scp = FindAnyObjectByType<StoryCardPlayer>();   // 不用 Instance：它找不到會自己生一個
        if (scp != null && scp.IsPlaying) return;

        Vector3 p = _pm.transform.position;
        bool inRuins = p.y < _cfg.landBelowY;
        if (!inRuins) { _settle = 0f; return; }

        // 進場 B：落地、站穩、可以操作
        if (!FateBookState.EnterShown(PageId))
        {
            bool settled = _pm.isGrounded && !_pm.isCutsceneFrozen;
            _settle = settled ? _settle + Time.deltaTime : 0f;
            if (_settle >= _cfg.landSettleSeconds)
            {
                if (_set.layout.PanelEnabled(_set.layout.enterPanel)) StartCoroutine(EnterRoutine());
                else FateBookState.MarkEnterShown(PageId);
                return;
            }
        }

        // 放巨石 C、推巨石 E F G
        bool released = _rock != null && !_rock.isKinematic;
        if (released && !_rockWasReleased)
        {
            _rockWasReleased = true;
            // ★1016 E F G 量她推的那一段：從巨石放下後停的地方算（P14 往下移之後，從 P13 算的話巨石自己滾下來就過 1/3 了）
            Transform rest = BoulderChallengeController.Instance != null ? BoulderChallengeController.Instance.BoulderSpawnPoint : null;
            _rockStartX = rest != null ? rest.position.x
                        : (_wpStart != null ? Mathf.Min(_wpStart.position.x, _rock.transform.position.x) : _rock.transform.position.x);
            if (!FateBookState.IsInked(PageId, "C") && _set.layout.PanelEnabled("C")) FateBookState.Ink(PageId, "C", "巨石放下");
        }
        if (released && _wpEnd != null)
        {
            float len = _wpEnd.position.x - _rockStartX;
            if (len > 1f)
            {
                float frac = Mathf.Clamp01((_rock.transform.position.x - _rockStartX) / len);
                if (frac > _maxFrac)
                {
                    _maxFrac = frac;
                    string[] keys = { "E", "F", "G" };
                    float[] fr = _cfg.pushFractions != null && _cfg.pushFractions.Length >= 3 ? _cfg.pushFractions : new float[] { 0.33f, 0.66f, 0.97f };
                    for (int i = 0; i < 3; i++)
                        if (_maxFrac >= fr[i] && !FateBookState.IsInked(PageId, keys[i]) && _set.layout.PanelEnabled(keys[i]))
                            FateBookState.Ink(PageId, keys[i], "推巨石到 " + Mathf.RoundToInt(fr[i] * 100f) + "%");
                }
            }
        }

        // 得到點 2 D（暫代）：巨石放下之後她走過石門往右這麼多米
        if (!FateBookState.IsInked(PageId, "D") && _set.layout.PanelEnabled("D") && released)
        {
            float gateX = _wpPhoto != null ? _wpPhoto.position.x : _rockStartX;
            if (p.x > gateX + _cfg.photoPastMeters) FateBookState.Ink(PageId, "D", "走過石門（得到點 2 還沒接，先用位置代）");
        }
    }

    private IEnumerator EnterRoutine()
    {
        _busy = true;
        bool prev = _pm.isCutsceneFrozen;
        _pm.isCutsceneFrozen = true;
        _frozeByUs = true;
        Rigidbody rb = _pm.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (LogEvents) Debug.Log("[命運之書] 她到廢墟了：翻開廢墟頁（整頁鉛筆 → B 上墨）");
        Transform root = FateBookPage.OwnCanvasRoot();
        yield return FateBookPage.PlayEnter(root, _set, true);
        FateBookPage.HideOwnCanvas();
        if (!PlayerRespawnSystem.IsAnyRespawning) _pm.isCutsceneFrozen = prev;
        _frozeByUs = false;
        _busy = false;
    }

    // ── 測試（Play 模式；FateBookMenu 也會叫）────────────────────
    [ContextMenu("測試：翻開廢墟頁（進場）")]
    public void TestEnter()
    {
        if (!Application.isPlaying || _set == null || _busy) return;
        StartCoroutine(EnterRoutine());
    }

    [ContextMenu("測試：結束頁（推到 I、翻頁）")]
    public void TestEnd()
    {
        if (!Application.isPlaying || _set == null || _busy) return;
        StartCoroutine(TestEndRoutine());
    }

    private IEnumerator TestEndRoutine()
    {
        _busy = true;
        bool prev = _pm.isCutsceneFrozen;
        _pm.isCutsceneFrozen = true;
        Transform root = FateBookPage.OwnCanvasRoot();
        yield return FateBookPage.PlayEnd(root, _set, true);
        FateBookPage.HideOwnCanvas();
        if (!PlayerRespawnSystem.IsAnyRespawning) _pm.isCutsceneFrozen = prev;
        _busy = false;
    }

    [ContextMenu("測試：全部格子都上墨（看整頁）")]
    public void TestInkAll()
    {
        if (_set == null) return;
        foreach (FateBookPage.Panel p in _set.layout.panels) if (p != null && !p.neverInk) FateBookState.Ink(PageId, p.key, "測試");
    }
}
