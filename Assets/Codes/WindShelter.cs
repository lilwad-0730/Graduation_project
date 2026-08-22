using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於真掩體與假掩體上，其自身或子物件需要有 Trigger Collider 作為躲避偵測區。
/// </summary>
[RequireComponent(typeof(Collider))]
public class WindShelter : MonoBehaviour, IResettable
{
    [Header("掩體屬性")]
    [Tooltip("是否為真掩體。若為 false，玩家在裡面吹風時掩體會崩解碎裂。")]
    public bool isTrueShelter = true;

    [Tooltip("【假掩體限定】開始崩解碎裂的延遲時間 (秒，預設 0.8)")]
    public float collapseDelay = 0.8f;

    private bool isPlayerInside = false;
    private bool hasCollapsed = false;
    private Destructible destructible;
    private WindGustSystem windSystem;
    private Coroutine collapseCoroutine;

    private void Start()
    {
        // 搜尋此物件上的 Trigger Collider 作為風暴感應區，絕對不修改主實體物理牆！
        Collider[] colliders = GetComponents<Collider>();
        bool hasTrigger = false;
        foreach (var c in colliders)
        {
            if (c != null && c.isTrigger)
            {
                hasTrigger = true;
                break;
            }
        }

        // 如果只有實體物理牆 (無 Trigger)，自動建立同尺寸的 Trigger 感應區，保留主實體牆防止玩家穿模！
        if (!hasTrigger && colliders.Length > 0)
        {
            Collider mainCol = colliders[0];
            if (mainCol is BoxCollider mainBox)
            {
                BoxCollider triggerBox = gameObject.AddComponent<BoxCollider>();
                triggerBox.center = mainBox.center;
                triggerBox.size = mainBox.size;
                triggerBox.isTrigger = true;
            }
            else if (mainCol is SphereCollider mainSphere)
            {
                SphereCollider triggerSphere = gameObject.AddComponent<SphereCollider>();
                triggerSphere.center = mainSphere.center;
                triggerSphere.radius = mainSphere.radius;
                triggerSphere.isTrigger = true;
            }
            else
            {
                BoxCollider triggerBox = gameObject.AddComponent<BoxCollider>();
                triggerBox.isTrigger = true;
            }
        }

        destructible = GetComponent<Destructible>();
        if (destructible == null) destructible = GetComponentInChildren<Destructible>();
        if (destructible == null) destructible = GetComponentInParent<Destructible>();

        windSystem = FindFirstObjectByType<WindGustSystem>();

        if (!isTrueShelter && destructible == null)
        {
            Debug.LogWarning($"[WindShelter] '{gameObject.name}' 被設為假掩體，但找不到 Destructible 元件，將無法正常崩解！");
        }
    }

    private void Update()
    {
        // 若為真掩體、已崩解、玩家不在裡面、或找不到風力系統，則不需處理崩解
        if (isTrueShelter || hasCollapsed || !isPlayerInside || windSystem == null) return;

        // 如果正值吹風狀態，且未啟動崩解協程，立即開始倒數
        if (windSystem.CurrentState == WindState.Blowing && collapseCoroutine == null)
        {
            collapseCoroutine = StartCoroutine(CollapseSequence());
        }
    }

    private void HandlePlayerEnter(GameObject playerObj)
    {
        if (playerObj.CompareTag("Player") || playerObj.name == "Player" || playerObj.GetComponentInParent<PlayerMovement>() != null)
        {
            if (!isPlayerInside)
            {
                isPlayerInside = true;
                if (!hasCollapsed)
                {
                    WindGustSystem.RegisterPlayerShelter();
                    Debug.Log($"【掩體偵測】玩家躲入掩體 '{gameObject.name}'。 (isTrueShelter: {isTrueShelter})");
                }
            }
        }
    }

    private void HandlePlayerExit(GameObject playerObj)
    {
        if (playerObj.CompareTag("Player") || playerObj.name == "Player" || playerObj.GetComponentInParent<PlayerMovement>() != null)
        {
            if (isPlayerInside)
            {
                isPlayerInside = false;
                if (!hasCollapsed)
                {
                    WindGustSystem.UnregisterPlayerShelter();
                }
                Debug.Log($"【掩體偵測】玩家離開掩體 '{gameObject.name}'。");

                if (!isTrueShelter && collapseCoroutine != null)
                {
                    StopCoroutine(collapseCoroutine);
                    collapseCoroutine = null;
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other) => HandlePlayerEnter(other.gameObject);
    private void OnTriggerExit(Collider other) => HandlePlayerExit(other.gameObject);

    private void OnTriggerEnter2D(Collider2D other) => HandlePlayerEnter(other.gameObject);
    private void OnTriggerExit2D(Collider2D other) => HandlePlayerExit(other.gameObject);

    private IEnumerator CollapseSequence()
    {
        Debug.LogWarning($"【假掩體警報】'{gameObject.name}' 開始崩解！於 {collapseDelay} 秒後碎裂！");
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

        // 2. 碎石爆開發生的同時，掩體保護才正式宣告失效！
        if (isPlayerInside)
        {
            WindGustSystem.UnregisterPlayerShelter();
        }
        collapseCoroutine = null;
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
    }
}
