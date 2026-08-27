using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 攝影機相向邊界碰撞器 (Direct Camera Viewport Boundary Confiner)
/// 掛載於 Main Camera 上。
/// 支援單一大背景包圍盒 或 4 面獨立邊界牆 (Left/Right/Top/Bottom CameraBoundary Walls)：
/// 1. 視野 4 個邊緣觸碰到邊界時硬性擋住，絕不露底
/// 2. 絕不因單面窄牆而把相機死鎖在牆壁中心點
/// 3. 當主角在城堡上層移動時，相機 100% 順暢跟隨！
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
    public bool autoScaleIfBoundaryTooSmall = false;

    public static bool isBypassed = false; // 過場演出時旁路邊界限制
    public static Transform customTarget = null; // 自訂目標 (例如追蹤巨石時防穿幫邊界計算)

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

    void ClampCameraToBoundary()
    {
        if (isBypassed || !enabled) return;
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;
        if (_playerTransform == null) FindPlayer();

        if (_cachedBoundaries == null || _cachedBoundaries.Length == 0) CacheBoundaries();
        if (_cachedBoundaries == null || _cachedBoundaries.Length == 0) return;

        Vector3 targetPos = (customTarget != null) ? customTarget.position : ((_playerTransform != null) ? _playerTransform.position : transform.position);

        // 計算相機視野半寬高
        float halfHeight = _cam.orthographic ? _cam.orthographicSize : 7f;
        float halfWidth = halfHeight * _cam.aspect;

        Vector3 camPos = transform.position;
        Vector3 clampedPos = camPos;

        float minX = float.MinValue;
        float maxX = float.MaxValue;
        float minY = float.MinValue;
        float maxY = float.MaxValue;

        // 智能計算環繞在玩家周遭的所有邊界牆或大包圍盒
        foreach (var col in _cachedBoundaries)
        {
            if (col == null || !col.enabled) continue;
            Bounds b = col.bounds;

            // 判斷是否為「大區域背景包圍盒」（能完全容納鏡頭視野）
            if (b.size.x >= halfWidth * 2f && b.size.y >= halfHeight * 2f)
            {
                // 若玩家身在此大背景內，限制不能看穿大背景邊界
                if (targetPos.x >= b.min.x - 2f && targetPos.x <= b.max.x + 2f &&
                    targetPos.y >= b.min.y - 2f && targetPos.y <= b.max.y + 2f)
                {
                    minX = Mathf.Max(minX, b.min.x + halfWidth);
                    maxX = Mathf.Min(maxX, b.max.x - halfWidth);
                    minY = Mathf.Max(minY, b.min.y + halfHeight);
                    maxY = Mathf.Min(maxY, b.max.y - halfHeight);
                }
            }
            else
            {
                // 判斷為「獨立單面邊界牆」 (如左牆、右牆、天花板、地面)
                // 檢查該牆面是否在玩家視野的高度/寬度範圍內
                bool inYRange = (targetPos.y >= b.min.y - halfHeight && targetPos.y <= b.max.y + halfHeight);
                bool inXRange = (targetPos.x >= b.min.x - halfWidth && targetPos.x <= b.max.x + halfWidth);

                if (inYRange)
                {
                    // 左側邊界牆：相機左邊緣不能穿透該牆的右側
                    if (b.max.x <= targetPos.x + 2f)
                    {
                        minX = Mathf.Max(minX, b.max.x + halfWidth);
                    }
                    // 右側邊界牆：相機右邊緣不能穿透該牆的左側
                    if (b.min.x >= targetPos.x - 2f)
                    {
                        maxX = Mathf.Min(maxX, b.min.x - halfWidth);
                    }
                }

                if (inXRange)
                {
                    // 下方邊界牆/地板：相機下邊緣不能穿透該牆的頂部
                    if (b.max.y <= targetPos.y + 2f)
                    {
                        minY = Mathf.Max(minY, b.max.y + halfHeight);
                    }
                    // 上方天花板：相機上邊緣不能穿透該牆的底部
                    if (b.min.y >= targetPos.y - 2f)
                    {
                        maxY = Mathf.Min(maxY, b.min.y - halfHeight);
                    }
                }
            }
        }

        // 3. 【限制相機座標，絕不強制死鎖】
        if (collideX)
        {
            if (minX <= maxX)
            {
                clampedPos.x = Mathf.Clamp(camPos.x, minX, maxX);
            }
            // 若邊界矛盾 (牆壁距離小於相機寬度)，不鎖死，直接跟隨目標 X
        }

        if (collideY)
        {
            if (minY <= maxY)
            {
                clampedPos.y = Mathf.Clamp(camPos.y, minY, maxY);
            }
        }

        // 寫回 Main Camera 座標
        transform.position = clampedPos;

        // 4. 同步更新 Cinemachine 虛擬相機位置
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
    }
}
