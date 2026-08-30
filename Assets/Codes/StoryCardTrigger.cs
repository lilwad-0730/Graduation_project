using System.Collections;
using UnityEngine;

/// <summary>
/// 我你他　過場文字卡觸發器（同場景用）
///
/// 用在「不換場景」的卡片 —— 施工單第二節的 M1、M2。
/// 棉花堡與廢墟都在 SampleScene 的 Area_Sky / Area_Ruined 分區，
/// 不會走 SceneTransitionZone，所以用這個。
///
/// 【怎麼用】
///   1. 在該章最後一拍的位置放一個空物件
///   2. 加一個 Collider，勾 Is Trigger
///   3. 掛這支腳本，cardId 填 M1 或 M2
///
/// 換場景的 M3 / M4 / M5 不要用這個，
/// 改在 SceneTransitionZone 裡掛（見《掛接說明》）。
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class StoryCardTrigger : MonoBehaviour
{
    [Header("要播哪張卡")]
    [Tooltip("M1 / M2。換場景的卡不要用這個觸發器")]
    public string cardId = "M1";

    [Header("行為")]
    [Tooltip("播卡期間鎖住玩家輸入")]
    public bool freezePlayer = true;
    [Tooltip("只觸發一次。重玩整關時會重新計算")]
    public bool onlyOnce = true;
    [Tooltip("播完之後把這個物件關掉")]
    public bool disableAfterPlay = true;

    private bool _fired;

    private void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_fired && onlyOnce) return;
        // ★死亡防護：死亡／重生流程進行中，經過觸發區不播卡。
        //   死亡本身也不會重播卡片——死亡只是傳回重生點，不重載場景，
        //   「只播一次」的記憶還在。只有整個遊戲重開才會再看到卡片。
        if (PlayerRespawnSystem.IsAnyRespawning) return;
        if (!IsPlayer(other)) return;

        _fired = true;
        StartCoroutine(Run(other));
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.gameObject != null && other.gameObject.tag == "Player") return true;
        if (other.name == "Player") return true;
        return other.GetComponentInParent<PlayerMovement>() != null;
    }

    private IEnumerator Run(Collider other)
    {
        PlayerMovement pm = null;
        if (freezePlayer && other != null)
        {
            pm = other.GetComponentInParent<PlayerMovement>();
            if (pm != null) pm.isCutsceneFrozen = true;
        }

        // 同場景播卡：黑幕（或紙底）由 StoryCardPlayer 自己淡入淡出。
        // ★用 PlayDetached：協程掛在播放器身上，就算這個觸發器
        //   跟著場景被銷毀（例如同幀觸發了換場景），卡片仍會播完。
        yield return StoryCardPlayer.Instance.PlayDetached(cardId, true, true);

        if (pm != null) pm.isCutsceneFrozen = false;

        if (disableAfterPlay && gameObject != null)
        {
            gameObject.SetActive(false);
        }
    }
}
