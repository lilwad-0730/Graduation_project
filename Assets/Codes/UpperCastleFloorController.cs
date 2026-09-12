using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 【上層城堡與雲海走道地板全自動防護系統】(Upper Castle & Cloud Floor Controller)
/// 特性：
/// 1. 支援「雲海走道」高度與長度調整
/// 2. 支援「城堡前庭走道」高度與長度調整
/// 3. 支援「斷橋破洞 (Bridge Gap)」：自動將城堡地面切成左右兩段，中間掏空
/// 4. 支援「深淵墜落死區 (Deep Death Zone)」：死區下沉至深淵（深度可調），完全無碰撞阻礙，角色享有逼真下墜失重感後重生
/// 5. 運行時防穿透絕對守護（自動避開破洞，讓角色可自然掉落深淵）
/// </summary>
[ExecuteAlways]
public class UpperCastleFloorController : MonoBehaviour
{
    [Header("☁️ 雲海長廊地面 (Upper Cloud Walkway)")]
    [Tooltip("雲海走道踩踏表面高度 Y (對齊背景雲海頂端)")]
    public float cloudFloorSurfaceY = 36.0f;

    [Tooltip("雲海走道中心 X 軸座標")]
    public float cloudFloorCenterX = -52f;

    [Tooltip("雲海走道長度 (X 軸寬度)")]
    public float cloudFloorWidth = 88f;

    [Tooltip("雲海走道碰撞框垂直厚度 (向下延伸)")]
    public float cloudFloorThickness = 5f;

    [Header("🏰 城堡前庭地面 (PatioFloor)")]
    [Tooltip("城堡前庭踩踏表面高度 Y (對齊橋面/走廊)")]
    public float patioFloorSurfaceY = 39.4f;

    [Tooltip("城堡前庭中心 X 軸座標")]
    public float patioFloorCenterX = 33.2f;

    [Tooltip("城堡前庭長度 (X 軸寬度)")]
    public float patioFloorWidth = 95f;

    [Tooltip("城堡前庭碰撞框垂直厚度 (向下延伸)")]
    public float patioFloorThickness = 4f;

    [Header("🕳️ 斷橋破洞與深淵墜落重生 (Bridge Gap & Deep Abyss)")]
    [Tooltip("是否開啟斷橋破洞（開啟後地面會自動切開，角色跳不過會自然掉落）")]
    public bool enableBridgeGap = true;

    [Tooltip("破洞左邊緣 X 座標（左段走道結束處）")]
    public float gapLeftX = 34.0f;

    [Tooltip("破洞右邊緣 X 座標（右段走道開始處）")]
    public float gapRightX = 39.0f;

    [Tooltip("死區位於橋面下方的深度 (米，建議 10 ~ 18，掉得越深越有高空下墜絕望感)")]
    public float deathZoneDepthOffsetY = 12.0f;

    [Tooltip("觸發死亡後的黑屏轉場延遲時間 (秒，讓玩家看著角色在空中下墜再黑屏，建議 0.3 ~ 0.6)")]
    public float fallDelayBeforeFade = 0.4f;

    [Tooltip("掉入破洞後的自訂重生點（若留空，則自動傳送回破洞左側安全走道）")]
    public Transform customRespawnPoint;

    [Tooltip("是否在深淵自動生成 DeathZone 重生觸發框")]
    public bool autoCreateDeathZone = true;

    [Header("📐 階梯過渡斜坡 (Stair Slope)")]
    [Tooltip("是否啟用平滑斜坡 (銜接雲海與城堡階梯)")]
    public bool enableStairSlope = true;

    [Tooltip("斜坡起點 X (銜接雲海處)")]
    public float slopeStartX = -8f;

    [Tooltip("斜坡終點 X (銜接前庭處)")]
    public float slopeEndX = -0.5f;

    [Header("🛡️ 物理與守護設定")]
    [Tooltip("碰撞框前後深度 Z (預設 30m，徹底解決 2.5D 角色 Z 軸偏移掉落)")]
    public float colliderDepthZ = 30f;

    [Tooltip("是否啟用防穿模即時修正守護 (非破洞區域物理出錯時即時托起)")]
    public bool enablePositionGuard = true;

    private PlayerMovement _cachedPlayer;
    private Rigidbody _cachedRb;

    private void Awake()
    {
        EnsureAllColliders();
    }

    private void OnEnable()
    {
        EnsureAllColliders();
    }

    private void Start()
    {
        EnsureAllColliders();
    }

    private void OnValidate()
    {
        EnsureAllColliders();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnSceneLoadedRuntime()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName == "SampleScene" || GameObject.Find("PatioFloor") != null)
        {
            EnsureControllerExists();
        }
    }

    public static UpperCastleFloorController EnsureControllerExists()
    {
        var controller = Object.FindFirstObjectByType<UpperCastleFloorController>();
        if (controller == null)
        {
            GameObject go = GameObject.Find("[UpperCastleFloorController]");
            if (go == null)
            {
                go = new GameObject("[UpperCastleFloorController]");
            }
            controller = go.AddComponent<UpperCastleFloorController>();
        }
        controller.EnsureAllColliders();
        return controller;
    }

    public void EnsureAllColliders()
    {
        SetupPatioFloorCollider();
        SetupCloudWalkwayFloor();
        SetupStairSlopeCollider();
        SetupBridgeGapDeathZone();
    }

    // ========================================================
    // 1. 城堡前庭地面（支援「單段完整」或「切開斷橋破洞」）
    // ========================================================
    private void SetupPatioFloorCollider()
    {
        GameObject patio = GameObject.Find("PatioFloor");
        if (patio == null) return;

        patio.tag = "Floor";
        patio.layer = 0;

        BoxCollider boxLeft = patio.GetComponent<BoxCollider>();
        if (boxLeft == null) boxLeft = patio.AddComponent<BoxCollider>();

        boxLeft.enabled = true;
        boxLeft.isTrigger = false;

        Vector3 patioWorldPos = patio.transform.position;
        float patioLeftX = slopeEndX;
        float patioRightX = patioFloorCenterX + patioFloorWidth * 0.5f;

        GameObject rightSegmentObj = GameObject.Find("PatioFloor_RightSegment");

        if (!enableBridgeGap)
        {
            // 不開破洞：單一完整長方形
            float centerLocalX = patioFloorCenterX - patioWorldPos.x;
            float centerLocalY = (patioFloorSurfaceY - patioFloorThickness * 0.5f) - patioWorldPos.y;
            boxLeft.center = new Vector3(centerLocalX, centerLocalY, 0f);
            boxLeft.size = new Vector3(patioFloorWidth, patioFloorThickness, colliderDepthZ);

            if (rightSegmentObj != null) rightSegmentObj.SetActive(false);
        }
        else
        {
            // 開啟破洞：切分成左段與右段
            // 1. 左半段 (從 slopeEndX 到 gapLeftX)
            float leftWidth = Mathf.Max(0.5f, gapLeftX - patioLeftX);
            float leftCenterX = patioLeftX + leftWidth * 0.5f;
            float centerLocalX = leftCenterX - patioWorldPos.x;
            float centerLocalY = (patioFloorSurfaceY - patioFloorThickness * 0.5f) - patioWorldPos.y;

            boxLeft.center = new Vector3(centerLocalX, centerLocalY, 0f);
            boxLeft.size = new Vector3(leftWidth, patioFloorThickness, colliderDepthZ);

            // 2. 右半段 (從 gapRightX 到 patioRightX)
            if (rightSegmentObj == null)
            {
                rightSegmentObj = new GameObject("PatioFloor_RightSegment");
            }
            rightSegmentObj.SetActive(true);
            rightSegmentObj.tag = "Floor";
            rightSegmentObj.layer = 0;

            float rightWidth = Mathf.Max(0.5f, patioRightX - gapRightX);
            float rightCenterX = gapRightX + rightWidth * 0.5f;
            float rightCenterY = patioFloorSurfaceY - patioFloorThickness * 0.5f;

            rightSegmentObj.transform.position = new Vector3(rightCenterX, rightCenterY, 0f);

            BoxCollider boxRight = rightSegmentObj.GetComponent<BoxCollider>();
            if (boxRight == null) boxRight = rightSegmentObj.AddComponent<BoxCollider>();

            boxRight.enabled = true;
            boxRight.isTrigger = false;
            boxRight.center = Vector3.zero;
            boxRight.size = new Vector3(rightWidth, patioFloorThickness, colliderDepthZ);
        }
    }

    // ========================================================
    // 2. 斷橋深淵墜落死區 (BridgeGap_DeathZone)
    // ========================================================
    private void SetupBridgeGapDeathZone()
    {
        GameObject deathZone = GameObject.Find("BridgeGap_DeathZone");
        if (!enableBridgeGap || !autoCreateDeathZone)
        {
            if (deathZone != null) deathZone.SetActive(false);
            return;
        }

        if (deathZone == null)
        {
            deathZone = new GameObject("BridgeGap_DeathZone");
        }
        deathZone.SetActive(true);
        deathZone.tag = "DeathZone"; // 專案原生 DeathZone 標籤
        deathZone.layer = 0;

        float gapWidth = Mathf.Max(1.0f, gapRightX - gapLeftX);
        float gapCenterX = (gapLeftX + gapRightX) * 0.5f;
        
        // ★ 關鍵：將死區沉降至橋面下方 deathZoneDepthOffsetY 米深淵處 (預設 12 米)
        float deathZoneY = patioFloorSurfaceY - deathZoneDepthOffsetY;
        deathZone.transform.position = new Vector3(gapCenterX, deathZoneY, 0f);

        BoxCollider box = deathZone.GetComponent<BoxCollider>();
        if (box == null) box = deathZone.AddComponent<BoxCollider>();

        box.enabled = true;
        box.isTrigger = true; // 必須為 Trigger，角色下墜完全不受阻擋
        box.center = Vector3.zero;
        // 左右加寬 25 米，厚度 6 米，確保角色下墜時有橫向慣性也能 100% 吃到判定
        box.size = new Vector3(gapWidth + 25f, 6.0f, colliderDepthZ);

        var fallTrigger = deathZone.GetComponent<BridgeGapFallTrigger>();
        if (fallTrigger == null) fallTrigger = deathZone.AddComponent<BridgeGapFallTrigger>();
        fallTrigger.controller = this;
    }

    // ========================================================
    // 3. 雲海長廊實體地面 (UpperCloudWalkwayFloor)
    // ========================================================
    private void SetupCloudWalkwayFloor()
    {
        GameObject cloudFloor = GameObject.Find("UpperCloudWalkwayFloor");
        if (cloudFloor == null)
        {
            cloudFloor = new GameObject("UpperCloudWalkwayFloor");
        }

        cloudFloor.tag = "Floor";
        cloudFloor.layer = 0;

        float centerY = cloudFloorSurfaceY - cloudFloorThickness * 0.5f;
        cloudFloor.transform.position = new Vector3(cloudFloorCenterX, centerY, 0f);

        BoxCollider box = cloudFloor.GetComponent<BoxCollider>();
        if (box == null) box = cloudFloor.AddComponent<BoxCollider>();

        box.enabled = true;
        box.isTrigger = false;
        box.center = Vector3.zero;
        box.size = new Vector3(cloudFloorWidth, cloudFloorThickness, colliderDepthZ);

        GameObject oldInvFloor = GameObject.Find("invisible_floor");
        if (oldInvFloor != null && oldInvFloor != cloudFloor)
        {
            Collider oldCol = oldInvFloor.GetComponent<Collider>();
            if (oldCol != null) oldCol.enabled = false;
        }
    }

    // ========================================================
    // 4. 連接雲海與前庭的平滑階梯斜坡 (PatioStairSlope)
    // ========================================================
    private void SetupStairSlopeCollider()
    {
        GameObject slope = GameObject.Find("PatioStairSlope");
        if (!enableStairSlope)
        {
            if (slope != null) slope.SetActive(false);
            return;
        }

        if (slope == null)
        {
            slope = new GameObject("PatioStairSlope");
        }
        slope.SetActive(true);
        slope.tag = "Floor";
        slope.layer = 0;

        float dx = slopeEndX - slopeStartX;
        float dy = patioFloorSurfaceY - cloudFloorSurfaceY;
        if (Mathf.Abs(dx) < 0.1f) dx = 0.1f;

        float length = Mathf.Sqrt(dx * dx + dy * dy);
        float angleRad = Mathf.Atan2(dy, dx);
        float angleDeg = angleRad * Mathf.Rad2Deg;

        Vector3 startPt = new Vector3(slopeStartX, cloudFloorSurfaceY, 0f);
        Vector3 endPt = new Vector3(slopeEndX, patioFloorSurfaceY, 0f);
        Vector3 midPt = (startPt + endPt) * 0.5f;

        slope.transform.position = midPt;
        slope.transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);

        BoxCollider box = slope.GetComponent<BoxCollider>();
        if (box == null) box = slope.AddComponent<BoxCollider>();

        box.enabled = true;
        box.isTrigger = false;
        box.center = new Vector3(0f, -0.75f, 0f);
        box.size = new Vector3(length + 0.5f, 1.5f, colliderDepthZ);
    }

    // ========================================================
    // 5. 運行時防穿模絕對守護（破洞區域不守護，讓其自然掉落深淵）
    // ========================================================
    private void FixedUpdate()
    {
        if (!Application.isPlaying || !enablePositionGuard) return;

        if (_cachedPlayer == null)
        {
            _cachedPlayer = Object.FindFirstObjectByType<PlayerMovement>();
            if (_cachedPlayer != null)
            {
                _cachedRb = _cachedPlayer.GetComponent<Rigidbody>();
            }
        }

        if (_cachedPlayer == null) return;

        Vector3 pos = _cachedPlayer.transform.position;

        // ★ 若角色位於斷橋破洞內：絕不托起，允許物理自然掉落深淵！
        if (enableBridgeGap && pos.x >= (gapLeftX - 0.2f) && pos.x <= (gapRightX + 0.2f))
        {
            return;
        }

        float minX = cloudFloorCenterX - cloudFloorWidth * 0.5f;
        float maxX = patioFloorCenterX + patioFloorWidth * 0.5f;

        if (pos.x >= minX && pos.x <= maxX && pos.y >= (cloudFloorSurfaceY - 8f) && pos.y <= (patioFloorSurfaceY + 8f))
        {
            float expectedMinY = GetExpectedGroundY(pos.x);

            if (pos.y < expectedMinY - 0.05f)
            {
                pos.y = expectedMinY;
                _cachedPlayer.transform.position = pos;

                if (_cachedRb != null)
                {
                    Vector3 vel = _cachedRb.linearVelocity;
                    if (vel.y < 0f) vel.y = 0f;
                    _cachedRb.linearVelocity = vel;
                }

                _cachedPlayer.isGrounded = true;
            }
        }
    }

    public float GetExpectedGroundY(float x)
    {
        if (x <= slopeStartX)
        {
            return cloudFloorSurfaceY;
        }
        else if (x >= slopeEndX)
        {
            return patioFloorSurfaceY;
        }
        else
        {
            float t = Mathf.InverseLerp(slopeStartX, slopeEndX, x);
            return Mathf.Lerp(cloudFloorSurfaceY, patioFloorSurfaceY, t);
        }
    }

    // ========================================================
    // 6. Scene 視窗即時可視化框框
    // ========================================================
    private void OnDrawGizmos()
    {
        // 1. 雲海地面 (綠色)
        Vector3 cloudCenter = new Vector3(cloudFloorCenterX, cloudFloorSurfaceY - cloudFloorThickness * 0.5f, 0f);
        Vector3 cloudSize = new Vector3(cloudFloorWidth, cloudFloorThickness, 6f);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
        Gizmos.DrawCube(cloudCenter, cloudSize);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(cloudCenter, cloudSize);

        // 2. 城堡前庭地面 (青藍色)
        float patioLeftX = slopeEndX;
        float patioRightX = patioFloorCenterX + patioFloorWidth * 0.5f;

        if (!enableBridgeGap)
        {
            Vector3 patioCenter = new Vector3(patioFloorCenterX, patioFloorSurfaceY - patioFloorThickness * 0.5f, 0f);
            Vector3 patioSize = new Vector3(patioFloorWidth, patioFloorThickness, 6f);
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.35f);
            Gizmos.DrawCube(patioCenter, patioSize);
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(patioCenter, patioSize);
        }
        else
        {
            // 左半段走道
            float leftWidth = Mathf.Max(0.5f, gapLeftX - patioLeftX);
            float leftCenterX = patioLeftX + leftWidth * 0.5f;
            Vector3 leftCenter = new Vector3(leftCenterX, patioFloorSurfaceY - patioFloorThickness * 0.5f, 0f);
            Vector3 leftSize = new Vector3(leftWidth, patioFloorThickness, 6f);
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.35f);
            Gizmos.DrawCube(leftCenter, leftSize);
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(leftCenter, leftSize);

            // 右半段走道
            float rightWidth = Mathf.Max(0.5f, patioRightX - gapRightX);
            float rightCenterX = gapRightX + rightWidth * 0.5f;
            Vector3 rightCenter = new Vector3(rightCenterX, patioFloorSurfaceY - patioFloorThickness * 0.5f, 0f);
            Vector3 rightSize = new Vector3(rightWidth, patioFloorThickness, 6f);
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.35f);
            Gizmos.DrawCube(rightCenter, rightSize);
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(rightCenter, rightSize);

            // 破洞缺口（金黃色虛線框，標示缺口）
            float gapWidth = Mathf.Max(1.0f, gapRightX - gapLeftX);
            float gapCenterX = (gapLeftX + gapRightX) * 0.5f;
            Vector3 gapVisualCenter = new Vector3(gapCenterX, patioFloorSurfaceY - 1f, 0f);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(gapVisualCenter, new Vector3(gapWidth, 2f, 6f));

            // 深淵死區（鮮紅色長框，位於下方）
            float deathZoneY = patioFloorSurfaceY - deathZoneDepthOffsetY;
            Vector3 deathZoneCenter = new Vector3(gapCenterX, deathZoneY, 0f);
            Vector3 deathZoneSize = new Vector3(gapWidth + 25f, 6f, 6f);
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.25f);
            Gizmos.DrawCube(deathZoneCenter, deathZoneSize);
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(deathZoneCenter, deathZoneSize);

            // 墜落引導虛線（連接破洞到底部死區）
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            Gizmos.DrawLine(new Vector3(gapLeftX, patioFloorSurfaceY, 0f), new Vector3(gapLeftX, deathZoneY, 0f));
            Gizmos.DrawLine(new Vector3(gapRightX, patioFloorSurfaceY, 0f), new Vector3(gapRightX, deathZoneY, 0f));

            #if UNITY_EDITOR
            Handles.Label(new Vector3(gapCenterX, patioFloorSurfaceY + 0.8f, 0f), $"🕳️ 斷橋破洞 (X: {gapLeftX:F1} ~ {gapRightX:F1})");
            Handles.Label(new Vector3(gapCenterX, deathZoneY + 1.2f, 0f), $"☠️ 深淵重生死區 (深度: -{deathZoneDepthOffsetY:F1}m)");
            #endif
        }

        #if UNITY_EDITOR
        Handles.Label(new Vector3(cloudFloorCenterX, cloudFloorSurfaceY + 0.8f, 0f), $"☁️ 雲海走道 (Y = {cloudFloorSurfaceY:F2})");
        #endif
    }
}

/// <summary>
/// 斷橋墜落重生觸發輔助組件 (BridgeGapFallTrigger)
/// </summary>
public class BridgeGapFallTrigger : MonoBehaviour
{
    public UpperCastleFloorController controller;
    private bool _triggering = false;

    private void OnTriggerEnter(Collider other)
    {
        CheckFall(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        CheckFall(other.gameObject);
    }

    private void CheckFall(GameObject obj)
    {
        if (_triggering || obj == null) return;

        PlayerMovement pm = obj.GetComponent<PlayerMovement>();
        if (pm == null) pm = obj.GetComponentInParent<PlayerMovement>();
        if (pm == null && (obj.CompareTag("Player") || obj.name.Contains("Player")))
        {
            pm = Object.FindFirstObjectByType<PlayerMovement>();
        }

        if (pm != null)
        {
            _triggering = true;
            StartCoroutine(RespawnRoutine(pm));
        }
    }

    private IEnumerator RespawnRoutine(PlayerMovement pm)
    {
        Debug.Log("🕳️【斷橋墜落】玩家跌入高空深淵，觸發真實下墜演出！");

        // 依設定延遲短暫時間，讓玩家清楚體會到失重下墜感
        if (controller != null && controller.fallDelayBeforeFade > 0f)
        {
            yield return new WaitForSeconds(controller.fallDelayBeforeFade);
        }

        PlayerRespawnSystem respawnSystem = pm.GetComponent<PlayerRespawnSystem>();
        if (respawnSystem == null) respawnSystem = Object.FindFirstObjectByType<PlayerRespawnSystem>();

        Vector3 targetRespawnPos;
        if (controller != null && controller.customRespawnPoint != null)
        {
            targetRespawnPos = controller.customRespawnPoint.position;
        }
        else
        {
            // 預設重生在破洞左側 3.5 米處的平地安全區
            float respawnX = (controller != null) ? (controller.gapLeftX - 3.5f) : (transform.position.x - 4f);
            float respawnY = (controller != null) ? (controller.patioFloorSurfaceY + 0.2f) : 39.6f;
            targetRespawnPos = new Vector3(respawnX, respawnY, 0f);
        }

        if (respawnSystem != null)
        {
            respawnSystem.TriggerRespawn(targetRespawnPos);
        }
        else
        {
            pm.WarpTo(targetRespawnPos);
        }

        yield return new WaitForSeconds(2.0f);
        _triggering = false;
    }
}
