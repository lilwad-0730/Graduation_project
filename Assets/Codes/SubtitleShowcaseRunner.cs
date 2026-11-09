#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1109q 字幕展示：在 Play 中跑一關。只存在於執行期（HideAndDontSave），不寫進任何場景。
/// 流程：等初始化 → 讓危險物件失效 → 玩家站到第一段語音的位置 → 開錄 → 逐段（淡黑換位置、等鏡頭、觸發語音）→ 淡黑收尾 → 停錄。
/// </summary>
public class SubtitleShowcaseRunner : MonoBehaviour
{
    public class LevelDef
    {
        public string scene;     // Assets/Scenes/{scene}.unity
        public string title;     // 左上角小標題
        public string clip;      // 輸出檔名（不含副檔名）
        public LevelDef(string sc, string t, string c) { scene = sc; title = t; clip = c; }
    }

    public static readonly LevelDef[] Levels =
    {
        new LevelDef("desert", "荒原", "clip1_desert"),
        new LevelDef("underwater", "水下", "clip2_underwater"),
        new LevelDef("dark glasses", "玻璃館", "clip3_glasshall"),
        new LevelDef("CottonCastle", "棉花堡", "clip4_cottoncastle"),
        new LevelDef("Ruined", "廢墟", "clip5_ruined"),
    };

    public const string OutputDir = "C:/Users/andyw/Desktop/字幕展示";

    // 由編輯器端（SubtitleShowcaseRecorder，Assets/Editor）在 InitializeOnLoad 時掛上；Runner 放在非 Editor 資料夾才能 AddComponent
    public static System.Action<string> LogHook;
    public static System.Func<string, bool> StartRecorderHook;
    public static System.Action StopRecorderHook;
    public static System.Action<bool> LevelFinishedHook;
    public static System.Action FocusHook;
    public static System.Func<string> DiagHook;
    static void Hook_Log(string m) { if (LogHook != null) LogHook(m); else Debug.Log(m); }
    static bool Hook_StartRecorder(string p) { return StartRecorderHook != null && StartRecorderHook(p); }
    static void Hook_StopRecorder() { if (StopRecorderHook != null) StopRecorderHook(); }
    static void Hook_LevelFinished(bool ok) { if (LevelFinishedHook != null) LevelFinishedHook(ok); }

    public int levelIndex;
    public bool record = true;

    class Step
    {
        public string label;
        public Vector3 anchor;     // 路徑點／燭台位置
        public float dx;           // 玩家相對 anchor 的水平位移
        public bool snapGround;
        public float pinZ = float.NaN;   // 落定後把她拉到鏡頭這邊（避免被前景岩石／植物擋住）
        public bool noDrop;        // 不落地、直接站在 anchor.y + fallbackDy（腳下可能沒有地板，掉下去會觸發重生鎖定）
        public bool timeoutOk;     // 落地等不到完全靜止（例如水中緩緩下沉）也算數
        public float fallbackDy;   // 落地逾時（腳下沒有地板）時改站在 anchor.y + fallbackDy、不受重力
        public float clipLength;
        public float fadeOut;
        public System.Action place;     // 玩家站定後順便做的事（例如把光球傳過來）
        public System.Action trigger;
        public System.Func<bool> isDone;   // 可選：觸發後等到條件成立
    }

    // 這些元件直接關掉（不再做事）。名字不存在就略過。
    static readonly string[] kDisableComponents =
    {
        "PlayerPetrification", "UnderwaterSuffocationEffect", "StormHazardWave", "WindGustSystem", "WindDirector",
        "DesertBeatDirector", "IndividualBirdEnemy", "SceneTransitionZone", "StoryCardZoneHook", "StoryCardTrigger",
        "StoryCardNoteHook", "BridgeGapFallTrigger", "SkyDiveTeleportTrigger", "TeleportTrigger", "CinemachineDeathZoneFloor",
        "BoulderChallengeController", "BoulderFailExitTrigger", "TornadoEncounterTrigger", "StormSceneTransition",
        "TornadoVortexTransition", "ShadowMonsterTriggerZone", "MirrorWallAbsorbCutscene", "BreakableGlassFloor",
        "BreakablePlatform", "CollectibleNote", "NoteRelief", "UnderwaterNurseryItem", "CliffSceneLoader",
        "WaterOasisTransition", "TimelineTrigger", "JumpTriggerZone", "TutorialZone", "DesertGuideHint", "DesertBraceHint",
        "RuinsLookBackHint", "LineHintAnchor", "LineHintDirector", "UnderwaterBreathGuide", "UnderwaterHintGlow",
        "GlassHallAbyssHint", "WolfSpawner", "BirdAttackScheduler", "BirdAttackTriggerZone", "GameRecoveryGuard",
        "PlayerRespawnSystem", "UpperCastleRespawnAssist", "PlayerUnstuck", "UnderwaterPenetrationProbe", "UnderwaterRockTrapAudit", "WindShelter", "WindStopZone",
    };

    // 這些整個物件關掉（敵人本體）。
    static readonly string[] kDeactivateObjects =
    {
        "WolfEnemy", "WanderingWolf2D", "ShadowMonsterController", "MonsterGlassBreaker", "FlyingEnemy",
    };

    Transform _player;
    PlayerMovement _pm;
    Rigidbody _rb;
    Collider _col;
    float _footOffset;
    bool _origGravity;
    float _playerZ;
    bool _pinned;
    Vector3 _pinPos;
    string _sceneName;
    bool _sceneChanged;

    CanvasGroup _blackGroup;
    CanvasGroup _titleGroup;
    float _recStart;

    void Awake() { DontDestroyOnLoad(gameObject); }

    /// <summary>levelIndex／record 設好之後才呼叫（AddComponent 當下欄位還是預設值）。</summary>
    public void Begin() { StartCoroutine(Run()); }

    void LateUpdate()
    {
        if (_pinned && _player != null)
        {
            _player.position = _pinPos;
            if (_rb != null) { _rb.position = _pinPos; _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        }
        if (!_sceneChanged && !string.IsNullOrEmpty(_sceneName) && SceneManager.GetActiveScene().name != _sceneName)
        {
            _sceneChanged = true;
            Hook_Log("!!! 場景被切換了：" + SceneManager.GetActiveScene().name);
        }
    }

    float RecT { get { return Time.unscaledTime - _recStart; } }

    void LogEv(string s)
    {
        Hook_Log("[" + (levelIndex + 1) + "] t=" + RecT.ToString("F2") + " " + s);
    }

    IEnumerator Run()
    {
        var def = Levels[levelIndex];
        Application.runInBackground = true;
        _sceneName = SceneManager.GetActiveScene().name;
        if (_sceneName != def.scene) { Hook_Log("!!! 目前場景是 " + _sceneName + "，不是預期的 " + def.scene); Hook_LevelFinished(false); yield break; }
        yield return new WaitForSecondsRealtime(1.5f);

        GameObject pgo = GameObject.FindGameObjectWithTag("Player");
        if (pgo == null) { Hook_Log("!!! 找不到玩家"); Hook_LevelFinished(false); yield break; }
        _player = pgo.transform;
        _pm = pgo.GetComponent<PlayerMovement>();
        _rb = pgo.GetComponent<Rigidbody>();
        _col = pgo.GetComponent<Collider>();
        if (_col == null) _col = pgo.GetComponentInChildren<Collider>();
        _playerZ = _player.position.z;
        _origGravity = _rb != null && _rb.useGravity;
        _footOffset = _col != null ? _player.position.y - _col.bounds.min.y : 0f;

        BuildOverlay(def.title);
        SetBlack(1f);

        Neutralise();
        FreezePlayer();

        // 蒐集這關的語音段落
        List<Step> steps = BuildSteps(def);
        if (steps.Count == 0) { Hook_Log("!!! 這關沒有語音段落"); Hook_LevelFinished(false); yield break; }
        Hook_Log("[" + (levelIndex + 1) + "] 共 " + steps.Count + " 段語音");

        // 第一段位置先站好、等鏡頭到位（畫面全黑），再開錄
        yield return PlaceRoutine(steps[0]);
        yield return new WaitForSeconds(2.0f);

        if (FocusHook != null) FocusHook();
        yield return new WaitForSecondsRealtime(0.5f);
        _recStart = Time.unscaledTime;
        if (record)
        {
            string path = OutputDir + "/" + def.clip;
            string full = path + ".mp4";
            if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
            if (!Hook_StartRecorder(path)) { Hook_Log("!!! Recorder 啟動失敗"); Hook_LevelFinished(false); yield break; }
            yield return null; yield return null; yield return null;
            _recStart = Time.unscaledTime;
        }
        LogEv("錄影開始（畫面全黑）");
        StartCoroutine(Diag());

        // 淡入 + 小標題
        yield return Fade(_blackGroup, 1f, 0f, 0.6f);
        StartCoroutine(TitleRoutine());

        for (int i = 0; i < steps.Count; i++)
        {
            Step s = steps[i];
            if (i > 0)
            {
                yield return Fade(_blackGroup, 0f, 1f, 0.45f);
                yield return PlaceRoutine(s);
                yield return new WaitForSeconds(1.2f);
                yield return Fade(_blackGroup, 1f, 0f, 0.5f);
            }
            yield return new WaitForSeconds(0.9f);
            Camera mc = Camera.main;
            LogEv("語音 " + (i + 1) + "/" + steps.Count + " 觸發 " + s.label + " 長度 " + s.clipLength.ToString("F2") + "s 鏡頭=" + (mc != null ? mc.transform.position.ToString("F1") : "?") + " 玩家=" + _player.position.ToString("F1"));
            s.trigger();
            yield return new WaitForSeconds(s.clipLength + s.fadeOut + 0.9f);
            LogEv("語音 " + (i + 1) + " 結束（字幕已淡出）");
        }

        yield return Fade(_blackGroup, 0f, 1f, 0.7f);
        yield return new WaitForSeconds(0.3f);
        LogEv("收尾，停止錄影");
        if (record)
        {
            Hook_StopRecorder();
            yield return new WaitForSecondsRealtime(1.0f);
        }
        Hook_LevelFinished(!_sceneChanged);
    }

    IEnumerator Diag()
    {
        yield return new WaitForSeconds(4f);
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        Color32[] px = tex.GetPixels32();
        long r = 0, g = 0, b = 0; int n = 0;
        for (int i = 0; i < px.Length; i += 97) { r += px[i].r; g += px[i].g; b += px[i].b; n++; }
        Destroy(tex);
        Camera c = Camera.main;
        Hook_Log("[診斷] 螢幕截圖平均色 " + (r / n) + "," + (g / n) + "," + (b / n) + " 大小 " + tex.width + "x" + tex.height + " Screen=" + Screen.width + "x" + Screen.height + " captureFramerate=" + Time.captureDeltaTime
            + " cam=" + (c != null ? c.name + " enabled=" + c.enabled + " tt=" + (c.targetTexture != null) + " depth=" + c.depth : "null")
            + " 黑幕alpha=" + (_blackGroup != null ? _blackGroup.alpha.ToString("F2") : "?") + " 視窗：" + (DiagHook != null ? DiagHook() : ""));
    }

    // ───────────────────────── 段落蒐集 ─────────────────────────

    List<Step> BuildSteps(LevelDef def)
    {
        var list = new List<Step>();
        bool underwater = def.scene == "underwater";

        // 到點語音（荒原／水下／棉花堡／廢墟）
        GuidanceLight gl = null;
        foreach (var g in FindObjectsByType<GuidanceLight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (g.waypointVoices != null && g.waypointVoices.Length > 0) { gl = g; break; }
        if (gl != null)
        {
            foreach (var v in gl.waypointVoices)
            {
                if (v == null || v.clip == null) continue;
                Transform wp = null;
                foreach (var w in gl.waypoints) if (w != null && w.name == v.waypointName) { wp = w; break; }
                if (wp == null) continue;
                string wpName = v.waypointName;
                Vector3 pos = wp.position;
                GuidanceLight g2 = gl;
                list.Add(new Step
                {
                    label = wpName + " " + v.clip.name,
                    anchor = pos,
                    dx = def.scene == "desert" ? -6.0f : -5.0f,   // 荒原綠洲岸邊有前景岩石會擋住她，站遠一點
                    snapGround = true,
                    timeoutOk = underwater,
                    pinZ = def.scene == "desert" ? -2.5f : float.NaN,
                    clipLength = v.clip.length,
                    fadeOut = g2.subtitleStyle != null ? g2.subtitleStyle.fadeOut : 0.6f,
                    place = () =>
                    {
                        // 光球傳到這個路徑點、凍結（不讓它自己飛走、也不會因為玩家靠近而觸發別的演出）
                        g2.TeleportToWaypointName(wpName);
                        g2.enabled = false;
                    },
                    // 走真正的 PlayVoiceForWaypoint（→ PlayWaypointVoice：語音＋字幕）
                    trigger = () => g2.PlayVoiceForWaypoint(wpName)
                });
            }
        }

        // 玻璃館：點燃燭台依序觸發
        CandleVoiceDirector dir = FindFirstObjectByType<CandleVoiceDirector>();
        if (dir != null)
        {
            var candles = new List<CandleAuraGlow>(FindObjectsByType<CandleAuraGlow>(FindObjectsSortMode.None));
            candles.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            MethodInfo begin = typeof(CandleAuraGlow).GetMethod("BeginLighting", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < dir.voices.Length && i < candles.Count; i++)
            {
                var v = dir.voices[i];
                if (v == null || v.clip == null) continue;
                CandleAuraGlow c = candles[i];
                list.Add(new Step
                {
                    label = c.name + " " + v.clip.name,
                    anchor = c.transform.position,
                    dx = -1.2f,
                    snapGround = true,
                    fallbackDy = -0.58f,
                    noDrop = true,
                    clipLength = v.clip.length + 1.0f,   // 點燃 1 秒後才開始播
                    fadeOut = dir.subtitleStyle != null ? dir.subtitleStyle.fadeOut : 0.6f,
                    trigger = () =>
                    {
                        c.lightDuration = 1.0f;   // 縮短點燃時間（真實流程是玩家按 S 點燃）
                        begin.Invoke(c, null);    // 走真正的點燃流程：UpdateLighting 完成 → LightingCompleted → CandleVoiceDirector 播語音
                        StartCoroutine(CandleProbe(c));
                    }
                });
            }
        }
        return list;
    }

    IEnumerator CandleProbe(CandleAuraGlow c)
    {
        for (int i = 0; i < 4; i++)
        {
            yield return new WaitForSeconds(0.4f);
            var col = c.GetComponent<CandleCollectible>();
            Hook_Log("   [燭台探針] " + c.name + " IsLighting=" + c.IsLighting + " IsLit=" + c.IsLit + " progress=" + c.LightProgress.ToString("F2")
                + " collected=" + (col != null && col.isCollected) + " missed=" + (col != null && col.missed)
                + " hardLock=" + PlayerMovement.IsHardCutsceneLocked + " ctrlLock=" + (_pm != null && _pm.IsControlLocked) + " pmEnabled=" + (_pm != null && _pm.isActiveAndEnabled)
                + " voicePlaying=" + (AudioManager.Instance != null && AudioManager.Instance.IsVoicePlaying));
        }
    }

    // ───────────────────────── 玩家／環境 ─────────────────────────

    void Neutralise()
    {
        var disable = new HashSet<string>(kDisableComponents);
        var deact = new HashSet<string>(kDeactivateObjects);
        int nd = 0, na = 0;
        PlayerUnstuck.Enabled = false;       // 玩家被定住不動，脫困系統會誤判卡住而把她搬走
        PlayerUnstuck.AutoDetect = false;
        // 展示用：字幕不因玩家擋到而淡化，才看得清楚字幕本身的樣子（只改執行期的副本，不存檔）
        foreach (var g in FindObjectsByType<GuidanceLight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (g.subtitleStyle != null) g.subtitleStyle.fadeWhenPlayerOverlaps = false;
        foreach (var d in FindObjectsByType<CandleVoiceDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.subtitleStyle != null) d.subtitleStyle.fadeWhenPlayerOverlaps = false;
        var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var toDeact = new List<GameObject>();
        foreach (var mb in all)
        {
            if (mb == null || mb == this) continue;
            string n = mb.GetType().Name;
            if (deact.Contains(n)) { toDeact.Add(mb.gameObject); continue; }
            if (disable.Contains(n) && mb.enabled && !mb.transform.IsChildOf(_player) && mb.gameObject != _player.gameObject) { mb.enabled = false; nd++; }
        }
        foreach (var go in toDeact) { if (go != null && go.activeSelf && !_player.IsChildOf(go.transform)) { go.SetActive(false); na++; } }
        Hook_Log("[" + (levelIndex + 1) + "] 已關閉元件 " + nd + " 個、物件 " + na + " 個");
    }

    // 廢墟：傳到 FallingBackground 觸發區會進「墜落模式」（鎖橫向、鏡頭特寫光球），落定後把這些狀態收掉
    void NormalizePlayer()
    {
        if (_pm == null) return;
        try
        {
            var m = typeof(PlayerMovement).GetMethod("UnlockFallingControl", BindingFlags.NonPublic | BindingFlags.Instance);
            var f = typeof(PlayerMovement).GetField("_fallingBGEntered", BindingFlags.NonPublic | BindingFlags.Instance);
            if (m != null && f != null && (bool)f.GetValue(_pm)) m.Invoke(_pm, new object[] { "字幕展示" });
            _pm.freezeHorizontal = false;
            _pm.isStrictLockingX = false;
        }
        catch (System.Exception e) { Hook_Log("[警告] NormalizePlayer：" + e.Message); }
        if (CameraTargetXFollower.Instance != null) CameraTargetXFollower.ClearCameraOverride();
    }

    void FreezePlayer()
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.useGravity = false;
            _rb.constraints = RigidbodyConstraints.FreezeAll;
        }
        _pinned = true;
        _pinPos = _player.position;
    }

    // 傳到目標上方一點，解凍讓她用真正的物理落到地面（射線打不到單面網格地板），落定後再凍住。
    // 先用玩家原本的 Z 試；掉到奇怪的地方（被重生系統搬走、掉出很遠）就換 Z=0 再試；還是不行才懸在 anchor 高度。
    IEnumerator PlaceRoutine(Step s)
    {
        float x = s.anchor.x + s.dx;
        string how = "";
        bool ok = false;
        float[] zs = s.noDrop ? new float[0] : new float[] { _playerZ, 0f };
        if (s.noDrop) how = "直接站在燭台旁";
        for (int zi = 0; zi < zs.Length && !ok; zi++)
        {
            if (zi == 1 && Mathf.Abs(zs[1] - zs[0]) < 0.01f) break;
            Vector3 pos = new Vector3(x, s.anchor.y + 2.5f, zs[zi]);
            _pinned = false;
            if (_rb != null) _rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            if (_rb != null) _rb.useGravity = true;
            _player.position = pos;
            if (_rb != null) { _rb.position = pos; _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();

            float still = 0f, t = 0f;
            yield return new WaitForSeconds(0.3f);
            while (t < 10f && still < 0.5f)
            {
                t += Time.deltaTime;
                float v = _rb != null ? _rb.linearVelocity.magnitude : 0f;
                still = v < 0.05f ? still + Time.deltaTime : 0f;
                yield return null;
            }
            Vector3 p = _player.position;
            bool near = Mathf.Abs(p.x - x) < 2f && p.y > s.anchor.y - 30f;
            bool settled = still >= 0.5f;
            ok = near && (settled || s.timeoutOk);
            how = (settled ? "落地" : "落地逾時") + (near ? "" : "（位置不對）") + " z=" + zs[zi].ToString("F2");
        }
        if (!ok)
        {
            _pinned = false;
            Vector3 hp = new Vector3(x, s.anchor.y + s.fallbackDy, _playerZ);
            _player.position = hp;
            if (_rb != null) { _rb.position = hp; _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            if (!s.noDrop) how += " → 改站在 anchor 高度";
        }
        NormalizePlayer();
        if (!float.IsNaN(s.pinZ))
        {
            Vector3 zp = _player.position; zp.z = s.pinZ;
            _player.position = zp;
            if (_rb != null) _rb.position = zp;
            Physics.SyncTransforms();
        }
        FreezePlayer();
        Hook_Log("[" + (levelIndex + 1) + "] 玩家站到 " + _player.position.ToString("F2") + "（" + how + "）for " + s.label);
        if (s.place != null) s.place();
    }

    // ───────────────────────── 畫面（黑幕、小標題） ─────────────────────────

    void BuildOverlay(string title)
    {
        var cgo = new GameObject("ShowcaseOverlay");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 小標題（在黑幕下面，淡入黑幕時會一起被蓋住）
        TMP_FontAsset font = null;
        foreach (var g in FindObjectsByType<GuidanceLight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (g.subtitleStyle != null && g.subtitleStyle.font != null) { font = g.subtitleStyle.font; break; }
        if (font == null)
        {
            var d = FindFirstObjectByType<CandleVoiceDirector>();
            if (d != null && d.subtitleStyle != null) font = d.subtitleStyle.font;
        }
        var tgo = new GameObject("Title", typeof(RectTransform));
        tgo.transform.SetParent(cgo.transform, false);
        var rt = (RectTransform)tgo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(70f, -55f);
        rt.sizeDelta = new Vector2(700f, 120f);
        var tmp = tgo.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = title;
        tmp.fontSize = 64f;
        tmp.characterSpacing = 8f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        var sh = tgo.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
        sh.effectDistance = new Vector2(3f, -3f);
        _titleGroup = tgo.AddComponent<CanvasGroup>();
        _titleGroup.alpha = 0f;

        // 黑幕
        var bgo = new GameObject("Black", typeof(RectTransform));
        bgo.transform.SetParent(cgo.transform, false);
        var brt = (RectTransform)bgo.transform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
        var img = bgo.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        _blackGroup = bgo.AddComponent<CanvasGroup>();
        _blackGroup.alpha = 1f;
    }

    void SetBlack(float a) { if (_blackGroup != null) _blackGroup.alpha = a; }

    IEnumerator Fade(CanvasGroup g, float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        g.alpha = to;
    }

    IEnumerator TitleRoutine()
    {
        yield return Fade(_titleGroup, 0f, 1f, 0.5f);
        yield return new WaitForSeconds(1.6f);
        yield return Fade(_titleGroup, 1f, 0f, 0.6f);
    }
}
#endif
