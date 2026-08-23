using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 攝影機相向邊界碰撞器 (Direct Camera Viewport Boundary Confiner)
/// 掛載於 Main Camera 上。
/// 針對 Main Camera 的 4 個視野邊緣 (上、下、左、右) 進行實體碰撞攔截：
/// 當相機視野邊緣觸碰到 CameraBoundary 碰撞盒時，會被當作實體牆壁擋住，絕對無法跨過去！
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(99999)]
public class CinemachineCameraConfiner3D : MonoBehaviour
{
    [Header("邊界 碰撞體 陣列 (可直接拖入 CameraBoundary，未拖入則全自動搜尋)")]
    public Collider[] explicitBackgrounds;

    [Header("過濾標籤名稱")]
    public string[] boundaryTags = { "CameraBoundary", "Background", "RuinedBackground", "FallingBackground" };

    [Header("相機視野碰撞鎖定")]
    public bool collideX = true;
    public bool collideY = true;
    public bool autoScaleIfBoundaryTooSmall = true;

    private Camera _cam;
    private Transform _playerTransform;
    private Collider[] _cachedBoundaries;

    void OnEnable()
    {
        Unity.Cinemachine.CinemachineCore.CameraUpdatedEvent.AddListener(OnCinemachineUpdated);
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void OnDisable()
    {
        Unity.Cinemachine.CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCinemachineUpdated);
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    void OnBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (camera == _cam) ClampCameraToBoundary();
    }

    void OnCinemachineUpdated(Unity.Cinemachine.CinemachineBrain brain)
    {
        if (brain != null && _cam != null && brain.OutputCamera == _cam) ClampCameraToBoundary();
    }

    void Start()
    {
        _cam = GetComponent<Camera>();
        FindPlayer();
        CacheBoundaries();
    }

    void FindPlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) playerObj = GameObject.Find("Player");
        if (playerObj != null) _playerTransform = playerObj.transform;
    }

    public void CacheBoundaries()
    {
        List<Collider> list = new List<Collider>();

        // 1. 優先使用手動拖入的 CameraBoundary
        if (explicitBackgrounds != null)
        {
            foreach (var col in explicitBackgrounds)
            {
                if (col != null && col.enabled && !list.Contains(col)) list.Add(col);
            }
        }

        // 2. 自動搜尋帶有 CameraBoundary 名稱或指定 Tag 的碰撞體
        Collider[] allCols = GameObject.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        if (allCols != null)
        {
            foreach (var c in allCols)
            {
                if (c == null || !c.enabled || list.Contains(c)) continue;
                string n = c.gameObject.name;
                string t = c.gameObject.tag;

                bool match = false;
                if (n.Contains("CameraBoundary") || n.Contains("Boundary")) match = true;
                else
                {
                    foreach (string bt in boundaryTags)
                    {
                        if (t.Equals(bt, System.StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                    }
                }

                if (match && c.bounds.size.sqrMagnitude > 0.1f)
                {
                    list.Add(c);
                }
            }
        }

        _cachedBoundaries = list.ToArray();
    }

    void LateUpdate()
    {
        ClampCameraToBoundary();
    }

    void OnPreRender()
    {
        ClampCameraToBoundary();
    }

    /// <summary>
    /// 找出包含玩家座標或離玩家最近的 CameraBoundary Collider (2.5D 深度自適應)
    /// </summary>
    Collider GetActiveBoundary(Vector3 point)
    {
        if (_cachedBoundaries == null || _cachedBoundaries.Length == 0) CacheBoundaries();
        if (_cachedBoundaries == null || _cachedBoundaries.Length == 0) return null;

        Collider closest = null;
        float minDist = float.MaxValue;

        foreach (var col in _cachedBoundaries)
        {
            if (col == null || !col.enabled) continue;
            Bounds b = col.bounds;

            // 2.5D 判定：忽略前後 Z 軸公差，只要主角落在該邊界箱的 X/Y 範圍內即視為身處該區域！
            bool isInside2D = point.x >= b.min.x && point.x <= b.max.x && point.y >= b.min.y && point.y <= b.max.y;
            if (isInside2D) return col;

            // 計算 2D 歐幾里得距離
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - point.x, point.x - b.max.x));
            float dy = Mathf.Max(0f, Mathf.Max(b.min.y - point.y, point.y - b.max.y));
            float d = Mathf.Sqrt(dx * dx + dy * dy);

            if (d < minDist)
            {
                minDist = d;
                closest = col;
            }
        }

        return closest;
    }

    void ClampCameraToBoundary()
    {
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;
        if (_playerTransform == null) FindPlayer();

        Vector3 targetPos = (_playerTransform != null) ? _playerTransform.position : transform.position;
        Collider activeCol = GetActiveBoundary(targetPos);
        if (activeCol == null) return;

        Bounds bounds = activeCol.bounds;

        // 1. 若 CameraBoundary 碰撞盒尺寸小於視角，自動適應 Orthographic Size 防止露底
        if (_cam.orthographic && autoScaleIfBoundaryTooSmall)
        {
            float maxHalfH = bounds.size.y * 0.5f;
            float maxHalfW = (bounds.size.x * 0.5f) / _cam.aspect;
            float maxAllowedSize = Mathf.Min(maxHalfH, maxHalfW);

            if (maxAllowedSize > 0.5f && _cam.orthographicSize > maxAllowedSize)
            {
                _cam.orthographicSize = maxAllowedSize;
            }
        }

        // 2. 計算相機視野半寬高
        float halfHeight = _cam.orthographic ? _cam.orthographicSize : 0f;
        float halfWidth = halfHeight * _cam.aspect;

        Vector3 camPos = transform.position;
        Vector3 clampedPos = camPos;

        // 3. 【實體碰撞攔截】若主角處於邊界範圍內，視野 4 個邊緣硬性擋住；若主角在過渡下墜區，允許鏡頭向下跟隨
        if (collideX)
        {
            float minX = bounds.min.x + halfWidth;
            float maxX = bounds.max.x - halfWidth;
            if (minX <= maxX) clampedPos.x = Mathf.Clamp(camPos.x, minX, maxX);
            else clampedPos.x = bounds.center.x;
        }

        if (collideY)
        {
            float minY = bounds.min.y + halfHeight;
            float maxY = bounds.max.y - halfHeight;
            if (minY <= maxY)
            {
                // 如果主角已經低於該邊界的底部 (正在向下方場景墜落)，允許相機追蹤主角下移，不再死卡在上方頂部
                if (targetPos.y < bounds.min.y)
                {
                    clampedPos.y = Mathf.Min(camPos.y, targetPos.y);
                }
                else
                {
                    clampedPos.y = Mathf.Clamp(camPos.y, minY, maxY);
                }
            }
            else
            {
                clampedPos.y = bounds.center.y;
            }
        }

        // 寫回 Main Camera 座標
        transform.position = clampedPos;

        // 4. 強制同步 Cinemachine 虛擬攝影機與 PlayerCameraTarget
        var activeVcam = Unity.Cinemachine.CinemachineCore.GetVirtualCamera(0);
        if (activeVcam != null)
        {
            Component vcamComp = activeVcam as Component;
            if (vcamComp != null)
            {
                Vector3 vcamPos = vcamComp.transform.position;
                if (collideX) vcamPos.x = clampedPos.x;
                if (collideY) vcamPos.y = clampedPos.y;
                vcamComp.transform.position = vcamPos;
            }
        }

        GameObject cameraTargetObj = GameObject.Find("PlayerCameraTarget_SmoothY");
        if (cameraTargetObj != null && collideY)
        {
            Vector3 targetObjPos = cameraTargetObj.transform.position;
            float minY = bounds.min.y + halfHeight;
            float maxY = bounds.max.y - halfHeight;
            if (minY <= maxY) targetObjPos.y = Mathf.Clamp(targetObjPos.y, minY, maxY);
            else targetObjPos.y = bounds.center.y;
            cameraTargetObj.transform.position = targetObjPos;
        }
    }
}
