using UnityEngine;

/// <summary>
/// 影子怪物觸發區。
/// 掛載於場景中放置的隱形碰撞框物件上。
/// 玩家進入此區域時呼叫 ShadowMonsterController.ActivateChase()，使怪物開始出現並追逐。
///
/// 【場景建置】
///   1. 在 dark glasses 場景中建立空物件，命名如 "ShadowMonsterTrigger"。
///   2. 新增 Box Collider，調整大小覆蓋玩家前進路徑，勾選 IsTrigger = true。
///   3. 掛載此腳本（不需要其他設定）。
///   4. 將物件 Mesh Renderer 關閉或不加，使其在遊戲中不可見。
///
/// 【注意】
///   重生後怪物回到 Dormant 狀態，玩家若再次進入此觸發區會再次啟動怪物（符合重置需求）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShadowMonsterTriggerZone : MonoBehaviour
{
    [Tooltip("觸發後是否只允許啟動一次（建議關閉，讓重生後重新觸發）")]
    public bool triggerOnce = false;

    private Collider _col;
    private bool _hasTriggered = false;

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggerOnce && _hasTriggered) return;
        if (!IsPlayer(other.gameObject)) return;

        Debug.Log("【觸發點】玩家進入影子怪物觸發區！");

        if (ShadowMonsterController.Instance != null)
        {
            ShadowMonsterController.Instance.ActivateChase();
            _hasTriggered = true;
        }
        else
        {
            Debug.LogWarning("【觸發點】找不到 ShadowMonsterController！請確認場景中有影子怪物物件。");
        }
    }

    private bool IsPlayer(GameObject go)
    {
        if (go.CompareTag("Player")) return true;
        if (go.GetComponent<PlayerMovement>() != null) return true;
        return false;
    }
}
