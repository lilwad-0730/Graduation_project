using UnityEngine;

/// <summary>
/// 【演出期間定住玩家】的共用計數鎖。
///
/// 為什麼要有這支：GuidanceLight（光絮運鏡）跟 QuestClearBarrierRock（巨石消散）各自寫了一套
/// 「先把玩家目前的 constraints 存起來 → 設成 FreezeAll → 演完再還原」。
/// 兩段演出一重疊就會出事：
///
///     光絮   Begin：存下「正常」→ 設 FreezeAll
///     巨石   Begin：存下「FreezeAll」← 存到別人凍結後的狀態！
///     光絮   End  ：還原成「正常」
///     巨石   End  ：還原成「FreezeAll」← 玩家從此永遠不能動
///
/// 收集完最後一張日誌時，光絮演出跟巨石解鎖演出剛好會前後腳發生，就會踩到這個。
/// 動畫速度（GuidanceLight 會設 animator.speed = 0）也是一樣的問題。
///
/// 改成計數鎖之後：第一個 Acquire 才存狀態並凍結，最後一個 Release 才還原，
/// 中間怎麼疊都不會存到別人的凍結狀態。
/// </summary>
public static class PlayerCutsceneHold
{
    private static int _count;
    private static PlayerMovement _pm;
    private static Rigidbody _rb;
    private static Animator _anim;

    private static RigidbodyConstraints _savedConstraints;
    private static bool _savedUseGravity;
    private static float _savedAnimSpeed = 1f;
    private static bool _animFrozen;

    /// <summary>目前有沒有人抓著玩家。</summary>
    public static bool IsHeld => _count > 0;

    /// <summary>目前有幾段演出同時抓著玩家（除錯用）。</summary>
    public static int HoldCount => _count;

    /// <summary>
    /// 抓住玩家：不能動、不會沉。freezeAnimator = true 時連動畫也定格。
    /// 每呼叫一次就要對應一次 Release()。
    /// </summary>
    public static void Acquire(PlayerMovement pm, bool freezeAnimator)
    {
        if (pm == null) return;

        // 換人了（重生換物件、換場景）→ 舊的計數作廢，重新開始，不要拿舊玩家的狀態去還原新玩家
        if (_pm != null && _pm != pm)
        {
            _count = 0;
            _animFrozen = false;
            _pm = null; _rb = null; _anim = null;
        }

        if (_count == 0)
        {
            _pm = pm;

            _rb = pm.GetComponent<Rigidbody>();
            if (_rb == null) _rb = pm.GetComponentInParent<Rigidbody>();
            if (_rb != null)
            {
                _savedConstraints = _rb.constraints;
                _savedUseGravity = _rb.useGravity;

                // 保險：萬一撿到的就是 FreezeAll（有人沒走這支就自己凍了），
                // 存成 PlayerMovement 開場設定的那組，不要把「凍住」當成正常狀態存下來。
                if (_savedConstraints == RigidbodyConstraints.FreezeAll)
                {
                    _savedConstraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
                    Debug.LogWarning("[PlayerCutsceneHold] 接手時玩家已經是 FreezeAll，改用 PlayerMovement 的標準約束當還原值，避免把凍結狀態存成正常狀態。");
                }
            }

            _anim = pm.animator;
            if (_anim == null) _anim = pm.GetComponentInChildren<Animator>();
            if (_anim != null) _savedAnimSpeed = _anim.speed <= 0.01f ? 1f : _anim.speed;

            _animFrozen = false;
        }

        _count++;
        pm.isCutsceneFrozen = true;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.useGravity = false;
            _rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // 只要有任何一段演出要求定格動畫，就定格；等最後一個放手才還原
        if (freezeAnimator && _anim != null)
        {
            _anim.speed = 0f;
            _animFrozen = true;
        }
    }

    /// <summary>放開玩家。要等所有抓著的人都放手，才會真的還原。</summary>
    public static void Release()
    {
        if (_count <= 0) return;

        _count--;
        if (_count > 0) return;   // 還有別人抓著，先不還原

        if (_rb != null)
        {
            _rb.constraints = _savedConstraints;
            _rb.useGravity = _savedUseGravity;
        }
        if (_anim != null && _animFrozen)
        {
            _anim.speed = _savedAnimSpeed;
        }
        if (_pm != null)
        {
            _pm.isCutsceneFrozen = false;
        }

        _animFrozen = false;
        _pm = null; _rb = null; _anim = null;
    }

    /// <summary>
    /// 【最後防線】不管現在有幾個人抓著，全部放掉並還原。
    /// 重生流程用：重生的規則是「重生＝玩家一定能動」，任何殘留的演出鎖都不該活過重生。
    /// </summary>
    public static void ForceReleaseAll()
    {
        if (_count == 0 && _pm == null) return;

        Debug.LogWarning($"[PlayerCutsceneHold] 強制解除全部演出鎖（原本還有 {_count} 個沒放手）。");
        _count = 1;
        Release();
        _count = 0;
    }
}
