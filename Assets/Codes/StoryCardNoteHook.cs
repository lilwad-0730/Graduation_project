using UnityEngine;

/// <summary>
/// 我你他　日誌紙拾取掛鉤
///
/// 掛在組員的 CollectibleNote（水下的 note paper）上。
/// 不用手掛：StoryCardPlayer 會在場景載入時自動幫每一張紙補上這個元件。
///
/// 【規則】撿到的第 1／2／3 張紙 → 播 D1／D2／D3（照片頁＋原句）。
///         之後的紙照常收集，不再出卡。
///         死亡不重來（重生不重載場景，計數還在）；整個遊戲重開才歸零。
///
/// 【為什麼不用 StoryCardTrigger】紙被撿到 0.35 秒後會被 Destroy，
///   掛在紙上的協程會跟著死。所以凍結／解凍交給 StoryCardPlayer 自己管
///   （PlayFrozen），宿主消失卡片照樣播完、玩家照樣解凍。
/// </summary>
[DisallowMultipleComponent]
public class StoryCardNoteHook : MonoBehaviour
{
    [Tooltip("最多出幾張日誌卡（D1..Dn）")]
    public int maxDiaries = 3;

    private static int _picked;   // 撿到第幾張（整個遊戲重開才歸零）
    private bool _fired;

    private void OnTriggerEnter(Collider other)
    {
        if (_fired) return;
        // ★死亡防護：重生流程中經過不觸發
        if (PlayerRespawnSystem.IsAnyRespawning) return;
        if (!IsPlayer(other)) return;

        _fired = true;
        if (_picked >= maxDiaries) return;

        _picked++;
        string cardId = "D" + _picked;
        PlayerMovement pm = other.GetComponentInParent<PlayerMovement>();
        StoryCardPlayer.Instance.PlayFrozen(cardId, pm);
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.gameObject != null && other.gameObject.tag == "Player") return true;
        if (other.name == "Player") return true;
        return other.GetComponentInParent<PlayerMovement>() != null;
    }
}
