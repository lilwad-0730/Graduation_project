using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

/// <summary>
/// 影子怪物（MonsterMutant7_Run1）主控制器。
/// 
/// 【動畫控制】
///   - 走路：walk4
///   - 遠距離奔跑：run2
///   - 吃到燭火受擊：gethit3 (播放受擊動態並「漸漸變小」)
///   - 捕獲玩家攻擊：attack2 (動態快結束時觸發重生)
/// 
/// 【狀態流程】
///   Dormant → (觸發點) → Appearing (Alpha漸顯登場) → Chasing → (全部燭火) → Vanishing (勝利)
///                                                            → (最後燭火未全集) → Punishing → Devouring → 重生→Dormant
/// </summary>
public class ShadowMonsterController : MonoBehaviour, IResettable
{
    // ──────────── Singleton ────────────
    public static ShadowMonsterController Instance { get; private set; }

    // ──────────── 狀態機 ────────────
    public enum MonsterState { Dormant, Appearing, Chasing, Punishing, Devouring, Vanishing }

    [Header("【狀態監控 (勿手動修改)】")]
    public MonsterState currentState = MonsterState.Dormant;

    // ──────────── Inspector 設定 ────────────
    [Header("目標設定")]
    [Tooltip("玩家 Transform（留空時自動透過 Tag 或 PlayerMovement 腳本尋找）")]
    public Transform player;

    [Header("燭火清單 (⚠ 請手動拖曳所有燭火物件到此陣列)")]
    public CandleCollectible[] candles;

    [Header("★ 結局：收完最後一根燭火就進結局")]
    [Tooltip("收完所有燭火後是否直接進結局（原本掛在鏡牆演出上，已改到這裡）")]
    public bool playEndingAfterAllCandles = true;

    [Tooltip("進結局前播的文字卡 ID")]
    public string endingCardId = "M5";

    [Tooltip("結局要載入的場景（繪本）")]
    public string endingBookScene = "Book";

    [Header("⚔️ 揮爪攻擊距離自由微調 (Attack Distance Settings)")]
    [Tooltip("怪物登場出現時，是否同步朝玩家追擊？")]
    public bool chaseWhileAppearing = true;
    [Tooltip("一般追逐速度（走路狀態）")]
    public float chaseSpeed = 3.5f;
    [Tooltip("懲罰階段追逐速度")]
    public float punishChaseSpeed = 4.8f;
    [Tooltip("★【起手距離】：黑影怪在此距離時會停止跑步、高舉雙爪開始揮擊 (預設 6.0，數值越小越靠近才出招)")]
    public float attackTriggerDistance = 6.0f;
    [Tooltip("★【命中傷害距離】：爪子揮下時，主角必須在此距離內才會真正受到傷害死亡 (預設 4.8)")]
    public float clawHitDistance = 4.8f;
    [Tooltip("垂直高度容許差 (預設 12.0)")]
    public float verticalCatchTolerance = 12.0f;

    [Header("自動貼地與隱形地板設定")]
    [Tooltip("是否開啟自動射線貼地 (讓怪物隨地形/隱形地板平滑貼合)")]
    public bool enableGroundSnap = true;
    [Tooltip("地面與專屬隱形地板的 LayerMask (預設 Everything)")]
    public LayerMask groundLayers = ~0;
    [Tooltip("向下偵測地面的最大射線距離 (預設 25)")]
    public float groundRaycastDistance = 25.0f;
    [Tooltip("腳底貼地高度微調偏移")]
    public float feetOffsetY = 0.0f;

    [Header("MonsterMutant7_Run1 動作動畫設定")]
    [Tooltip("待機動畫名稱 (預設 idle1)")]
    public string idleAnimationName = "idle1";
    [Tooltip("走路追擊動畫名稱 (預設 walk4)")]
    public string walkAnimationName = "walk4";
    [Tooltip("遠距離奔跑動畫名稱 (預設 run2)")]
    public string runAnimationName = "run2";
    [Tooltip("吃到燭火受擊動畫名稱 (預設 gethit3)")]
    public string hitAnimationName = "gethit3";
    [Tooltip("捕獲玩家攻擊動畫名稱 (預設 attack2)")]
    public string attackAnimationName = "attack2";

    [Header("🎵 怪物追逐專屬背景音樂 (Chase BGM)")]
    [Tooltip("怪物追逐時播放的專屬背景音樂 (例如 玻璃館_追逐_loop.wav 或 EyesInTheVoid.mp3，留空時自動載入 玻璃館_追逐_loop)")]
    public AudioClip chaseBGM;
    [Range(0f, 1f)]
    [Tooltip("追逐音樂音量 (預設 0.85)")]
    public float chaseBGMVolume = 0.85f;
    [Tooltip("追逐音樂淡入時間 (秒，預設 1.0)")]
    public float chaseBGMFadeInDuration = 1.0f;
    [Tooltip("怪物死亡/消散時追逐音樂淡出時間 (秒，預設 1.5)")]
    public float chaseBGMFadeOutDuration = 1.5f;
    [Tooltip("空間 2D/3D 混合 (0 = 全景環繞電影級BGM，1 = 純3D定位音效，建議 0.15 保持音樂清晰且具方向感)")]
    [Range(0f, 1f)]
    public float spatialBlend = 0.15f;

    [Header("💡 音效進階增強 (Proximity & Victory)")]
    [Tooltip("當怪物極度逼近主角時 (距離 < 8米)，是否自動增強追逐音樂緊張度/音量")]
    public bool enableProximityTension = true;
    [Tooltip("怪物全收集死亡消散時播放的解脫勝利氛圍音效 (例如 玻璃館_追逐_脫離.wav)")]
    public AudioClip victoryReliefSFX;

    [Header("🎵 怪物音效設定 (Monster SFX)")]
    [Tooltip("怪物登場 / 巨影過頂音效 (例如 水下_巨影過頂.wav)")]
    public AudioClip appearSFX;
    [Tooltip("怪物追擊咆哮 / 獸吼音效 (例如 玻璃館_怪物吼叫_01.wav, 獸吼.mp3)")]
    public AudioClip roarSFX;
    [Tooltip("怪物揮爪攻擊音效 (例如 獸吼.mp3)")]
    public AudioClip attackSFX;
    [Tooltip("抓到主角受擊音效 (例如 玻璃館_被抓住.wav)")]
    public AudioClip catchPlayerSFX;
    [Tooltip("吃到燭火受擊縮小音效 (例如 玻璃館_降級_1色散 / 2顫抖 / 3暈眩)")]
    public AudioClip hitSFX;
    [Tooltip("漏吃燭火懲罰送回音效 (例如 玻璃館_送回上一盞.wav)")]
    public AudioClip punishTeleportSFX;
    [Tooltip("玩家全收集勝利、怪物消散脫離音效 (例如 玻璃館_追逐_脫離.wav)")]
    public AudioClip vanishSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.95f;

    [Header("動態追逐與距離設定")]
    [Tooltip("與玩家X距離大於此門檻時，切換為奔跑動畫 run2（預設30）")]
    public float runDistanceThreshold = 30.0f;
    [Tooltip("奔跑狀態時的速度倍率")]
    public float runSpeedMultiplier = 1.35f;

    [Header("玩家減速設定（黑影怪物靠近時）")]
    [Tooltip("與玩家X距離小於此值時，玩家移動速度會被降低")]
    public float slowdownDistance = 5.0f;
    [Tooltip("減速倍率（0.3 = 降為原速的 30%）")]
    [Range(0.05f, 1.0f)]
    public float slowdownFactor = 0.3f;
    [Tooltip("開始追逐後幾秒才會啟動 run2（確保一開始是 walk4，預設4秒）")]
    public float runActivationDelay = 4.0f;

    [Header("轉場與變小時間設定")]
    [Tooltip("怪物漸漸透明顯示登場的時間（秒）")]
    public float appearDuration = 2.0f;
    [Tooltip("怪物勝利後漸漸消失的時間（秒）")]
    public float vanishDuration = 2.0f;
    [Tooltip("吃到燭火後平滑漸漸變小的持續時間（秒）")]
    public float smoothShrinkDuration = 1.2f;
    [Tooltip("懲罰時光暈增大的漸變時間（秒）")]
    public float punishHaloGrowDuration = 1.5f;

    [Header("燭火縮放設定")]
    [Range(0.05f, 0.5f)]
    [Tooltip("每收集一根燭火，怪物縮小的比例（0.15 = 15%）")]
    public float shrinkPerCandle = 0.15f;
    [Range(0.1f, 0.6f)]
    [Tooltip("怪物縮小的下限，避免縮到消失（0.2 = 最小為原本的 20%）")]
    public float minScaleMultiplier = 0.2f;

    [Header("光暈設定")]
    [Tooltip("光暈基礎半徑/倍率")]
    public float haloBaseRadius = 3.5f;
    [Tooltip("光暈視覺顏色（暗灰紅光環）")]
    public Color haloColor = new Color(0.95f, 0.25f, 0.25f, 0.75f);
    [Tooltip("點光源顏色")]
    public Color lightColor = new Color(1.0f, 0.2f, 0.2f, 1f);
    [Tooltip("點光源強度")]
    public float lightBaseIntensity = 8.0f;
    [Tooltip("點光源影響範圍")]
    public float lightRange = 15.0f;
    [Tooltip("懲罰時光暈增大倍率")]
    public float punishHaloMultiplier = 1.2f;

    [Header("恐懼懲罰設定")]
    [Range(0.1f, 0.9f)]
    [Tooltip("懲罰期間玩家速度倍率")]
    public float fearSpeedMultiplier = 0.5f;
    [Range(0.1f, 0.9f)]
    [Tooltip("懲罰期間玩家跳躍力倍率")]
    public float fearJumpMultiplier = 0.65f;

    [Header("重生系統參考")]
    [Tooltip("留空時自動尋找場景中的 PlayerRespawnSystem")]
    public PlayerRespawnSystem respawnSystem;

    // ──────────── 內部狀態 ────────────
    private int _candlesCollected = 0;
    private int _totalCandles = 0;
    private CandleCollectible _lastCandleByX;

    private Vector3 _initialPosition;
    private Vector3 _baseScale;
    private float _currentScaleMultiplier = 1f;
    private bool _isHitShrinking = false;
    private float _chaseTimer = 0f;  // 追逐計時，用於延遲 run2 啟動

    // 光暈組件
    private SpriteRenderer _haloSpriteRenderer;
    private ParticleSystem _haloPs;
    private Light _haloLight;

    // 視覺與動畫組件快取
    private Renderer[] _renderers;
    private Color[] _originalColors;
    private MaterialPropertyBlock _mpb;
    private Animator _animator;

    // 玩家組件快取
    private PlayerMovement _pm;
    private float _origSpeed;
    private float _origJumpForce;
    private bool _fearActive = false;

    // 相機鎖定
    private Camera _mainCam;
    private Transform _camLockDummy;
    private bool _camLocked = false;
    private List<CinemachineCamera> _vcams3 = new List<CinemachineCamera>();
    private List<CinemachineVirtualCamera> _vcamsLegacy = new List<CinemachineVirtualCamera>();
    private List<Transform> _origFollow = new List<Transform>();

    // Coroutine 追蹤
    private Coroutine _stateCoroutine;
    private Coroutine _hitShrinkCoroutine;
    private Coroutine _chaseBgmFadeCoroutine;

    // 追逐專屬 AudioSource
    private AudioSource _chaseAudioSource;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity 生命週期
    // ──────────────────────────────────────────────────────────────────────────

    private bool _isInitialized = false;

    public void EnsureInitialized()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        if (Instance == null) Instance = this;

        _baseScale = transform.localScale;
        if (_baseScale == Vector3.zero || Mathf.Abs(_baseScale.x) < 0.01f)
        {
            _baseScale = Vector3.one;
        }
        _initialPosition = transform.position;

        // 自動修正動畫名稱設定，確保目標為 walk4 與 run2
        if (string.IsNullOrEmpty(walkAnimationName) || walkAnimationName.Equals("Walk", System.StringComparison.OrdinalIgnoreCase))
            walkAnimationName = "walk4";
        if (string.IsNullOrEmpty(runAnimationName) || runAnimationName.Equals("Run", System.StringComparison.OrdinalIgnoreCase))
            runAnimationName = "run2";

        if (_mainCam == null) _mainCam = Camera.main;

        if (_animator == null) _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        // 自動檢查並確保 Avatar (3D骨骼繫結) 存在，解決動畫有執行但模型不動問題
        if (_animator != null && _animator.avatar == null)
        {
            Animator[] childAnimators = GetComponentsInChildren<Animator>(true);
            foreach (var a in childAnimators)
            {
                if (a != null && a.avatar != null)
                {
                    _animator.avatar = a.avatar;
                    break;
                }
            }
        }

        // 自動檢查並確保套用 AnimatorController
        if (_animator != null && _animator.runtimeAnimatorController == null)
        {
#if UNITY_EDITOR
            var ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/MonsterMutant 7/MonsterMutant7 Animator Controller.controller");
            if (ctrl != null) _animator.runtimeAnimatorController = ctrl;
#endif
        }

        // ★ 關鍵修復：強制 Always Animate，防止模型超出畫面時動畫凍結
        if (_animator != null)
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 快取視覺組件
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        if (_renderers != null)
        {
            _originalColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Material mat = _renderers[i].sharedMaterial;
                if (mat != null && mat.HasProperty("_BaseColor"))
                    _originalColors[i] = mat.GetColor("_BaseColor");
                else if (mat != null && mat.HasProperty("_Color"))
                    _originalColors[i] = mat.GetColor("_Color");
                else
                    _originalColors[i] = Color.white;
            }
        }
        else
        {
            _originalColors = new Color[0];
        }

        EnsurePlayerReference();

        if (respawnSystem == null)
            respawnSystem = FindFirstObjectByType<PlayerRespawnSystem>();

        SetupCandles();
        CreateHaloEffect();
        SetupChaseAudioSource();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        EnsureInitialized();
    }

    void Start()
    {
        EnsureInitialized();

        // 初始狀態：播放待機動畫，保持全尺寸但隱藏 Alpha 透明度
        PlayAnimationByName(idleAnimationName);
        SetVisualAlpha(0f);
        transform.localScale = _baseScale * _currentScaleMultiplier;
    }

    void Update()
    {
        EnsurePlayerReference();

        switch (currentState)
        {
            case MonsterState.Appearing:
                if (chaseWhileAppearing)
                {
                    MoveTowardPlayer(chaseSpeed);
                    CheckCatch();
                    CheckCandleCollisions();
                }
                if (enableProximityTension) UpdateProximityAudioTension();
                break;

            case MonsterState.Chasing:
                MoveTowardPlayer(chaseSpeed);
                CheckCatch();
                CheckCandleCollisions();
                if (enableProximityTension) UpdateProximityAudioTension();
                break;

            case MonsterState.Punishing:
                MoveTowardPlayer(punishChaseSpeed);
                CheckCatch();
                CheckCandleCollisions();
                if (_camLocked) ClampPlayerToCameraView();
                if (enableProximityTension) UpdateProximityAudioTension();
                break;
        }
    }

    /// <summary>
    /// 主動 2.5D 檢測怪物身軀是否走過燭火 (前後 3.5 米、上下 6.5 米覆蓋，徹底無視 Z 軸落差)
    /// </summary>
    private void CheckCandleCollisions()
    {
        if (candles == null || candles.Length == 0) return;

        foreach (var c in candles)
        {
            if (c != null && !c.isCollected)
            {
                float dx = Mathf.Abs(transform.position.x - c.transform.position.x);
                float dy = Mathf.Abs(transform.position.y - c.transform.position.y);

                if (dx <= 3.5f && dy <= 6.5f)
                {
                    c.Collect();
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckCollisionCatch(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckCollisionCatch(collision.gameObject);
    }

    private void CheckCollisionCatch(GameObject go)
    {
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Punishing && currentState != MonsterState.Appearing) return;
        if (go.CompareTag("Player") || go.GetComponent<PlayerMovement>() != null || go.name.ToLower().Contains("player"))
        {
            TriggerDevour();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 公開介面
    // ──────────────────────────────────────────────────────────────────────────

    public void ActivateChase()
    {
        if (currentState != MonsterState.Dormant) return;
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(AppearSequence());
    }

    public void OnCandleCollected(CandleCollectible candle)
    {
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Appearing)
        {
            Debug.Log($"【影子怪物】非追逐狀態（{currentState}），燭火收集無效。");
            return;
        }

        _candlesCollected++;
        Debug.Log($"【影子怪物】燭火收集 {_candlesCollected}/{_totalCandles}（{candle.gameObject.name}）");

        float targetScaleMultiplier = Mathf.Max(minScaleMultiplier, 1f - shrinkPerCandle * _candlesCollected);

        // 觸發 gethit3 受擊動畫與漸漸變小 (Smooth Shrink)
        if (_hitShrinkCoroutine != null) StopCoroutine(_hitShrinkCoroutine);
        _hitShrinkCoroutine = StartCoroutine(HitAndSmoothShrinkRoutine(targetScaleMultiplier));

        if (candle == _lastCandleByX && _candlesCollected < _totalCandles)
        {
            Debug.Log("【影子怪物】吃到最後一根燭火但未全部收集！觸發懲罰！");
            TriggerPunishment();
            return;
        }

        if (_candlesCollected >= _totalCandles)
        {
            Debug.Log("【影子怪物】全部燭火收集完畢！怪物開始消失...");
            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(VanishSequence());

            // ★最後一根燭火＝結局。不等消散動畫演完，立刻接。
            if (playEndingAfterAllCandles) StartCoroutine(PlayEndingRoutine());
        }
    }

    private static bool _endingFired;   // ★防重入：整場遊戲只進一次結局

    /// <summary>
    /// 最後一根燭火 → 結局。
    /// 凍結玩家 → 播 M5 文字卡（播完維持全黑，把畫面交給下一個場景）→ 載入繪本結局。
    /// 原本這段掛在 MirrorWallAbsorbCutscene，但鏡牆觸發區就在玩家起點右邊 2.7 公尺，
    /// 走一秒就會結束遊戲，整個 Boss 段跳過。改掛在這裡才是對的順序。
    /// </summary>
    private IEnumerator PlayEndingRoutine()
    {
        if (_endingFired) yield break;
        _endingFired = true;

        if (_pm != null) _pm.isCutsceneFrozen = true;

        if (StoryCardPlayer.Instance != null && !string.IsNullOrEmpty(endingCardId)
            && StoryCardPlayer.Instance.HasCard(endingCardId))
        {
            // 第二個參數 true ＝播完維持全黑，不要閃一下亮再黑
            yield return StoryCardPlayer.Instance.Play(endingCardId, true, false);
        }

        EndCredits.EndingMode = true;
        if (!string.IsNullOrEmpty(endingBookScene))
            UnityEngine.SceneManagement.SceneManager.LoadScene(endingBookScene);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：狀態機與協程邏輯
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary> 登場：保持尺寸漸漸顯示 (Alpha Fade)，不從小放大 </summary>
    private IEnumerator AppearSequence()
    {
        currentState = MonsterState.Appearing;
        Debug.Log("【影子怪物】開始登場（漸漸顯示）...");

        // 啟動追逐專屬背景音樂 (平滑淡入)
        StartChaseBGM();

        if (appearSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(appearSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(appearSFX, transform.position, sfxVolume);
        }

        transform.localScale = _baseScale * _currentScaleMultiplier;
        SetVisualAlpha(0f);

        PlayAnimationByName(walkAnimationName);

        float t = 0f;
        while (t < appearDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / appearDuration);

            // 漸漸顯示登場
            SetVisualAlpha(progress);
            transform.localScale = _baseScale * _currentScaleMultiplier;

            yield return null;
        }

        SetVisualAlpha(1f);
        transform.localScale = _baseScale * _currentScaleMultiplier;

        if (roarSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(roarSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(roarSFX, transform.position, sfxVolume);
        }

        currentState = MonsterState.Chasing;
        Debug.Log("【影子怪物】登場完成，全力追逐！");
    }

    /// <summary> 吃到燭火：播放 gethit3 受擊動畫並漸漸變小 </summary>
    private IEnumerator HitAndSmoothShrinkRoutine(float targetMult)
    {
        _isHitShrinking = true;

        PlayAnimationByName(hitAnimationName);

        if (hitSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(hitSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(hitSFX, transform.position, sfxVolume);
        }

        float startMult = _currentScaleMultiplier;
        float elapsed = 0f;

        while (elapsed < smoothShrinkDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / smoothShrinkDuration);
            _currentScaleMultiplier = Mathf.Lerp(startMult, targetMult, progress);

            ApplyCurrentScale();
            yield return null;
        }

        _currentScaleMultiplier = targetMult;
        ApplyCurrentScale();
        _isHitShrinking = false;
    }

    private IEnumerator VanishSequence()
    {
        currentState = MonsterState.Vanishing;

        // 停止追逐背景音樂 (平滑淡出)
        StopChaseBGM(false);

        if (victoryReliefSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(victoryReliefSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(victoryReliefSFX, transform.position, sfxVolume);
        }
        else if (vanishSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(vanishSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(vanishSFX, transform.position, sfxVolume);
        }

        // 恢復玩家的移動速度
        if (_pm != null) _pm.currentSpeed = _pm.baseSpeed;
        RemoveFear();
        UnlockCamera();

        float t = 0f;
        Vector3 startScale = transform.localScale;

        while (t < vanishDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(1f, 0f, t / vanishDuration);

            SetVisualAlpha(progress);
            transform.localScale = startScale * progress;

            yield return null;
        }

        SetVisualAlpha(0f);
        transform.localScale = _baseScale * _currentScaleMultiplier;
        currentState = MonsterState.Dormant;
        Debug.Log("【影子怪物】玩家收集所有燭火，怪物消散！");
    }

    private void TriggerDevour()
    {
        if (currentState == MonsterState.Devouring) return;
        currentState = MonsterState.Devouring;
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(ClawAttackSequence());
    }

    /// <summary> 真實巨爪揮擊判定演出：抬手前搖 ➔ 巨爪揮下碰撞檢測 ➔ 命中定身與震動 ➔ 轉場重生 </summary>
    private IEnumerator ClawAttackSequence()
    {
        Debug.Log("【影子怪物】進入揮爪距離，開始抬手準備揮擊 (attack2)...");

        // 1. 播放 attack2 揮爪動畫 (開始抬手前搖)
        PlayAnimationByName(attackAnimationName);

        if (attackSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(attackSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(attackSFX, transform.position, sfxVolume);
        }

        // 2. 等待巨爪向下揮擊的命中點瞬間 (約 0.55 秒)
        yield return new WaitForSeconds(0.55f);

        // 3. 巨爪揮至最低點：檢測主角是否在爪擊命中範圍內 (純水平 X 軸判定，不受巨怪腳底 Y 軸高度差影響)
        bool isHit = false;
        if (player != null)
        {
            float xDist = Mathf.Abs(transform.position.x - player.position.x);
            if (xDist <= clawHitDistance)
            {
                isHit = true;
            }
        }

        if (isHit)
        {
            Debug.Log("💥【影子怪物】巨爪命中主角！定身並觸發重擊震動反饋！");

            // 命中主角時立即中斷追逐音樂
            StopChaseBGM(true);

            if (catchPlayerSFX != null)
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(catchPlayerSFX, player.position, sfxVolume);
                else AudioSource.PlayClipAtPoint(catchPlayerSFX, player.position, sfxVolume);
            }

            // 定身主角
            if (_pm != null)
            {
                _pm.isCutsceneFrozen = true;
                Rigidbody prb = _pm.GetComponent<Rigidbody>();
                if (prb != null) prb.linearVelocity = Vector3.zero;
            }

            // 觸發螢幕受傷震動與紅光閃爍
            if (ScreenFeedbackManager.Instance != null)
            {
                ScreenFeedbackManager.Instance.TriggerHitFeedback();
            }

            // 等待完整揮爪後續動態結束 (約 0.85 秒)
            yield return new WaitForSeconds(0.85f);

            // 觸發重生機制
            if (respawnSystem != null)
            {
                respawnSystem.TriggerRespawn();
            }

            if (_pm != null)
            {
                _pm.isCutsceneFrozen = false;
            }

            yield return new WaitForSecondsRealtime(1.5f);
            ResetToInitialState();
        }
        else
        {
            Debug.Log("💨【影子怪物】巨爪揮空 (主角已及時逃出範圍)，攻擊結束後繼續追擊！");
            yield return new WaitForSeconds(0.85f);
            currentState = MonsterState.Chasing;
        }
    }

    private void TriggerPunishment()
    {
        if (currentState == MonsterState.Punishing) return;
        currentState = MonsterState.Punishing;

        if (punishTeleportSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(punishTeleportSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(punishTeleportSFX, transform.position, sfxVolume);
        }

        _currentScaleMultiplier = 1f;
        transform.localScale = _baseScale;

        ApplyFear();
        LockCamera();

        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(GrowHaloRoutine(
            _currentScaleMultiplier,
            punishHaloMultiplier,
            punishHaloGrowDuration
        ));

        Debug.Log("【影子怪物】懲罰狀態：鏡頭鎖定、玩家減速、怪物恢復大小！");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：移動與動畫切換邏輯
    // ──────────────────────────────────────────────────────────────────────────

    private void EnsurePlayerReference()
    {
        if (player != null) return;

        GameObject p = GameObject.FindWithTag("Player");
        if (p == null)
        {
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) p = pm.gameObject;
        }
        if (p == null) p = GameObject.Find("Player");

        if (p != null)
        {
            player = p.transform;
            _pm = player.GetComponent<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInChildren<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInParent<PlayerMovement>();
            Debug.Log($"【影子怪物】成功連結玩家物件：{player.name}");
        }
    }

    private void MoveTowardPlayer(float speed)
    {
        if (player == null) return;

        if (_animator != null && _animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 計算怪物落後玩家的距離（玩家在前面時為正值）
        float lagDistance = player.position.x - transform.position.x;
        float absoluteDist = Mathf.Abs(lagDistance);

        float currentSpeed = speed;
        if (!_isHitShrinking)
        {
            // 只有當「黑影怪在玩家後面 30 距離以上」時才執行 run2，其餘情況一律用 walk4
            if (lagDistance > runDistanceThreshold)
            {
                currentSpeed = speed * runSpeedMultiplier;
                PlayAnimationByName(runAnimationName);
            }
            else
            {
                PlayAnimationByName(walkAnimationName);
            }
        }

        // 玩家減速：距離小於 slowdownDistance 時降低玩家移動速度
        if (_pm != null)
        {
            if (absoluteDist < slowdownDistance)
            {
                _pm.currentSpeed = _pm.baseSpeed * slowdownFactor;
            }
            else
            {
                // 离開減速範圍後恢復玩家完整速度
                if (_pm.currentSpeed < _pm.baseSpeed)
                    _pm.currentSpeed = _pm.baseSpeed;
            }
        }

        // 面向玩家 (3D Euler 旋轉)
        if (player.position.x < transform.position.x - 0.2f)
        {
            transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        }
        else if (player.position.x > transform.position.x + 0.2f)
        {
            transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        }

        float targetX = player.position.x;
        float xDist = Mathf.Abs(transform.position.x - targetX);

        // ★ 當接近至揮爪起手距離 (約 6.0 單位) 時，就地停步並開始發動揮爪攻擊
        if (xDist <= attackTriggerDistance)
        {
            CheckCatch();
            return;
        }

        float newX = Mathf.MoveTowards(transform.position.x, targetX, currentSpeed * Time.deltaTime);
        float newY = transform.position.y;

        // ★ 自動貼地 / 隱形地板吸附邏輯
        if (enableGroundSnap)
        {
            Vector3 rayOrigin = new Vector3(newX, transform.position.y + 5.0f, transform.position.z);
            RaycastHit hit;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, groundRaycastDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                float targetY = hit.point.y + feetOffsetY;
                newY = Mathf.Lerp(transform.position.y, targetY, Time.deltaTime * 12f);
            }
        }

        transform.position = new Vector3(newX, newY, transform.position.z);
    }

    private string _currentAnimName = "";

    private void PlayAnimationByName(string animName)
    {
        if (_animator == null || string.IsNullOrEmpty(animName)) return;

        if (_animator.runtimeAnimatorController == null)
        {
#if UNITY_EDITOR
            var defaultCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/MonsterMutant 7/MonsterMutant7 Animator Controller.controller");
            if (defaultCtrl != null) _animator.runtimeAnimatorController = defaultCtrl;
            else return;
#else
            return;
#endif
        }

        // 用字串比對防止每幀重複呼叫
        if (_currentAnimName == animName) return;

        _currentAnimName = animName;
        _animator.Play(animName, 0, 0f);
    }


    private void CheckCatch()
    {
        if (player == null || currentState == MonsterState.Devouring) return;

        float xDist = Mathf.Abs(transform.position.x - player.position.x);

        // ★ 核心修復：純粹以水平 X 軸距離為準 (不受怪物腳底 Pivot 與主角走道 Y 軸高差影響)！
        if (xDist <= attackTriggerDistance)
        {
            TriggerDevour();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：相機與恐懼機制
    // ──────────────────────────────────────────────────────────────────────────

    private void LockCamera()
    {
        if (_camLocked) return;
        _camLocked = true;

        if (_camLockDummy == null)
            _camLockDummy = new GameObject("ShadowMonster_CamLockDummy").transform;
        _camLockDummy.position = player != null ? player.position : Vector3.zero;

        _vcams3.Clear();
        _vcamsLegacy.Clear();
        _origFollow.Clear();

        foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            _vcams3.Add(v);
            _origFollow.Add(v.Follow);
            v.Follow = _camLockDummy;
        }
        foreach (var v in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None))
        {
            _vcamsLegacy.Add(v);
            _origFollow.Add(v.Follow);
            v.Follow = _camLockDummy;
        }

        Debug.Log("【影子怪物】相機已鎖定。");
    }

    private void UnlockCamera()
    {
        if (!_camLocked) return;
        _camLocked = false;

        int idx = 0;
        foreach (var v in _vcams3)
        {
            if (v != null && idx < _origFollow.Count)
                v.Follow = _origFollow[idx];
            idx++;
        }
        foreach (var v in _vcamsLegacy)
        {
            if (v != null && idx < _origFollow.Count)
                v.Follow = _origFollow[idx];
            idx++;
        }

        Debug.Log("【影子怪物】相機已解鎖。");
    }

    private void ClampPlayerToCameraView()
    {
        if (_mainCam == null || player == null) return;

        float halfW;
        if (_mainCam.orthographic)
        {
            halfW = _mainCam.orthographicSize * _mainCam.aspect;
        }
        else
        {
            float dist = Mathf.Abs(_mainCam.transform.position.z - player.position.z);
            halfW = dist * Mathf.Tan(_mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _mainCam.aspect;
        }

        float margin = 0.5f;
        float minX = _mainCam.transform.position.x - halfW + margin;
        float maxX = _mainCam.transform.position.x + halfW - margin;

        Vector3 pos = player.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        player.position = pos;
    }

    private void ApplyFear()
    {
        if (_fearActive || _pm == null) return;
        _fearActive = true;
        _origSpeed = _pm.baseSpeed;
        _origJumpForce = _pm.jumpForce;
        _pm.baseSpeed *= fearSpeedMultiplier;
        _pm.jumpForce *= fearJumpMultiplier;
        Debug.Log($"【影子怪物】恐懼效果：速度 {_origSpeed} → {_pm.baseSpeed}，跳躍力 {_origJumpForce} → {_pm.jumpForce}");
    }

    private void RemoveFear()
    {
        if (!_fearActive || _pm == null) return;
        _fearActive = false;
        _pm.baseSpeed = _origSpeed;
        _pm.jumpForce = _origJumpForce;
        Debug.Log("【影子怪物】恐懼效果已解除。");
    }

    private void SetupCandles()
    {
        if (candles == null || candles.Length == 0)
        {
            Debug.LogWarning("【影子怪物】⚠ 燭火清單為空！請在 Inspector 手動拖曳所有 CandleCollectible 物件至 candles[]。");
            return;
        }

        // ★只算真的接上的燭火。用 candles.Length 會把空格算進去，
        //   收永遠收不滿，玩家會打不倒怪物也看不到結局，而且完全沒有錯誤訊息。
        int nullSlots = 0;
        _totalCandles = 0;
        _lastCandleByX = null;
        foreach (var c in candles)
        {
            if (c == null) { nullSlots++; continue; }
            _totalCandles++;
            if (_lastCandleByX == null || c.transform.position.x > _lastCandleByX.transform.position.x)
                _lastCandleByX = c;
        }
        if (nullSlots > 0)
            Debug.LogWarning($"【影子怪物】⚠ candles[] 有 {nullSlots} 個空格沒接東西，已忽略。" +
                             $"實際有效燭火 {_totalCandles} 根——請到 Inspector 補齊或縮短陣列。", this);
        if (_totalCandles == 0)
        {
            Debug.LogError("【影子怪物】⚠⚠ 一根有效燭火都沒有！怪物將無法被打倒，結局也不會觸發。", this);
            return;
        }

        Debug.Log($"【影子怪物】共 {_totalCandles} 根燭火。" +
                  $"X 最大（懲罰判定）：{_lastCandleByX?.gameObject.name} " +
                  $"(X = {_lastCandleByX?.transform.position.x:F1})");
    }

    private void ApplyCurrentScale()
    {
        transform.localScale = _baseScale * _currentScaleMultiplier;
        UpdateHaloRadius(_currentScaleMultiplier);
    }

    private IEnumerator GrowHaloRoutine(float fromMult, float toMult, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float m = Mathf.SmoothStep(fromMult, toMult, t / duration);
            UpdateHaloRadius(m);
            yield return null;
        }
        UpdateHaloRadius(toMult);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：強效光暈（Sprite 貼圖 + 粒子 + 光源）
    // ──────────────────────────────────────────────────────────────────────────

    private void CreateHaloEffect()
    {
        GameObject glowObj = new GameObject("Shadow_HaloGlowSprite");
        glowObj.transform.SetParent(transform);
        glowObj.transform.localPosition = new Vector3(0f, 1.2f, -0.2f);
        glowObj.transform.localScale = Vector3.one * haloBaseRadius * 1.8f;

        _haloSpriteRenderer = glowObj.AddComponent<SpriteRenderer>();
        _haloSpriteRenderer.sprite = GenerateSoftGlowSprite();
        _haloSpriteRenderer.color = haloColor;
        _haloSpriteRenderer.sortingOrder = 999;

        GameObject lightObj = new GameObject("Shadow_HaloLight");
        lightObj.transform.SetParent(transform);
        lightObj.transform.localPosition = new Vector3(0f, 1.2f, -1.2f);

        _haloLight = lightObj.AddComponent<Light>();
        _haloLight.type = LightType.Point;
        _haloLight.color = lightColor;
        _haloLight.intensity = lightBaseIntensity;
        _haloLight.range = lightRange;
        _haloLight.shadows = LightShadows.None;

        GameObject psObj = new GameObject("Shadow_HaloParticles");
        psObj.transform.SetParent(transform);
        psObj.transform.localPosition = new Vector3(0f, 1.0f, -0.5f);
        psObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _haloPs = psObj.AddComponent<ParticleSystem>();

        var main = _haloPs.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(haloColor.r, haloColor.g, haloColor.b, 0.8f),
            new Color(haloColor.r * 0.7f, 0.05f, 0.05f, 0.9f)
        );

        var emission = _haloPs.emission;
        emission.rateOverTime = 120f;

        var shape = _haloPs.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = haloBaseRadius;

        var psRend = _haloPs.GetComponent<ParticleSystemRenderer>();
        psRend.renderMode = ParticleSystemRenderMode.Billboard;
        psRend.sortingOrder = 1000;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Transparent");
        if (sh != null)
        {
            Material mat = new Material(sh);
            mat.color = haloColor;
            psRend.material = mat;
        }

        _haloPs.Play();
        Debug.Log("【影子怪物】強效光暈（2D貼圖 + 粒子 + 光源）建立完成。");
    }

    private Sprite GenerateSoftGlowSprite()
    {
        int res = 128;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
        float radius = res * 0.48f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float normDist = Mathf.Clamp01(dist / radius);
                float alpha = Mathf.Pow(1f - normDist, 2.0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
    }

    private void UpdateHaloRadius(float multiplier)
    {
        float m = Mathf.Max(0.1f, multiplier);
        if (_haloSpriteRenderer != null)
        {
            _haloSpriteRenderer.transform.localScale = Vector3.one * haloBaseRadius * 1.8f * m;
        }
        if (_haloPs != null)
        {
            var shape = _haloPs.shape;
            shape.radius = haloBaseRadius * m;
        }
        if (_haloLight != null)
        {
            _haloLight.range = lightRange * m;
            _haloLight.intensity = lightBaseIntensity * m;
        }
    }

    private void SetVisualAlpha(float alpha)
    {
        EnsureInitialized();

        bool showVisuals = alpha > 0.001f;

        if (_renderers != null && _originalColors != null)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                _renderers[i].enabled = showVisuals;

                if (showVisuals)
                {
                    Color c = (i < _originalColors.Length) ? _originalColors[i] : Color.white;
                    c.a = c.a * alpha;

                    _renderers[i].GetPropertyBlock(_mpb);
                    if (_renderers[i].sharedMaterial != null && _renderers[i].sharedMaterial.HasProperty("_BaseColor"))
                        _mpb.SetColor("_BaseColor", c);
                    else
                        _mpb.SetColor("_Color", c);

                    _renderers[i].SetPropertyBlock(_mpb);

                    if (_renderers[i] is SpriteRenderer sr)
                        sr.color = c;
                }
            }
        }

        if (_haloSpriteRenderer != null)
        {
            _haloSpriteRenderer.enabled = showVisuals;
            Color sc = haloColor;
            sc.a = haloColor.a * alpha;
            _haloSpriteRenderer.color = sc;
        }

        if (_haloPs != null)
        {
            if (!showVisuals)
            {
                _haloPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            else
            {
                if (!_haloPs.isPlaying) _haloPs.Play();
            }
        }

        if (_haloLight != null)
        {
            _haloLight.enabled = showVisuals;
            _haloLight.intensity = lightBaseIntensity * alpha;
        }
    }

    public void ResetToInitialState()
    {
        EnsureInitialized();

        StopAllCoroutines();
        _stateCoroutine = null;
        _hitShrinkCoroutine = null;
        _isHitShrinking = false;
        _currentAnimName = "";
        _chaseTimer = 0f;  // 重置追逐計時，確保下次出場先播 walk4

        // 立即停止追逐背景音樂
        StopChaseBGM(true);

        currentState = MonsterState.Dormant;
        _candlesCollected = 0;
        _currentScaleMultiplier = 1f;

        RemoveFear();
        UnlockCamera();

        transform.position = _initialPosition;
        transform.localScale = _baseScale * _currentScaleMultiplier;

        UpdateHaloRadius(1f);
        PlayAnimationByName(idleAnimationName);
        SetVisualAlpha(0f);

        if (candles != null)
        {
            foreach (var c in candles)
            {
                if (c != null) c.ResetToInitialState();
            }
        }

        Debug.Log("【影子怪物】已完整重置至初始狀態（含全部燭火與音樂）。");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 🎵 追逐專屬背景音樂核心管理 (Chase BGM Management)
    // ──────────────────────────────────────────────────────────────────────────

    private void SetupChaseAudioSource()
    {
        if (_chaseAudioSource == null)
        {
            _chaseAudioSource = gameObject.GetComponent<AudioSource>();
            if (_chaseAudioSource == null) _chaseAudioSource = gameObject.AddComponent<AudioSource>();
            _chaseAudioSource.loop = true;
            _chaseAudioSource.playOnAwake = false;
            _chaseAudioSource.spatialBlend = spatialBlend;
            _chaseAudioSource.volume = 0f;
            _chaseAudioSource.rolloffMode = AudioRolloffMode.Linear;
            _chaseAudioSource.minDistance = 5f;
            _chaseAudioSource.maxDistance = 60f;
        }

        if (chaseBGM == null)
        {
#if UNITY_EDITOR
            chaseBGM = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/玻璃館/玻璃館_追逐_loop.wav");
#endif
        }

        if (victoryReliefSFX == null)
        {
#if UNITY_EDITOR
            victoryReliefSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/玻璃館/玻璃館_追逐_脫離.wav");
#endif
        }

        if (_chaseAudioSource != null && chaseBGM != null)
        {
            _chaseAudioSource.clip = chaseBGM;
        }
    }

    public void StartChaseBGM()
    {
        if (_chaseAudioSource == null) SetupChaseAudioSource();
        if (_chaseAudioSource == null || _chaseAudioSource.clip == null) return;

        if (_chaseBgmFadeCoroutine != null) StopCoroutine(_chaseBgmFadeCoroutine);
        _chaseBgmFadeCoroutine = StartCoroutine(FadeChaseBgmRoutine(chaseBGMVolume, chaseBGMFadeInDuration, true));
        Debug.Log($"🎵【影子怪物】追逐背景音樂啟動淡入播放（{_chaseAudioSource.clip.name}，音量 {chaseBGMVolume}）");
    }

    public void StopChaseBGM(bool immediate = false)
    {
        if (_chaseAudioSource == null) return;

        if (_chaseBgmFadeCoroutine != null) StopCoroutine(_chaseBgmFadeCoroutine);

        if (immediate)
        {
            _chaseAudioSource.Stop();
            _chaseAudioSource.volume = 0f;
            _chaseAudioSource.pitch = 1.0f;
        }
        else
        {
            _chaseBgmFadeCoroutine = StartCoroutine(FadeChaseBgmRoutine(0f, chaseBGMFadeOutDuration, false));
        }
    }

    private IEnumerator FadeChaseBgmRoutine(float targetVolume, float duration, bool playIfStarting)
    {
        if (_chaseAudioSource == null) yield break;

        if (playIfStarting && !_chaseAudioSource.isPlaying)
        {
            _chaseAudioSource.volume = 0f;
            _chaseAudioSource.pitch = 1.0f;
            _chaseAudioSource.Play();
        }

        float startVol = _chaseAudioSource.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _chaseAudioSource.volume = Mathf.Lerp(startVol, targetVolume, t);
            yield return null;
        }

        _chaseAudioSource.volume = targetVolume;

        if (!playIfStarting && targetVolume <= 0.001f)
        {
            _chaseAudioSource.Stop();
            _chaseAudioSource.pitch = 1.0f;
        }

        _chaseBgmFadeCoroutine = null;
    }

    private void UpdateProximityAudioTension()
    {
        if (_chaseAudioSource == null || !_chaseAudioSource.isPlaying || player == null || _chaseBgmFadeCoroutine != null) return;

        float dist = Mathf.Abs(player.position.x - transform.position.x);
        if (dist < 8.5f)
        {
            // 怪物越靠近，追逐音樂音量與緊張度稍微提高
            float t = Mathf.InverseLerp(8.5f, 2.5f, dist);
            _chaseAudioSource.volume = Mathf.Lerp(chaseBGMVolume, Mathf.Min(1f, chaseBGMVolume * 1.25f), t);
            _chaseAudioSource.pitch = Mathf.Lerp(1.0f, 1.05f, t);
        }
        else
        {
            _chaseAudioSource.volume = Mathf.MoveTowards(_chaseAudioSource.volume, chaseBGMVolume, Time.deltaTime * 0.5f);
            _chaseAudioSource.pitch = Mathf.MoveTowards(_chaseAudioSource.pitch, 1.0f, Time.deltaTime * 0.5f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 1. 繪製黃色線框：起手揮爪距離 (Attack Trigger Distance)，涵蓋怪物全身高度
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.7f);
        Vector3 forwardDir = transform.rotation * Vector3.forward;
        Vector3 triggerCenter = transform.position + forwardDir * (attackTriggerDistance * 0.5f) + Vector3.up * 15f;
        Gizmos.DrawWireCube(triggerCenter, new Vector3(attackTriggerDistance, 35f, 4f));

        // 2. 繪製紅色線框：巨爪命中傷害範圍 (Claw Hit Distance)，涵蓋怪物全身高度
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.85f);
        Vector3 hitCenter = transform.position + forwardDir * (clawHitDistance * 0.5f) + Vector3.up * 15f;
        Gizmos.DrawWireCube(hitCenter, new Vector3(clawHitDistance, 35f, 4f));
    }
}

