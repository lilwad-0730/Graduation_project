using UnityEngine;

/// <summary>
/// 掛載於遮陽傘下方的觸發偵測區域 (IsTrigger)。
/// 標記玩家是否處於遮陽傘安全區以規避鳥群襲擊。
/// </summary>
[RequireComponent(typeof(Collider))]
public class UmbrellaZone : MonoBehaviour, IResettable
{
    // 全域靜態變數，方便鳥群系統直接讀取玩家是否受保護
    public static bool IsPlayerUnderUmbrella = false;

    private void Start()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            IsPlayerUnderUmbrella = true;
            Debug.Log("【遮陽傘安全區】玩家已進入傘下，避難中。");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            IsPlayerUnderUmbrella = false;
            Debug.Log("【遮陽傘安全區】玩家已離開傘下，失去鳥群規避保護。");
        }
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        IsPlayerUnderUmbrella = false;
    }
}
