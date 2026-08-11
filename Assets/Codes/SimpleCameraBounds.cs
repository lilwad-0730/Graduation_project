using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 業界標準：區域分層與視場邊界動態夾緊相機 Confiner (Zone-Aware Viewport Clamping)
/// 直接掛在 Main Camera 上，不依賴 Cinemachine Extension，永遠有效。
///
/// ★ 天空區（Y > 廢墟門檻）：使用天空背景 Bounds 限制
/// ★ 廢墟區（Y <= 廢墟門檻）：
///    - 優先使用 explicitRuinedBoundsColliders（直接拖入 CameraBoundary 隱形框）
///    - 若沒有設定，自動用 RuinedBackground + Floor Collider 合并算 Bounds
/// ★ 掉落途中（isFallingInAir）：Y 軸完全不限制，攝影機 100% 自由跟隨主角
/// ★ 落地後才啟動廢墟邊界限制（_playerLandedInRuinedZone = true 後永不關閉）
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(9999)]
public class SimpleCameraBounds : MonoBehaviour
{
    [Header("背景與地板標籤設定")]
    public string[] backgroundTags = { "Background", "RuinedBackground", "Floor", "FallingBackground" };

    [Header("廢墟區邊界 — 直接拖入你的 CameraBoundary 隱形框 Collider（優先使用）")]
    [Tooltip("把場景中的 CameraBoundary 隱形框物件的 Collider 直接拖進來，這是最精確的設定方式！")]
    public Collider[] explicitRuinedBoundsColliders;

    [Header("天空區背景標籤設定（自動搜尋用）")]
    public string skyBackgroundTag = "Background";

    [Header("廢墟區 Y 軸門檻")]
    [Tooltip("玩家 Y <= 此值視為進入廢墟層，低於此值才啟動廢墟邊界 Clamp")]
    public float ruinedZoneYThreshold = -85f;

    [Header("邊界鎖定設定")]
    public bool clampYAxis = true;

    // ─── 內部狀態 ───
    private Camera _cam;
    private Bounds _skyZoneBounds;
    private Bounds _ruinedZoneBounds;
    private bool _hasSkyZone = false;
    private bool _hasRuinedZone = false;

    // 玩家是否已確實落地於廢墟層（一旦為 true 就不再關閉）
    private bool _playerLandedInRuinedZone = false;

    private float _cacheTimer = 0f;
    private Transform _playerTransform;
    private PlayerMovement _playerMovement;

    void Start()
    {
        _cam = GetComponent<Camera>();
        FindPlayer();
        RebuildZoneClusters();
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

    public void RebuildZoneClusters()
    {
        _hasSkyZone = false;
        _hasRuinedZone = false;

        // ── 天空區 Bounds：自動搜尋 Background tag ──
        try
        {
            GameObject[] skyObjs = GameObject.FindGameObjectsWithTag(skyBackgroundTag);
            if (skyObjs != null && skyObjs.Length > 0)
            {
                List<Bounds> bList = new List<Bounds>();
                foreach (var obj in skyObjs)
                {
                    Collider col = obj.GetComponent<Collider>();
                    if (col != null) bList.Add(col.bounds);
                }
                if (bList.Count > 0)
                {
                    Bounds b = bList[0];
                    for (int i = 1; i < bList.Count; i++) b.Encapsulate(bList[i]);
                    _skyZoneBounds = b;
                    _hasSkyZone = true;
                }
            }
        }
        catch { }

        // ── 廢墟區 Bounds：優先使用手動指定的 CameraBoundary 隱形框 ──
        if (explicitRuinedBoundsColliders != null && explicitRuinedBoundsColliders.Length > 0)
        {
            List<Bounds> bList = new List<Bounds>();
            foreach (var col in explicitRuinedBoundsColliders)
            {
                if (col != null) bList.Add(col.bounds);
            }
            if (bList.Count > 0)
            {
                Bounds b = bList[0];
                for (int i = 1; i < bList.Count; i++) b.Encapsulate(bList[i]);
                _ruinedZoneBounds = b;
                _hasRuinedZone = true;
                Debug.Log($"[SimpleCameraBounds] ✅ 使用手動指定的 CameraBoundary，廢墟區 Bounds = {_ruinedZoneBounds}");
                return; // 手動指定優先，不再自動搜尋
            }
        }

        // ── 廢墟區 Bounds：備用自動搜尋 RuinedBackground + 廢墟地板 ──
        List<Collider> ruinedCols = new List<Collider>();
        try
        {
            GameObject[] ruinedBgs = GameObject.FindGameObjectsWithTag("RuinedBackground");
            if (ruinedBgs != null)
                foreach (var bg in ruinedBgs)
                {
                    Collider c = bg.GetComponent<Collider>();
                    if (c != null) ruinedCols.Add(c);
                }
        }
        catch { }
        try
        {
            GameObject[] floors = GameObject.FindGameObjectsWithTag("Floor");
            if (floors != null)
                foreach (var f in floors)
                    if (f != null && f.transform.position.y < -80f)
                    {
                        Collider c = f.GetComponent<Collider>();
                        if (c != null) ruinedCols.Add(c);
                    }
        }
        catch { }

        if (ruinedCols.Count > 0)
        {
            Bounds b = ruinedCols[0].bounds;
            for (int i = 1; i < ruinedCols.Count; i++)
                if (ruinedCols[i] != null) b.Encapsulate(ruinedCols[i].bounds);
            _ruinedZoneBounds = b;
            _hasRuinedZone = true;
        }
    }

    void LateUpdate()
    {
        if (_playerTransform == null) FindPlayer();
        if (_playerTransform == null) return;

        // 定時重建（每 3 秒，以防場景動態變化）
        _cacheTimer += Time.deltaTime;
        if (_cacheTimer > 3f)
        {
            _cacheTimer = 0f;
            RebuildZoneClusters();
        }

        float playerY = _playerTransform.position.y;
        bool isGrounded = (_playerMovement != null) ? _playerMovement.isGrounded : true;
        bool isFallingInAir = (_playerMovement != null) && _playerMovement.freezeHorizontal && !isGrounded;

        bool inRuinedZone = (playerY <= ruinedZoneYThreshold);

        // ★ 落地於廢墟層才永久啟動廢墟邊界（掉落途中不啟動，避免攝影機被卡在外面）
        if (inRuinedZone && isGrounded && !_playerLandedInRuinedZone)
        {
            _playerLandedInRuinedZone = true;
            // 落地時立刻重建一次確保 Bounds 最新
            RebuildZoneClusters();
            Debug.Log("[SimpleCameraBounds] ✅ 玩家已落地於廢墟層，開啟攝影機邊界限制！");
        }

        // 選擇當前使用的 Bounds
        Bounds activeZone;
        if (inRuinedZone && _playerLandedInRuinedZone)
        {
            if (!_hasRuinedZone) return;
            activeZone = _ruinedZoneBounds;
        }
        else if (!inRuinedZone)
        {
            if (!_hasSkyZone) return;
            activeZone = _skyZoneBounds;
        }
        else
        {
            // 掉落途中（還沒落地於廢墟）：不限制任何邊界，攝影機完全自由
            return;
        }

        // ── 計算相機視野的一半寬高 ──
        float halfHeight, halfWidth;
        if (_cam.orthographic)
        {
            halfHeight = _cam.orthographicSize;
            halfWidth = halfHeight * _cam.aspect;
        }
        else
        {
            float distance = Mathf.Abs(transform.position.z - activeZone.center.z);
            halfHeight = distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            halfWidth = halfHeight * _cam.aspect;
        }

        Vector3 camPos = transform.position;
        Vector3 clampedPos = camPos;

        // ── X 軸邊界 Clamp（始終執行）──
        float minX = activeZone.min.x + halfWidth;
        float maxX = activeZone.max.x - halfWidth;
        if (minX <= maxX)
            clampedPos.x = Mathf.Clamp(camPos.x, minX, maxX);

        // ── Y 軸邊界 Clamp（掉落途中跳過）──
        if (clampYAxis && !isFallingInAir)
        {
            float minY = activeZone.min.y + halfHeight;
            float maxY = activeZone.max.y - halfHeight;

            if (minY <= maxY)
            {
                clampedPos.y = Mathf.Clamp(camPos.y, minY, maxY);
            }
            else
            {
                float t = Mathf.InverseLerp(activeZone.min.y, activeZone.max.y, playerY);
                clampedPos.y = Mathf.Lerp(minY, maxY, t);
            }
        }

        if (Vector3.Distance(camPos, clampedPos) > 0.001f)
            transform.position = clampedPos;
    }
}
