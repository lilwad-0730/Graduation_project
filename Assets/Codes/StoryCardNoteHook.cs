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
/// ★1024 間隔（組外試玩：「日誌出現間隔：前三個物件連著出」）：
///   場景裡 (2) 和 (1) 兩張紙只隔 8 公尺，D2、D3 會背靠背跳出來。
///   現在撿到紙的時候，如果離上一張卡出現的地方還不到 minSpacingMeters（20 公尺）、
///   而且上一張卡出現還不到 maxWaitSeconds（40 秒），這張卡先「欠著」：紙照常算一張（通關巨石的張數不變），
///   等她再游遠一點（離上一張卡 20 公尺）或時間到了，卡片才在她身邊翻開（世界暫停、氣不會減）。
///   這樣三張卡一定都會看到，只是不會連著出。
///   ★1126 改：欠著的卡不再於半路自己翻開（玩家會以為沒碰到日誌就出卡），改成下一張碰到的紙才出；
///   三張撿完還欠著的，巨石消散演出前補出。
///   關掉：StoryCardNoteHook.SpacingEnabled = false（回到撿到就出）。
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

    /// <summary>★1024 日誌卡要隔開（false＝撿到就出，10/23 以前的行為）。</summary>
    public static bool SpacingEnabled = true;
    /// <summary>★1024 離上一張卡出現的地方至少這麼遠（公尺）才出下一張。</summary>
    public static float MinSpacingMeters = 20f;
    /// <summary>★1024 不管距離，上一張卡出現超過這麼多秒也可以出下一張。</summary>
    public static float MaxWaitSeconds = 40f;

    private static int _picked;       // 撿到第幾張（整個遊戲重開才歸零）
    private static int _shown;        // 已經翻開幾張卡
    private static int _pending;      // 欠著還沒出的卡
    private static Vector3 _lastCardPos;
    private static float _lastCardTime = -999f;
    private static bool _hasLastCard;

    /// <summary>目前已撿到幾張日誌（給通關巨石等系統判斷；跟有沒有翻開卡片無關）。</summary>
    public static int PickedCount => _picked;
    /// <summary>★1024 欠著還沒出的卡。</summary>
    public static int PendingCount => Mathf.Max(0, _picked - _shown);

    /// <summary>進入有日誌的關卡時歸零（同一輪重生不會歸零）。</summary>
    public static void ResetCount()
    {
        _picked = 0; _shown = 0; _pending = 0;
        _pickedKeys.Clear(); _shownIds.Clear();
        _hasLastFirePos = false; _hasLastCard = false; _lastCardTime = -999f;
    }
    private bool _fired;

    // 同一張日誌有外層＋內層兩份腳本，用位置去重：離上次出卡點 2.5 公尺內視為同一張
    private static Vector3 _lastFirePos;
    private static bool _hasLastFirePos;

    /// <summary>
    /// ★1125 日誌張數與日誌卡改成「紙真的被撿到」才算：由 CollectibleNote.CollectItem 呼叫（單一入口）。
    /// 以前這支自己也有一個觸發框，跟紙本身的觸發框各算各的，先碰到的那個就把卡翻開了（水下提早出卡的根因）。
    /// 每張紙只算一次（用紙的位置記下來），撿過的不會再算；最多 maxDiaries 張，D1/D2/D3 各只翻一次，不會重複。
    /// </summary>
    public static void OnNotePicked(CollectibleNote note)
    {
        if (note == null) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;
        string key = KeyOf(note.transform.position);
        if (_pickedKeys.Contains(key)) return;
        _pickedKeys.Add(key);
        // 外層容器＋內層紙兩份腳本：離上次算的點 2.5 公尺內視為同一張
        if (_hasLastFirePos && (note.transform.position - _lastFirePos).sqrMagnitude < 2.5f * 2.5f) return;
        _lastFirePos = note.transform.position;
        _hasLastFirePos = true;

        // ★1126 卡片只在「碰到紙的那一刻」出現，絕不在空曠水裡自己跳出來。
        //   以前太近的卡會「欠著」，等她游遠就在半路翻開 → 玩家看起來就是「沒碰到日誌卡就出來了」（提早觸發）。
        //   現在：太近的卡留到「下一張碰到的紙」才出；欠兩張以上一定當場出一張；
        //   三張都撿完還欠著的，巨石消散演出前補出（FlushPending）。
        bool counts = _picked < 3;
        if (counts) _picked++;
        int owed = _picked - _shown;
        if (owed <= 0) return;
        if (StoryCardPlayer.Instance == null) return;
        PlayerMovement pm = Object.FindAnyObjectByType<PlayerMovement>();
        if (SpacingEnabled && counts && owed < 2 && !SpacingOk(note.transform.position))
        {
            Debug.Log("【日誌卡】★1126 第 " + _picked + " 張紙離上一張卡太近（" + Mathf.RoundToInt((note.transform.position - _lastCardPos).magnitude) + " 公尺），這張卡留到下一張碰到的紙再出");
            return;
        }
        Debug.Log("【日誌卡】★1126 碰到紙 " + note.name + " → 翻開 D" + (_shown + 1) + "（已撿 " + _picked + " 張）");
        ShowNext(pm, note.transform.position);
    }

    /// <summary>★1126 三張都撿完、還有卡沒翻（最後兩張太近）→ 巨石消散演出前在這裡補出，保證三張都看得到。</summary>
    public static bool FlushPending()
    {
        if (_picked - _shown <= 0) return false;
        StoryCardPlayer sp = StoryCardPlayer.Instance;
        if (sp == null || sp.IsPlaying) return false;
        PlayerMovement pm = Object.FindAnyObjectByType<PlayerMovement>();
        Debug.Log("【日誌卡】★1126 巨石演出前補出還沒翻的卡 D" + (_shown + 1));
        ShowNext(pm, pm != null ? pm.transform.position : Vector3.zero);
        return true;
    }

    private static readonly System.Collections.Generic.HashSet<string> _pickedKeys = new System.Collections.Generic.HashSet<string>();
    private static readonly System.Collections.Generic.HashSet<int> _shownIds = new System.Collections.Generic.HashSet<int>();
    private static string KeyOf(Vector3 p) { return Mathf.RoundToInt(p.x * 2f) + "," + Mathf.RoundToInt(p.y * 2f); }

    /// <summary>這張紙這一輪已經被撿過了嗎（撿過的紙不會再出現、不會再算）。</summary>
    public static bool WasPicked(Vector3 pos) { return _pickedKeys.Contains(KeyOf(pos)); }

    private static bool SpacingOk(Vector3 at)
    {
        if (!_hasLastCard) return true;
        if ((at - _lastCardPos).magnitude >= MinSpacingMeters) return true;
        return Time.time - _lastCardTime >= MaxWaitSeconds;
    }

    private static void ShowNext(PlayerMovement pm, Vector3 at)
    {
        if (_shown >= _picked) return;
        if (_shownIds.Contains(_shown + 1)) { _shown++; return; }   // ★1125 這張卡已經翻過，絕不重複
        _shown++;
        _shownIds.Add(_shown);
        _lastCardPos = at; _lastCardTime = Time.time; _hasLastCard = true;
        string cardId = "D" + _shown;
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
