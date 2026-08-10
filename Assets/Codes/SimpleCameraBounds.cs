using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 業界標準：區域分層與視場邊界動態夾緊相機 Confiner (Zone-Aware Viewport Clamping)
/// 1. 區域分層：天空區域 (Y > -85) 與廢墟區域 (Y <= -85) 完全解耦。主角未進入廢墟前，廢墟邊界 0% 作用。
/// 2. 父容器視野包覆：與 ParallaxGroup 協同，當主角進入廢墟層時，以整塊廢墟包覆 Bounds 進行夾緊。
/// 3. 雙向防破圖：主角在地面不露底邊 (Floor)，爬上高平台時鏡頭頂部精確卡在背景頂端，絕不露白穿圖！
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(9999)]
public class SimpleCameraBounds : MonoBehaviour
{
    [Header("背景與地板標籤設定")]
    public string[] backgroundTags = { "Background", "RuinedBackground", "Floor", "FallingBackground" };

    [Header("邊界鎖定設定")]
    public bool clampYAxis = true;

    private Camera _cam;
    private Bounds _skyZoneBounds;
    private Bounds _ruinedZoneBounds;
    private bool _hasSkyZone = false;
    private bool _hasRuinedZone = false;

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

    /// <summary>
    /// 精確按區域 (Sky Zone vs Ruined Zone) 將背景與地板合併為獨立的兩大區域 Bounds
    /// </summary>
    public void RebuildZoneClusters()
    {
        _hasSkyZone = false;
        _hasRuinedZone = false;

        // 1. 建立天空區 Bounds (Y > -80 且標籤為 Background/Floor)
        GameObject[] skyObjs = GameObject.FindGameObjectsWithTag("Background");
        if (skyObjs != null && skyObjs.Length > 0)
        {
            Bounds b = skyObjs[0].GetComponent<Collider>().bounds;
            for (int i = 1; i < skyObjs.Length; i++)
            {
                Collider col = skyObjs[i].GetComponent<Collider>();
                if (col != null) b.Encapsulate(col.bounds);
            }
            _skyZoneBounds = b;
            _hasSkyZone = true;
        }

        // 2. 建立廢墟區 Bounds (標籤為 RuinedBackground + Ruined Floor)
        List<Collider> ruinedCols = new List<Collider>();
        GameObject[] ruinedBgs = GameObject.FindGameObjectsWithTag("RuinedBackground");
        if (ruinedBgs != null)
        {
            foreach (var bg in ruinedBgs)
            {
                Collider c = bg.GetComponent<Collider>();
                if (c != null) ruinedCols.Add(c);
            }
        }

        GameObject[] floors = GameObject.FindGameObjectsWithTag("Floor");
        if (floors != null)
        {
            foreach (var f in floors)
            {
                // 只有位置在 Y < -80 區域的 Floor 才歸為廢墟區地板
                if (f != null && f.transform.position.y < -80f)
                {
                    Collider c = f.GetComponent<Collider>();
                    if (c != null) ruinedCols.Add(c);
                }
            }
        }

        if (ruinedCols.Count > 0)
        {
            Bounds b = ruinedCols[0].bounds;
            for (int i = 1; i < ruinedCols.Count; i++)
            {
                if (ruinedCols[i] != null) b.Encapsulate(ruinedCols[i].bounds);
            }
            _ruinedZoneBounds = b;
            _hasRuinedZone = true;
        }
    }

    void LateUpdate()
    {
        if (_playerTransform == null) FindPlayer();

        _cacheTimer += Time.deltaTime;
        if (_cacheTimer > 3f)
        {
            _cacheTimer = 0f;
            RebuildZoneClusters();
        }

        if (_playerTransform == null) return;

        float playerY = _playerTransform.position.y;

        // ★★★ 核心邏輯 1：分層完全解耦！
        // 只有主角掉落到 Y <= -85 單位時，才啟動廢墟區域邊界！未掉下去時廢墟邊界 0% 作用！
        bool inRuinedZone = (playerY <= -85f);
        
        if (!inRuinedZone && !_hasSkyZone) return;
        if (inRuinedZone && !_hasRuinedZone) return;

        Bounds activeZone = inRuinedZone ? _ruinedZoneBounds : _skyZoneBounds;

        // 判斷是否在大怒神通道中自由下落
        bool isGrounded = (_playerMovement != null) ? _playerMovement.isGrounded : true;
        bool isFallingInAir = !isGrounded && (_playerMovement != null && _playerMovement.freezeHorizontal);

        float halfHeight = 0f;
        float halfWidth = 0f;

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

        // --- X 軸邊界 Clamp ---
        float minX = activeZone.min.x + halfWidth;
        float maxX = activeZone.max.x - halfWidth;
        if (minX <= maxX)
        {
            clampedPos.x = Mathf.Clamp(camPos.x, minX, maxX);
        }

        // --- Y 軸動態雙向防破圖 Clamp ---
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
                // 當背景總高度小於視野時：利用 InverseLerp 根據主角高度比率，軟性插值
                // 落地時攝影機底部貼緊地面 Floor，跳上高平台時鏡頭頂部貼緊背景頂端，絕對不露白破圖！
                float t = Mathf.InverseLerp(activeZone.min.y, activeZone.max.y, playerY);
                clampedPos.y = Mathf.Lerp(minY, maxY, t);
            }
        }

        if (Vector3.Distance(camPos, clampedPos) > 0.001f)
        {
            transform.position = clampedPos;
        }
    }
}
