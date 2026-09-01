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

    [Tooltip("進結局前要不要再播一張卡。M5 已在鏡牆演出後播過，這裡預設留空＝不重播")]
    public string endingCardId = "";

    [Tooltip("結局要載入的場景（繪本）")]
    public string endingBookScene = "Book";

    [Header("★ 登場運鏡：先看怪物，再回到主角")]
    [Tooltip("觸發追逐時，先把鏡頭帶到怪物身上演一段，再把鏡頭交還主角")]
    public bool useRevealCutscene = true;

    [Tooltip("鏡頭飛去怪物、以及飛回主角，各自等多久（留時間給 Cinemachine 的阻尼追上）")]
    public float revealCameraTravel = 1.2f;

    [Tooltip("怪物完全浮現之後、開始移動之前的停頓")]
    public float revealHoldBeforeMove = 0.4f;

    [Tooltip("怪物開始移動後，鏡頭還要停在牠身上看多久")]
    public float revealWatchMoveSeconds = 0.9f;

    [Tooltip("鏡頭對準點的微調。對準點預設取全身 Renderer 的中心（怪物 Pivot 在腳底，直接看 Pivot 會對到地板下面）")]
    public Vector2 revealFocusOffset = Vector2.zero;

    [Tooltip("演出期間暫時把 Cinemachine 的位置阻尼調小，鏡頭才追得上。場景預設是 1，太黏會飛不到怪物身上就被叫回來。設負數＝不動它")]
    public float revealCameraDamping = 0.4f;

    [Tooltip("演出時把鏡頭拉遠讓整隻怪物進畫面，留多少邊。1.2＝上下各留 20%。這隻怪物約 45 單位高、鏡頭只看得到 27 單位，不拉遠只會看到牠的肚子。設 0＝不拉遠")]
    public float revealZoomPadding = 1.2f;

    [Tooltip("鏡頭要退到怪物背面多遠。怪物很厚（前後約 29 單位）又擺在 z=-13.2，相機只在焦點後方 15 單位，近裁面會直接切進牠身體裡，畫面上就是破圖、看得到內部")]
    public float revealDepthMargin = 3f;

    [Header("★ 開場距離：怪物要離主角多遠")]
    [Tooltip("觸發追逐時，把怪物擺到主角後方固定距離，玩家才有跑的空間。關掉＝用怪物在場景裡擺的位置")]
    public bool repositionOnActivate = true;

    [Tooltip("怪物出現在主角後方幾個世界單位。鏡頭一次看得到 48 單位寬，所以 45 大約是一個畫面外")]
    public float spawnDistanceBehindPlayer = 45f;

    [Header("★ 追逐速度智慧調整")]
    [Tooltip("開啟後怪物會依落後距離自動調速，維持在理想距離帶內。關掉＝回到原本的 chaseSpeed／runSpeedMultiplier 二段式")]
    public bool smartChaseSpeed = true;

    [Tooltip("理想落後距離的下限。比這個近，怪物會放慢腳步（要大於 attackTriggerDistance，不然牠會直接貼上來）")]
    public float idealLagMin = 16f;

    [Tooltip("理想落後距離的上限。比這個遠，怪物會加速追上來")]
    public float idealLagMax = 26f;

    [Tooltip("落後很多時允許的最高速度。要大於玩家速度（預設 5）牠才追得回來，也才走得到後面的燭火")]
    public float catchUpMaxSpeed = 7.5f;

    [Tooltip("貼太近時降到的速度")]
    public float easeOffSpeed = 2.2f;

    [Tooltip("速度變化的平滑度，每秒最多變化多少。太大會忽快忽慢")]
    public float speedSmooth = 3f;

    [Header("★ 縮小與燭火判定")]
    [Tooltip("怪物的 Pivot 在腳底，直接縮放會整隻沉到玻璃地板下面、也就吃不到燭火了。開啟後縮放時會補償 Y，讓視覺中心維持在同一個高度")]
    public bool keepVisualCenterHeight = true;

    [Tooltip("燭火判定的水平範圍")]
    public float candleReachX = 6f;

    [Tooltip("燭火判定的垂直額外寬容量（實際判定＝怪物目前半高 ＋ 這個值）")]
    public float candleReachYMargin = 3f;

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

    // 登場運鏡（跟 LockCamera 分開記錄，免得兩邊互相蓋掉還原值）
    private bool _revealActive = false;          // 整段演出期間為 true：不准抓人
    /// <summary> ★給 PlayerMovement 用：登場運鏡期間要硬鎖玩家，按鍵不能自己解鎖。 </summary>
    public static bool IsRevealRunning = false;
    private bool _revealCamTaken = false;
    private Transform _revealFocus;              // 鏡頭要看的點（怪物視覺中心）
    private List<CinemachineCamera> _revealVcams3 = new List<CinemachineCamera>();
    private List<CinemachineVirtualCamera> _revealVcamsLegacy = new List<CinemachineVirtualCamera>();
    private List<Transform> _revealOrigFollow = new List<Transform>();
    private List<CinemachineFollow> _revealFollows = new List<CinemachineFollow>();
    private List<Vector3> _revealOrigDamping = new List<Vector3>();
    private List<float> _revealOrigLens = new List<float>();
    private Bounds _visualLocalBounds = new Bounds(Vector3.zero, Vector3.one);
    private bool _visualBoundsMeasured = false;
    private float _visualAnchorY = 0f;      // 縮放時要維持的視覺中心高度
    private float _smoothedChaseSpeed = -1f;
    private float _revealTargetLens = 0f;
    private bool _revealZoomEnabled = false;

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
        EnsureVisualBounds();
        _visualAnchorY = _initialPosition.y + _visualLocalBounds.center.y * Mathf.Abs(_baseScale.y);

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
        // ★每次載入本場景都把結局旗標歸零：
        //   不然玩家「通關 → 回主選單 → 再玩一次」時（同一個 Play 期間不會重置 static），
        //   _endingFired 還留著 true，第二輪收完最後一根燭火就不會進結局了。
        _endingFired = false;
        IsRevealRunning = false;

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
                // ★登場運鏡進行中：怪物先站著讓鏡頭看，不移動也不抓人
                if (chaseWhileAppearing && !_revealActive)
                {
                    MoveTowardPlayer(chaseSpeed);
                    CheckCatch();
                    CheckCandleCollisions();
                }
                if (enableProximityTension) UpdateProximityAudioTension();
                break;

            case MonsterState.Chasing:
                MoveTowardPlayer(chaseSpeed);
                // ★演出還沒收尾（鏡頭還在飛回來、主角還被凍住）時不准抓人
                if (!_revealActive) CheckCatch();
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
            if (c != null && !c.isCollected && OverlapsCandle(c.transform.position))
            {
                c.Collect();
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
        if (_revealActive) return;   // ★登場運鏡期間主角是凍住的，不能被吃
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

        // ★開場先把怪物擺到主角後方一段距離。
        //   場景裡牠只離觸發點 23 單位，而鏡頭一次就看得到 48 單位寬——
        //   等於一開始怪物就貼在臉上，沒有跑的空間。
        //   只改 X，Y／Z 保持美術擺好的樣子（牠是半身埋在玻璃地板下的巨影）。
        EnsurePlayerReference();
        _smoothedChaseSpeed = -1f;   // 每次重新開追都從當下的目標速度起步
        if (repositionOnActivate && player != null && spawnDistanceBehindPlayer > 0f)
        {
            Vector3 sp = transform.position;
            sp.x = player.position.x - spawnDistanceBehindPlayer;   // 主角往 +X 前進，怪物在 -X 那側
            transform.position = sp;
            Debug.Log($"【影子怪物】開場定位：主角 x={player.position.x:F1}，怪物擺到 x={sp.x:F1}（後方 {spawnDistanceBehindPlayer} 單位）");
        }

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
            // (true, false) ＝自己淡入黑幕，播完維持全黑交給場景載入
            yield return StoryCardPlayer.Instance.Play(endingCardId, true, false);
        }

        EndCredits.EndingMode = true;
        if (string.IsNullOrEmpty(endingBookScene)) yield break;

        // 沒播卡的話用專案既有的轉場控制器蓋黑再切，別硬切
        if (endingCardId == "" && SceneTransitionController.Instance != null)
        {
            SceneTransitionController.Instance.TransitionToScene(endingBookScene);
            yield break;
        }
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
            else AudioSource.PlayClipAtPoint(appearSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        transform.localScale = _baseScale * _currentScaleMultiplier;
        SetVisualAlpha(0f);

        PlayAnimationByName(walkAnimationName);

        // ★有運鏡就走運鏡版：鏡頭先看怪物 → 怪物動起來 → 鏡頭回到主角 → 還控制權
        if (useRevealCutscene && player != null)
        {
            yield return StartCoroutine(RevealCutsceneRoutine());
        }
        else
        {
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

            PlayRoar();
        }

        currentState = MonsterState.Chasing;
        Debug.Log("【影子怪物】登場完成，全力追逐！");
    }

    private void PlayRoar()
    {
        if (roarSFX == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(roarSFX, transform.position, sfxVolume);
        else AudioSource.PlayClipAtPoint(roarSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ★登場運鏡
    //   1. 凍住主角，鏡頭飛到怪物身上
    //   2. 怪物在鏡頭裡浮現、吼一聲
    //   3. 怪物在鏡頭前開始移動（讓玩家看清楚是什麼東西在追她）
    //   4. 鏡頭飛回主角，還原相機，解凍，開始遊戲
    //   全程 _revealActive = true：怪物抓不到被凍住的主角。
    // ══════════════════════════════════════════════════════════════════════════
    private IEnumerator RevealCutsceneRoutine()
    {
        _revealActive = true;
        IsRevealRunning = true;

        // 1) 凍住主角並把她停下來，免得放開時還帶著慣性
        if (_pm != null)
        {
            _pm.isCutsceneFrozen = true;
            Rigidbody prb = _pm.GetComponent<Rigidbody>();
            if (prb != null) prb.linearVelocity = new Vector3(0f, prb.linearVelocity.y, 0f);
        }

        // 2) 先量出怪物的視覺範圍。
        //    ★不能用 Renderer.bounds：這時候 SetVisualAlpha(0) 已經把所有 Renderer 關掉，
        //      關掉的 Renderer（尤其 SkinnedMeshRenderer）回報的 bounds 是舊的／空的，
        //      鏡頭就會飛去一個沒有東西的地方——這就是「鏡頭鎖定怪物但怪物沒顯示」。
        //      改成從 Mesh 資產本身的 bounds 算，關著也算得出來。
        EnsureVisualBounds();
        EnsureRevealFocus();
        UpdateRevealFocus();

        // 3) 接管相機，飛向怪物
        RevealCameraTakeOver();
        RevealCameraLookAt(_revealFocus);

        // 4) ★怪物立刻開始現身，不能等鏡頭飛到才開始。
        //    鏡頭飛過去要一秒多，那段時間怪物還是隱形的話，玩家看到的就是一片空地。
        //    同時把鏡頭拉遠，這隻怪物比整個畫面還高，不拉遠只會看到牠的肚子。
        float dur = Mathf.Max(0.01f, appearDuration);
        float travel = Mathf.Max(0.01f, revealCameraTravel);
        float span = Mathf.Max(dur, travel);
        float e = 0f;
        while (e < span)
        {
            e += Time.deltaTime;
            SetVisualAlpha(Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, e / dur)));
            transform.localScale = _baseScale * _currentScaleMultiplier;
            UpdateRevealFocus();
            UpdateRevealZoom(Mathf.Clamp01(e / travel));
            yield return null;
        }
        SetVisualAlpha(1f);
        transform.localScale = _baseScale * _currentScaleMultiplier;
        UpdateRevealZoom(1f);

        PlayRoar();

        e = 0f;
        while (e < Mathf.Max(0f, revealHoldBeforeMove))
        {
            e += Time.deltaTime; UpdateRevealFocus(); yield return null;
        }

        // 5) ★怪物在鏡頭前開始移動
        currentState = MonsterState.Chasing;
        e = 0f;
        while (e < Mathf.Max(0f, revealWatchMoveSeconds))
        {
            e += Time.deltaTime; UpdateRevealFocus(); yield return null;
        }

        // 6) 鏡頭飛回主角，鏡頭大小同時收回原本的
        RevealCameraLookAt(player != null ? player : transform);
        e = 0f;
        while (e < travel)
        {
            e += Time.deltaTime;
            UpdateRevealZoom(1f - Mathf.Clamp01(e / travel));
            yield return null;
        }
        UpdateRevealZoom(0f);

        // 7) 還原相機、解凍、開始遊戲
        RevealFinish();
        Debug.Log("【影子怪物】登場運鏡結束，控制權交還玩家。");
    }

    /// <summary> 演出正常收尾：還原相機、解凍主角。 </summary>
    private void RevealFinish()
    {
        RevealCameraRestore();
        if (_pm != null) _pm.isCutsceneFrozen = false;
        _revealActive = false;
        IsRevealRunning = false;
    }

    /// <summary> 演出被中斷（死亡重生 / 關卡重置）時，一定要把鏡頭和控制權還回去。 </summary>
    private void RevealCancel()
    {
        if (!_revealActive && !_revealCamTaken) return;
        UpdateRevealZoom(0f);
        RevealFinish();
        if (_revealFocus != null) Destroy(_revealFocus.gameObject);
        _revealFocus = null;
    }

    private void EnsureRevealFocus()
    {
        if (_revealFocus == null)
            _revealFocus = new GameObject("ShadowMonster_RevealFocus").transform;
    }

    /// <summary> 對準點＝視覺中心（不是 transform，牠的 Pivot 在腳底），Z 沿用主角。 </summary>
    private void UpdateRevealFocus()
    {
        if (_revealFocus == null) return;
        EnsureVisualBounds();
        Vector3 c = transform.TransformPoint(_visualLocalBounds.center);

        // ★Z 不能直接沿用主角的。
        //   相機在焦點後方 15 單位（CinemachineFollow.FollowOffset.z），而這隻怪物
        //   前後厚約 29 單位、又擺在 z=-13.2。焦點用主角的 z=0 的話相機落在 z=-15，
        //   剛好切進怪物身體——正交投影的近裁面會把牠剖開，畫面上就是破圖、
        //   可以看穿到裡面去。這裡把焦點往後推到相機能整隻看完為止。
        float scaleXZ = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float halfDepth = Mathf.Max(_visualLocalBounds.extents.x, _visualLocalBounds.extents.z) * scaleXZ;

        float offsetZ = -15f;
        for (int i = 0; i < _revealFollows.Count; i++)
        {
            if (_revealFollows[i] != null) { offsetZ = _revealFollows[i].FollowOffset.z; break; }
        }

        float needZ = (c.z - halfDepth) - Mathf.Max(0f, revealDepthMargin) - offsetZ;
        float baseZ = player != null ? player.position.z : c.z;
        float z = Mathf.Min(baseZ, needZ);

        _revealFocus.position = new Vector3(c.x + revealFocusOffset.x, c.y + revealFocusOffset.y, z);
    }

    // ── 從 Mesh 資產量身高（Renderer 關著也算得出來）────────────────────────
    private void EnsureVisualBounds()
    {
        if (_visualBoundsMeasured) return;
        _visualBoundsMeasured = true;

        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        bool any = false;
        Bounds acc = new Bounds(Vector3.zero, Vector3.zero);

        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r == null || r.sharedMesh == null) continue;
            // SkinnedMeshRenderer 的 sharedMesh.bounds 是在 rootBone 的空間裡
            Transform space = r.rootBone != null ? r.rootBone : r.transform;
            AccumulateBounds(ref acc, ref any, r.sharedMesh.bounds, toLocal * space.localToWorldMatrix);
        }
        foreach (var f in GetComponentsInChildren<MeshFilter>(true))
        {
            if (f == null || f.sharedMesh == null) continue;
            if (f.GetComponent<MeshRenderer>() == null) continue;
            AccumulateBounds(ref acc, ref any, f.sharedMesh.bounds, toLocal * f.transform.localToWorldMatrix);
        }

        if (!any)
        {
            // 真的量不到就退回 Renderer.bounds（至少有個值），再不行就用自己的原點
            Renderer[] rs = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                if (r == null) continue;
                Vector3 lc = transform.InverseTransformPoint(r.bounds.center);
                if (!any) { acc = new Bounds(lc, Vector3.zero); any = true; }
                else acc.Encapsulate(lc);
            }
            if (!any) acc = new Bounds(Vector3.zero, Vector3.one);
        }

        _visualLocalBounds = acc;
        Debug.Log($"【影子怪物】量到的視覺範圍（本地）中心 {acc.center}，大小 {acc.size}；" +
                  $"世界高度約 {acc.size.y * Mathf.Abs(transform.lossyScale.y):F1} 單位。");
    }

    /// <summary> 怪物的視覺中心（世界座標）。牠的 Pivot 在腳底，所有判定都該用這個點。 </summary>
    public Vector3 VisualCenter
    {
        get
        {
            EnsureVisualBounds();
            return transform.TransformPoint(_visualLocalBounds.center);
        }
    }

    /// <summary> 目前縮放下的視覺半高。 </summary>
    public float VisualHalfHeight
    {
        get
        {
            EnsureVisualBounds();
            return _visualLocalBounds.extents.y * Mathf.Abs(transform.lossyScale.y);
        }
    }

    /// <summary>
    /// 燭火是否進入怪物的身體範圍。
    /// ★原本兩邊都是寫死的 dx&lt;=3.5、dy&lt;=6.5，而怪物腳底在 y≈-60、燭火在 y≈-36，
    ///   dy 差 23 個單位，那個判定從頭到尾就沒成立過——真正在收燭火的是碰撞體重疊，
    ///   所以怪物一縮小、身體構不到燭火，後面幾根就吃不到了。
    ///   改成用視覺中心＋目前半高判定，縮放多少都算得準。
    /// </summary>
    public bool OverlapsCandle(Vector3 candlePosition)
    {
        Vector3 c = VisualCenter;
        float dx = Mathf.Abs(candlePosition.x - c.x);
        if (dx > candleReachX) return false;
        float dy = Mathf.Abs(candlePosition.y - c.y);
        return dy <= VisualHalfHeight + candleReachYMargin;
    }

    private static void AccumulateBounds(ref Bounds acc, ref bool any, Bounds local, Matrix4x4 m)
    {
        Vector3 c = local.center, ex = local.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 pt = new Vector3(
                c.x + (((i & 1) == 0) ? -ex.x : ex.x),
                c.y + (((i & 2) == 0) ? -ex.y : ex.y),
                c.z + (((i & 4) == 0) ? -ex.z : ex.z));
            Vector3 w = m.MultiplyPoint3x4(pt);
            if (!any) { acc = new Bounds(w, Vector3.zero); any = true; }
            else acc.Encapsulate(w);
        }
    }

    // ── 相機接管 ──────────────────────────────────────────────────────────
    private void RevealCameraTakeOver()
    {
        if (_revealCamTaken) return;
        _revealCamTaken = true;

        _revealVcams3.Clear();
        _revealVcamsLegacy.Clear();
        _revealOrigFollow.Clear();
        _revealFollows.Clear();
        _revealOrigDamping.Clear();
        _revealOrigLens.Clear();

        if (_mainCam == null) _mainCam = Camera.main;
        bool ortho = _mainCam == null || _mainCam.orthographic;

        // 這隻怪物比整個畫面還高，算出要把鏡頭拉多遠才裝得下
        float halfH = _visualLocalBounds.extents.y * Mathf.Abs(transform.lossyScale.y);
        _revealZoomEnabled = ortho && revealZoomPadding > 0f && halfH > 0.01f;
        _revealTargetLens = halfH * revealZoomPadding;

        foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            _revealVcams3.Add(v);
            _revealOrigFollow.Add(v.Follow);
            _revealOrigLens.Add(v != null ? v.Lens.OrthographicSize : 0f);

            // 場景的 PositionDamping 是 1，鏡頭會很黏；演出期間先調小，結束再還原
            CinemachineFollow cf = v != null ? v.GetComponent<CinemachineFollow>() : null;
            _revealFollows.Add(cf);
            _revealOrigDamping.Add(cf != null ? cf.TrackerSettings.PositionDamping : Vector3.zero);
            if (cf != null && revealCameraDamping >= 0f)
            {
                var ts = cf.TrackerSettings;
                ts.PositionDamping = new Vector3(revealCameraDamping, revealCameraDamping, ts.PositionDamping.z);
                cf.TrackerSettings = ts;
            }
        }
        foreach (var v in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None))
        {
            _revealVcamsLegacy.Add(v);
            _revealOrigFollow.Add(v.Follow);
        }
    }

    /// <summary> t: 0 ＝ 原本的鏡頭大小，1 ＝ 拉遠到整隻怪物裝得下。 </summary>
    private void UpdateRevealZoom(float t)
    {
        if (!_revealZoomEnabled || !_revealCamTaken) return;
        for (int i = 0; i < _revealVcams3.Count && i < _revealOrigLens.Count; i++)
        {
            var v = _revealVcams3[i];
            if (v == null) continue;
            float orig = _revealOrigLens[i];
            float target = Mathf.Max(orig, _revealTargetLens);   // 只拉遠，不拉近
            var lens = v.Lens;
            lens.OrthographicSize = Mathf.Lerp(orig, target, Mathf.Clamp01(t));
            v.Lens = lens;
        }
    }

    private void RevealCameraLookAt(Transform target)
    {
        if (target == null) return;

        foreach (var v in _revealVcams3)
        {
            if (v == null) continue;
            var tg = v.Target;
            tg.TrackingTarget = target;
            v.Target = tg;
            v.Follow = target;
        }
        foreach (var v in _revealVcamsLegacy)
        {
            if (v != null) v.Follow = target;
        }
    }

    private void RevealCameraRestore()
    {
        if (!_revealCamTaken) return;
        _revealCamTaken = false;

        // 鏡頭大小還原
        for (int i = 0; i < _revealVcams3.Count && i < _revealOrigLens.Count; i++)
        {
            var v = _revealVcams3[i];
            if (v == null) continue;
            var lens = v.Lens;
            lens.OrthographicSize = _revealOrigLens[i];
            v.Lens = lens;
        }

        // 阻尼還原
        for (int i = 0; i < _revealFollows.Count; i++)
        {
            CinemachineFollow cf = _revealFollows[i];
            if (cf == null || i >= _revealOrigDamping.Count) continue;
            var ts = cf.TrackerSettings;
            ts.PositionDamping = _revealOrigDamping[i];
            cf.TrackerSettings = ts;
        }

        int idx = 0;
        foreach (var v in _revealVcams3)
        {
            if (v != null && idx < _revealOrigFollow.Count)
            {
                // 接管前如果本來就沒有目標（場景裡 TrackingTarget 是空的，靠腳本在跑時指定），
                // 還原成 null 會讓相機從此不跟人。這種情況退回主角。
                Transform back = _revealOrigFollow[idx] != null ? _revealOrigFollow[idx] : player;
                var tg = v.Target;
                tg.TrackingTarget = back;
                v.Target = tg;
                v.Follow = back;
            }
            idx++;
        }
        foreach (var v in _revealVcamsLegacy)
        {
            if (v != null && idx < _revealOrigFollow.Count)
                v.Follow = _revealOrigFollow[idx] != null ? _revealOrigFollow[idx] : player;
            idx++;
        }
    }

    /// <summary> 吃到燭火：播放 gethit3 受擊動畫並漸漸變小 </summary>
    private IEnumerator HitAndSmoothShrinkRoutine(float targetMult)
    {
        _isHitShrinking = true;

        PlayAnimationByName(hitAnimationName);

        if (hitSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(hitSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(hitSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
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
            else AudioSource.PlayClipAtPoint(victoryReliefSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }
        else if (vanishSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(vanishSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(vanishSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
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
            else AudioSource.PlayClipAtPoint(attackSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
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
                else AudioSource.PlayClipAtPoint(catchPlayerSFX, player.position, AudioManager.ScaleSfx(sfxVolume));
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
            else AudioSource.PlayClipAtPoint(punishTeleportSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
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

    /// <summary>
    /// 依落後距離決定速度，把怪物維持在 idealLagMin ~ idealLagMax 的距離帶內。
    ///
    /// 為什麼要做這個：原本最快只有 chaseSpeed(3.2) × runSpeedMultiplier(1.35) ＝ 4.32，
    /// 而玩家是 5，所以怪物永遠追不上，也永遠走不到後面那幾根燭火——
    /// 追逐沒有壓迫感，最後一根燭火也收不到。
    ///
    /// 落後太多 → 允許超過玩家速度追回來；貼太近 → 放慢，給玩家喘息，
    /// 在距離帶內 → 大致跟著玩家的速度走。
    /// </summary>
    private float SmartSpeed(float baseChaseSpeed, float lagDistance)
    {
        float playerSpeed = (_pm != null && _pm.baseSpeed > 0.1f) ? _pm.baseSpeed : 5f;
        float lo = Mathf.Min(idealLagMin, idealLagMax);
        float hi = Mathf.Max(idealLagMin, idealLagMax);

        float target;
        if (lagDistance > hi)
        {
            // 落後了：越遠追越快，最多到 catchUpMaxSpeed
            float t = Mathf.InverseLerp(hi, hi + 45f, lagDistance);
            target = Mathf.Lerp(playerSpeed * 1.05f, Mathf.Max(catchUpMaxSpeed, playerSpeed * 1.05f), t);
        }
        else if (lagDistance < lo)
        {
            // 太近了（含跑到玩家前面去的情況）：放慢
            float t = Mathf.InverseLerp(lo, 0f, lagDistance);
            target = Mathf.Lerp(playerSpeed * 0.92f, Mathf.Max(0.5f, easeOffSpeed), t);
        }
        else
        {
            // 在距離帶內：跟著玩家走，讓距離慢慢收斂
            target = playerSpeed * 0.98f;
        }

        // 別低於原本設定的基礎速度太多，也別瞬間變速
        target = Mathf.Max(target, Mathf.Min(baseChaseSpeed, easeOffSpeed));
        if (_smoothedChaseSpeed < 0f) _smoothedChaseSpeed = target;
        _smoothedChaseSpeed = Mathf.MoveTowards(_smoothedChaseSpeed, target, Mathf.Max(0.1f, speedSmooth) * Time.deltaTime);
        return _smoothedChaseSpeed;
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
            if (smartChaseSpeed)
            {
                currentSpeed = SmartSpeed(speed, lagDistance);
                // 用速度決定動畫，不再用固定距離門檻
                float walkTop = Mathf.Max(0.01f, speed);
                PlayAnimationByName(currentSpeed > walkTop * 1.05f ? runAnimationName : walkAnimationName);
            }
            else
            {
                // 原本的二段式：落後超過門檻才切跑步
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
        //   keepVisualCenterHeight 開著時 Y 由 ApplyCurrentScale 決定，這裡不要搶。
        if (enableGroundSnap && !keepVisualCenterHeight)
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

        // ★Pivot 在腳底，直接縮放整隻會往下沉。補償 Y，讓視覺中心留在原本的高度，
        //   牠才會一直「半身埋在玻璃地板」的樣子，也才構得到地板上的燭火。
        if (keepVisualCenterHeight)
        {
            EnsureVisualBounds();
            Vector3 pos = transform.position;
            pos.y = _visualAnchorY - _visualLocalBounds.center.y * Mathf.Abs(_baseScale.y) * _currentScaleMultiplier;
            transform.position = pos;
        }

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
        _smoothedChaseSpeed = -1f;

        RemoveFear();
        UnlockCamera();
        RevealCancel();   // ★StopAllCoroutines 會把演出砍在半路，這裡要把鏡頭與控制權還回去

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
            float scaledTargetVolume = targetVolume * AudioManager.BgmVolume;
            _chaseAudioSource.volume = Mathf.Lerp(startVol, scaledTargetVolume, t);
            yield return null;
        }

        _chaseAudioSource.volume = targetVolume * AudioManager.BgmVolume;

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

        float bgmVolume = AudioManager.BgmVolume;
        float dist = Mathf.Abs(player.position.x - transform.position.x);
        if (dist < 8.5f)
        {
            // 怪物越靠近，追逐音樂音量與緊張度稍微提高
            float t = Mathf.InverseLerp(8.5f, 2.5f, dist);
            _chaseAudioSource.volume = Mathf.Lerp(chaseBGMVolume, Mathf.Min(1f, chaseBGMVolume * 1.25f), t) * bgmVolume;
            _chaseAudioSource.pitch = Mathf.Lerp(1.0f, 1.05f, t);
        }
        else
        {
            _chaseAudioSource.volume = Mathf.MoveTowards(_chaseAudioSource.volume, chaseBGMVolume * bgmVolume, Time.unscaledDeltaTime * 0.5f);
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

