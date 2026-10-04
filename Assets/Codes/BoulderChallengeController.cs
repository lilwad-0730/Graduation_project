using System.Collections;
using UnityEngine;

/// <summary>
/// ★1002 廢墟巨石挑戰：「狼咬到第一口 → 失敗 → 巨石下滾押人下坡 → 離開坡面 → 場景內重置」的總指揮。
///
/// 流程：
///   1. PlayerMovement.AddWolf() 呼叫 NotifyWolfBite()。挑戰進行中（石頭已解鎖）時，第一隻咬到就進入失敗：
///        ・玩家鎖操作（isCutsceneFrozen）：不能按前進抵抗，但仍會被石頭推著走
///        ・沒咬住的狼全部停下、不再咬人（WolfEnemy.ChallengeFailHalt）
///        ・石頭進入 Rollback：沿目前坡面往下滾（RollingRockVisual.BeginRollback）
///        ・舊的「咬滿 N 隻才死」這段不再走（AddWolf 直接 return）
///   2. 石頭滾進平面（法線夠平、連續穩定、滾夠遠）才重置；Fail Exit 碰到（也要滾夠遠）是輔助，逾時是最後保險。
///   3. 重置沿用既有 PlayerRespawnSystem.TriggerRespawn(位置)：黑屏、全場景 IResettable 重置（狼出生點、拉桿、
///      鳥…）、把玩家傳到 Challenge Player Spawn。巨石在黑屏中、玩家傳送前先放回 Boulder Spawn。
///      不重載場景。
///
/// 沒設定好（沒拖 Player Spawn、找不到石頭）或挑戰還沒開始（石頭還鎖著）→ NotifyWolfBite 回傳 false，
/// 舊的狼咬流程原封不動，所以這支掛上去之前行為完全沒變。
/// </summary>
public class BoulderChallengeController : MonoBehaviour
{
    public static BoulderChallengeController Instance { get; private set; }

    [Header("手動拖曳（Scene 物件由你自己建立）")]
    [Tooltip("重置後玩家出現的位置。必填，沒填這個系統不會啟動")]
    [SerializeField] private Transform challengePlayerSpawn;

    [Tooltip("重置後巨石出現的位置（放在平地，原本拉桿放下巨石後停的地方）。沒填＝重置時巨石留在原地")]
    [SerializeField] private Transform challengeBoulderSpawn;

    [Tooltip("巨石（rock-new）。留空會自動找場景裡的 RollingRockVisual")]
    [SerializeField] private RollingRockVisual boulder;

    [Header("失敗流程")]
    [Tooltip("狼咬到之後，到巨石開始下滾之間的停頓 (秒)。給玩家一個「被咬了」的瞬間")]
    public float rollbackStartDelay = 0.3f;

    [Tooltip("巨石滾進平面（RollingRockVisual 判定：法線夠平、連續穩定、滾夠遠）之後，再等多久才重置 (秒)。讓石頭減速、玩家被壓到定位")]
    public float afterFlatDelay = 0.8f;

    [Tooltip("最後保險：下滾開始後超過這麼多秒還沒滾到平面也沒碰到 Fail Exit，才強制重置。正常情況不該用到")]
    public float maxRollbackSeconds = 15f;

    [Tooltip("重置後巨石是否直接解鎖（可以推）。關掉＝巨石鎖在 Boulder Spawn，要重新拉拉桿")]
    public bool unlockBoulderAfterReset = true;

    private Rigidbody _boulderRb;
    private bool _failing;
    private bool _rollbackStarted;
    private bool _exitReached;
    private bool _resetPending;
    private PlayerMovement _player;

    /// <summary>失敗流程進行中（咬到第一口 → 重置完成之前）。</summary>
    public static bool IsFailing => Instance != null && Instance._failing;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        PlayerRespawnSystem.OnResettablesReset += HandleResettablesReset;
    }

    private void OnDisable()
    {
        PlayerRespawnSystem.OnResettablesReset -= HandleResettablesReset;
        if (_failing) EndFailState();   // static 會跨場景留著，不能把狼永遠停住
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void ResolveBoulder()
    {
        if (boulder == null) boulder = FindFirstObjectByType<RollingRockVisual>();
        if (boulder != null && _boulderRb == null) _boulderRb = boulder.GetComponent<Rigidbody>();
    }

    private bool IsConfigured
    {
        get
        {
            if (challengePlayerSpawn == null) return false;
            ResolveBoulder();
            return boulder != null && _boulderRb != null;
        }
    }

    /// <summary>石頭已經被拉桿放開（不是 kinematic）＝挑戰開始了。</summary>
    private bool IsChallengeRunning => _boulderRb != null && !_boulderRb.isKinematic && !PlayerRespawnSystem.IsAnyRespawning;

    /// <summary>
    /// PlayerMovement.AddWolf() 呼叫。回傳 true＝這一口由控制器處理（開始失敗，或已在失敗中被吞掉），
    /// 呼叫端不要再走舊的死亡流程；false＝控制器不管，舊流程照走。
    /// </summary>
    public static bool NotifyWolfBite(PlayerMovement pm)
    {
        BoulderChallengeController c = Instance;
        if (c == null || !c.isActiveAndEnabled || !c.IsConfigured) return false;
        if (c._failing) return true;
        if (!c.IsChallengeRunning) return false;

        c.BeginFail(pm);
        return true;
    }

    /// <summary>BoulderFailExitTrigger 呼叫：玩家或巨石被押出坡面了。</summary>
    public void NotifyFailExit()
    {
        // 滾得不夠遠就碰到出口（例如出口放得太靠近坡中）不算，避免咬下去馬上重生
        if (_failing && _rollbackStarted && boulder != null && boulder.RollbackDistance >= boulder.rollbackMinDistance) _exitReached = true;
    }

    private void BeginFail(PlayerMovement pm)
    {
        _failing = true;
        _rollbackStarted = false;
        _exitReached = false;
        _player = pm;

        Debug.Log("【巨石挑戰】第一隻狼咬到＝失敗！玩家鎖操作、狼群停下、巨石準備下滾。");

        WolfEnemy.ChallengeFailHalt = true;
        var ignore = new System.Collections.Generic.List<Collider>();
        if (pm != null) ignore.AddRange(pm.GetComponentsInChildren<Collider>());
        if (boulder != null) ignore.AddRange(boulder.GetComponentsInChildren<Collider>());
        Collider[] ignoreArr = ignore.ToArray();
        foreach (WolfEnemy w in FindObjectsByType<WolfEnemy>(FindObjectsSortMode.None))
        {
            if (w != null) w.HaltForChallengeFail(ignoreArr);
        }

        if (_player != null) _player.isCutsceneFrozen = true;   // 不能反抗；ApplyExternalSlopePush 仍會帶著她走

        StartCoroutine(FailRoutine());
    }

    private IEnumerator FailRoutine()
    {
        if (rollbackStartDelay > 0f) yield return new WaitForSeconds(rollbackStartDelay);

        boulder.BeginRollback(_player != null ? _player.transform : null);
        _rollbackStarted = true;

        // 主要條件：石頭真的滾進平面。Fail Exit 是輔助（滾夠遠才算），maxRollbackSeconds 是最後保險。
        float t = 0f;
        while (!boulder.RollbackReachedFlat && !_exitReached && t < maxRollbackSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        string why = boulder.RollbackReachedFlat ? "滾進平面" : (_exitReached ? "碰到 Fail Exit" : "下滾逾時（保險）");
        Debug.Log($"【巨石挑戰】{why}，滾了 {boulder.RollbackDistance:F1} 公尺，{afterFlatDelay:F1} 秒後重置。");
        if (afterFlatDelay > 0f) yield return new WaitForSeconds(afterFlatDelay);
        yield return ResetRoutine();
    }

    private IEnumerator ResetRoutine()
    {
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs == null) rs = FindFirstObjectByType<PlayerRespawnSystem>();

        if (rs == null)
        {
            // 沒有重生系統：不做黑屏，直接重置
            ApplyBoulderReset();
            if (_player != null) _player.WarpTo(challengePlayerSpawn.position);
            EndFailState();
            yield break;
        }

        // 剛好有別的重生在跑（TriggerRespawn 會被擋掉）：等它結束再接
        float wait = 0f;
        while (rs.IsRespawning && wait < 10f) { wait += Time.deltaTime; yield return null; }

        _resetPending = true;
        rs.TriggerRespawn(challengePlayerSpawn.position);

        // 正常情況下黑屏中 OnResettablesReset 會觸發 HandleResettablesReset 把狀態收乾淨。
        // 萬一事件一直沒來（被擋掉之類），超時自己收，免得狼永遠停住。
        float guard = 0f;
        while (_resetPending && guard < 15f) { guard += Time.deltaTime; yield return null; }
        if (_resetPending) { _resetPending = false; ApplyBoulderReset(); EndFailState(); }
    }

    // 黑屏中、全場景 IResettable 剛跑完、玩家還沒傳送：先放巨石，再讓重生流程把玩家傳走（巨石先到、玩家後到）
    private void HandleResettablesReset()
    {
        if (!_resetPending) return;
        _resetPending = false;
        ApplyBoulderReset();
        EndFailState();
    }

    private void ApplyBoulderReset()
    {
        if (boulder == null || _boulderRb == null) return;

        boulder.EndRollback();
        if (challengeBoulderSpawn != null)
        {
            _boulderRb.isKinematic = true;   // 先轉 kinematic 才能乾淨地改位置與速度
            boulder.transform.position = challengeBoulderSpawn.position;
            _boulderRb.position = challengeBoulderSpawn.position;
            _boulderRb.linearVelocity = Vector3.zero;
            _boulderRb.angularVelocity = Vector3.zero;
        }
        else if (!_boulderRb.isKinematic)
        {
            _boulderRb.linearVelocity = Vector3.zero;
            _boulderRb.angularVelocity = Vector3.zero;
        }

        if (unlockBoulderAfterReset)
        {
            _boulderRb.isKinematic = false;
            boulder.enabled = true;
        }
        // 不解鎖：留在 kinematic，等玩家重新拉拉桿（拉桿的 UnlockRock 會放開它）
    }

    private void EndFailState()
    {
        _failing = false;
        _rollbackStarted = false;
        _exitReached = false;
        WolfEnemy.ChallengeFailHalt = false;
        if (boulder != null) boulder.EndRollback();
    }
}
