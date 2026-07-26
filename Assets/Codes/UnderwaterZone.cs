using UnityEngine;

/// <summary>
/// 掛載於水體 Trigger 物件 (Collider 需勾選 Is Trigger)。
/// 當玩家進入此區域時，自動將 PlayerMovement 切換為水下物理模式；離開時恢復陸地模式。
/// </summary>
[RequireComponent(typeof(Collider))]
public class UnderwaterZone : MonoBehaviour
{
    private void Start()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerMovement pm = other.GetComponent<PlayerMovement>();
            if (pm == null) pm = other.GetComponentInParent<PlayerMovement>();
            if (pm == null) pm = other.GetComponentInChildren<PlayerMovement>();

            if (pm != null)
            {
                pm.SetTriggerUnderwater(true);
                Debug.Log("【水域 Trigger】玩家踩入水體，切換為水下物理模式！");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerMovement pm = other.GetComponent<PlayerMovement>();
            if (pm == null) pm = other.GetComponentInParent<PlayerMovement>();
            if (pm == null) pm = other.GetComponentInChildren<PlayerMovement>();

            if (pm != null)
            {
                pm.SetTriggerUnderwater(false);
                Debug.Log("【水域 Trigger】玩家離開水體，切回陸地物理模式！");
            }
        }
    }
}
