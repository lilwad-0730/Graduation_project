using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>鳥的兩種攻擊：鎖「發現當下的位置」，或鎖「預判她會跑到的位置」。</summary>
public enum BirdAttackType { FixedPosition, PredictedPosition }

/// <summary>
/// ★1002 荒原鳥群攻擊排程（全場共用，static，不用掛在場景上）。
///
/// 兩件事：
///   1. 挑攻擊類型：加權隨機，但同一種最多連續 maxSameTypeInRow 次，超過就強制換另一種。
///      玩家猜不到下一隻是哪種，但也不是純看運氣。
///   2. Wind 結束後放行排隊的鳥：每隻間隔 queuedReleaseInterval 秒，避免風一停十隻同時鎖定。
///
/// 參數預設值在這裡，想在 Inspector 調就用 DesertBeatDirector 上的「★1002 鳥攻擊排程」欄位，
/// 它每次套用都會把數字寫進來。換場景自動歸零。
/// </summary>
public static class BirdAttackScheduler
{
    public static float fixedWeight = 1f;
    public static float predictedWeight = 1f;
    public static int maxSameTypeInRow = 2;
    public static float queuedReleaseInterval = 0.45f;

    private static BirdAttackType _lastType = BirdAttackType.FixedPosition;
    private static int _sameCount = 0;
    private static float _nextQueuedRelease = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _lastType = BirdAttackType.FixedPosition;
        _sameCount = 0;
        _nextQueuedRelease = 0f;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _sameCount = 0;
        _nextQueuedRelease = 0f;
    }

    /// <summary>挑下一次攻擊類型並記入歷史。</summary>
    public static BirdAttackType PickNextType()
    {
        float wf = Mathf.Max(0f, fixedWeight);
        float wp = Mathf.Max(0f, predictedWeight);
        BirdAttackType pick;

        if (wf <= 0f && wp <= 0f) pick = BirdAttackType.PredictedPosition;
        else if (wp <= 0f) pick = BirdAttackType.FixedPosition;
        else if (wf <= 0f) pick = BirdAttackType.PredictedPosition;
        else pick = Random.value * (wf + wp) < wf ? BirdAttackType.FixedPosition : BirdAttackType.PredictedPosition;

        // 同一種連續太多次就強制換（兩種權重都 > 0 才有意義）
        int limit = Mathf.Max(1, maxSameTypeInRow);
        if (wf > 0f && wp > 0f && _sameCount >= limit && pick == _lastType)
        {
            pick = pick == BirdAttackType.FixedPosition ? BirdAttackType.PredictedPosition : BirdAttackType.FixedPosition;
        }

        if (_sameCount > 0 && pick == _lastType) _sameCount++;
        else _sameCount = 1;
        _lastType = pick;
        return pick;
    }

    /// <summary>
    /// 排隊的鳥在風停後領一個放行時間（Time.time）。
    /// 每領一次，下一隻就被往後排 queuedReleaseInterval 秒。
    /// </summary>
    public static float ReserveQueuedRelease()
    {
        float t = Mathf.Max(Time.time, _nextQueuedRelease);
        _nextQueuedRelease = t + Mathf.Max(0.05f, queuedReleaseInterval);
        return t;
    }
}
