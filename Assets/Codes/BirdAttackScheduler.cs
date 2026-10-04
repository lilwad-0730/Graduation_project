using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>鳥的兩種攻擊：鎖「發現當下的位置」，或鎖「預判她會跑到的位置」。</summary>
public enum BirdAttackType { FixedPosition, PredictedPosition }

/// <summary>
/// ★1006 荒原鳥群攻擊排程（Request Queue 版，static，不用掛在場景上）。
///
/// 架構：偵測（Detection）與放行（Attack Slot）解耦。
///   鳥偵測到玩家 → Enqueue 一張「攻擊需求」→ 鳥進入 Pending（照常盤旋）→
///   本類別每幀檢查：風、鳥影壓制、重生、傘下、放行間隔、同時攻擊上限 →
///   通過才從佇列挑一張發給鳥（GrantAttack），鳥才鎖定目標、亮紅線、俯衝。
///   被風／鳥影／忙碌擋住只會「延後」，需求不會消失；只有三種情況會取消需求：
///   鳥本身失效（被銷毀、不再 Pending）、玩家已經離這隻鳥太遠（> 偵測範圍 × requestKeepRangeMultiplier）、重生。
///
/// 挑誰（deterministic，沒有任何每幀亂數）：
///   1. 預設先放最早進佇列的需求（同時間以 InstanceID 當穩定決勝）
///   2. 區域輪替：以玩家為中心把鳥分成 左／中／右，同一區連續放行超過 maxSameZoneInRow 次，就優先挑別區
///   3. 強制需求（BirdAttackTriggerZone 直接觸發）插隊到最前面
/// 攻擊類型（定點／預判）：照 attackPattern 字串循環（C＝定點、P＝預判），固定可學習、不是亂數。
///
/// 節奏：每次放行間隔 slotInterval；每 attacksPerRound 次多休息 restSeconds；同時在攻擊中（前搖＋俯衝）的鳥不超過 maxSimultaneous。
///
/// Debug：DesertBeatDirector 的 enableBirdAttackDebug 打開後，事件才會印 [BIRD ...] log，
/// 並在重生／結束時印一次「攻擊健康度」統計。預設關閉，沒有任何每幀 log。
/// </summary>
public static class BirdAttackScheduler
{
    // ── 設定（DesertBeatDirector 開局寫入）──
    public static bool debugEnabled = false;
    public static string attackPattern = "CPCPPCCPCPPC";
    public static int maxSimultaneous = 6;
    public static int maxSimultaneousHard = 10;
    public static int queuePressureStep = 5;
    public static float slotInterval = 0.2f;
    public static int attacksPerRound = 4;
    public static float restSeconds = 0.4f;
    public static int maxSameZoneInRow = 2;
    public static float zoneHalfWidth = 5f;
    public static float requestKeepRangeMultiplier = 1.5f;
    public static float behindPenaltyMeters = 8f;
    public static float agingMetersPerSecond = 2f;

    // ── 內部狀態 ──
    private class Request
    {
        public IndividualBirdEnemy bird;
        public float time;
        public bool force;
        public float score;
    }

    private static readonly List<Request> _queue = new List<Request>();
    private static readonly HashSet<IndividualBirdEnemy> _active = new HashSet<IndividualBirdEnemy>();
    private static readonly Dictionary<string, int> _stats = new Dictionary<string, int>();
    private static int _patternIndex = 0;
    private static int _slotCount = 0;
    private static float _nextSlotTime = 0f;
    private static int _lastZone = -1;
    private static int _sameZoneCount = 0;
    private static string _lastBlock = "";
    private static Transform _player;
    private static BirdAttackSchedulerRunner _runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitStatics()
    {
        _queue.Clear();
        _active.Clear();
        _stats.Clear();
        _runner = null;
        _player = null;
        ResetCounters();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        PlayerRespawnSystem.OnResettablesReset -= OnRespawnReset;
        PlayerRespawnSystem.OnResettablesReset += OnRespawnReset;
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        ClearQueueAndCancelBirds();
        _stats.Clear();
        ResetCounters();
        _player = null;
    }

    // 重生：全場景 IResettable 剛跑完（鳥各自回到 Idle），佇列、放行計時、輪替歷史一併清乾淨
    private static void OnRespawnReset()
    {
        if (debugEnabled) PrintStats("重生");
        ClearQueueAndCancelBirds();
        ResetCounters();
    }

    private static void ResetCounters()
    {
        _patternIndex = 0;
        _slotCount = 0;
        _nextSlotTime = 0f;
        _lastZone = -1;
        _sameZoneCount = 0;
        _lastBlock = "";
    }

    private static void ClearQueueAndCancelBirds()
    {
        for (int i = 0; i < _queue.Count; i++)
        {
            if (_queue[i].bird != null) _queue[i].bird.CancelRequest(false);
        }
        _queue.Clear();
        _active.Clear();
    }

    // ── 統計／Debug ──
    public static void Stat(string key)
    {
        int v;
        _stats.TryGetValue(key, out v);
        _stats[key] = v + 1;
    }

    private static int S(string key)
    {
        int v;
        _stats.TryGetValue(key, out v);
        return v;
    }

    public static void Log(string tag, IndividualBirdEnemy bird, string extra = "")
    {
        if (!debugEnabled) return;
        Debug.Log($"[BIRD {tag}] {(bird != null ? bird.name : "-")} {extra}");
    }

    private static string Pct(int a, int b)
    {
        return b <= 0 ? "n/a" : (100f * a / b).ToString("F0") + "%";
    }

    /// <summary>印一次「攻擊健康度」。一眼看出是偵測少、佇列被吃、放行不夠、取消、還是俯衝中途撞地。</summary>
    public static void PrintStats(string when)
    {
        int detect = S("DETECT"), queued = S("QUEUED"), slot = S("SLOT"), lockn = S("LOCK"), dive = S("DIVE");
        int hit = S("HIT"), retreat = S("RETREAT"), cancel = S("CANCEL"), removed = S("REMOVED");
        Debug.Log($"[BIRD STATS @{when}] Detection={detect} Queue={queued} WindBlock={S("BLOCK-WIND")} ShadowBlock={S("BLOCK-SHADOW")} " +
                  $"SchedulerBusy={S("BLOCK-BUSY")} OtherBlock={S("BLOCK-OTHER")} AttackStart(Slot)={slot} Lock={lockn} Dive={dive} " +
                  $"Hit={hit} Retreat={retreat} Cancel={cancel} Removed={removed} DiveEndedByEnvironment={S("DIVE-ENV")}\n" +
                  $"  Detection→Queue={Pct(queued, detect)}  Queue→Attack={Pct(slot, queued)}  Attack→Dive={Pct(dive, slot)}  Dive→Hit/Retreat={Pct(hit + retreat, dive)}  佇列剩餘={_queue.Count}");
    }

    // ── 需求進出 ──
    public static bool IsQueued(IndividualBirdEnemy bird)
    {
        for (int i = 0; i < _queue.Count; i++) if (_queue[i].bird == bird) return true;
        return false;
    }

    public static void Enqueue(IndividualBirdEnemy bird, bool force, float distance)
    {
        if (bird == null || IsQueued(bird)) return;
        EnsureRunner();
        _queue.Add(new Request { bird = bird, time = Time.time, force = force });
        Stat("QUEUED");
        Log("QUEUED", bird, $"Distance={distance:F1} Queue={_queue.Count}{(force ? " Force" : "")}");
    }

    public static void Remove(IndividualBirdEnemy bird)
    {
        for (int i = _queue.Count - 1; i >= 0; i--) if (_queue[i].bird == bird) _queue.RemoveAt(i);
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        GameObject go = new GameObject("BirdAttackSchedulerRunner (自動生成)");
        _runner = go.AddComponent<BirdAttackSchedulerRunner>();
    }

    private static Transform GetPlayer()
    {
        if (_player != null) return _player;
        PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
        if (pm != null) _player = pm.transform;
        return _player;
    }

    // 玩家「前進方向」的正負號：看實際水平速度，太慢就用面向
    private static float PlayerForwardSign(Transform player)
    {
        PlayerMovement pm = player.GetComponent<PlayerMovement>();
        if (pm == null) return 1f;
        Rigidbody prb = player.GetComponent<Rigidbody>();
        if (prb != null && Mathf.Abs(prb.linearVelocity.x) > 0.5f) return Mathf.Sign(prb.linearVelocity.x);
        return pm.FacingDirection.x < 0f ? -1f : 1f;
    }

    private static int ZoneOf(float dx)
    {
        float h = Mathf.Max(0.5f, zoneHalfWidth);
        return dx < -h ? 0 : (dx > h ? 2 : 1);
    }

    // 同時攻擊上限：基本值 maxSimultaneous；佇列越長（每 queuePressureStep 張需求）上限多 +1，最多到 maxSimultaneousHard。
    // 這樣鳥多的時候會自動多放幾隻、不會積一堆在身後；鳥少的時候維持基本值，不會變成整群一起衝。
    private static int EffectiveCap()
    {
        int bonus = queuePressureStep > 0 ? _queue.Count / queuePressureStep : 0;
        return Mathf.Clamp(Mathf.Max(1, maxSimultaneous) + bonus, 1, Mathf.Max(maxSimultaneous, maxSimultaneousHard));
    }

    private static string ZoneName(int z) { return z == 0 ? "LEFT" : (z == 1 ? "CENTER" : "RIGHT"); }

    /// <summary>由 Runner 每幀呼叫一次。佇列為空時幾乎沒有成本。</summary>
    public static void Tick()
    {
        // 1. 清理無效需求／結束的攻擊
        _active.RemoveWhere(b => b == null || !b.IsAttacking);
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            IndividualBirdEnemy b = _queue[i].bird;
            if (b == null || !b.isActiveAndEnabled || !b.IsPending) _queue.RemoveAt(i);
        }
        if (_queue.Count == 0) { _lastBlock = ""; return; }

        // 2. 全場阻擋：只延後，不丟需求（事件只在「開始被擋」那一刻記一次）
        string block = "";
        if (PlayerRespawnSystem.IsAnyRespawning || !PlayerRespawnSystem.IsPlayerMovingAfterRespawn) block = "OTHER";
        else if (UmbrellaZone.IsPlayerUnderUmbrella) block = "OTHER";
        else if (IndividualBirdEnemy.IsWindPhase()) block = "WIND";
        else if (IndividualBirdEnemy.IsAllSuppressed) block = "SHADOW";
        else if (Time.time < _nextSlotTime || _active.Count >= EffectiveCap()) block = "BUSY";

        if (block != _lastBlock)
        {
            if (block != "") { Stat("BLOCK-" + block); Log("BLOCKED-" + block, null, $"Queue={_queue.Count} Active={_active.Count}"); }
            _lastBlock = block;
        }
        if (block != "") return;

        // 3. 挑一張需求
        Transform player = GetPlayer();
        if (player == null) return;

        float fwd = PlayerForwardSign(player);
        int pick = -1;
        int oldestAny = -1;
        for (int i = 0; i < _queue.Count; i++)
        {
            Request r = _queue[i];
            float dx = r.bird.transform.position.x - player.position.x;

            // 玩家早就跑出這隻鳥的有效範圍：取消需求（鳥回到待機，她再靠近會重新偵測）
            if (!r.force && Mathf.Abs(dx) > r.bird.detectionRange * Mathf.Max(1f, requestKeepRangeMultiplier))
            {
                Stat("CANCEL");
                Log("CANCEL", r.bird, $"Reason=PlayerFar dx={dx:F1}");
                r.bird.CancelRequest(false);
                _queue.RemoveAt(i);
                i--;
                continue;
            }

            // 優先分數（越小越先）：離玩家越近越先；在她身後的加罰分；等越久扣越多（避免餓死）。
            // 這樣風停時「前方剛偵測到的鳥」不會被「早就排著、現在已在身後或很遠的鳥」搶走名額。
            bool behind = dx * fwd < 0f;
            r.score = Mathf.Abs(dx) + (behind ? behindPenaltyMeters : 0f) - (Time.time - r.time) * agingMetersPerSecond;

            if (IsBetter(r, oldestAny >= 0 ? _queue[oldestAny] : null)) oldestAny = i;

            int zone = ZoneOf(dx);
            bool zoneBlocked = !r.force && _sameZoneCount >= Mathf.Max(1, maxSameZoneInRow) && zone == _lastZone;
            if (!zoneBlocked && IsBetter(r, pick >= 0 ? _queue[pick] : null)) pick = i;
        }
        if (pick < 0) pick = oldestAny;       // 全部都在同一區：照最早的
        if (pick < 0) return;

        Request chosen = _queue[pick];
        _queue.RemoveAt(pick);

        // 4. 放行
        float chosenDx = chosen.bird.transform.position.x - player.position.x;
        int chosenZone = ZoneOf(chosenDx);
        if (chosenZone == _lastZone) _sameZoneCount++; else { _lastZone = chosenZone; _sameZoneCount = 1; }

        string pat = string.IsNullOrEmpty(attackPattern) ? "CP" : attackPattern;
        char c = char.ToUpperInvariant(pat[_patternIndex % pat.Length]);
        _patternIndex++;
        BirdAttackType type = c == 'P' ? BirdAttackType.PredictedPosition : BirdAttackType.FixedPosition;

        _slotCount++;
        float gap = Mathf.Max(0.05f, slotInterval);
        if (_slotCount % Mathf.Max(1, attacksPerRound) == 0) gap += Mathf.Max(0f, restSeconds);
        _nextSlotTime = Time.time + gap;

        _active.Add(chosen.bird);
        Stat("SLOT");
        float wait = Time.time - chosen.time;
        Log("SLOT", chosen.bird, $"Mode={(type == BirdAttackType.FixedPosition ? "Current" : "Predicted")} Zone={ZoneName(chosenZone)} QueueWait={wait:F1}s Queue={_queue.Count} Active={_active.Count}");
        chosen.bird.GrantAttack(type, wait);
    }

    // 排序：強制需求優先；其次優先分數（近＞遠、前方＞身後、等越久越優先）；同分用 InstanceID 穩定決勝（可重現）
    private static bool IsBetter(Request a, Request than)
    {
        if (than == null) return true;
        if (a.force != than.force) return a.force;
        if (!Mathf.Approximately(a.score, than.score)) return a.score < than.score;
        return a.bird.GetInstanceID() < than.bird.GetInstanceID();
    }
}

/// <summary>排程器的心跳：只負責每幀呼叫 BirdAttackScheduler.Tick()，場景卸載自動消失。</summary>
public class BirdAttackSchedulerRunner : MonoBehaviour
{
    private void Update()
    {
        BirdAttackScheduler.Tick();
    }

    private void OnDestroy()
    {
        if (BirdAttackScheduler.debugEnabled && Application.isPlaying) BirdAttackScheduler.PrintStats("結束");
    }
}
