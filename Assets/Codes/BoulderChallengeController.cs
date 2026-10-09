using System.Collections;
using UnityEngine;

/// <summary>
/// ★1002/1017 廢墟巨石挑戰：「狼咬到第一口 → 巨石慢速壓迫下滾 → 狼咬滿門檻才重生」的總指揮。
///
/// 流程：
///   1. PlayerMovement.AddWolf() 呼叫 NotifyWolfBite()。挑戰進行中（石頭已解鎖）時，第一隻咬到只啟動壓迫：
///        ・不鎖玩家操作：坡上巨石下滾時推不動、只會被帶著走；平地照常可走可推。
///        ・其他狼不再被停掉，仍可追上繼續咬。
///        ・石頭進入 Rollback：沿目前坡面慢速往下滾。
///        ・AddWolf 不被吞掉，咬住數照常累積，達到 PlayerMovement.wolvesToRespawn 才重生。
///   2. 重置沿用既有 PlayerRespawnSystem 的黑屏重生事件；巨石在黑屏中、玩家傳送前先放回 Boulder Spawn。
///      不重載場景。
///
/// 沒設定好（沒拖 Player Spawn、找不到石頭）或挑戰還沒開始（石頭還鎖著）→ 只走原本狼咬流程。
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

    [Tooltip("觸發備援重生前的等待秒數。到平面、卡住、逾時都會先等這段時間，讓畫面不要硬切。")]
    public float afterFlatDelay = 0.8f;

    [Tooltip("壓迫開始後超過這麼多秒仍沒有被狼咬滿時，啟用備援重生。設 0 或負數＝關閉逾時備援。")]
    public float maxRollbackSeconds = 15f;

    [Header("防卡死備援")]
    [Tooltip("巨石滾到平面後，如果玩家還沒被狼咬滿，是否直接觸發重生。")]
    public bool respawnWhenRollbackReachesFlat = true;

    [Tooltip("玩家被壓迫流程鎖住後，幾乎沒有位移持續這麼久，就判定卡死並觸發重生。設 0 或負數＝關閉卡住備援。")]
    public float stuckRespawnSeconds = 4f;

    [Tooltip("玩家位移大於這個距離就視為還有在動，會重置卡住計時。")]
    public float stuckMovementThreshold = 0.25f;

    [Tooltip("巨石開始下滾後先寬限幾秒，再開始計算玩家卡住時間，避免剛起步就誤判。")]
    public float stuckCheckGraceSeconds = 1f;

    [Tooltip("壓迫流程逾時時是否觸發重生。關掉則只印警告，通常建議保持開啟避免卡死。")]
    public bool respawnOnRollbackTimeout = true;

    [Tooltip("重置後巨石是否直接解鎖（可以推）。關掉＝巨石鎖在 Boulder Spawn，要重新拉拉桿")]
    public bool unlockBoulderAfterReset = true;

    [Tooltip("玩家腳下坡度（度）達到這個值，被狼咬到才會觸發巨石強推。低於它視為平地，只走一般狼咬。")]
    public float minSlopeAngleForPressure = 8f;

    private Rigidbody _boulderRb;
    private bool _failing;
    private bool _rollbackStarted;
    private bool _exitReached;
    private bool _resetPending;
    private bool _resetRoutineStarted;
    private PlayerMovement _player;

    /// <summary>失敗流程進行中（咬到第一口 → 重置完成之前）。</summary>
    public static bool IsFailing => Instance != null && Instance._failing;

    /// <summary>
    /// 巨石壓迫期間（咬到第一口 → 重置完成之前）不管是哪條路觸發重生（狼咬滿、備援…），
    /// 玩家都要回 Challenge Player Spawn。PlayerRespawnSystem.TriggerRespawn() 會先問這裡。
    /// </summary>
    public static bool TryGetChallengeRespawnPos(out Vector3 pos)
    {
        BoulderChallengeController c = Instance;
        if (c != null && c._failing && c.challengePlayerSpawn != null)
        {
            pos = c.challengePlayerSpawn.position;
            return true;
        }
        pos = Vector3.zero;
        return false;
    }

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
    /// PlayerMovement.AddWolf() 呼叫。回傳值保留舊介面；現在永遠不吞狼咬，讓死亡門檻仍由 AddWolf 統一管理。
    /// </summary>
    public static bool NotifyWolfBite(PlayerMovement pm)
    {
        BoulderChallengeController c = Instance;
        if (c == null || !c.isActiveAndEnabled || !c.IsConfigured) return false;
        if (c._failing) return false;
        if (!c.IsChallengeRunning) return false;

        // 只有玩家站在山坡上被咬才會觸發巨石強推；平地被咬只走一般狼咬（減速、累積咬數）
        if (pm == null || !pm.isGrounded || pm.GroundSlopeAngle < c.minSlopeAngleForPressure) return false;

        c.BeginPressure(pm);
        return false;
    }

    /// <summary>BoulderFailExitTrigger 呼叫：玩家或巨石被押出坡面了。</summary>
    public void NotifyFailExit()
    {
        // 滾得不夠遠就碰到出口（例如出口放得太靠近坡中）不算，避免咬下去馬上重生
        if (_failing && _rollbackStarted && boulder != null && boulder.RollbackDistance >= boulder.rollbackMinDistance) _exitReached = true;
    }

    private void BeginPressure(PlayerMovement pm)
    {
        _failing = true;
        _rollbackStarted = false;
        _exitReached = false;
        _resetPending = true;
        _resetRoutineStarted = false;
        _player = pm;

        Debug.Log("【巨石挑戰】第一隻狼咬到＝壓迫開始！玩家可繼續操作（坡上推不動、平地照推），狼群維持攻擊，等待狼咬滿門檻重生。");

        // 不再鎖玩家操作：平地上可以繼續走、繼續推巨石；坡面上巨石下滾時 RollingRockVisual 本來就不登記推石，
        // 玩家推不動，只會被 ApplyExternalSlopePush 帶著走，動作還在。

        StartCoroutine(PressureRoutine());
    }

    private IEnumerator PressureRoutine()
    {
        if (rollbackStartDelay > 0f) yield return new WaitForSeconds(rollbackStartDelay);

        boulder.BeginRollback(_player != null ? _player.transform : null);
        _rollbackStarted = true;

        bool reportedFlat = false;
        bool reportedTimeout = false;
        Vector3 lastMovingPlayerPos = _player != null ? _player.transform.position : Vector3.zero;
        float stillTimer = 0f;
        float t = 0f;
        while (_failing && !PlayerRespawnSystem.IsAnyRespawning)
        {
            t += Time.deltaTime;

            if (!reportedFlat && boulder.RollbackReachedFlat)
            {
                reportedFlat = true;
                Debug.Log($"【巨石挑戰】巨石已壓到平面，已滾 {boulder.RollbackDistance:F1} 公尺。");
                if (respawnWhenRollbackReachesFlat)
                {
                    yield return TriggerFallbackRespawn($"巨石已滾到平面（已滾 {boulder.RollbackDistance:F1}m）");
                    yield break;
                }
            }

            if (!reportedTimeout && maxRollbackSeconds > 0f && t >= maxRollbackSeconds)
            {
                reportedTimeout = true;
                if (respawnOnRollbackTimeout)
                {
                    yield return TriggerFallbackRespawn($"壓迫時間超過 {maxRollbackSeconds:F1}s");
                    yield break;
                }
                Debug.LogWarning("【巨石挑戰】壓迫時間已超過保險秒數，但 respawnOnRollbackTimeout 關閉，所以只印警告。");
            }

            if (_exitReached)
            {
                yield return TriggerFallbackRespawn($"玩家或巨石已離開坡面出口（已滾 {boulder.RollbackDistance:F1}m）");
                yield break;
            }

            if (_player != null && stuckRespawnSeconds > 0f && t >= Mathf.Max(0f, stuckCheckGraceSeconds))
            {
                Vector3 playerPos = _player.transform.position;
                float moved = Vector2.Distance(new Vector2(playerPos.x, playerPos.y), new Vector2(lastMovingPlayerPos.x, lastMovingPlayerPos.y));
                if (moved > Mathf.Max(0.01f, stuckMovementThreshold))
                {
                    lastMovingPlayerPos = playerPos;
                    stillTimer = 0f;
                }
                else
                {
                    stillTimer += Time.deltaTime;
                    if (stillTimer >= stuckRespawnSeconds)
                    {
                        yield return TriggerFallbackRespawn($"玩家幾乎不動 {stillTimer:F1}s（小於 {stuckMovementThreshold:F2}m）");
                        yield break;
                    }
                }
            }

            yield return null;
        }
    }

    private IEnumerator TriggerFallbackRespawn(string reason)
    {
        if (_resetRoutineStarted) yield break;

        _resetRoutineStarted = true;
        Debug.LogWarning($"【巨石挑戰】備援重生：{reason}。避免玩家被鎖住但狼咬數不足而卡死。");
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

        _resetPending = true;
        _resetRoutineStarted = true;

        // 剛好有別的重生在跑（TriggerRespawn 會被擋掉）：等它結束再接。
        // 若那一輪黑屏已經透過 OnResettablesReset 收掉挑戰，就不要再排第二次重生。
        float wait = 0f;
        while (rs.IsRespawning && wait < 10f) { wait += Time.deltaTime; yield return null; }
        if (!_resetPending) yield break;

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
        _resetRoutineStarted = false;
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
        _resetPending = false;
        _resetRoutineStarted = false;
        WolfEnemy.ChallengeFailHalt = false;
        if (boulder != null) boulder.EndRollback();
    }
}
