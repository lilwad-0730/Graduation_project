using UnityEngine;

/// <summary>
/// 山景區專屬鏡頭垂直跟隨特例 (Ruin_Background_Mountain 這種特別高的背景)。
///
/// 背景：整個關卡的鏡頭是由 CameraTargetXFollower 驅動的 Mario 橫向捲軸模式——
/// 它把 CameraFollowTarget 的 Y 鎖死在每一層寫死的固定高度 (ruinedZoneFixedY 等)，
/// 只有 X 會跟著玩家跑，所以鏡頭本來就完全不會上下移動。
///
/// 做法：玩家進入本區域時，呼叫 CameraTargetXFollower.EnableVerticalFollow()，
/// 讓 Y 軸改成跟隨玩家並夾在本區指定的上下邊界內；離開時關閉，鏡頭平滑滑回原本的固定高度。
/// 完全不影響其他區域的鏡頭行為。
/// </summary>
[RequireComponent(typeof(Collider))]
public class MountainCameraZone : MonoBehaviour
{
    [Header("垂直跟隨邊界 (世界座標 Y，請對照 Ruin_Background_Mountain 實際美術範圍調整)")]
    [Tooltip("鏡頭允許上升到的最高 Y 座標（背景圖頂端）")]
    public float maxY = -40f;

    [Tooltip("鏡頭允許下降到的最低 Y 座標（背景圖底部）")]
    public float minY = -140f;

    [Header("跟隨手感")]
    [Tooltip("Y 軸跟隨玩家的平滑速度 (越大越即時，越小越黏。預設 8)")]
    public float followSpeed = 8f;

    [Tooltip("鏡頭相對玩家的 Y 軸偏移 (正數鏡頭偏上，負數偏下)")]
    public float yOffset = 0f;

    [Header("邊界自動取得 (選填，優先於上面手動填的數字)")]
    [Tooltip("直接把 Ruin_Background_Mountain 的 Renderer 拖進來，上下邊界會自動用它的實際圖片範圍，不用自己填數字")]
    public Renderer boundaryBackground;

    private bool _playerInside = false;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private bool IsPlayer(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("Player")) return true;
        return go.GetComponentInParent<PlayerMovement>() != null;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other.gameObject) || _playerInside) return;

        _playerInside = true;
        ApplyVerticalFollow();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other.gameObject) || !_playerInside) return;

        _playerInside = false;
        CameraTargetXFollower.DisableVerticalFollow();
    }

    private void Update()
    {
        // 背景若會被 Parallax 移動，邊界每幀重新套用一次，確保永遠貼合當下的背景實際位置
        if (_playerInside && boundaryBackground != null) ApplyVerticalFollow();
    }

    private void ApplyVerticalFollow()
    {
        float lo = minY;
        float hi = maxY;

        if (boundaryBackground != null)
        {
            Bounds b = boundaryBackground.bounds;
            lo = b.min.y;
            hi = b.max.y;
        }

        CameraTargetXFollower.EnableVerticalFollow(lo, hi, followSpeed, yOffset);
    }

    private void OnDisable()
    {
        if (_playerInside)
        {
            _playerInside = false;
            CameraTargetXFollower.DisableVerticalFollow();
        }
    }
}
