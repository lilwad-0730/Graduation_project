using System.Collections;
using UnityEngine;

/// <summary>
/// 影子怪物觸發區。
/// 掛載於場景中放置的隱形碰撞框物件上。
/// 玩家進入此區域時呼叫 ShadowMonsterController.ActivateChase()，使怪物開始出現並追逐。
///
/// 【場景建置】
///   1. 在 dark glasses 場景中建立空物件，命名如 "ShadowMonsterTrigger"。
///   2. 新增 Box Collider，調整大小覆蓋玩家前進路徑，勾選 IsTrigger = true。
///   3. 掛載此腳本（不需要其他設定）。
///   4. 將物件 Mesh Renderer 關閉或不加，使其在遊戲中不可見。
///
/// 【注意】
///   重生後怪物回到 Dormant 狀態，玩家若再次進入此觸發區會再次啟動怪物（符合重置需求）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShadowMonsterTriggerZone : MonoBehaviour, IResettable
{
    [Tooltip("觸發後是否只允許啟動一次（建議關閉，讓重生後重新觸發）")]
    public bool triggerOnce = false;

    [Header("★1021 觸發位置")]
    [Tooltip("把觸發區往玩家來的方向（左）提前幾米。試玩回饋：有時玩家能直接跑過去、怪物追不到；提早一點觸發，只是加難度，不是必中。0＝照場景原位置。注意：玻璃館的存檔點在 X≈-36.6、觸發區原本在 X≈-35.2。提前超過約 1.4 米，存檔點就會跑到觸發區「後面」，重生後不會再踩到——這種情況由下面的「重生後強制出場」補上，但建議別超過 2 米。")]
    public float moveEarlierMeters = 1f;

    [Header("★1021 等演出跑完才出場（備用機制）")]
    [Tooltip("勾選：光球／鏡牆演出、文字轉場還沒跑完（包括還沒開始）時，就算玩家踩到觸發區，也先不叫怪物；等演出整段結束（再多等 afterCutsceneDelay 秒）才出場。這樣把觸發往前移也不會變成『光球演出跟怪物同時出現』。")]
    public bool waitForCutscenes = true;
    [Tooltip("演出全部結束後，再多等幾秒才讓怪物出場（給玩家一個喘息）。")]
    public float afterCutsceneDelay = 0f;
    [Tooltip("演出卡住時的保險：玩家踩到觸發區後，演出（光球吸入、鏡牆碎裂）過了這麼多秒還沒跑完，怪物就直接出場。但如果這時文字卡還在播，會等文字卡播完才出場，不會在玩家讀字時冒出來。")]
    public float forceAfterSeconds = 30f;

    [Header("★1021 重生後強制出場（備用機制）")]
    [Tooltip("重生後如果玩家已經在觸發區的後面（存檔點在觸發區之後，不會再踩到），玩家開始移動後過了這麼多秒，怪物就強制出場。重生在觸發區前面的不受影響，照常踩到才出場。")]
    public float respawnForceSeconds = 10f;

    private Collider _col;
    private bool _hasTriggered = false;
    private Coroutine _waitRoutine;
    private Coroutine _respawnRoutine;

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;
        if (moveEarlierMeters != 0f)
        {
            Vector3 before = transform.position;
            transform.position = before + Vector3.left * moveEarlierMeters;   // 玩家往右走，往左＝更早碰到
            Debug.Log($"【觸發點】★1021 影子怪物觸發區提前 {moveEarlierMeters:F1} 米：X {before.x:F1} → {transform.position.x:F1}");
        }
    }
    /// <summary>★1021 演出（鏡牆／光球演出、文字卡、玩家被鎖定）是不是還在跑。</summary>
    private static bool CutsceneBusy()
    {
        if (MirrorWallAbsorbCutscene.IsAnyCutsceneRunning) return true;
        // ★1021 就算演出「還沒開始」也要等：場景裡有鏡牆演出、而且還沒完整跑完（光球吸入＋碎裂＋過場文字），怪物就不准出場
        foreach (MirrorWallAbsorbCutscene m in Object.FindObjectsByType<MirrorWallAbsorbCutscene>(FindObjectsInactive.Exclude))
            if (m != null && m.isActiveAndEnabled && !m.SequenceCompleted) return true;
        StoryCardPlayer card = StoryCardPlayer.Instance;
        if (card != null && card.IsPlaying) return true;
        return false;
    }

    private IEnumerator ActivateAfterCutscenes()
    {
        float t0 = Time.time;
        Debug.Log("【觸發點】★1021 演出還沒跑完，影子怪物先等著");
        while (CutsceneBusy())
        {
            float el = Time.time - t0;
            StoryCardPlayer card = StoryCardPlayer.Instance;
            bool reading = card != null && card.IsPlaying;
            if (el >= forceAfterSeconds && !reading)
            {
                Debug.LogWarning($"【觸發點】★1021 演出 {forceAfterSeconds:F0} 秒還沒結束（疑似卡住），強制讓影子怪物出場");
                break;
            }
            if (el >= forceAfterSeconds + 90f) break;      // 文字卡再怎麼久也不會超過這個
            yield return null;
        }
        if (afterCutsceneDelay > 0f) yield return new WaitForSeconds(afterCutsceneDelay);
        _waitRoutine = null;
        Debug.Log($"【觸發點】★1021 演出結束（等了 {Time.time - t0:F1} 秒），影子怪物出場");
        Activate();
    }

    /// <summary>★1021 重生後：玩家如果已經在觸發區後面（不會再踩到），玩家動了之後 respawnForceSeconds 秒強制讓怪物出場。</summary>
    private IEnumerator RespawnForceRoutine()
    {
        float w = 0f;
        while (PlayerRespawnSystem.IsAnyRespawning && w < 30f) { w += Time.deltaTime; yield return null; }   // 等重生整段演完
        w = 0f;
        while (!PlayerRespawnSystem.IsPlayerMovingAfterRespawn && w < 15f) { w += Time.deltaTime; yield return null; }   // 等玩家動
        yield return new WaitForSeconds(Mathf.Max(0f, respawnForceSeconds));

        _respawnRoutine = null;
        if (_hasTriggered) yield break;                                       // 這段時間已經正常出場了
        ShadowMonsterController mon = ShadowMonsterController.Instance;
        if (mon == null || mon.currentState != ShadowMonsterController.MonsterState.Dormant) yield break;
        PlayerMovement pm = Object.FindAnyObjectByType<PlayerMovement>();
        if (pm == null) yield break;
        // 玩家還在觸發區前面 → 會自己踩到，不用強制（也避免死在鏡牆前面的人被怪物追）
        if (pm.transform.position.x < _col.bounds.min.x) yield break;
        // 鏡牆演出還沒完整跑完 → 這個存檔點還不該有怪物
        if (CutsceneBusy()) yield break;
        Debug.Log($"【觸發點】★1021 重生後玩家已在觸發區後面、動了 {respawnForceSeconds:F0} 秒，強制讓影子怪物出場");
        Activate();
    }

    private void Activate()
    {
        if (ShadowMonsterController.Instance != null)
        {
            ShadowMonsterController.Instance.ActivateChase();
            _hasTriggered = true;
        }
        else
        {
            Debug.LogWarning("【觸發點】找不到 ShadowMonsterController！請確認場景中有影子怪物物件。");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggerOnce && _hasTriggered) return;
        if (!IsPlayer(other.gameObject)) return;

        Debug.Log("【觸發點】玩家進入影子怪物觸發區！");

        if (waitForCutscenes && CutsceneBusy())
        {
            if (_waitRoutine == null) _waitRoutine = StartCoroutine(ActivateAfterCutscenes());
            return;
        }
        Activate();
    }

    private bool IsPlayer(GameObject go)
    {
        if (go.CompareTag("Player")) return true;
        if (go.GetComponent<PlayerMovement>() != null) return true;
        return false;
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        if (_waitRoutine != null) { StopCoroutine(_waitRoutine); _waitRoutine = null; }
        if (_respawnRoutine != null) { StopCoroutine(_respawnRoutine); _respawnRoutine = null; }
        _hasTriggered = false;
        if (isActiveAndEnabled && respawnForceSeconds > 0f) _respawnRoutine = StartCoroutine(RespawnForceRoutine());
    }
}
