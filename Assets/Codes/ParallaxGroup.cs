using UnityEngine;

/// <summary>
/// 廢墟背景群組控制器 (Ruins Parallax Group Controller)
///
/// ★ 支援 RuinsParallaxRoot 父物件統一管理，或獨立 RuinedBackground 視覺清單
/// ★ Start() 時記住初始世界座標，維持所有視覺背景與裝飾物件彼此相對位置 100% 恆定
/// ★ 靜止階段：玩家尚未著地廢墟前，硬鎖初始世界座標
/// ★ 跟隨階段：玩家著地於廢墟 (tag=Floor + Y <= ruinedZoneYThreshold) 後啟動
///     - 【玩家移動時】：背景移動 = Player Follow (playerDeltaX * followFactorX) + Autonomous Drift (driftSpeedX * Time.deltaTime)
///     - 【玩家停止時】：背景移動 = 0 (Player Follow 停止) + Autonomous Drift (漂移持續進行)
///     - 【杜絕跳動】：每幀嚴格更新 previousPlayerX，玩家停止期間不累積歷史 Follow 補償，起步時絕不瞬移
/// ★ 物理隔離：僅負責純視覺層 (Background, Debris, Decorations)，絕不移動 Gameplay Colliders / Rigidbodies / Triggers
/// </summary>
public class ParallaxGroup : MonoBehaviour, IResettable
{
    [Header("玩家參考 (留空自動搜尋)")]
    [Tooltip("要追蹤的玩家 Transform (留空時自動搜尋 Tag='Player' 或名稱為 Player 的物件)")]
    public Transform player;

    [Header("統一根節點管理 (RuinsParallaxRoot)")]
    [Tooltip("若指定此 Root 物件，將直接統一平移此 Root（其底下的 Background、Debris、Decorations 將自然同步移動，效率最高）")]
    public Transform parallaxRoot;

    [Tooltip("若未指定 parallaxRoot，是否使用掛載此腳本的 GameObject 作為 Root？(若有子物件則預設建議開啟)")]
    public bool useCurrentTransformAsRoot = false;

    [Header("標籤搜尋備用設定 (無 Root 時自動啟用)")]
    [Tooltip("若未指定 Root 且關閉 useCurrentTransformAsRoot，將自動搜尋帶有此 Tag 的所有獨立背景物件")]
    public string targetTag = "RuinedBackground";

    [Header("觸發設定")]
    [Tooltip("廢墟區 Y 軸門檻：玩家 Y <= 此值且著地才算進入廢墟層")]
    public float ruinedZoneYThreshold = -85f;

    [Header("玩家視差跟隨設定 (Player Parallax)")]
    [Tooltip("是否啟用玩家移動時的背景視差跟隨？(預設開啟)")]
    public bool enablePlayerParallax = true;

    [Tooltip("背景跟隨主角 X 移動的視差比例 (0 = 完全不跟, 0.5 = 跟隨一半, 預設 0.3)")]
    public float followFactorX = 0.3f;

    [Tooltip("判定玩家停止的位移門檻 (小於此值視為玩家停止，預設 0.001)")]
    public float playerStopThreshold = 0.001f;

    [Header("自主緩慢漂移設定 (Autonomous Drift)")]
    [Tooltip("是否啟用背景自主緩慢漂移？(預設開啟，玩家停止時依然保持緩慢漂移)")]
    public bool enableAutonomousDrift = true;

    [Tooltip("每秒背景自主漂移的距離 (正值 = +X 方向, 負值 = -X 方向，預設 0.5)")]
    public float driftSpeedX = 0.5f;

    [Header("觀察用狀態 (唯讀)")]
    public bool isPlayerInRuinedZone = false;
    public bool isFollowActive = false;
    public bool isPlayerMoving = false;
    public float currentTotalOffsetX = 0f;

    // 模式一：管理單一 Root Transform
    private Transform _rootTransform;
    private Vector3 _rootLockedPos;
    private Vector3 _initialRootPos;

    // 模式二：管理離散多個 Transform
    private Transform[] _bgTransforms;
    private Vector3[] _lockedPositions;
    private Vector3[] _initialPositions;

    private PlayerMovement _playerMovement;

    // 記住啟動跟隨當下主角的 X 位置，作為後續位移計算的基準
    private float _basePlayerX = 0f;
    private float _lastPlayerX = 0f;

    private bool _initialized = false;

    void Start()
    {
        Init();
    }

    public void Init()
    {
        // 1. 決定管理模式：Root 模式優先
        if (parallaxRoot != null)
        {
            _rootTransform = parallaxRoot;
        }
        else if (useCurrentTransformAsRoot && transform.childCount > 0)
        {
            _rootTransform = transform;
        }

        if (_rootTransform != null)
        {
            _initialRootPos = _rootTransform.position;
            _rootLockedPos = _initialRootPos;
            _bgTransforms = null;
            _lockedPositions = null;
            _initialPositions = null;
            ValidateVisualRootSafety(_rootTransform);
            Debug.Log($"[ParallaxGroup] 採用統一 Root 模式管理: '{_rootTransform.name}' (初始世界座標: {_rootLockedPos})");
        }
        else
        {
            // 2. 備用模式：掃描 Tag 離散背景物件
            GameObject[] bgs = GameObject.FindGameObjectsWithTag(targetTag);
            if (bgs == null || bgs.Length == 0)
            {
                Debug.LogWarning($"[ParallaxGroup] 找不到任何 Root 或 Tag='{targetTag}' 的物件！");
                return;
            }

            _bgTransforms = new Transform[bgs.Length];
            _lockedPositions = new Vector3[bgs.Length];
            _initialPositions = new Vector3[bgs.Length];

            for (int i = 0; i < bgs.Length; i++)
            {
                _bgTransforms[i] = bgs[i].transform;
                _initialPositions[i] = bgs[i].transform.position; // 記住初始世界座標
                _lockedPositions[i] = _initialPositions[i];
            }
            Debug.Log($"[ParallaxGroup] 採用離散 Tag 模式管理 {bgs.Length} 個 '{targetTag}' 物件。");
        }

        FindPlayer();
        _initialized = true;
    }

    /// <summary>
    /// 安全防呆：檢查 ParallaxRoot 內是否包含 Gameplay 碰撞體或物理物件，防止誤將遊戲物理跟著背景漂移
    /// </summary>
    private void ValidateVisualRootSafety(Transform root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders)
        {
            if (!col.isTrigger && col.gameObject.layer != LayerMask.NameToLayer("Ignore Raycast"))
            {
                Debug.LogWarning($"⚠️ [ParallaxGroup 安全警示] 在 ParallaxRoot '{root.name}' 底下偵測到實體碰撞體 '{col.gameObject.name}'！" +
                                 $"請確認該物件是否為純視覺背景。若是 Gameplay 地板/牆壁，請移出 ParallaxRoot 以免碰撞體隨背景位移！");
            }
        }
    }

    void FindPlayer()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj == null) playerObj = GameObject.Find("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }

        if (player != null)
        {
            _playerMovement = player.GetComponent<PlayerMovement>();
            _lastPlayerX = player.position.x;
        }
    }

    void LateUpdate()
    {
        if (!_initialized) return;
        if (player == null) FindPlayer();
        if (player == null) return;

        // 判斷玩家是否已踩到廢墟地板（isGrounded + Y <= 門檻）
        bool playerGrounded = (_playerMovement != null) ? _playerMovement.isGrounded : true;
        bool playerInRuinedY = player.position.y <= ruinedZoneYThreshold;

        isPlayerInRuinedZone = playerGrounded && playerInRuinedY;

        // 一旦玩家踩到廢墟地板，啟動跟隨（之後持續處於啟動狀態）
        if (isPlayerInRuinedZone && !isFollowActive)
        {
            isFollowActive = true;
            _basePlayerX = player.position.x;
            _lastPlayerX = _basePlayerX;
            Debug.Log($"[ParallaxGroup] 玩家已著地於廢墟！正式啟動視覺 Parallax + 移動漂移系統。基準 X = {_basePlayerX}");
        }

        // 尚未進入廢墟時：硬鎖所有背景於初始世界座標
        if (!isFollowActive)
        {
            LockToInitialPositions();
            return;
        }

        // --- 跟隨階段：計算玩家實際 X 位移 ---
        float currentPlayerX = player.position.x;
        float playerDeltaX = currentPlayerX - _lastPlayerX;
        _lastPlayerX = currentPlayerX; // 每一幀嚴格更新，杜絕停止後重新起步時累積任何歷史位移

        // 判定玩家本幀是否正在移動
        isPlayerMoving = Mathf.Abs(playerDeltaX) >= playerStopThreshold;

        // 1. 玩家視差位移 (Player Parallax Delta) - 玩家停止時歸零
        float playerParallaxDelta = 0f;
        if (enablePlayerParallax && isPlayerMoving)
        {
            playerParallaxDelta = playerDeltaX * followFactorX;
        }

        // 2. 自主緩慢漂移 (Autonomous Drift Delta) - 玩家停止時依然持續平滑漂移！
        float driftDelta = 0f;
        if (enableAutonomousDrift)
        {
            driftDelta = driftSpeedX * Time.deltaTime;
        }

        // 3. 總位移量
        float totalDeltaX = playerParallaxDelta + driftDelta;
        currentTotalOffsetX += totalDeltaX;

        // 統一套用位移至受管物件（確保彼此相對位置 100% 剛性不變）
        if (Mathf.Abs(totalDeltaX) > 0.000001f)
        {
            ApplyDelta(totalDeltaX, 0f);
        }
    }

    private void LockToInitialPositions()
    {
        if (_rootTransform != null)
        {
            _rootLockedPos = _initialRootPos;
            _rootTransform.position = _rootLockedPos;
        }
        else if (_bgTransforms != null && _initialPositions != null)
        {
            for (int i = 0; i < _bgTransforms.Length; i++)
            {
                if (_bgTransforms[i] != null)
                {
                    _lockedPositions[i] = _initialPositions[i];
                    _bgTransforms[i].position = _lockedPositions[i];
                }
            }
        }
    }

    private void ApplyDelta(float deltaX, float deltaY)
    {
        if (_rootTransform != null)
        {
            _rootLockedPos.x += deltaX;
            _rootLockedPos.y += deltaY;
            _rootTransform.position = _rootLockedPos;
        }
        else if (_bgTransforms != null && _lockedPositions != null)
        {
            for (int i = 0; i < _bgTransforms.Length; i++)
            {
                if (_bgTransforms[i] != null)
                {
                    _lockedPositions[i].x += deltaX;
                    _lockedPositions[i].y += deltaY;
                    _bgTransforms[i].position = _lockedPositions[i];
                }
            }
        }
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        isFollowActive = false;
        isPlayerInRuinedZone = false;
        isPlayerMoving = false;
        currentTotalOffsetX = 0f;
        LockToInitialPositions();
        if (player != null)
        {
            _lastPlayerX = player.position.x;
        }
    }
}
