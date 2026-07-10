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
        // 確保 Collider 被設為 Trigger
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        destructible = GetComponent<Destructible>();
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

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInside = true;
            if (!hasCollapsed)
            {
                WindGustSystem.IsPlayerSheltered = true;
                Debug.Log($"【掩體偵測】玩家躲入掩體 '{gameObject.name}'。");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInside = false;
            // 離開掩體時，取消保護狀態
            WindGustSystem.IsPlayerSheltered = false;
            Debug.Log($"【掩體偵測】玩家離開掩體 '{gameObject.name}'。");

            // 玩家在崩解前逃離，則暫停崩解倒數
            if (!isTrueShelter && collapseCoroutine != null)
            {
                StopCoroutine(collapseCoroutine);
                collapseCoroutine = null;
            }
        }
    }

    private IEnumerator CollapseSequence()
    {
        Debug.LogWarning($"【假掩體警報】'{gameObject.name}' 開始震動崩解！於 {collapseDelay} 秒後碎裂！");
        yield return new WaitForSeconds(collapseDelay);

        hasCollapsed = true;
        WindGustSystem.IsPlayerSheltered = false; // 失去保護

        if (destructible != null)
        {
            destructible.Shatter();
        }
        else
        {
            gameObject.SetActive(false); // 備用方案：直接關閉物件
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
        isPlayerInside = false;
        hasCollapsed = false;
        // 註：若假掩體被 Shatter 關閉，Destructible.cs 的 Reset 亦會將其 activeSelf 設回原樣，因此這裡僅重置變數。
    }
}
