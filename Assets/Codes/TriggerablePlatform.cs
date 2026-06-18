using UnityEngine;
using System.Collections.Generic;

public class TriggerablePlatform : MonoBehaviour
{
    private List<Collider> platformColliders = new List<Collider>();
    private bool hasInitializedIgnore = false;

    private float disableTimer = 0f;
    private bool isPlayerOnPlatform = false;
    private bool isCollisionEnabled = false;

    [Header("物理判定設定")]
    [Tooltip("離開平台後，延遲幾秒才關閉碰撞（防止微小抖動/微跳躍導致穿模，建議 0.2 ~ 0.3 秒）")]
    public float disableDelay = 0.25f;

    [Tooltip("啟用碰撞後，若玩家遲遲沒有踩上來，最長維持幾秒後自動關閉（逾時保護，建議 1.5 秒）")]
    public float activateTimeout = 1.5f;

    void Start()
    {
        // 預設為穿透狀態
        hasInitializedIgnore = false;
    }

    void Update()
    {
        // 持續確保在初始狀態下，平台是穿透狀態 (關閉實體碰撞)
        if (!hasInitializedIgnore)
        {
            SetCollisionIgnore(true);
            hasInitializedIgnore = true;
            Debug.Log($"【樓梯/平台】'{gameObject.name}' 初始化成功，預設設定為「穿透狀態」。");
        }

        // 如果目前處於「實體碰撞狀態」且「玩家沒有站在上面」
        if (isCollisionEnabled && !isPlayerOnPlatform)
        {
            disableTimer -= Time.deltaTime;
            if (disableTimer <= 0f)
            {
                // 逾時或真正離開了，恢復穿透狀態
                SetCollisionIgnore(true);
                Debug.Log($"【樓梯/平台】'{gameObject.name}' 逾時/離開，恢復為穿透狀態。");
            }
        }
    }

    // 當玩家接觸到樓梯表面時
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponent<PlayerMovement>() != null)
        {
            isPlayerOnPlatform = true;
            Debug.Log($"【樓梯/平台】玩家踩上 '{gameObject.name}'，鎖定實體狀態。");
        }
    }

    // 當玩家離開樓梯表面時
    private void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponent<PlayerMovement>() != null)
        {
            isPlayerOnPlatform = false;
            // 啟動延遲倒數，防止微小抖動/跳躍瞬間穿透
            disableTimer = disableDelay; 
            Debug.Log($"【樓梯/平台】玩家離開 '{gameObject.name}'，開始倒數 {disableDelay} 秒後恢復穿透。");
        }
    }

    /// <summary>
    /// 供 JumpTriggerZone 呼叫：啟用實體碰撞，讓玩家可以落下來踩
    /// </summary>
    public void EnableCollision()
    {
        SetCollisionIgnore(false); // 關閉忽略 = 開啟實體碰撞
        // 給予一定的時間視窗讓玩家降落
        disableTimer = activateTimeout; 
        Debug.Log($"【樓梯/平台】'{gameObject.name}' 已被 Trigger 叫醒！開啟實體碰撞，最長維持 {activateTimeout} 秒。");
    }

    // 輔助函數：啟用或關閉平台的實體碰撞器
    private void SetCollisionIgnore(bool ignore)
    {
        // 重新抓取最新的碰撞器，確保包含動態產生的 3D MeshCollider
        platformColliders.Clear();
        platformColliders.AddRange(GetComponentsInChildren<Collider>());

        if (platformColliders.Count == 0) return;

        int changedCount = 0;
        foreach (var platformCol in platformColliders)
        {
            if (platformCol == null) continue;

            // 排除觸發器本身 (例如平台自己身上若有掛載 Trigger)
            if (platformCol.isTrigger) continue;

            // ignore = true 代表要穿透，所以碰撞器 enabled = false
            // ignore = false 代表要碰撞，所以碰撞器 enabled = true
            platformCol.enabled = !ignore;
            changedCount++;
        }

        isCollisionEnabled = !ignore;
        Debug.Log($"【樓梯/平台】'{gameObject.name}' 已將 {changedCount} 個實體碰撞器設定為：{(ignore ? "關閉 (穿透)" : "開啟 (實體)")}");
    }
}
