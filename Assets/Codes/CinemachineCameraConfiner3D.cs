using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 攝影機防穿圖限制器 (Camera Bounds Confiner)
/// 掛載於 Main Camera 上。
/// 支援直接把多個 CameraBoundary Collider 拖入 Explicit Backgrounds 陣列。
///
/// 解決所有穿圖與卡住問題：
/// 1. 動態比對主角/相機目前位於哪一個 CameraBoundary 碰撞盒內（不會將多個框誤算為全區超大矩形）。
/// 2. 隨時精確限制 X 軸邊界，絕對不超出 CameraBoundary 範圍。
/// 3. 墜落空中 (Falling) 時放開 Y 軸限制，避免攝影機與主角被卡在外面；著地後自動過渡防露空。
/// 4. 同步修正 PlayerMovement 的 cameraTarget，解決 Cinemachine 反向拉扯穿圖的問題。
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(99999)]
public class CinemachineCameraConfiner3D : MonoBehaviour
{
    [Header("全局背景邊界控制器 (直接拖入你的 CameraBoundary 隱形框 Colliders)")]
    [Tooltip("把場景中所有 CameraBoundary 物件的 Collider 拖進這裡！")]
    public Collider[] explicitBackgrounds;

    [Header("備用自動尋找標籤")]
    public bool autoFindBackground = true;

    [Header("邊界鎖定設定")]
    [Tooltip("當某個 CameraBoundary 比螢幕還小時，是否自動把攝影機鎖在該框中心")]
    public bool lockToCenterIfTooSmall = true;

    private Camera _cam;
    private Collider[] _cachedColliders;
    private Transform _playerTransform;
    private PlayerMovement _playerMovement;

    void Start()
    {
        _cam = GetComponent<Camera>();
        FindPlayer();
        CacheColliders();
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

    public void CacheColliders()
    {
        List<Collider> cols = new List<Collider>();

        if (explicitBackgrounds != null)
        {
            foreach (var col in explicitBackgrounds)
            {
                if (col != null && !cols.Contains(col)) cols.Add(col);
            }
        }

        if (cols.Count == 0 && autoFindBackground)
        {
            string[] tags = { "Background", "RuinedBackground", "CameraBoundary" };
            foreach (string tag in tags)
            {
                try
                {
                    GameObject[] bgs = GameObject.FindGameObjectsWithTag(tag);
                    if (bgs != null)
                    {
                        foreach (var bg in bgs)
                        {
                            Collider col = bg.GetComponent<Collider>();
                            if (col != null && !cols.Contains(col)) cols.Add(col);
                        }
                    }
                }
                catch { }
            }
        }

        _cachedColliders = cols.ToArray();
        if (_cachedColliders.Length > 0)
        {
            Debug.Log($"[CinemachineCameraConfiner3D] ✅ 成功載入 {_cachedColliders.Length} 個 CameraBoundary 邊界 Collider！");
        }
    }

    void LateUpdate()
    {
        ClampCamera();
    }

    void OnPreRender()
    {
        ClampCamera();
    }

    void ClampCamera()
    {
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;
        if (_playerTransform == null) FindPlayer();

        if (_cachedColliders == null || _cachedColliders.Length == 0)
        {
            CacheColliders();
            if (_cachedColliders == null || _cachedColliders.Length == 0) return;
        }

        // 以主角位置（或相機位置）尋找當前作用的 CameraBoundary
        Vector3 targetPos = (_playerTransform != null) ? _playerTransform.position : transform.position;
        Collider activeCol = GetActiveCollider(targetPos);

        if (activeCol == null) return;

        Bounds bounds = activeCol.bounds;

        // 計算相機視野半寬高
        float halfHeight, halfWidth;
        if (_cam.orthographic)
        {
            halfHeight = _cam.orthographicSize;
            halfWidth = halfHeight * _cam.aspect;
        }
        else
        {
            float distance = Mathf.Abs(transform.position.z - bounds.center.z);
            if (distance < 0.1f) distance = 10f; // 安全防護
            halfHeight = distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            halfWidth = halfHeight * _cam.aspect;
        }

        bool isGrounded = (_playerMovement != null) ? _playerMovement.isGrounded : true;
        bool isFallingInAir = (_playerMovement != null) && _playerMovement.freezeHorizontal && !isGrounded;

        Vector3 camPos = transform.position;
        Vector3 clampedPos = camPos;

        // 1. X 軸邊界限制（防止左右穿圖）
        float minX = bounds.min.x + halfWidth;
        float maxX = bounds.max.x - halfWidth;
        if (minX <= maxX)
        {
            clampedPos.x = Mathf.Clamp(camPos.x, minX, maxX);
        }
        else if (lockToCenterIfTooSmall)
        {
            clampedPos.x = bounds.center.x;
        }

        // 2. Y 軸邊界限制（掉落空中時不夾 Y 軸，避免相機被卡住；著地時限制防露白）
        if (!isFallingInAir)
        {
            float minY = bounds.min.y + halfHeight;
            float maxY = bounds.max.y - halfHeight;

            if (minY <= maxY)
            {
                clampedPos.y = Mathf.Clamp(camPos.y, minY, maxY);
            }
            else if (lockToCenterIfTooSmall && camPos.y <= bounds.max.y)
            {
                clampedPos.y = bounds.center.y;
            }
        }

        transform.position = clampedPos;

        // 同步修改 PlayerCameraTarget_SmoothY，防止 Cinemachine 反向拉扯
        GameObject cameraTargetObj = GameObject.Find("PlayerCameraTarget_SmoothY");
        if (cameraTargetObj != null)
        {
            Vector3 targetObjPos = cameraTargetObj.transform.position;
            targetObjPos.x = clampedPos.x;
            if (!isFallingInAir) targetObjPos.y = clampedPos.y;
            cameraTargetObj.transform.position = targetObjPos;
        }
    }

    /// <summary>
    /// 找出涵蓋點或距離最近的 CameraBoundary Collider
    /// </summary>
    Collider GetActiveCollider(Vector3 point)
    {
        Collider closest = null;
        float minDistance = float.MaxValue;

        foreach (var col in _cachedColliders)
        {
            if (col == null) continue;
            Bounds b = col.bounds;

            // 優先：點直接位於該 Bounds 內
            if (b.Contains(point))
            {
                return col;
            }

            // 備選：計算與點最近的 Bounds
            float dist = Vector3.Distance(point, b.ClosestPoint(point));
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = col;
            }
        }

        return closest;
    }
}
