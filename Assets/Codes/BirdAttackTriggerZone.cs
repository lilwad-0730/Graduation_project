using UnityEngine;

/// <summary>
/// 區域觸發器：當玩家踏入此區域時，觸發指定的鳥類敵人俯衝攻擊（支援單隻或指定群組，不再無差別觸發全圖所有鳥）。
/// </summary>
public class BirdAttackTriggerZone : MonoBehaviour, IResettable
{
    [Header("目標鳥敵人 (若指定則只觸發這些鳥)")]
    [Tooltip("踏入此區域時要觸發的特定鳥類敵人清單")]
    public IndividualBirdEnemy[] specificBirds;

    [Tooltip("若未指定特定鳥，是否僅觸發此 Trigger 範圍內的鳥敵人？(預設開啟)")]
    public bool onlyTriggerBirdsInsideZone = true;

    [Tooltip("是否只觸發一次？")]
    public bool triggerOnce = true;
    
    private bool hasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && (!hasTriggered || !triggerOnce))
        {
            hasTriggered = true;

            if (specificBirds != null && specificBirds.Length > 0)
            {
                foreach (var bird in specificBirds)
                {
                    if (bird != null) bird.StartAttackSequence();
                }
            }
            else if (onlyTriggerBirdsInsideZone)
            {
                Collider col = GetComponent<Collider>();
                if (col != null)
                {
                    Bounds b = col.bounds;
                    IndividualBirdEnemy[] allBirds = FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None);
                    foreach (var bird in allBirds)
                    {
                        if (bird != null && b.Contains(bird.transform.position))
                        {
                            bird.StartAttackSequence();
                        }
                    }
                }
            }
        }
    }

    public void ResetToInitialState()
    {
        hasTriggered = false;
    }
}
