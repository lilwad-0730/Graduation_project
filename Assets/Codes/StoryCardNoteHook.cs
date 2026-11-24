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
    public static int PendingCount => _pending;

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
        if (_picked >= 3) return;

        _picked++;
        PlayerMovement pm = Object.FindAnyObjectByType<PlayerMovement>();
        if (StoryCardPlayer.Instance == null) return;
        if (SpacingEnabled && !SpacingOk(note.transform.position))
        {
            // ★1024 太近：先欠著，等她游遠一點再出（Pacer 每幀看）
            _pending++;
            Debug.Log("【日誌卡】★1024 第 " + _picked + " 張紙離上一張卡太近（" + Mathf.RoundToInt((note.transform.position - _lastCardPos).magnitude) + " 公尺），卡片欠著，游遠一點再出");
            Pacer.Ensure();
            return;
        }
        ShowNext(pm, note.transform.position);
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

    /// <summary>★1024 欠著的卡：每幀看她離上一張卡多遠，夠遠（或等夠久）就在她身邊翻開。場景物件，換場景自己消失。</summary>
    private sealed class Pacer : MonoBehaviour
    {
        private static Pacer _one;
        public static void Ensure()
        {
            if (_one != null) return;
            GameObject go = new GameObject("[日誌卡間隔]");
            go.hideFlags = HideFlags.HideInHierarchy;
            _one = go.AddComponent<Pacer>();
        }
        private PlayerMovement _pm;
        private void Update()
        {
            if (_pending <= 0) { Destroy(gameObject); _one = null; return; }
            if (Time.timeScale <= 0f) return;                                // 世界暫停中（別的卡片、設定選單）
            if (PlayerRespawnSystem.IsAnyRespawning) return;
            StoryCardPlayer sp = StoryCardPlayer.Instance;
            if (sp == null || sp.IsPlaying) return;
            if (_pm == null) _pm = FindAnyObjectByType<PlayerMovement>();
            if (_pm == null) return;
            if (_pm.isCutsceneFrozen) return;                                // 別的演出中
            if (!SpacingOk(_pm.transform.position)) return;
            _pending--;
            Debug.Log("【日誌卡】★1024 游夠遠了，補出欠著的卡 D" + (_shown + 1));
            ShowNext(_pm, _pm.transform.position);
        }
    }
}
