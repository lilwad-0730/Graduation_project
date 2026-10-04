using UnityEngine;

/// <summary>
/// ★1002 巨石失敗下滾的「出口」。放在坡腳往外一點的位置，掛一個 Is Trigger 的 Collider。
/// 失敗下滾期間，玩家或巨石碰到它，就通知 BoulderChallengeController 開始重置。
/// 沒有這個物件也能運作（控制器有逾時保險），只是會多等到逾時才重置。
/// </summary>
[RequireComponent(typeof(Collider))]
public class BoulderFailExitTrigger : MonoBehaviour
{
    [Tooltip("留空會自動用場景裡的 BoulderChallengeController")]
    [SerializeField] private BoulderChallengeController controller;

    [Tooltip("巨石碰到也算（玩家被押著走，通常她先到；兩個都算最保險）")]
    public bool triggerOnBoulder = true;

    private void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!BoulderChallengeController.IsFailing) return;

        bool isPlayer = other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>() != null;
        bool isBoulder = triggerOnBoulder && other.GetComponentInParent<RollingRockVisual>() != null;
        if (!isPlayer && !isBoulder) return;

        BoulderChallengeController c = controller != null ? controller : BoulderChallengeController.Instance;
        if (c != null) c.NotifyFailExit();
    }
}
