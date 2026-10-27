using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Text = UnityEngine.UI.Text;

/// <summary>
/// ★1009 廢墟的光球（1008 會議：廢墟 b、e；02 補十五 #34「去掉拉桿，光球放下巨石」、#37「風暴改由光球觸發，由左到右掃過」）。
///
/// ★1010 改成「以修毅為準」（M 10-10：重疊的只留修毅的；巨石與風暴保留這支）：
///   ・光球怎麼走、鏡頭怎麼跟，全照修毅的 GuidanceLight 與路徑點：
///       P12（墜落時就在她前方，落地後碰到）→ 鏡頭跟著光球飛到 P13（原本拉桿旁）
///       P13 碰到 → 鏡頭跟著光球飛到 P14（斜坡頂）
///   ・這支只多做兩件事，不搬光球、不搶鏡頭：
///       1. 拉桿藏起來；她在 P13 碰到光球（光球停在 P13 時她走到 touchDistance 內），光球一亮，巨石掉下來（等於拉桿放開巨石）。
///       2. 原本的兩個風暴觸發區關掉、斜坡上的龍捲風先藏起來；她推巨石上坡、在 P14 碰到光球（或走過 P14）：
///          她、狼群、巨石停住，龍捲風從畫面左邊掃過來，掃到她就接文字卡（M2）、黑幕、進荒原。
///   ・保險：她放下巨石後沒碰光球就往右走（光球還停在 P13），走遠了光球直接傳到 P14 等她；
///          她走到 P14 時光球不在，也先傳過去再叫風暴。
///          ★1010 實機補：光球已經往 P14 去、巨石卻還沒放（例如她跳起來碰光球，修毅的光球先飛走）→ 馬上補放，不會卡關。
///          「碰到」的量法改成跟修毅一樣（她身上最靠近光球的點）。
///   ・重生：巨石已經放下（推巨石失敗，BoulderChallengeController 重置後巨石仍是放開的）→ 光球直接在 P14 等，不用再飛一次；
///          還沒放下就死 → 照修毅的 GuidanceLight 重置（回最近的路徑點）。
///   ・拿掉的（10-09 版有、10-10 起交給修毅）：落地時這支自己放光球、帶路到拉桿、追著她跑。
///
/// ★1016 M（27 章通關檢查：斜坡推巨石推不完）：風暴維持「碰到最後的光球才出現」，但最後的光球（P14）往下移到推巨石的路的約 6 成
///   （巨石起點 x≈141 → 原本 P14 x≈243；前約 49 米是平地、坡從 x≈190 開始，所以 6 成落在坡腳往上約 12 米）。
///   不動場景：進場兩幀後（等碰撞體建好）把 P14 這個路徑點的位置搬到「巨石起點 → 原本 P14」水平距離的 stormSlopeFraction 處、貼著地面、
///   離地高度跟原本一樣；光球飛過去、碰到叫風暴、重生在 P14 等，全部照舊讀這個點。moveStormWaypoint 關掉＝回到坡頂。
///   這支是執行時自己生的（Hierarchy 的 [廢墟光球導演]）：Play 中選它拉 stormSlopeFraction 會馬上重放 P14；
///   定好的數字要改這裡的預設值（Inspector 的改動停止 Play 就還原）。
///
/// 整包關掉：RuinsOrbDirector.Enabled = false（回到拉桿與原本的風暴）。
/// </summary>
[DisallowMultipleComponent]
public class RuinsOrbDirector : MonoBehaviour
{
    public static bool Enabled = true;

    /// <summary>★1015 從她在 P13 碰到光球（巨石放下）到風暴之前＝推巨石階段。這段不能跳（RuinsChaseNoJump 讀這個）。</summary>
    public static bool PushPhase;

    [Header("位置（路徑點名稱照修毅的場景）")]
    [Tooltip("她低於這個高度＝在廢墟")]
    public float ruinsBelowY = -60f;
    [Tooltip("原本拉桿旁的路徑點：光球停在這裡時，她碰到就放下巨石")]
    public string leverWaypointName = "P13";
    [Tooltip("斜坡頂的路徑點：光球停在這裡時，她碰到就叫風暴")]
    public string stormWaypointName = "P14";
    [Tooltip("她離光球多近算碰到（量法跟修毅的 GuidanceLight 一樣：她身上最靠近光球的點到光球。要比修毅的 touchTriggerDistance 1.5 大一點，才會比光球起飛早或同時）")]
    public float touchDistance = 2f;
    [Tooltip("光球離路徑點多近算「停在那裡」")]
    public float atWaypointTolerance = 2.5f;
    [Tooltip("巨石放下後光球還停在 P13、她已經往右走了這麼遠：光球直接傳到 P14 等她")]
    public float skipToStormIfPastLeverBy = 22f;
    [Tooltip("找不到 P13 時：她走到原本拉桿這麼近就放下巨石（光球不在也放，免得卡關）")]
    public float leverFallbackDistance = 2.5f;

    [Header("風暴")]
    [Tooltip("龍捲風由左往右掃的速度（米／秒）")]
    public float sweepSpeed = 16f;
    [Tooltip("從畫面左邊外面多遠開始")]
    public float sweepStartMargin = 6f;
    [Tooltip("掃到她之後，罩著她多久才接文字卡")]
    public float sweepHoldSeconds = 1.2f;
    [Tooltip("★1010 龍捲風掃過時加上 StormBoostFX（沙霧、鏡頭震、呼嘯；修毅清單待完成 8）")]
    public bool boostStormFx = true;

    [Header("★1011 風暴開場：鏡頭拉近玩家、狼淡出")]
    [Tooltip("鏡頭漸漸放大（拉近）到玩家身上要幾秒。要比『風暴掃到＋文字卡』短，在文字出現前就到位。0＝不拉近")]
    public float stormZoomSeconds = 1.8f;
    [Tooltip("拉近後的鏡頭大小（Orthographic Size，越小越近；廢墟平常約 11）")]
    public float stormZoomOrtho = 7.5f;
    [Tooltip("狼淡出消失要幾秒（稍快）")]
    public float wolfFadeSeconds = 0.6f;
    [Tooltip("開場（拉近＋狼淡出）先演這麼久，龍捲風才從左邊出現")]
    public float stormLeadSeconds = 0.9f;
    [Tooltip("P14 的語音（廢墟-3）播完才進文字卡；沒播完前龍捲風和沙霧會一直颳著")]
    public bool waitVoiceBeforeCard = true;
    [Tooltip("語音比龍捲風掃過來還長時：龍捲風配合語音長度慢慢逼近，在最後一句時吞沒她（關掉＝照原速掃過來、再停著等語音）")]
    public bool stretchSweepToVoice = true;
    [Tooltip("龍捲風慢慢逼近的期間，鏡頭推近到多大（正交大小，越小越近）。0＝不推")]
    public float voicePushInOrtho = 6f;
    [Tooltip("逼近的加速感（1＝等速，越大越是一開始慢、最後猛撲過來）")]
    [Range(1f, 3f)] public float approachEase = 1.6f;

    [Header("★1015 先演完砸石牆、再讓光球飛")]
    [Tooltip("光球要起飛時，最多等巨石砸碎石牆多久（秒）。保底，避免石牆沒碎就卡住")]
    public float gateMaxWaitSeconds = 8f;
    [Tooltip("石牆碎了之後，再多停多久才讓光球飛（秒），讓玩家看清楚")]
    public float gateHoldAfterBreakSeconds = 0.5f;   // ★1024 0.8→0.5

    [Header("★1015 保險：她跑到巨石前面（沒有推）")]
    [Tooltip("她比巨石還右邊超過這麼遠（公尺）＝越過巨石，拉回碰光球的地方")]
    public float passBoulderMargin = 2.5f;
    [Tooltip("拉回之後，這麼多秒內不再檢查（避免連續被拉）")]
    public float passGuardCooldownSeconds = 3f;
    [TextArea]
    [Tooltip("拉回時顯示的提示文字")]
    public string passBoulderHint = "光球帶你來到這裡了……\n要用力把巨石往前推，才能繼續前進。";
    public float hintSeconds = 4.5f;

    [Header("★1016 最後的光球往下移（風暴還是碰到它才來）")]
    [Tooltip("開著＝P14 搬到推巨石的路的 stormSlopeFraction 處（不動場景，執行時搬）；關掉＝留在原本的坡頂")]
    public bool moveStormWaypoint = false;   // ★1019 改回原本：P14 留在場景裡擺的位置（坡頂）。隊友覺得挑戰性不夠，要降低難度時直接在場景裡把 P14 往下拖就好
    [Tooltip("從巨石起點（Boulder Spawn）到原本 P14 的比例，照水平距離算。目前場景：前約 49 米平地、坡從 x≈190 開始——\n" +
             "0.48≈坡腳、0.6≈坡腳往上 12 米（預設，推約 61 米，原本約 102 米）、0.8≈坡的 6 成、1＝原本坡頂。太難就調小。\n" +
             "Play 中可以直接拉（馬上重放）；要保留就改程式裡的預設值")]
    [Range(0.3f, 1f)] public float stormSlopeFraction = 0.6f;

    [Header("★1016 砸完石牆，巨石滾到推巨石的起點")]
    [Tooltip("巨石砸碎石牆後停在牆下（x≈119.8），就在最上面那階石台旁邊：她從石台走下來會直接落在巨石頂上、被當成「越過巨石」拉回去；\n" +
             "而且第一次推的起點（119.8）跟死掉重來的起點（Boulder Spawn 140.9）不一樣。\n" +
             "打開＝石牆碎了、光球起飛時，巨石順勢滾過碎牆，停在 Boulder Spawn（之後第一次推跟重來一樣）。關掉＝原本（停在牆下）")]
    public bool rollBoulderToChallengeStart = true;
    [Tooltip("滾過去的最高速度（公尺／秒）")]
    public float boulderRollMaxSpeed = 7f;
    [Tooltip("快到起點時的減速度（公尺／秒²），越小越早開始慢下來")]
    public float boulderRollDecel = 3.5f;
    [Tooltip("最多滾幾秒；超過（被什麼卡住）就直接放到起點")]
    public float boulderRollMaxSeconds = 8f;

    public bool logEvents = true;

    private enum Stage { Idle, WaitLever, Releasing, WaitStorm, Storm }
    private Stage _stage = Stage.Idle;

    private PlayerMovement _pm;
    private GuidanceLight _orb;
    private LeverSystem _lever;
    private Rigidbody _rock;
    private StormSceneTransition _storm;
    private TornadoEncounterTrigger _encounter;
    private TornadoFollowCamera _tornado;
    private ParticleSystem[] _orbFx = new ParticleSystem[0];
    private Renderer[] _tornadoRenderers = new Renderer[0];

    private Transform _leverWp, _stormWp;
    private Collider _pcol;
    private bool _pendingReset;
    private Destructible _door;
    private bool _gateActive;
    private bool _hasPushStartPos;
    private Vector3 _pushStartPos;
    private float _guardCooldownUntil;
    private Text _hintText;
    private readonly System.Collections.Generic.List<Behaviour> _frozen = new System.Collections.Generic.List<Behaviour>();
    private UnityEngine.UI.Image _fade;

    // ── 自動掛載 ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { Install(); }

    private static void Install()
    {
        if (!Enabled) return;
        if (FindAnyObjectByType<RuinsOrbDirector>() != null) return;
        if (FindAnyObjectByType<GuidanceLight>() == null || FindAnyObjectByType<LeverSystem>() == null || FindAnyObjectByType<StormSceneTransition>() == null) return;
        new GameObject("[廢墟光球導演]").AddComponent<RuinsOrbDirector>();
    }

    // ── 初始 ─────────────────────────────────────────────────
    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (_pm != null)
        {
            // 跟修毅的 GuidanceLight.CachePlayerCollider 一樣找她的碰撞體
            _pcol = _pm.GetComponent<Collider>();
            if (_pcol == null) _pcol = _pm.GetComponentInChildren<Collider>();
            if (_pcol == null) _pcol = _pm.GetComponentInParent<Collider>();
        }
        _orb = FindAnyObjectByType<GuidanceLight>();
        _lever = FindAnyObjectByType<LeverSystem>();
        _rock = _lever != null ? _lever.targetRock : null;
        _storm = FindAnyObjectByType<StormSceneTransition>();
        _encounter = FindAnyObjectByType<TornadoEncounterTrigger>();
        if (_storm != null && _storm.backgroundTornadoes != null)
            foreach (TornadoFollowCamera t in _storm.backgroundTornadoes) if (t != null) { _tornado = t; break; }
        if (_tornado == null && _encounter != null) _tornado = _encounter.targetTornado;
        if (_tornado == null) _tornado = FindAnyObjectByType<TornadoFollowCamera>();
        if (_orb != null)
        {
            _orbFx = _orb.GetComponentsInChildren<ParticleSystem>(true);
            _leverWp = FindWaypoint(leverWaypointName);
            _stormWp = FindWaypoint(stormWaypointName);
        }

        RuinsDoor rd = FindAnyObjectByType<RuinsDoor>();
        if (rd != null) _door = rd.GetComponent<Destructible>();
        if (_orb != null) _orb.DepartureGate = DepartureGateRoutine;
        if (_stormWp != null) StartCoroutine(RelocateStormWaypoint());   // ★1016 記下原本的 P14；moveStormWaypoint 開著就往下移
        PushPhase = false;

        HideLever();
        DisableOldStormTriggers();
        HideTornado();
        PlayerRespawnSystem.OnResettablesReset += OnResettablesReset;
        if (logEvents)
            Debug.Log("【廢墟光球】拉桿已藏起來、原本的風暴觸發區已關；光球照修毅的路徑點走，在 " + leverWaypointName + (_leverWp != null ? "" : "（找不到，改用拉桿位置）") +
                      " 碰到放巨石、在 " + stormWaypointName + (_stormWp != null ? "" : "（找不到，改用斜坡頂附近）") + " 碰到叫風暴");
    }

    private void OnDestroy()
    {
        PushPhase = false;
        if (_orb != null) _orb.DepartureGate = null;
        PlayerRespawnSystem.OnResettablesReset -= OnResettablesReset;
    }

    private Transform FindWaypoint(string wpName)
    {
        if (_orb == null || _orb.waypoints == null || string.IsNullOrEmpty(wpName)) return null;
        foreach (Transform w in _orb.waypoints) if (w != null && w.name == wpName) return w;
        return null;
    }

    /// <summary>拉桿看不到、碰不到（LeverSystem 本身留著：重生時它負責把巨石放回原位）。</summary>
    private void HideLever()
    {
        if (_lever == null) return;
        foreach (Renderer r in _lever.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (Collider c in _lever.GetComponentsInChildren<Collider>(true)) c.enabled = false;
    }

    private void DisableOldStormTriggers()
    {
        if (_encounter != null)
        {
            foreach (Collider c in _encounter.GetComponents<Collider>()) c.enabled = false;
            foreach (Collider2D c in _encounter.GetComponents<Collider2D>()) c.enabled = false;
        }
        if (_storm != null)
        {
            foreach (Collider c in _storm.GetComponents<Collider>()) c.enabled = false;
            foreach (Collider2D c in _storm.GetComponents<Collider2D>()) c.enabled = false;
        }
        if (_tornado != null) _tornado.StopFollow();
    }

    /// <summary>斜坡上原本站著的龍捲風先看不到（風聲照舊），叫風暴時才出現；不然她會直接走過一個不動的龍捲風。</summary>
    private void HideTornado()
    {
        if (_tornado == null) return;
        _tornadoRenderers = _tornado.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in _tornadoRenderers) if (r != null) r.enabled = false;
    }

    private void ShowTornado()
    {
        foreach (Renderer r in _tornadoRenderers) if (r != null) r.enabled = true;
        if (_tornado == null) return;
        foreach (ParticleSystem ps in _tornado.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            ps.Simulate(2.5f, false, false, false);   // 先轉成形，出現時就是完整的龍捲風
            ps.Play(false);
        }
    }

    // ── 每幀 ─────────────────────────────────────────────────
    private void Update()
    {
        if (!Enabled || _pm == null || _orb == null) { PushPhase = false; return; }
        PushPhase = _stage == Stage.Releasing || _stage == Stage.WaitStorm || _gateActive;
        if (_stage == Stage.Storm) { PushPhase = false; return; }

        // ★1016 Play 中改 moveStormWaypoint／stormSlopeFraction → 馬上重放 P14（試手感用；風暴開始後就不動）
        if (_stormWpReady && (moveStormWaypoint != _stormMoveApplied || (moveStormWaypoint && !Mathf.Approximately(stormSlopeFraction, _stormFracApplied))))
            ApplyStormWaypoint();

        if (_pendingReset)
        {
            if (PlayerRespawnSystem.IsAnyRespawning && _pm.transform.position.y > ruinsBelowY) return;   // 還沒傳到存檔點
            _pendingReset = false;
            PlaceAfterRespawn();
        }

        Vector3 p = _pm.transform.position;
        bool inRuins = p.y < ruinsBelowY;

        if (_stage == Stage.Idle)
        {
            if (inRuins && !PlayerRespawnSystem.IsAnyRespawning) BeginRuins();
            return;
        }
        if (p.y > ruinsBelowY + 30f) { EndRuins(); return; }   // 測試時把她放回棉花堡
        if (PlayerRespawnSystem.IsAnyRespawning) return;

        switch (_stage)
        {
            case Stage.WaitLever:
                if (RockReleased) { _stage = Stage.WaitStorm; break; }   // 巨石已經放開了（其他路徑），直接等風暴
                if (_leverWp != null)
                {
                    // 修毅的光球停在 P13（不是飛行途中）、她走到光球旁 → 放巨石；光球接著照修毅的設定飛往 P14（鏡頭跟著）
                    if (OrbAt(_leverWp) && PlayerTouchesOrb()) StartCoroutine(ReleaseRoutine());
                    // 保險：光球已經往 P14 去了，巨石卻還沒放 → 馬上放（不然門破不了、光球在前面，卡關）
                    else if (OrbPastLever())
                    {
                        if (logEvents) Debug.Log("【廢墟光球】光球已離開 " + leverWaypointName + " 往 " + stormWaypointName + " 去，巨石還沒放：補放");
                        StartCoroutine(ReleaseRoutine());
                    }
                }
                else if (_lever != null && Vector2.Distance(p, _lever.transform.position) <= leverFallbackDistance)
                {
                    StartCoroutine(ReleaseRoutine());
                }
                break;

            case Stage.WaitStorm:
            {
                Vector3 stormPos = StormPoint();
                // 巨石放下了，她卻沒碰光球就往右走：光球直接到 P14 等（不再飛一次）
                if (_leverWp != null && _stormWp != null && OrbAt(_leverWp) && !_pm.isCutsceneFrozen && p.x > _leverWp.position.x + skipToStormIfPastLeverBy)
                {
                    _orb.TeleportToWaypointName(stormWaypointName);
                    if (logEvents) Debug.Log("【廢墟光球】她沒碰 " + leverWaypointName + " 的光球就往前走了：光球直接到 " + stormWaypointName + " 等她");
                }
                // ★1015 保險：她越過巨石（跑到巨石右邊）、沒有推 → 拉回碰光球的地方，並提示要推巨石
                if (_rock != null && RockReleased && _hasPushStartPos && !_pm.isCutsceneFrozen && !BoulderChallengeController.IsFailing
                    && Time.time >= _guardCooldownUntil && p.x > _rock.transform.position.x + passBoulderMargin && p.x < stormPos.x - 12f)
                {
                    _guardCooldownUntil = Time.time + passGuardCooldownSeconds;
                    if (logEvents) Debug.Log("【廢墟光球】她越過巨石了（人 x=" + p.x.ToString("F1") + "、巨石 x=" + _rock.transform.position.x.ToString("F1") + "）：拉回碰光球的地方並提示");
                    _pm.WarpTo(_pushStartPos);
                    ShowHint(passBoulderHint, hintSeconds);
                    break;
                }

                bool touched = _stormWp != null && OrbAt(_stormWp) && PlayerTouchesOrb();
                bool passed = p.x >= stormPos.x - 1f && Mathf.Abs(p.y - stormPos.y) < 8f;
                if ((touched || passed) && !BoulderChallengeController.IsFailing && !_pm.isCutsceneFrozen) StartCoroutine(StormRoutine());
                break;
            }
        }
    }

    private bool RockReleased => _rock != null && !_rock.isKinematic;

    /// <summary>光球（修毅的 GuidanceLight）停在這個路徑點上：在容許範圍內，而且她沒有被光球飛行鎖住（飛行途中不算）。</summary>
    private bool OrbAt(Transform wp)
    {
        if (wp == null || _orb == null || !_orb.isActiveAndEnabled) return false;
        Vector2 d = (Vector2)(_orb.transform.position - wp.position);
        return d.magnitude <= atWaypointTolerance;
    }

    /// <summary>
    /// ★1010 實機：原本量她的腳底（transform）到光球，她跳起來用頭碰光球時，修毅的「碰到」（量她身上最近的點，1.5）先成立，
    /// 光球飛走了這裡還沒到 3 → 巨石沒放下、光球已在 P14，卡關。改成跟修毅同一種量法。
    /// </summary>
    /// <summary>
    /// ★1010 碰到光球：用修毅 GuidanceLight 同一套範圍（圓＋地面／跳起的長方形＋X 備援，高度差有上限），
    /// 圓用 touchDistance（比修毅的 1.5 大），長方形和備援兩邊一樣，所以放巨石不會比光球起飛晚。
    /// </summary>
    private bool PlayerTouchesOrb()
    {
        if (_orb == null) return false;
        return _orb.IsPlayerInTouchRange(touchDistance) || PlayerToOrb() <= touchDistance;
    }

    private float PlayerToOrb()
    {
        Vector3 b = _orb.transform.position;
        Vector3 a = (_pcol != null && _pcol.enabled && _pcol.gameObject.activeInHierarchy) ? _pcol.ClosestPoint(b) : _pm.transform.position;
        return Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y));
    }

    /// <summary>光球已經離開 P13 往 P14 那邊去了（她碰到光球、修毅的光球先飛了；或重生後光球回到 P14）。</summary>
    private bool OrbPastLever()
    {
        if (_leverWp == null || _orb == null || !_orb.isActiveAndEnabled) return false;
        float dir = (_stormWp != null && _stormWp.position.x < _leverWp.position.x) ? -1f : 1f;   // P14 在 P13 的哪一邊
        return (_orb.transform.position.x - _leverWp.position.x) * dir > atWaypointTolerance;
    }

    private Vector3 StormPoint()
    {
        if (_stormWp != null) return _stormWp.position;
        if (_storm != null) return _storm.transform.position + Vector3.left * 6f;
        return _pm.transform.position + Vector3.right * 1000f;
    }

    private void BeginRuins()
    {
        _stage = RockReleased ? Stage.WaitStorm : Stage.WaitLever;
        if (logEvents) Debug.Log("【廢墟光球】她到廢墟了：光球照修毅的路徑點走（" + (RockReleased ? "巨石已放下，等她到 " + stormWaypointName : "在 " + leverWaypointName + " 碰到就放巨石") + "）");
    }

    private void EndRuins()
    {
        StopAllCoroutines();
        _gateActive = false;
        _hasPushStartPos = false;
        PushPhase = false;
        _stage = Stage.Idle;
        if (_orb != null && !_orb.enabled) _orb.enabled = true;
    }

    // ── 放下巨石 ─────────────────────────────────────────────
    private IEnumerator ReleaseRoutine()
    {
        _stage = Stage.Releasing;
        _pushStartPos = _pm.transform.position;
        _hasPushStartPos = true;
        Flare(40);
        PlayOrbSfx();
        yield return new WaitForSeconds(0.15f);   // ★1024 0.35→0.15，碰到光球到巨石放下少等 0.2 秒
        if (_rock != null && _rock.isKinematic)
        {
            _rock.isKinematic = false;
            _rock.linearVelocity = Vector3.zero;
            RollingRockVisual v = _rock.GetComponent<RollingRockVisual>();
            if (v != null) v.enabled = true;
        }
        if (logEvents) Debug.Log("【廢墟光球】她在 " + leverWaypointName + " 碰到光球：巨石放下（取代拉桿）；光球照修毅的設定飛往 " + stormWaypointName);
        _stage = Stage.WaitStorm;
    }

    // ── ★1015 光球起飛前的閘門：先演完巨石砸碎石牆 ──────────────
    private IEnumerator DepartureGateRoutine()
    {
        if (!Enabled || _stage == Stage.Idle || _stage == Stage.Storm) yield break;
        if (_leverWp == null || !OrbAt(_leverWp)) yield break;   // 不是從 P13 起飛，不等

        _gateActive = true;
        PushPhase = true;
        if (_stage == Stage.WaitLever) StartCoroutine(ReleaseRoutine());   // 她碰到光球＝放巨石（已經在放就不重複）

        float t = 0f;
        while (t < gateMaxWaitSeconds && !PlayerRespawnSystem.IsAnyRespawning)
        {
            if (_door == null || _door.HasShattered) break;
            t += Time.deltaTime;
            yield return null;
        }
        // ★1024 巨石砸碎牆的那一刻就接著滾（原本要等光球起飛前的停留 0.8 秒過完才開始滾，砸完停一下才滾很不順）
        if (rollBoulderToChallengeStart && _door != null && _door.HasShattered && !PlayerRespawnSystem.IsAnyRespawning)
            StartCoroutine(RollBoulderToChallengeStartRoutine());

        float h = 0f;
        while (h < gateHoldAfterBreakSeconds && !PlayerRespawnSystem.IsAnyRespawning)
        {
            h += Time.deltaTime;
            yield return null;
        }
        if (logEvents) Debug.Log("【廢墟光球】石牆演完了（等了 " + t.ToString("F1") + " 秒）：光球起飛，鏡頭跟過去");
        _gateActive = false;
    }

    /// <summary>★1016 砸完石牆：巨石往右滾到 Boulder Spawn 停下（用物理滾，外觀會跟著轉）。</summary>
    private IEnumerator RollBoulderToChallengeStartRoutine()
    {
        BoulderChallengeController bcc = BoulderChallengeController.Instance;
        Transform spawn = bcc != null ? bcc.BoulderSpawnPoint : null;
        if (_rock == null || spawn == null) yield break;

        float targetX = spawn.position.x;
        if (_rock.position.x >= targetX - 0.3f) yield break;   // 已經在起點（或更右邊）

        if (_rock.isKinematic) yield break;                     // 還沒放開就不動它
        float startX = _rock.position.x;
        if (logEvents) Debug.Log("【廢墟光球】★1016 巨石滾過碎牆：x " + startX.ToString("F1") + " → 推巨石起點 " + targetX.ToString("F1"));

        float t = 0f;
        float maxSpeed = Mathf.Max(0.5f, boulderRollMaxSpeed);
        float decel = Mathf.Max(0.1f, boulderRollDecel);
        while (t < boulderRollMaxSeconds)
        {
            if (_rock == null || _rock.isKinematic) yield break;
            if (PlayerRespawnSystem.IsAnyRespawning || BoulderChallengeController.IsFailing) yield break;
            if (RollingRockVisual.ActivelyPushed != null) break;   // 她已經上來推了，交給她

            float remain = targetX - _rock.position.x;
            if (remain <= 0.05f) break;
            float v = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * decel * remain));   // 越接近越慢，剛好停在起點
            v = Mathf.Max(v, 0.4f);
            Vector3 vel = _rock.linearVelocity;
            _rock.linearVelocity = new Vector3(v, Mathf.Min(vel.y, 0.5f), 0f);
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        if (_rock == null || _rock.isKinematic) yield break;
        _rock.linearVelocity = new Vector3(0f, Mathf.Min(_rock.linearVelocity.y, 0f), 0f);
        _rock.angularVelocity = Vector3.zero;

        // 被卡住、沒滾到：直接放到起點（跟死掉重來時巨石回去的做法一樣）
        if (RollingRockVisual.ActivelyPushed == null && Mathf.Abs(_rock.position.x - targetX) > 0.6f)
        {
            _rock.isKinematic = true;
            _rock.transform.position = spawn.position;
            _rock.position = spawn.position;
            _rock.isKinematic = false;
            _rock.linearVelocity = Vector3.zero;
            _rock.angularVelocity = Vector3.zero;
            if (logEvents) Debug.Log("【廢墟光球】★1016 巨石沒滾到（停在 x " + _rock.position.x.ToString("F1") + "），直接放到推巨石起點");
        }
    }

    // ── ★1015 提示文字 ───────────────────────────────────────
    private void ShowHint(string msg, float seconds)
    {
        if (_hintText == null)
        {
            GameObject canvasObj = new GameObject("[廢墟提示]");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9000;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            GameObject go = new GameObject("Hint");
            go.transform.SetParent(canvasObj.transform, false);
            _hintText = go.AddComponent<Text>();
            _hintText.font = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft JhengHei", "Microsoft YaHei", "Arial" }, 34);
            _hintText.fontSize = 34;
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.color = new Color(1f, 0.96f, 0.85f, 0f);
            _hintText.raycastTarget = false;
            go.AddComponent<UnityEngine.UI.Shadow>().effectDistance = new Vector2(2f, -2f);
            RectTransform rt = _hintText.rectTransform;
            rt.anchorMin = new Vector2(0.1f, 0.14f);
            rt.anchorMax = new Vector2(0.9f, 0.34f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        _hintText.text = msg;
        StartCoroutine(HintRoutine(seconds));
    }

    private IEnumerator HintRoutine(float seconds)
    {
        float fade = 0.5f;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(Mathf.Min(t / fade, (seconds - t) / fade));
            Color c = _hintText.color;
            c.a = a;
            _hintText.color = c;
            yield return null;
        }
        Color c0 = _hintText.color;
        c0.a = 0f;
        _hintText.color = c0;
    }

    // ── 風暴 ─────────────────────────────────────────────────
    private IEnumerator StormRoutine()
    {
        _stage = Stage.Storm;
        if (logEvents) Debug.Log("【廢墟光球】她在斜坡頂碰到光球：叫風暴，龍捲風由左往右掃過");

        // 光球停在原地（之後淡掉）；修毅的 GuidanceLight 不再動它
        if (_stormWp != null && !OrbAt(_stormWp)) _orb.TeleportToWaypointName(stormWaypointName);
        // 這裡的「碰到」範圍比光球自己的大，常常先觸發：先確定 P14 的語音有播（播過就不會重播）
        _orb.PlayVoiceForWaypoint(stormWaypointName);
        float voiceEnd = waitVoiceBeforeCard ? _orb.VoiceEndTime : 0f;
        _orb.enabled = false;

        // 她、狼群、巨石都停住
        _pm.isCutsceneFrozen = true;
        Rigidbody prb = _pm.GetComponent<Rigidbody>();
        if (prb != null && !prb.isKinematic) { prb.linearVelocity = Vector3.zero; prb.angularVelocity = Vector3.zero; }
        FreezeWolves();
        if (stormZoomSeconds > 0.05f) CameraTargetXFollower.SetCameraOverrideSmooth(_pm.transform, stormZoomOrtho, stormZoomSeconds);   // ★1011 鏡頭漸漸放大到玩家身上
        StartCoroutine(FadeOutWolves(wolfFadeSeconds));   // ★1011 狼稍快地隱形消失
        if (_rock != null) { if (!_rock.isKinematic) { _rock.linearVelocity = Vector3.zero; _rock.angularVelocity = Vector3.zero; } _rock.isKinematic = true; }

        Flare(40);
        PlayOrbSfx();
        if (_storm != null && _storm.stormVortexSFX != null)
            AudioSource.PlayClipAtPoint(_storm.stormVortexSFX, _pm.transform.position, AudioManager.ScaleSfx(_storm.sfxVolume));

        StormBoostFX fx = null;
        if (boostStormFx && StormBoostFX.Enabled)
        {
            fx = StormBoostFX.Ensure();
            fx.SetManual(0.45f, +1f, StormBoostFX.RuinsDustColor);   // 龍捲風還在畫面外：天色先暗、風吼起來
        }

        if (stormLeadSeconds > 0f) yield return new WaitForSeconds(stormLeadSeconds);   // ★1011 先讓拉近與狼淡出演一下，龍捲風再進來

        Camera cam = Camera.main;
        if (_tornado != null && cam != null)
        {
            _tornado.StopFollow();
            _tornado.enabled = false;   // 停掉它自己的跟隨／巡邏，位置由這裡帶
            Transform tt = _tornado.transform;
            float halfW = cam.orthographicSize * cam.aspect;
            float y0 = cam.transform.position.y + _tornado.offsetY;
            tt.position = new Vector3(cam.transform.position.x - halfW - sweepStartMargin, y0, _tornado.fixedZ);
            ShowTornado();
            if (fx != null) fx.Hit(0.8f);   // 龍捲風出現在畫面左邊的那一下
            float targetX = _pm.transform.position.x;
            float startX = tt.position.x;

            // 語音還很長：龍捲風配合語音慢慢逼近（越來越快），在最後一句時吞沒她；鏡頭同時一路推近
            float normalTime = (targetX - startX) / Mathf.Max(0.1f, sweepSpeed);
            float approach = stretchSweepToVoice ? voiceEnd - Time.unscaledTime - sweepHoldSeconds : 0f;
            bool slow = approach > normalTime + 0.5f;
            if (slow && voicePushInOrtho > 0.1f) CameraTargetXFollower.SetCameraOverrideSmooth(_pm.transform, voicePushInOrtho, approach);

            float guard = 0f;
            float approachStart = Time.unscaledTime;   // 跟語音用同一個時鐘：遊戲掉幀時龍捲風也不會跟台詞脫節
            while (tt.position.x < targetX && guard < (slow ? approach + 5f : 10f))
            {
                float dt = Time.deltaTime;
                guard = slow ? Time.unscaledTime - approachStart : guard + dt;
                float camY = cam.transform.position.y + _tornado.offsetY;
                float nx = slow
                    ? Mathf.Lerp(startX, targetX, Mathf.Pow(Mathf.Clamp01(guard / approach), approachEase))
                    : tt.position.x + sweepSpeed * dt;
                if (slow && guard >= approach) nx = targetX;
                tt.position = new Vector3(nx, Mathf.Lerp(tt.position.y, camY, dt * 6f), _tornado.fixedZ);
                FadeOrb(1f - Mathf.Clamp01((tt.position.x - (targetX - halfW)) / halfW));
                if (fx != null) fx.SetManual(Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(startX, targetX, tt.position.x)), +1f, StormBoostFX.RuinsDustColor);
                yield return null;
            }
            float hold = Mathf.Max(sweepHoldSeconds, voiceEnd - Time.unscaledTime);   // 語音還沒播完就繼續颳風，播完才進文字卡
            if (fx != null) { fx.SetManual(1f, +1f, StormBoostFX.RuinsDustColor, hold + 8f); fx.Hit(1f); }   // 掃到她（罩到文字卡、黑幕）
            float h = 0f;
            while (h < sweepHoldSeconds || Time.unscaledTime < voiceEnd)
            {
                float dt = Time.deltaTime;
                h += dt;
                tt.position += Vector3.right * (sweepSpeed * (h <= sweepHoldSeconds ? 0.15f : 0.04f) * dt);   // 等語音時飄慢一點，別飄出畫面
                yield return null;
            }
        }
        else
        {
            float hold = Mathf.Max(sweepHoldSeconds, voiceEnd - Time.unscaledTime);
            if (fx != null) { fx.SetManual(1f, +1f, StormBoostFX.RuinsDustColor, hold + 8f); fx.Hit(1f); }
            yield return new WaitForSeconds(sweepHoldSeconds);
            while (Time.unscaledTime < voiceEnd) yield return null;
        }
        FadeOrb(0f);

        // 接原本風暴轉場的後半：文字卡 → 黑幕 → 荒原
        string card = _storm != null ? _storm.storyCardId : "M2";
        if (StoryCardPlayer.Instance != null && !string.IsNullOrEmpty(card) && StoryCardPlayer.Instance.HasCard(card))
            yield return StoryCardPlayer.Instance.Play(card, true, false);

        float fadeDur = _storm != null ? Mathf.Max(0.05f, _storm.fadeDuration) : 1.2f;
        CreateFadeImage();
        float ft = 0f;
        while (ft < fadeDur)
        {
            ft += Time.deltaTime;
            if (_fade != null) _fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(ft / fadeDur));
            yield return null;
        }
        if (_fade != null) _fade.color = Color.black;
        yield return new WaitForSeconds(0.1f);

        string spawn = _storm != null ? _storm.targetSpawnPointName : "SpawnPoint_FromSampleScene";
        string next = _storm != null && !string.IsNullOrWhiteSpace(_storm.nextSceneName) ? _storm.nextSceneName.Trim() : "desert";
        if (!string.IsNullOrEmpty(spawn)) PlayerRespawnSystem.QueueNextSceneSpawn(spawn);
        if (logEvents) Debug.Log("【廢墟光球】風暴轉場完成，載入 " + next);
        SceneLoader.Load(next);
    }

    /// <summary>★1011 狼淡出（只改圖片透明度），淡完關掉。咬在她身上的也一起消失。</summary>
    private IEnumerator FadeOutWolves(float seconds)
    {
        var wolves = FindObjectsByType<WolfEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var srs = new System.Collections.Generic.List<SpriteRenderer>();
        foreach (WolfEnemy w in wolves) srs.AddRange(w.GetComponentsInChildren<SpriteRenderer>());
        float[] startA = new float[srs.Count];
        for (int i = 0; i < srs.Count; i++) startA[i] = srs[i] != null ? srs[i].color.a : 0f;

        seconds = Mathf.Max(0.05f, seconds);
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            for (int i = 0; i < srs.Count; i++)
            {
                if (srs[i] == null) continue;
                Color c = srs[i].color;
                c.a = Mathf.Lerp(startA[i], 0f, k);
                srs[i].color = c;
            }
            yield return null;
        }
        foreach (WolfEnemy w in wolves) if (w != null) w.gameObject.SetActive(false);
    }

    private void FreezeWolves()
    {
        _frozen.Clear();
        foreach (WolfEnemy w in FindObjectsByType<WolfEnemy>(FindObjectsInactive.Exclude))
            if (w.enabled) { w.enabled = false; _frozen.Add(w); }
        foreach (WolfSpawner s in FindObjectsByType<WolfSpawner>(FindObjectsInactive.Exclude))
            if (s.enabled) { s.enabled = false; _frozen.Add(s); }
    }

    private void CreateFadeImage()
    {
        GameObject canvasObj = new GameObject("[廢墟光球 黑幕]");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        GameObject img = new GameObject("Fade");
        img.transform.SetParent(canvasObj.transform, false);
        _fade = img.AddComponent<UnityEngine.UI.Image>();
        _fade.raycastTarget = false;
        _fade.color = new Color(0f, 0f, 0f, 0f);
        RectTransform rt = _fade.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // ── 重生 ─────────────────────────────────────────────────
    /// <summary>全場景 IResettable 跑完之後（GuidanceLight 自己會回到離重生點最近的路徑點），下一幀等她傳到存檔點再決定。</summary>
    private void OnResettablesReset()
    {
        if (_stage == Stage.Idle || _stage == Stage.Storm) return;
        StopAllCoroutines();
        _pendingReset = true;
    }

    private void PlaceAfterRespawn()
    {
        HideLever();   // 拉桿的重置會重綁動畫，保險再藏一次
        Vector3 p = _pm.transform.position;
        if (p.y > ruinsBelowY) { EndRuins(); return; }
        if (RockReleased)
        {
            // 推巨石失敗：巨石重置後仍是放開的（BoulderChallengeController 的 unlockBoulderAfterReset）→ 光球直接在 P14 等，不用再飛一次
            if (_stormWp != null) _orb.TeleportToWaypointName(stormWaypointName);
            _stage = Stage.WaitStorm;
        }
        else
        {
            // 還沒放下巨石就死（拉桿的重置會把巨石放回原位）→ 照修毅的 GuidanceLight 重置，光球回最近的路徑點
            _stage = Stage.WaitLever;
        }
        FadeOrb(1f);
        if (logEvents) Debug.Log("【廢墟光球】重生：" + (RockReleased ? "巨石已放下，光球在 " + stormWaypointName + " 等" : "巨石還沒放下，光球照修毅的重置（最近的路徑點）"));
    }

    // ── ★1016 最後的光球往下移 ───────────────────────────────
    private Vector3 _stormWpOriginal;      // 場景裡原本的 P14（坡頂）
    private bool _stormWpReady;            // 已經記下原本的 P14
    private bool _stormMoveApplied;
    private float _stormFracApplied = -1f;

    /// <summary>等兩幀（坡面的碰撞體有些是 Start 才建：PolygonToMeshCollider），記下原本的 P14，再照欄位放。</summary>
    private IEnumerator RelocateStormWaypoint()
    {
        yield return null;
        yield return null;
        if (_stormWp == null) yield break;
        _stormWpOriginal = _stormWp.position;
        _stormWpReady = true;
        ApplyStormWaypoint();
    }

    /// <summary>
    /// 照 moveStormWaypoint／stormSlopeFraction 放 P14；關掉＝放回原本的坡頂。Play 中改這兩個欄位，Update 會再叫一次（試手感用）。
    /// 起點用巨石挑戰的 Boulder Spawn（推巨石失敗時巨石放回的地方＝坡底；沒接就找叫 ChallengeBoulderSpawn 的物件）；都找不到就用 P13 往右 25 米。
    /// 高度：從巨石起點沿著地面走到新位置找地面（TraceGround），再加上原本 P14 離地的高度（光球一樣懸在地面上方）；找不到地面就不搬。
    /// 光球如果正停在 P14 等，跟著傳過去。
    /// </summary>
    private void ApplyStormWaypoint()
    {
        if (!_stormWpReady || _stormWp == null) return;
        _stormMoveApplied = moveStormWaypoint;
        _stormFracApplied = stormSlopeFraction;
        Vector3 end = _stormWpOriginal;
        bool orbWaiting = OrbAt(_stormWp);

        if (!moveStormWaypoint)
        {
            if ((_stormWp.position - end).sqrMagnitude < 0.0001f) return;
            _stormWp.position = end;
            if (orbWaiting) _orb.TeleportToWaypointName(stormWaypointName);
            if (logEvents) Debug.Log("【廢墟光球】★1016 最後的光球 " + stormWaypointName + " 放回原本的坡頂 (" + end.x.ToString("F1") + ", " + end.y.ToString("F1") + ")");
            return;
        }

        Transform startT = BoulderChallengeController.Instance != null ? BoulderChallengeController.Instance.BoulderSpawnPoint : null;
        if (startT == null)
        {
            GameObject g = GameObject.Find("ChallengeBoulderSpawn");
            if (g == null) g = GameObject.Find("ChallengePlayerSpawn");
            if (g != null) startT = g.transform;
        }
        Vector3 start = startT != null ? startT.position
                      : (_leverWp != null ? _leverWp.position + Vector3.right * 25f : end + Vector3.left * 100f);
        if (end.x - start.x < 10f)
        {
            if (logEvents) Debug.LogWarning("【廢墟光球】★1016 P14 跟坡底太近（" + (end.x - start.x).ToString("F1") + " 米），不搬");
            return;
        }
        float z = _pm != null ? _pm.transform.position.z : 0f;
        float yStart = GroundBelow(start.x, start.y + 3f, z, start.y - 1f);
        float yEnd = GroundBelow(end.x, end.y, z, end.y - 1.5f);
        float hover = Mathf.Clamp(end.y - yEnd, 0.5f, 6f);
        float f = Mathf.Clamp(stormSlopeFraction, 0.3f, 1f);
        float x = Mathf.Lerp(start.x, end.x, f);
        // 坡前有一段平地、坡腳才開始陡（不是一條直線），所以沿著地面一步步走過去找高度
        float gy = TraceGround(start.x, yStart, x, z, out int lost, out int steps);
        if (lost > steps / 5)
        {
            if ((_stormWp.position - end).sqrMagnitude > 0.0001f)
            {
                _stormWp.position = end;
                if (orbWaiting) _orb.TeleportToWaypointName(stormWaypointName);
            }
            if (logEvents) Debug.LogWarning("【廢墟光球】★1016 沿地面找坡面失敗（" + lost + "/" + steps + " 步沒打到地面），P14 不搬，留在原本的坡頂");
            return;
        }
        _stormWp.position = new Vector3(x, gy + hover, end.z);
        if (orbWaiting) _orb.TeleportToWaypointName(stormWaypointName);
        if (logEvents)
            Debug.Log("【廢墟光球】★1016 最後的光球 " + stormWaypointName + " 往下移：(" + end.x.ToString("F1") + ", " + end.y.ToString("F1") + ") → (" +
                      x.ToString("F1") + ", " + (gy + hover).ToString("F1") + ")，推的路的 " + Mathf.RoundToInt(f * 100f) + "%（從巨石起點 x " + start.x.ToString("F1") +
                      " 起推約 " + (x - start.x).ToString("F0") + " 米，原本 " + (end.x - start.x).ToString("F0") + " 米；水平距離）；離地 " + hover.ToString("F1") + " 米");
    }

    /// <summary>
    /// 從 (x0, y0) 沿著地面一步步走到 x1（每步約 0.5 米、每步在上一步高度上下各看 2.5 米），回傳 x1 處的地面高度。
    /// 平地、坡腳、坡面都跟得上（坡度到約 78°），不會跳到坡上方的浮石。lost＝沒打到地面的步數（縫就沿用上一步的高度）。
    /// </summary>
    private float TraceGround(float x0, float y0, float x1, float z, out int lost, out int steps)
    {
        int mask = ~LayerMask.GetMask("Ignore Raycast");   // 跟 PlayerMovement 踩地的射線一樣
        steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(x1 - x0) / 0.5f));
        float dx = (x1 - x0) / steps, y = y0;
        lost = 0;
        for (int i = 1; i <= steps; i++)
        {
            float x = x0 + dx * i;
            float best = float.PositiveInfinity, ny = y;
            foreach (float zz in new float[] { z, 0f })
            {
                RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, y + 2.5f, zz), Vector3.down, 5f, mask, QueryTriggerInteraction.Ignore);
                foreach (RaycastHit h in hits)
                {
                    if (!IsGround(h)) continue;
                    if (h.distance < best) { best = h.distance; ny = h.point.y; }
                }
                if (!float.IsPositiveInfinity(best)) break;
            }
            if (float.IsPositiveInfinity(best)) lost++;
            y = ny;
        }
        return y;
    }

    /// <summary>從 fromY 往下找最近的地面（略過她、狼、巨石、光球、觸發區）。找不到回 fallback。</summary>
    private float GroundBelow(float x, float fromY, float z, float fallback)
    {
        float best = float.PositiveInfinity, y = fallback;
        foreach (float zz in new float[] { z, 0f })
        {
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, fromY, zz), Vector3.down, 60f, ~LayerMask.GetMask("Ignore Raycast"), QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (!IsGround(h)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; }
            }
            if (!float.IsPositiveInfinity(best)) break;
        }
        return y;
    }

    private bool IsGround(RaycastHit h)
    {
        if (h.collider == null || h.normal.y < 0.25f) return false;
        if (h.collider.GetComponentInParent<PlayerMovement>() != null) return false;
        if (h.collider.GetComponentInParent<WolfEnemy>() != null) return false;
        if (h.collider.GetComponentInParent<GuidanceLight>() != null) return false;
        if (_rock != null && h.rigidbody == _rock) return false;
        if (h.collider.GetComponentInParent<RollingRockVisual>() != null) return false;
        return true;
    }

    // ── 小工具 ───────────────────────────────────────────────
    private void Flare(int count)
    {
        foreach (ParticleSystem ps in _orbFx) if (ps != null) ps.Emit(count);
    }

    private void FadeOrb(float a)
    {
        foreach (ParticleSystem ps in _orbFx)
        {
            if (ps == null) continue;
            var em = ps.emission;
            em.enabled = a > 0.05f;
        }
    }

    private void PlayOrbSfx()
    {
        if (_orb != null && _orb.absorbSFX != null)
            AudioSource.PlayClipAtPoint(_orb.absorbSFX, _orb.transform.position, AudioManager.ScaleSfx(_orb.sfxVolume));
    }
}
