using UnityEngine;

/// <summary>
/// 廢墟背景群組控制器 (RuinedBackground Group Controller)
///
/// ★ 完全不修改任何物件的父子關係
/// ★ Start() 時記住每個 RuinedBackground 的初始世界座標
/// ★ 靜止階段：LateUpdate() 每幀硬鎖所有背景回初始位置，相對高度/距離 100% 不變
/// ★ 跟隨階段：玩家落地於廢墟區 (tag=Floor + Y <= ruinedZoneYThreshold) 後啟動
///            - 所有背景整體跟隨主角的 X 位移（視差偏移比例可調）
///            - 同時緩慢往 -X 方向持續漂移（背景往左流動的視覺差）
/// </summary>
public class ParallaxGroup : MonoBehaviour
{
    [Header("標籤設定")]
    public string targetTag = "RuinedBackground";

    [Header("觸發設定")]
    [Tooltip("廢墟區 Y 軸門檻：玩家 Y <= 此值且著地才算進入廢墟層")]
    public float ruinedZoneYThreshold = -85f;

    [Header("跟隨設定 (踩到廢墟地板後啟動)")]
    [Tooltip("踩地後，背景整體跟隨主角 X 移動的視差比例 (0 = 完全不跟, 0.5 = 跟隨一半, 建議 0.3)")]
    public float followFactorX = 0.3f;

    [Tooltip("Y 軸跟隨比例 (建議設 0，背景不跟上下)")]
    public float followFactorY = 0f;

    [Header("方向緩慢漂移設定")]
    [Tooltip("每秒背景自主漂移的距離（正值 = 往右 +X 方向, 負值 = 往左 -X 方向，建議 0.3 ~ 1.0）")]
    public float driftSpeedX = 0.5f;

    [Header("觀察用 (不要手動改)")]
    public bool isPlayerInRuinedZone = false;
    public bool isFollowActive = false;

    // 每個背景物件的 Transform 與目前鎖定的世界座標
    private Transform[] _bgTransforms;
    private Vector3[] _lockedPositions;

    private Transform _playerTransform;
    private PlayerMovement _playerMovement;

    // 記住啟動跟隨當下主角的 X 位置，作為後續位移計算的基準
    private float _basePlayerX = 0f;
    private float _lastPlayerX = 0f;

    private bool _initialized = false;

    void Start()
    {
        Init();
    }

    void Init()
    {
        GameObject[] bgs = GameObject.FindGameObjectsWithTag(targetTag);
        if (bgs == null || bgs.Length == 0)
        {
            Debug.LogWarning("[ParallaxGroup] 找不到任何 RuinedBackground 物件！");
            return;
        }

        _bgTransforms = new Transform[bgs.Length];
        _lockedPositions = new Vector3[bgs.Length];

        for (int i = 0; i < bgs.Length; i++)
        {
            _bgTransforms[i] = bgs[i].transform;
            _lockedPositions[i] = bgs[i].transform.position; // 記住初始世界座標
        }

        FindPlayer();
        _initialized = true;

        Debug.Log($"[ParallaxGroup] 已記住 {bgs.Length} 個 RuinedBackground 初始位置，靜止鎖定中。");
    }

    void FindPlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) playerObj = GameObject.Find("Player");
        if (playerObj != null)
        {
            _playerTransform = playerObj.transform;
            _playerMovement = playerObj.GetComponent<PlayerMovement>();
        }
    }

    void LateUpdate()
    {
        if (!_initialized || _bgTransforms == null) return;
        if (_playerTransform == null) FindPlayer();

        // ─────────────────────────────────────────────────────────────
        // 判斷玩家是否已踩到廢墟地板（isGrounded + Y <= 門檻）
        // ─────────────────────────────────────────────────────────────
        bool playerGrounded = (_playerMovement != null) ? _playerMovement.isGrounded : false;
        bool playerInRuinedY = (_playerTransform != null) && (_playerTransform.position.y <= ruinedZoneYThreshold);

        isPlayerInRuinedZone = playerGrounded && playerInRuinedY;

        // 一旦玩家踩到廢墟地板，啟動跟隨（之後不再關閉）
        if (isPlayerInRuinedZone && !isFollowActive)
        {
            isFollowActive = true;
            _basePlayerX = _playerTransform.position.x;
            _lastPlayerX = _basePlayerX;
            Debug.Log($"[ParallaxGroup] 玩家已踩到廢墟地板！啟動背景跟隨 + 漂移。基準 X = {_basePlayerX}");
        }

        if (!isFollowActive)
        {
            // ★ 靜止模式：硬鎖所有背景回初始世界座標
            for (int i = 0; i < _bgTransforms.Length; i++)
            {
                if (_bgTransforms[i] != null)
                    _bgTransforms[i].position = _lockedPositions[i];
            }
            return;
        }

        // ─────────────────────────────────────────────────────────────
        // 跟隨模式：整體視差跟隨主角 X + 持續 -X 漂移
        // ─────────────────────────────────────────────────────────────
        float playerX = (_playerTransform != null) ? _playerTransform.position.x : _lastPlayerX;
        float playerY = (_playerTransform != null) ? _playerTransform.position.y : 0f;

        // 本幀主角 X 位移量
        float playerDeltaX = playerX - _lastPlayerX;
        _lastPlayerX = playerX;

        // 本幀 Y 位移量（主角 Y 相對初始基準）
        float playerDeltaY = 0f; // 如果需要 Y 跟隨，可展開這部分

        // 時間漂移 offset（往 -X 緩慢移動）
        float driftX = driftSpeedX * Time.deltaTime;

        // 每幀 offset = 視差跟隨 + 漂移
        float totalOffsetX = (playerDeltaX * followFactorX) + driftX;
        float totalOffsetY = playerDeltaY * followFactorY;

        // 套用到所有背景（所有物件用完全相同的 offset → 相對位置永遠不變）
        for (int i = 0; i < _bgTransforms.Length; i++)
        {
            if (_bgTransforms[i] != null)
            {
                _lockedPositions[i].x += totalOffsetX;
                _lockedPositions[i].y += totalOffsetY;
                _bgTransforms[i].position = _lockedPositions[i];
            }
        }
    }
}
