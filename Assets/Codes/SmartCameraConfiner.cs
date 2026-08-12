using UnityEngine;

/// <summary>
/// 聰明相機邊界限制器 (Smart Camera Confiner)
/// 掛載於 Main Camera 上。
/// 徹底解決攝影機超出背景圖片/邊界碰撞體導致的頂部/底部/左右破圖問題。
/// 支援動態 Orthographic Size 縮放微調，確保視野永遠被限制在背景圖範圍內。
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(99999)]
public class SmartCameraConfiner : MonoBehaviour
{
    [Header("目標背景 / 邊界 (拖入背景 SpriteRenderer 或隱形邊界 Collider)")]
    public SpriteRenderer boundarySprite;
    public Collider boundaryCollider;
    public string backgroundTag = "Background";

    [Header("邊界限制設定")]
    public bool clampX = true;
    public bool clampY = true;

    [Header("自動微調相機尺寸 (防止背景高度/寬度不足時露底破圖)")]
    public bool autoFitOrthographicSize = true;

    private Camera _cam;
    private Transform _playerTransform;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        FindPlayer();
    }

    void Start()
    {
        FindBoundaryIfMissing();
    }

    void FindPlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) playerObj = GameObject.Find("Player");
        if (playerObj != null) _playerTransform = playerObj.transform;
    }

    void FindBoundaryIfMissing()
    {
        if (boundarySprite == null && boundaryCollider == null)
        {
            // 嘗試自動在場景中尋找 Background 標籤物件
            try
            {
                GameObject bgObj = GameObject.FindWithTag(backgroundTag);
                if (bgObj != null)
                {
                    boundarySprite = bgObj.GetComponent<SpriteRenderer>();
                    boundaryCollider = bgObj.GetComponent<Collider>();
                }
            }
            catch { }
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

    public void ClampCamera()
    {
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;

        if (boundarySprite == null && boundaryCollider == null)
        {
            FindBoundaryIfMissing();
            if (boundarySprite == null && boundaryCollider == null) return;
        }

        // 取得邊界 Bounds
        Bounds bounds = new Bounds();
        if (boundaryCollider != null && boundaryCollider.enabled)
        {
            bounds = boundaryCollider.bounds;
        }
        else if (boundarySprite != null && boundarySprite.sprite != null)
        {
            bounds = boundarySprite.bounds;
        }
        else
        {
            return;
        }

        if (bounds.size.sqrMagnitude < 0.01f) return;

        // 1. 若開啟動態尺寸修剪，防止 Orthographic Size 大於背景範圍導致強行破圖
        if (_cam.orthographic && autoFitOrthographicSize)
        {
            float maxAllowedHalfHeight = bounds.size.y * 0.5f;
            float maxAllowedHalfWidth = (bounds.size.x * 0.5f) / _cam.aspect;
            float maxAllowedSize = Mathf.Min(maxAllowedHalfHeight, maxAllowedHalfWidth);

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

        // 3. X 軸 Clamp
        if (clampX)
        {
            float minX = bounds.min.x + halfWidth;
            float maxX = bounds.max.x - halfWidth;
            if (minX <= maxX)
            {
                clampedPos.x = Mathf.Clamp(camPos.x, minX, maxX);
            }
            else
            {
                clampedPos.x = bounds.center.x;
            }
        }

        // 4. Y 軸 Clamp（徹底防止頂部/底部露出背景外的底色）
        if (clampY)
        {
            float minY = bounds.min.y + halfHeight;
            float maxY = bounds.max.y - halfHeight;
            if (minY <= maxY)
            {
                clampedPos.y = Mathf.Clamp(camPos.y, minY, maxY);
            }
            else
            {
                clampedPos.y = bounds.center.y;
            }
        }

        transform.position = clampedPos;

        // 5. 同步相機目標物件 (若有 PlayerCameraTarget_SmoothY)
        GameObject cameraTargetObj = GameObject.Find("PlayerCameraTarget_SmoothY");
        if (cameraTargetObj != null && clampY)
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
