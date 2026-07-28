using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(9999)] // 確保此腳本在 Cinemachine 算完位置之後才執行
public class SimpleCameraBounds : MonoBehaviour
{
    [Header("背景邊界設定")]
    [Tooltip("自動偵測這些標籤的 BoxCollider 作為邊界 (僅套用於天空與全景背景，勿給地面套用此標籤)")]
    public string[] backgroundTags = { "Background", "FallingBackground", "RuinedBackground" };

    [Tooltip("是否同時限制上下邊界？(只有背景高度大於相機視野時才會觸發限制)")]
    public bool clampYAxis = true;

    private Camera cam;
    private Collider[] _cachedBackgrounds;
    private float _cacheTimer = 0f;
    private Transform playerTransform;

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
        if (playerObj != null) playerTransform = playerObj.transform;
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

    void LateUpdate()
    {
        if (playerTransform == null)
        {
            FindPlayer();
        }

        _cacheTimer += Time.deltaTime;
        if (_cacheTimer > 2f)
        {
            _cacheTimer = 0f;
            CacheBackgrounds();
        }

        if (_cachedBackgrounds == null || _cachedBackgrounds.Length == 0) return;

        // 合併全景背景 Bounds
        Bounds combinedBounds = _cachedBackgrounds[0].bounds;
        for (int i = 1; i < _cachedBackgrounds.Length; i++)
        {
            if (_cachedBackgrounds[i] != null)
            {
                combinedBounds.Encapsulate(_cachedBackgrounds[i].bounds);
            }
        }

        float halfHeight = 0f;
        float halfWidth = 0f;

        if (cam.orthographic)
        {
            halfHeight = cam.orthographicSize;
            halfWidth = halfHeight * cam.aspect;
        }
        else
        {
            float distance = Mathf.Abs(transform.position.z - combinedBounds.center.z);
            halfHeight = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            halfWidth = halfHeight * cam.aspect;
        }

        Vector3 pos = transform.position;
        Vector3 targetPos = pos;

        // 限制 X 軸
        float minX = combinedBounds.min.x + halfWidth;
        float maxX = combinedBounds.max.x - halfWidth;
        if (minX <= maxX)
        {
            targetPos.x = Mathf.Clamp(pos.x, minX, maxX);
        }

        // 限制 Y 軸
        if (clampYAxis && combinedBounds.size.y >= (halfHeight * 2f - 0.1f))
        {
            float minY = combinedBounds.min.y + halfHeight;
            float maxY = combinedBounds.max.y - halfHeight;
            if (minY <= maxY)
            {
                targetPos.y = Mathf.Clamp(pos.y, minY, maxY);
            }
            else
            {
                targetPos.y = combinedBounds.center.y;
            }
        }

        // 核心修正：徹底消滅站立時的微幅抖動/震盪 (Jittering)！
        // 只有當限制後的邊界與當前位置差異大於極小值時才套用，避免與 Cinemachine 每幀爭奪座標導致的微幅跳動！
        if (Mathf.Abs(pos.x - targetPos.x) > 0.001f || Mathf.Abs(pos.y - targetPos.y) > 0.001f)
        {
            transform.position = targetPos;
        }
    }
}
