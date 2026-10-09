using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1009 廢墟的光球（1008 會議：廢墟 b、e；02 補十五 #34「去掉拉桿，光球放下巨石」、#37「風暴改由光球觸發，由左到右掃過」）。
///
/// 改動前：
///   ・光球（FairyLight 的 GuidanceLight）只有棉花堡的路徑點，跳下廢墟後留在天上，廢墟裡看不到光球。
///   ・巨石要走到石階上的拉桿（LeverSystem）按 E 才掉下來。
///   ・走上斜坡碰到 Tornado_FollowTrigger，龍捲風就黏在鏡頭中間一路跟著（留存）；走到 Storm_TransitionTrigger 才被吸進去轉場。
/// 改動後（這支一手包辦，不改場景、不改 LeverSystem／StormSceneTransition／TornadoFollowCamera）：
///   1. 落地後光球出現在她前方 landAheadX 米，帶她往原本拉桿的位置走（她落後太多就停下來等；她跑到前面，光球就加快趕過去）。
///   2. 拉桿藏起來（看不到、碰不到、不會出「按下 E」）；光球停在拉桿上方，她走近，光球一亮，巨石掉下來（等於拉桿放開巨石）。
///   3. 光球飛到斜坡上 stormOrbX 的位置等她；她推到那裡碰到光球：她、狼群、巨石都停住，
///      龍捲風從畫面左邊出現、往右掃，掃到她之後接原本的文字卡（M2）、黑幕、載入荒原（設定照 StormSceneTransition 上的）。
///      原本的兩個風暴觸發區關掉，龍捲風不再跟著鏡頭；斜坡上原本站著的龍捲風先藏起來（聲音留著），叫風暴時才從左邊出現。
///   重生：還沒放下巨石就死 → 光球回到她前面重新帶路；推巨石失敗（巨石重置後仍是放開的）→ 光球留在斜坡上等。
/// 整包關掉：RuinsOrbDirector.Enabled = false（回到拉桿與原本的風暴）。
///
/// ★1010 跟修毅 10-10（d0f715a）合併：墜落時 PlayerMovement 先把光球傳到 P12、鏡頭特寫光球，落到廢墟地板才放開；
/// 這支等墜落鎖放開才接手，光球已經在她前方（P12）就從那裡帶路。修毅加的 P13、P14 在這支開著時用不到。
/// </summary>
[DisallowMultipleComponent]
public class RuinsOrbDirector : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("位置")]
    [Tooltip("她低於這個高度＝在廢墟")]
    public float ruinsBelowY = -60f;
    [Tooltip("落地後光球出現在她前方幾米")]
    public float landAheadX = 9f;
    [Tooltip("★1010 落地時光球如果已經在廢墟、在她前方（修毅的 P12：墜落時鏡頭特寫的那顆），就從那裡開始帶路，不另外放")]
    public bool startFromCurrentOrb = true;
    [Tooltip("光球離地多高")]
    public float orbHeight = 2.2f;
    [Tooltip("光球停在原本拉桿位置的上方多少")]
    public float leverOrbOffsetY = 1.4f;
    [Tooltip("斜坡上叫風暴的位置（x）。原本龍捲風跟隨區在 192～239、轉場在 249")]
    public float stormOrbX = 238f;
    public float stormOrbHeight = 2.5f;

    [Header("帶路")]
    public float orbSpeed = 7f;
    [Tooltip("她離光球超過這個距離，光球停下來等")]
    public float leadStopDistance = 13f;
    [Tooltip("停下來等之後，她走到這個距離內光球才繼續")]
    public float leadResumeDistance = 7f;
    [Tooltip("放下巨石後，光球飛往斜坡的速度")]
    public float toStormSpeed = 12f;
    [Tooltip("她離光球多近算碰到")]
    public float touchDistance = 3f;
    public float bobHeight = 0.3f;
    public float bobSpeed = 3f;

    [Header("風暴")]
    [Tooltip("龍捲風由左往右掃的速度（米／秒）")]
    public float sweepSpeed = 16f;
    [Tooltip("從畫面左邊外面多遠開始")]
    public float sweepStartMargin = 6f;
    [Tooltip("掃到她之後，罩著她多久才接文字卡")]
    public float sweepHoldSeconds = 1.2f;

    public bool logEvents = true;

    private enum Stage { Idle, Lead, WaitRelease, Releasing, ToStorm, WaitStorm, Storm }
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

    private Vector3 _r2, _r3;
    private bool _r3Ready;
    private Vector3 _orbPos;
    private bool _waiting;
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
        _orb = FindAnyObjectByType<GuidanceLight>();
        _lever = FindAnyObjectByType<LeverSystem>();
        _rock = _lever != null ? _lever.targetRock : null;
        _storm = FindAnyObjectByType<StormSceneTransition>();
        _encounter = FindAnyObjectByType<TornadoEncounterTrigger>();
        if (_storm != null && _storm.backgroundTornadoes != null)
            foreach (TornadoFollowCamera t in _storm.backgroundTornadoes) if (t != null) { _tornado = t; break; }
        if (_tornado == null && _encounter != null) _tornado = _encounter.targetTornado;
        if (_tornado == null) _tornado = FindAnyObjectByType<TornadoFollowCamera>();
        if (_orb != null) _orbFx = _orb.GetComponentsInChildren<ParticleSystem>(true);

        HideLever();
        DisableOldStormTriggers();
        HideTornado();
        PlayerRespawnSystem.OnResettablesReset += OnResettablesReset;
        if (logEvents) Debug.Log("【廢墟光球】拉桿已藏起來、原本的風暴觸發區已關；廢墟改由光球帶路、放巨石、叫風暴");
    }

    private void OnDestroy()
    {
        PlayerRespawnSystem.OnResettablesReset -= OnResettablesReset;
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
            // ★1010 等墜落鎖真的放開（修毅的 PlayerMovement 要落到廢墟地板才放）再接手，不跟墜落中的光球特寫搶
            if (inRuins && _pm.isGrounded && !_pm.freezeHorizontal && !PlayerRespawnSystem.IsAnyRespawning) BeginRuins();
            return;
        }
        if (p.y > ruinsBelowY + 30f) { EndRuins(); return; }   // 測試時把她放回棉花堡

        float dt = Time.deltaTime;
        float dist = Vector2.Distance(new Vector2(_orbPos.x, _orbPos.y), new Vector2(p.x, p.y));
        switch (_stage)
        {
            case Stage.Lead:
            {
                bool ahead = p.x > _orbPos.x + 1f;   // 她跑到光球前面了：光球不等她，加快趕去原本拉桿的位置
                if (ahead) _waiting = false;
                else if (!_waiting && dist > leadStopDistance) _waiting = true;
                else if (_waiting && dist <= leadResumeDistance) _waiting = false;
                if (!_waiting) _orbPos = Vector3.MoveTowards(_orbPos, _r2, (ahead ? toStormSpeed : orbSpeed) * dt);
                if ((_orbPos - _r2).sqrMagnitude < 0.0025f) _stage = Stage.WaitRelease;
                break;
            }
            case Stage.WaitRelease:
                if (dist <= touchDistance && !PlayerRespawnSystem.IsAnyRespawning) StartCoroutine(ReleaseRoutine());
                break;
            case Stage.ToStorm:
                _orbPos = Vector3.MoveTowards(_orbPos, _r3, toStormSpeed * dt);
                if ((_orbPos - _r3).sqrMagnitude < 0.0025f) _stage = Stage.WaitStorm;
                break;
            case Stage.WaitStorm:
                bool reached = dist <= touchDistance || (p.x >= _r3.x - 1f && Mathf.Abs(p.y - _r3.y) < 8f);
                if (reached && !PlayerRespawnSystem.IsAnyRespawning && !BoulderChallengeController.IsFailing) StartCoroutine(StormRoutine());
                break;
        }
        ApplyOrb();
    }

    private void ApplyOrb()
    {
        if (_orb == null) return;
        _orb.transform.position = new Vector3(_orbPos.x, _orbPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight, _orbPos.z);
    }

    private bool RockReleased => _rock != null && !_rock.isKinematic;

    private void BeginRuins()
    {
        _orb.enabled = false;   // GuidanceLight 停手；廢墟由這裡帶（它的重生重置照常會被呼叫，下面再放回來）
        _r2 = _lever != null ? _lever.transform.position + Vector3.up * leverOrbOffsetY : _pm.transform.position + Vector3.right * 40f;
        if (RockReleased)
        {
            EnsureStormPoint();
            _orbPos = _r3;
            _stage = Stage.WaitStorm;
        }
        else
        {
            Vector3 p = _pm.transform.position;
            Vector3 cur = _orb.transform.position;
            if (startFromCurrentOrb && cur.y < ruinsBelowY + 20f && cur.x > p.x + 2f && cur.x < _r2.x - 2f)
            {
                _orbPos = new Vector3(cur.x, cur.y, 0f);   // ★1010 光球已經在前方（P12），從那裡帶路
                _waiting = true;
                _stage = Stage.Lead;
            }
            else
            {
                float x = p.x < _r2.x - 2f ? Mathf.Min(p.x + landAheadX, _r2.x - 2f) : _r2.x;
                _orbPos = x >= _r2.x ? _r2 : new Vector3(x, GroundY(x, p.y) + orbHeight, 0f);
                _waiting = true;
                _stage = x >= _r2.x ? Stage.WaitRelease : Stage.Lead;
            }
        }
        ApplyOrb();
        Flare(25);
        if (logEvents) Debug.Log("【廢墟光球】她落地了：光球出現在 " + _orbPos.ToString("F1") + "，往原本拉桿的位置 " + _r2.ToString("F1") + " 帶路");
    }

    private void EndRuins()
    {
        StopAllCoroutines();
        _stage = Stage.Idle;
        if (_orb != null) _orb.enabled = true;
    }

    // ── 放下巨石 ─────────────────────────────────────────────
    private IEnumerator ReleaseRoutine()
    {
        _stage = Stage.Releasing;
        Flare(40);
        PlayOrbSfx();
        yield return new WaitForSeconds(0.35f);
        if (_rock != null)
        {
            _rock.isKinematic = false;
            _rock.linearVelocity = Vector3.zero;
            RollingRockVisual v = _rock.GetComponent<RollingRockVisual>();
            if (v != null) v.enabled = true;
        }
        if (logEvents) Debug.Log("【廢墟光球】她碰到光球：巨石放下（取代拉桿）");
        float t = 0f;
        while (t < 1.0f) { t += Time.deltaTime; ApplyOrb(); yield return null; }   // 停一下，讓她看見巨石掉下來
        EnsureStormPoint();
        _stage = Stage.ToStorm;
    }

    private void EnsureStormPoint()
    {
        if (_r3Ready) return;
        float y = GroundY(stormOrbX, -60f);
        _r3 = new Vector3(stormOrbX, y + stormOrbHeight, 0f);
        _r3Ready = true;
        if (logEvents) Debug.Log("【廢墟光球】斜坡上等她叫風暴的位置：" + _r3.ToString("F1"));
    }

    // ── 風暴 ─────────────────────────────────────────────────
    private IEnumerator StormRoutine()
    {
        _stage = Stage.Storm;
        if (logEvents) Debug.Log("【廢墟光球】她在斜坡上碰到光球：叫風暴，龍捲風由左往右掃過");

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
            float targetX = _pm.transform.position.x;
            float guard = 0f;
            while (tt.position.x < targetX && guard < 10f)
            {
                float dt = Time.deltaTime;
                guard += dt;
                float camY = cam.transform.position.y + _tornado.offsetY;
                tt.position = new Vector3(tt.position.x + sweepSpeed * dt, Mathf.Lerp(tt.position.y, camY, dt * 6f), _tornado.fixedZ);
                FadeOrb(1f - Mathf.Clamp01((tt.position.x - (targetX - halfW)) / halfW));
                yield return null;
            }
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
    /// <summary>全場景 IResettable 跑完之後（GuidanceLight 的重置會把光球拉回棉花堡），下一幀等她傳到存檔點再放回廢墟。</summary>
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
            // 推巨石失敗：巨石重置後仍是放開的（BoulderChallengeController 的 unlockBoulderAfterReset）→ 光球在斜坡上等
            EnsureStormPoint();
            _orbPos = _r3;
            _stage = Stage.WaitStorm;
        }
        else
        {
            // 還沒放下巨石就死（拉桿的重置會把巨石放回原位）→ 光球回到她前面重新帶路
            float x = p.x < _r2.x - 2f ? Mathf.Min(p.x + landAheadX, _r2.x - 2f) : _r2.x;
            _orbPos = x >= _r2.x ? _r2 : new Vector3(x, GroundY(x, p.y) + orbHeight, 0f);
            _waiting = true;
            _stage = x >= _r2.x ? Stage.WaitRelease : Stage.Lead;
        }
        FadeOrb(1f);
        ApplyOrb();
        if (logEvents) Debug.Log("【廢墟光球】重生後光球放在 " + _orbPos.ToString("F1") + "（" + (RockReleased ? "巨石已放下，斜坡上等" : "重新帶路") + "）");
    }

    // ── 小工具 ───────────────────────────────────────────────
    /// <summary>x 這一點的地面高度：從上往下打射線，略過觸發區與會動的東西（她、狼、巨石）。</summary>
    private float GroundY(float x, float nearY)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, -40f, 0f), Vector3.down, 160f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            Collider c = h.collider;
            if (c == null) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
            if (c.GetComponentInParent<PlayerMovement>() != null || c.GetComponentInParent<WolfEnemy>() != null) continue;
            if (c.GetComponentInParent<GameArea>() != null) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? nearY : best;
    }

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
