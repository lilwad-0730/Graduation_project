using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於真掩體與假掩體上。
/// 核心機制：
/// 1. 【保留實體物理碰撞】：石柱/掩體自身保持實體物理碰撞體 (isTrigger = false)，主角可踩踏、跳躍、倚靠，保有真實世界物件質感。
/// 2. 【背風面防風保護區】：程式在石柱「背風面 (後面/左側)」生成防風保護範圍，迎風面 (前面/右側) 則不保護。
/// 3. 【高度可自訂】：Y 軸高度與垂直偏移量可在 Inspector 自由調整，Scene 視窗綠色 Gizmos 與實際代碼 100% 一致。
/// 4. 【假掩體吹風崩解】：假掩體在吹風時延遲碎裂，碎裂後實體碰撞與防風區域同步消失。
/// </summary>
public class WindShelter : MonoBehaviour, IResettable
{
    [Header("掩體屬性")]
    [Tooltip("是否為真掩體。若為 false，玩家在裡面吹風時掩體會崩解碎裂。")]
    public bool isTrueShelter = true;

    [Tooltip("【假掩體限定】開始崩解碎裂的延遲時間 (秒，預設 0.8)")]
    public float collapseDelay = 0.8f;

    [Header("防風保護區域設定 (背風面躲避範圍)")]
    [Tooltip("【背風面/後面】保護延伸寬度 (公尺，預設 2.0，向左側/背風面延伸)")]
    public float protectBehindDistance = 2.0f;

    [Tooltip("【迎風面/前面】保護延伸寬度 (公尺，預設 0.0，不向右側/迎風面延伸，防止在前面被保護)")]
    public float protectFrontDistance = 0.0f;

    [Tooltip("【垂直保護高度】(公尺，預設 4.5，可直接在 Inspector 調整)")]
    public float protectHeight = 4.5f;

    [Tooltip("【垂直高度偏移】(公尺，預設 0.0，可上下微調保護框底部位置)")]
    public float offsetY = 0.0f;

    private bool isPlayerInside = false;
    private bool hasCollapsed = false;
    private Destructible destructible;
    private WindGustSystem windSystem;
    private Coroutine collapseCoroutine;
    private Transform playerTrans;
    private Collider[] shelterColliders;
    private Renderer[] shelterRenderers;

    private void Start()
    {
        // 獲取掩體實體碰撞體與渲染器 (保留原有的 isTrigger 設定，不破壞實體物理碰撞！)
        shelterColliders = GetComponentsInChildren<Collider>(true);
        shelterRenderers = GetComponentsInChildren<Renderer>(true);

        destructible = GetComponent<Destructible>();
        if (destructible == null) destructible = GetComponentInChildren<Destructible>();
        if (destructible == null) destructible = GetComponentInParent<Destructible>();

        windSystem = FindFirstObjectByType<WindGustSystem>();

        EnsurePlayer();

        if (!isTrueShelter && destructible == null)
        {
            Debug.LogWarning($"[WindShelter] '{gameObject.name}' 被設為假掩體，但找不到 Destructible 元件，將無法正常崩解！");
        }
    }

    private void EnsurePlayer()
    {
        if (playerTrans == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTrans = playerObj.transform;
            }
            else
            {
                PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
                if (pm != null) playerTrans = pm.transform;
            }
        }
    }

    private void Update()
    {
        EnsurePlayer();

        // 核心運作：即時判定主角是否處於石柱背風面的防風保護範圍內
        if (playerTrans != null && !hasCollapsed && gameObject.activeInHierarchy)
        {
            bool inBounds = CheckPlayerInShelterZone(playerTrans.position);
            if (inBounds && !isPlayerInside)
            {
                SetPlayerInside(true);
            }
            else if (!inBounds && isPlayerInside)
            {
                SetPlayerInside(false);
            }
        }

        // 假掩體崩解邏輯 (若正在重生轉場或破曉緩衝中，絕對不執行崩解倒數)
        if (isTrueShelter || hasCollapsed || !isPlayerInside || windSystem == null || PlayerRespawnSystem.IsAnyRespawning) return;

        // 如果正值吹風狀態，且未啟動崩解協程，立即開始倒數
        if (windSystem.CurrentState == WindState.Blowing && collapseCoroutine == null)
        {
            collapseCoroutine = StartCoroutine(CollapseSequence());
        }
    }

    /// <summary>
    /// 計算保護區的精確世界座標邊界 (minX, maxX, minY, maxY)
    /// </summary>
    private void GetShelterZoneBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        float baseY = transform.position.y;
        float pillarLeft = transform.position.x - 0.5f;
        float pillarRight = transform.position.x + 0.5f;

        if (shelterColliders != null && shelterColliders.Length > 0 && shelterColliders[0] != null)
        {
            Bounds b = shelterColliders[0].bounds;
            pillarLeft = b.min.x;
            pillarRight = b.max.x;
            baseY = b.min.y;
        }
        else if (shelterRenderers != null && shelterRenderers.Length > 0 && shelterRenderers[0] != null)
        {
            Bounds b = shelterRenderers[0].bounds;
            pillarLeft = b.min.x;
            pillarRight = b.max.x;
            baseY = b.min.y;
        }

        // 逆風由右向左吹：
        // 左側 (後面/背風面) = pillarLeft - protectBehindDistance
        // 右側 (前面/迎風面) = pillarRight + protectFrontDistance
        minX = pillarLeft - protectBehindDistance;
        maxX = pillarRight + protectFrontDistance;

        // Y 軸精確高度控制
        minY = baseY + offsetY;
        maxY = baseY + offsetY + protectHeight;
    }

    /// <summary>
    /// 計算 2D 空間中玩家是否位於掩體的保護範圍內
    /// </summary>
    private bool CheckPlayerInShelterZone(Vector3 playerPos)
    {
        GetShelterZoneBounds(out float minX, out float maxX, out float minY, out float maxY);
        return (playerPos.x >= minX && playerPos.x <= maxX && playerPos.y >= minY && playerPos.y <= maxY);
    }

    private void SetPlayerInside(bool inside)
    {
        if (isPlayerInside == inside) return;

        isPlayerInside = inside;
        if (inside)
        {
            if (!hasCollapsed)
            {
                WindGustSystem.RegisterPlayerShelter();
                Debug.Log($"🛡️【掩體保護】玩家進入掩體 '{gameObject.name}' 背風安全區域。(isTrueShelter: {isTrueShelter})");
            }
        }
        else
        {
            if (!hasCollapsed)
            {
                WindGustSystem.UnregisterPlayerShelter();
                Debug.Log($"💨【掩體離開】玩家離開掩體 '{gameObject.name}'。");
            }

            if (!isTrueShelter && collapseCoroutine != null)
            {
                StopCoroutine(collapseCoroutine);
                collapseCoroutine = null;
            }
        }
    }

    private IEnumerator CollapseSequence()
    {
        Debug.LogWarning($"💥【假掩體警報】'{gameObject.name}' 開始崩解！於 {collapseDelay} 秒後碎裂！");
        yield return new WaitForSeconds(collapseDelay);

        hasCollapsed = true;

        // 1. 優先觸發 2D 碎石爆破散落效果！
        if (destructible != null)
        {
            destructible.Shatter();
        }
        else
        {
            gameObject.SetActive(false);
        }

        // 2. 碎石爆開的同時，掩體保護與物理實體同步宣告失效！
        if (isPlayerInside)
        {
            WindGustSystem.UnregisterPlayerShelter();
        }
        collapseCoroutine = null;
    }

    // 在 Scene 視窗繪製綠色防風保護範圍，便於關卡編輯檢視 (與代碼計算 100% 嚴格一致)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isTrueShelter ? new Color(0f, 1f, 0.2f, 0.4f) : new Color(1f, 0.8f, 0f, 0.4f);
        GetShelterZoneBounds(out float minX, out float maxX, out float minY, out float maxY);

        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, transform.position.z);
        Vector3 size = new Vector3(maxX - minX, maxY - minY, 2.0f);

        Gizmos.DrawWireCube(center, size);
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        if (collapseCoroutine != null)
        {
            StopCoroutine(collapseCoroutine);
            collapseCoroutine = null;
        }
        if (isPlayerInside && !hasCollapsed)
        {
            WindGustSystem.UnregisterPlayerShelter();
        }
        isPlayerInside = false;
        hasCollapsed = false;

        // 重新喚醒與刷新可能崩解的假掩體
        gameObject.SetActive(true);
        if (destructible != null)
        {
            destructible.ResetToInitialState();
        }
        else
        {
            SpriteRenderer[] allSrs = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in allSrs) if (sr != null) sr.enabled = true;

            Collider[] allCols = GetComponentsInChildren<Collider>(true);
            foreach (var col in allCols) if (col != null) col.enabled = true;
        }
    }
}
