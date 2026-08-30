using UnityEngine;

/// <summary>
/// 區域觸發器：當玩家踏入此區域時，同步發起所有鳥類敵人的俯衝攻擊。
/// </summary>
public class BirdAttackTriggerZone : MonoBehaviour, IResettable
{
    [Tooltip("是否只觸發一次？")]
    public bool triggerOnce = true;
    
    private bool hasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && (!hasTriggered || !triggerOnce))
        {
            hasTriggered = true;
            IndividualBirdEnemy.TriggerAllBirdsAttack();
        }
    }

    public void ResetToInitialState()
    {
        hasTriggered = false;
    }
}
