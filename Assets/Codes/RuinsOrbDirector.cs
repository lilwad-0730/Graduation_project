using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

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
/// 整包關掉：RuinsOrbDirector.Enabled = false（回到拉桿與原本的風暴）。
/// </summary>
[DisallowMultipleComponent]
public class RuinsOrbDirector : MonoBehaviour
{
    public static bool Enabled = true;

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
        if (!Enabled || _pm == null || _orb == null) return;
        if (_stage == Stage.Storm) return;

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
                    if (OrbAt(_leverWp) && PlayerToOrb() <= touchDistance) StartCoroutine(ReleaseRoutine());
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
                bool touched = _stormWp != null && OrbAt(_stormWp) && PlayerToOrb() <= touchDistance;
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
        _stage = Stage.Idle;
        if (_orb != null && !_orb.enabled) _orb.enabled = true;
    }

    // ── 放下巨石 ─────────────────────────────────────────────
    private IEnumerator ReleaseRoutine()
    {
        _stage = Stage.Releasing;
        Flare(40);
        PlayOrbSfx();
        yield return new WaitForSeconds(0.35f);
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

    // ── 風暴 ─────────────────────────────────────────────────
    private IEnumerator StormRoutine()
    {
        _stage = Stage.Storm;
        if (logEvents) Debug.Log("【廢墟光球】她在斜坡頂碰到光球：叫風暴，龍捲風由左往右掃過");

        // 光球停在原地（之後淡掉）；修毅的 GuidanceLight 不再動它
        if (_stormWp != null && !OrbAt(_stormWp)) _orb.TeleportToWaypointName(stormWaypointName);
        _orb.enabled = false;

        // 她、狼群、巨石都停住
        _pm.isCutsceneFrozen = true;
        Rigidbody prb = _pm.GetComponent<Rigidbody>();
        if (prb != null && !prb.isKinematic) { prb.linearVelocity = Vector3.zero; prb.angularVelocity = Vector3.zero; }
        FreezeWolves();
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
            float guard = 0f;
            while (tt.position.x < targetX && guard < 10f)
            {
                float dt = Time.deltaTime;
                guard += dt;
                float camY = cam.transform.position.y + _tornado.offsetY;
                tt.position = new Vector3(tt.position.x + sweepSpeed * dt, Mathf.Lerp(tt.position.y, camY, dt * 6f), _tornado.fixedZ);
                FadeOrb(1f - Mathf.Clamp01((tt.position.x - (targetX - halfW)) / halfW));
                if (fx != null) fx.SetManual(Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(startX, targetX, tt.position.x)), +1f, StormBoostFX.RuinsDustColor);
                yield return null;
            }
            if (fx != null) { fx.SetManual(1f, +1f, StormBoostFX.RuinsDustColor, sweepHoldSeconds + 8f); fx.Hit(1f); }   // 掃到她（罩到文字卡、黑幕）
            float h = 0f;
            while (h < sweepHoldSeconds)
            {
                float dt = Time.deltaTime;
                h += dt;
                tt.position += Vector3.right * (sweepSpeed * 0.15f * dt);
                yield return null;
            }
        }
        else
        {
            if (fx != null) { fx.SetManual(1f, +1f, StormBoostFX.RuinsDustColor, sweepHoldSeconds + 8f); fx.Hit(1f); }
            yield return new WaitForSeconds(sweepHoldSeconds);
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
        SceneManager.LoadScene(next);
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
