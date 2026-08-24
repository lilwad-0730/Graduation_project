using UnityEngine;

/// <summary>
/// 龍捲風相機跟隨與左右橫掃組件 (Tornado Follow Camera & Horizontal Sweep)
/// 掛載於背景龍捲風視覺物件 (如 TornadoWithWindEfc 或 ToonTornadoWithWindEfc) 上：
/// 1. 【左右橫掃擺動 (Sweep)】：支援獨立開關，左右來回自然擺動橫掃，營造強大的風暴壓迫動態！
/// 2. 【相機跟隨 (Camera Follow)】：進入轉場區域時自動鎖定跟隨相機水平 X 軸。
/// 3. 【純背景視覺】：固定景深 Z 軸，絕不影響前景玩家物理與碰撞。
/// </summary>
public class TornadoFollowCamera : MonoBehaviour
{
    [Header("🌪️ 左右橫掃巡邏 (Horizontal Sweep)")]
    [Tooltip("是否開啟左右橫掃巡邏擺動 (打勾後龍捲風會左右來回橫掃，增強風暴動態感)")]
    public bool enableSweep = true;

    [Tooltip("左右橫掃的單側擺動距離 (例如 4 代表往左 4 米、往右 4 米來回橫掃)")]
    [Range(0f, 20f)]
    public float sweepDistance = 4.5f;

    [Tooltip("左右橫掃擺動速度 (數值越大來回越快，建議 1.0 ~ 2.0)")]
    [Range(0.2f, 5f)]
    public float sweepSpeed = 1.3f;

    [Header("📷 相機跟隨控制")]
    [Tooltip("是否一開始就跟隨相機 (若為 false，平時在原地橫掃，進入轉場區時才被轉場腳本啟動相機跟隨)")]
    public bool autoFollowOnStart = false;

    [Tooltip("是否跟隨相機水平 X 軸移動")]
    public bool followX = true;

    [Tooltip("相對於相機的水平偏移量 (例如 0 代表置中)")]
    public float offsetX = 0f;

    [Tooltip("固定景深 Z 座標 (建議 8 ~ 15，保證在背景層絕不干擾前景玩家)")]
    public float fixedZ = 10f;

    [Header("🌊 視差平滑度")]
    [Range(0f, 15f)]
    public float smoothSpeed = 6f;

    private bool _isFollowing = false;
    private float _startWorldX;
    private Camera _mainCam;

    private void Start()
    {
        _startWorldX = transform.position.x;
        _isFollowing = autoFollowOnStart;
    }

    public void ActivateFollow()
    {
        _isFollowing = true;
    }

    public void StopFollow()
    {
        _isFollowing = false;
    }

    private void LateUpdate()
    {
        float sweepOffset = enableSweep ? Mathf.Sin(Time.time * sweepSpeed) * sweepDistance : 0f;

        if (_mainCam == null) _mainCam = Camera.main ?? Object.FindFirstObjectByType<Camera>();

        Vector3 targetPos = transform.position;

        if (_isFollowing && _mainCam != null)
        {
            Vector3 camPos = _mainCam.transform.position;

            if (followX)
            {
                float targetX = camPos.x + offsetX + sweepOffset;
                if (smoothSpeed > 0.01f && Application.isPlaying)
                {
                    targetPos.x = Mathf.Lerp(targetPos.x, targetX, Time.deltaTime * smoothSpeed);
                }
                else
                {
                    targetPos.x = targetX;
                }
            }
        }
        else
        {
            // 尚未啟動相機跟隨時，在原地基準點進行左右橫掃
            if (enableSweep)
            {
                targetPos.x = _startWorldX + sweepOffset;
            }
        }

        targetPos.z = fixedZ;
        transform.position = targetPos;
    }
}
