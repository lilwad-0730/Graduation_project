using UnityEngine;
using System.Collections.Generic;
using Unity.Cinemachine;

[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(9999)] // 確保此腳本在 Cinemachine 算完位置之後才執行
public class SimpleCameraBounds : MonoBehaviour
{
    [Header("背景邊界設定")]
    [Tooltip("自動偵測這些標籤的 BoxCollider 作為邊界")]
    public string[] backgroundTags = { "Background", "FallingBackground", "RuinedBackground" };

    [Tooltip("是否同時限制上下邊界？")]
    public bool clampYAxis = true;

    [Tooltip("背景比視野小時，是否鎖定到中心？")]
    public bool lockToCenterIfTooSmall = true;

    private Camera cam;
    private Collider[] _cachedBackgrounds;
    private float _cacheTimer = 0f;
    private Transform playerTransform;
    private PlayerMovement _playerMovement;

    void Start()
    {
        cam = GetComponent<Camera>();
        FindPlayer();
        CacheBackgrounds();
    }

    void FindPlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) playerObj = GameObject.Find("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            _playerMovement = playerObj.GetComponent<PlayerMovement>();
        }
    }

    void CacheBackgrounds()
    {
        List<Collider> colliders = new List<Collider>();
        foreach (string tag in backgroundTags)
        {
            try
            {
                GameObject[] bgs = GameObject.FindGameObjectsWithTag(tag);
                if (bgs != null)
                {
                    foreach (GameObject bg in bgs)
                    {
                        if (bg.name.ToLower().Contains("ground")) continue;
                        Collider col = bg.GetComponent<Collider>();
                        if (col != null)
                        {
                            col.isTrigger = true;
                            colliders.Add(col);
                        }
                    }
                }
            }
            catch {}
        }
        _cachedBackgrounds = colliders.ToArray();
    }

    // ★★★ 找到玩家「當前所在」的背景（僅在玩家落地時才啟用邊界）
    private Collider FindActiveBoundary()
    {
        if (playerTransform == null) return null;
        if (_cachedBackgrounds == null || _cachedBackgrounds.Length == 0) return null;

        // 只有玩家在地面上才啟用邊界，空中時攝影機完全自由跟隨
        bool isGrounded = (_playerMovement != null) ? _playerMovement.isGrounded : false;
        if (!isGrounded) return null;

        Vector3 pos = playerTransform.position;
        Collider bestCol = null;
        float bestDist = float.MaxValue;

        foreach (var col in _cachedBackgrounds)
        {
            if (col == null) continue;
            Bounds b = col.bounds;

            bool insideX = pos.x >= b.min.x && pos.x <= b.max.x;
            bool insideY = pos.y >= b.min.y && pos.y <= b.max.y;

            if (insideX && insideY)
            {
                float dist = Vector3.Distance(pos, b.center);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestCol = col;
                }
            }
        }
        return bestCol;
    }

    void LateUpdate()
    {
        if (playerTransform == null) FindPlayer();

        _cacheTimer += Time.deltaTime;
        if (_cacheTimer > 2f)
        {
            _cacheTimer = 0f;
            CacheBackgrounds();
        }

        // ★★★ 核心修正：只用玩家當前所在的背景作為邊界，不合併全部背景！
        Collider activeBg = FindActiveBoundary();
        if (activeBg == null) return; // 玩家在空中 or 不在任何背景內 → 攝影機完全自由

        // FallingBackground 不做邊界限制
        if (activeBg.CompareTag("FallingBackground")) return;

        Bounds bgBounds = activeBg.bounds;

        float halfHeight = 0f;
        float halfWidth = 0f;

        if (cam.orthographic)
        {
            halfHeight = cam.orthographicSize;
            halfWidth = halfHeight * cam.aspect;
        }
        else
        {
            float distance = Mathf.Abs(transform.position.z - bgBounds.center.z);
            halfHeight = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            halfWidth = halfHeight * cam.aspect;
        }

        Vector3 pos = transform.position;
        Vector3 targetPos = pos;

        // 限制 X 軸
        float minX = bgBounds.min.x + halfWidth;
        float maxX = bgBounds.max.x - halfWidth;
        if (minX <= maxX)
        {
            targetPos.x = Mathf.Clamp(pos.x, minX, maxX);
        }
        else if (lockToCenterIfTooSmall)
        {
            targetPos.x = bgBounds.center.x;
        }

        // 限制 Y 軸（只有背景高度足夠大時才限制）
        if (clampYAxis)
        {
            float minY = bgBounds.min.y + halfHeight;
            float maxY = bgBounds.max.y - halfHeight;
            if (minY <= maxY)
            {
                targetPos.y = Mathf.Clamp(pos.y, minY, maxY);
            }
            else if (lockToCenterIfTooSmall)
            {
                targetPos.y = bgBounds.center.y;
            }
        }

        // 套用邊界修正
        if (Mathf.Abs(pos.x - targetPos.x) > 0.001f || Mathf.Abs(pos.y - targetPos.y) > 0.001f)
        {
            transform.position = targetPos;
        }
    }
}
