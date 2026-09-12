using UnityEngine;

/// <summary>
/// 龍捲風相機跟隨與左右橫移擺動組件 (Tornado Follow Camera & Horizontal Sweep)
/// 1. 【前景層級 (Foreground)】：預設景深 Z = -4，位於玩家 (Z=0) 與相機 (Z=-10) 之間，在畫面最前方呼嘯捲動。
/// 2. 【左右橫移 (Horizontal Sweep)】：平時在原地左右橫移巡邏；被觸發後以畫面中央為中心持續左右橫移擺動。
/// 3. 【玩家觸碰/接近觸發】：當玩家觸碰到龍捲風或靠近時，中心自動鎖定追蹤畫面中央。
/// 4. 【顏色自訂 (Color Tuning)】：Inspector 支援任意顏色調整，預設為淺灰色 (Light Grey)。
/// </summary>
public class TornadoFollowCamera : MonoBehaviour
{
    [Header("🌪️ 左右橫移擺動 (Horizontal Sweep)")]
    [Tooltip("是否開啟左右橫移擺動 (打勾後龍捲風會左右來回擺動，增強風暴動態感)")]
    public bool enableSweep = true;

    [Tooltip("左右橫移的單側擺動距離 (例如 4.5 代表往左 4.5 米、往右 4.5 米來回擺動)")]
    [Range(0f, 20f)]
    public float sweepDistance = 4.5f;

    [Tooltip("左右橫移擺動速度 (數值越大來回越快，建議 1.0 ~ 2.0)")]
    [Range(0.2f, 5f)]
    public float sweepSpeed = 1.3f;

    [Header("📷 畫面中央鎖定與相機跟隨 (Screen Center Tracking)")]
    [Tooltip("是否一開始就跟隨相機 (若為 false，平時在原地橫移，被觸碰後才鎖定畫面中央)")]
    public bool autoFollowOnStart = false;

    [Tooltip("是否跟隨相機水平 X 軸移動")]
    public bool followX = true;

    [Tooltip("相對於畫面中央的水平偏移量 (0 代表完全置中)")]
    public float offsetX = 0f;

    [Tooltip("是否跟隨相機垂直 Y 軸移動 (打勾可讓龍捲風始終保持在畫面可見中央)")]
    public bool followY = true;

    [Tooltip("相對於畫面中央的垂直偏移量 (預設 -11，因龍捲風錨點在底部，負值讓底部剛好對齊畫面底邊，狂風貫穿整個畫面)")]
    public float offsetY = -11f;

    [Tooltip("景深 Z 座標 (預設 -4，保證在前景最前方渲染，遮擋玩家與地形營造強大吞噬感)")]
    public float fixedZ = -4f;

    [Header("🌊 追蹤平滑度")]
    [Range(0.5f, 20f)]
    public float smoothSpeed = 6f;

    [Header("🎯 玩家觸碰/接近觸發 (Trigger on Player)")]
    [Tooltip("是否在玩家進入觸發器或接近時自動啟動畫面中心追蹤")]
    public bool triggerOnPlayer = true;

    [Tooltip("接近觸發距離 (米，若未設置實體 Collider 則靠距離自動觸發)")]
    public float triggerDistance = 6.0f;

    [Header("🎨 龍捲風顏色調整 (Color Tuning)")]
    [Tooltip("龍捲風顏色 (可在 Inspector 自由調整，預設為淺灰冷風色)")]
    public Color tornadoColor = new Color(0.82f, 0.85f, 0.88f, 0.65f);

    private bool _isFollowing = false;
    private float _startWorldX;
    private float _startWorldY;
    private Camera _mainCam;
    private Transform _playerTransform;
    private AudioSource _ambientAudioSource;
    private float _ambientBaseVolume = 1f;
    private Color _lastTornadoColor;

    private void Awake()
    {
        _startWorldX = transform.position.x;
        _startWorldY = transform.position.y;
        _lastTornadoColor = tornadoColor;
    }

    private void Start()
    {
        _isFollowing = autoFollowOnStart;
        _ambientAudioSource = GetComponent<AudioSource>();
        if (_ambientAudioSource != null)
            _ambientBaseVolume = _ambientAudioSource.volume;

        FindPlayer();
        ApplyColor();
    }

    private void FindPlayer()
    {
        if (_playerTransform != null) return;
        var pm = Object.FindFirstObjectByType<PlayerMovement>();
        if (pm != null)
        {
            _playerTransform = pm.transform;
            return;
        }

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _playerTransform = p.transform;
    }

    /// <summary>
    /// 外部或轉場腳本手動啟動跟隨
    /// </summary>
    public void ActivateFollow()
    {
        if (_isFollowing) return;
        _isFollowing = true;
        Debug.Log($"🌪️【龍捲風】啟動畫面中央鎖定追蹤 (Z={fixedZ})！", this);
    }

    /// <summary>
    /// 停止相機跟隨
    /// </summary>
    public void StopFollow()
    {
        _isFollowing = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckAndTriggerPlayer(other.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        CheckAndTriggerPlayer(other.gameObject);
    }

    private void CheckAndTriggerPlayer(GameObject obj)
    {
        if (!triggerOnPlayer || _isFollowing || obj == null) return;

        if (obj.CompareTag("Player") || obj.name.Contains("Player") || obj.GetComponent<PlayerMovement>() != null)
        {
            ActivateFollow();
        }
    }

    private void LateUpdate()
    {
        // 1. 音量適配
        if (_ambientAudioSource != null)
            _ambientAudioSource.volume = AudioManager.ScaleSfx(_ambientBaseVolume);

        // 2. 玩家距離接近自動觸發
        if (triggerOnPlayer && !_isFollowing)
        {
            if (_playerTransform == null) FindPlayer();
            if (_playerTransform != null)
            {
                float diffX = Mathf.Abs(_playerTransform.position.x - transform.position.x);
                float diffY = Mathf.Abs(_playerTransform.position.y - transform.position.y);
                // 水平接近 triggerDistance 且垂直差在 20 米內即觸發 (完美適應巨木斜坡的高度)
                if (diffX <= triggerDistance && diffY <= 20f)
                {
                    ActivateFollow();
                }
            }
        }

        // 3. 顏色即時變更檢查 (方便在 Inspector 隨時調色預覽)
        if (tornadoColor != _lastTornadoColor)
        {
            ApplyColor();
        }

        // 4. 計算左右橫移擺動偏移量
        float sweepOffset = enableSweep ? Mathf.Sin(Time.time * sweepSpeed) * sweepDistance : 0f;

        if (_mainCam == null) _mainCam = Camera.main ?? Object.FindFirstObjectByType<Camera>();

        Vector3 currentPos = transform.position;
        Vector3 targetPos = currentPos;

        if (_isFollowing && _mainCam != null)
        {
            Vector3 camPos = _mainCam.transform.position;

            // X 軸：畫面中央 + 左右橫移
            if (followX)
            {
                float targetX = camPos.x + offsetX + sweepOffset;
                targetPos.x = Mathf.Lerp(currentPos.x, targetX, Time.deltaTime * smoothSpeed);
            }

            // Y 軸：畫面中央
            if (followY)
            {
                float targetY = camPos.y + offsetY;
                targetPos.y = Mathf.Lerp(currentPos.y, targetY, Time.deltaTime * smoothSpeed);
            }
        }
        else
        {
            // 尚未觸發跟隨時：在原地基準點進行左右橫移巡邏
            if (enableSweep)
            {
                targetPos.x = _startWorldX + sweepOffset;
            }
            targetPos.y = _startWorldY;
        }

        // 強制固定在最前端前景層 (Z 軸)
        targetPos.z = fixedZ;
        transform.position = targetPos;
    }

    /// <summary>
    /// 套用自訂顏色到所有子粒子特效與材質
    /// </summary>
    public void ApplyColor()
    {
        _lastTornadoColor = tornadoColor;

        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;

            if (Application.isPlaying)
            {
                foreach (var mat in r.materials)
                {
                    if (mat != null && mat.HasProperty("_Color"))
                    {
                        mat.SetColor("_Color", tornadoColor);
                    }
                }
            }
            else
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat != null && mat.HasProperty("_Color"))
                    {
                        mat.SetColor("_Color", tornadoColor);
                    }
                }
            }
        }
    }

    private void OnValidate()
    {
        if (tornadoColor != _lastTornadoColor)
        {
            ApplyColor();
        }
    }

    private void OnDrawGizmos()
    {
        Vector3 basePos = Application.isPlaying ? new Vector3(_startWorldX, _startWorldY, transform.position.z) : transform.position;

        // 1. 繪製平時左右橫移巡邏範圍 (黃線)
        if (enableSweep)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawLine(basePos + Vector3.left * sweepDistance, basePos + Vector3.right * sweepDistance);
            Gizmos.DrawWireSphere(basePos + Vector3.left * sweepDistance, 0.6f);
            Gizmos.DrawWireSphere(basePos + Vector3.right * sweepDistance, 0.6f);
        }

        // 2. 繪製玩家碰觸/接近觸發半徑 (青色半透明球)
        if (triggerOnPlayer)
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.25f);
            Gizmos.DrawSphere(transform.position, triggerDistance);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, triggerDistance);
        }

        #if UNITY_EDITOR
        string statusText = _isFollowing ? "🌪️ 龍捲風【已鎖定相機鏡頭】" : $"🌪️ 龍捲風 (平時巡邏橫移 | 碰觸半徑: {triggerDistance}m)";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 4f, statusText);
        #endif
    }
}
