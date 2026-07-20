using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(9999)] // 確保此腳本在 Cinemachine 算完位置之後才執行，強制蓋過它的設定！
public class SimpleCameraBounds : MonoBehaviour
{
    [Header("背景邊界設定")]
    [Tooltip("自動偵測這些標籤的 BoxCollider 作為邊界。只要給背景圖片加上 BoxCollider 跟標籤即可！")]
    public string[] backgroundTags = { "Background", "FallingBackground", "RuinedBackground" };

    [Tooltip("是否同時限制上下邊界？(勾選後攝影機也不會拍到背景上下方的穿幫處)")]
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
                        Collider col = bg.GetComponent<Collider>();
                        if (col != null)
                        {
                            // 關鍵修復 1：背景碰撞器必須設為 isTrigger = true，否則會當作實體牆壁擋住玩家前進！
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

        // 每 2 秒重新掃描一次背景，避免每幀搜尋造成卡頓
        _cacheTimer += Time.deltaTime;
        if (_cacheTimer > 2f)
        {
            _cacheTimer = 0f;
            CacheBackgrounds();
        }

        Collider closestBg = GetClosestBackgroundFromCache();
        if (closestBg == null) return;

        // 如果是 FallingBackground，攝影機強制跟隨玩家，不做邊界限制
        if (closestBg.CompareTag("FallingBackground")) 
        {
            return;
        }

        Bounds bgBounds = closestBg.bounds;

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

        float minX = bgBounds.min.x + halfWidth;
        float maxX = bgBounds.max.x - halfWidth;
        float minY = bgBounds.min.y + halfHeight;
        float maxY = bgBounds.max.y - halfHeight;

        // 防呆：如果背景比螢幕視野還小，鎖死在背景中心
        if (minX > maxX) minX = maxX = bgBounds.center.x;
        if (minY > maxY) minY = maxY = bgBounds.center.y;

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        
        if (clampYAxis)
        {
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
        }

        transform.position = pos;
    }

    private Collider GetClosestBackgroundFromCache()
    {
        if (_cachedBackgrounds == null || _cachedBackgrounds.Length == 0) return null;

        // 關鍵修復 2：計算距離必須使用【玩家的位置 (playerTransform)】而非【相機的位置 (transform.position)】！
        // 否則相機卡在舊背景邊緣後，會永遠無法切換到玩家踩入的新背景！
        Vector3 referencePos = (playerTransform != null) ? playerTransform.position : transform.position;

        Collider closest = null;
        float minDist = float.MaxValue;

        foreach (Collider col in _cachedBackgrounds)
        {
            if (col == null) continue;
            
            // 確保 isTrigger 為 true
            if (!col.isTrigger) col.isTrigger = true;

            Vector3 closestPt = col.bounds.ClosestPoint(referencePos);
            float dist = Vector3.Distance(referencePos, closestPt);
            
            if (dist < minDist)
            {
                minDist = dist;
                closest = col;
            }
        }
        return closest;
    }
}
