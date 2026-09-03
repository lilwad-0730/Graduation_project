using UnityEngine;

/// <summary>
/// 動畫 state 名稱容錯：腳本寫「flying」、controller 卻叫「fly」（living birds 套件的 crow 就是這樣），
/// 直接 Animator.Play 會刷 Animator.GotoState 警告、鳥也永遠不拍翅。
/// 這裡先用 HasState 確認，找不到就試別名，全都沒有就安靜放棄。
/// </summary>
public static class AnimStateResolver
{
    private static readonly string[][] Aliases =
    {
        new[] { "flying", "fly", "Fly", "Flying", "flyStraight" },
        new[] { "idle", "Idle" },
        new[] { "worried", "Worried", "warn" },
        new[] { "landing", "Landing", "land", "Land" },
        new[] { "die", "Die", "death", "Death", "dead" },
        new[] { "run", "Run", "running" },
        new[] { "walk", "Walk", "walking" },
    };

    /// <summary>找出 controller 第 0 層真的存在的 state 名（先原名、再別名）。</summary>
    public static bool TryResolve(Animator anim, string requested, out string resolved)
    {
        resolved = null;
        if (anim == null || string.IsNullOrEmpty(requested) || anim.runtimeAnimatorController == null) return false;
        if (anim.HasState(0, Animator.StringToHash(requested))) { resolved = requested; return true; }
        foreach (string[] group in Aliases)
        {
            bool inGroup = false;
            foreach (string s in group) { if (s == requested) { inGroup = true; break; } }
            if (!inGroup) continue;
            foreach (string s in group)
            {
                if (s == requested) continue;
                if (anim.HasState(0, Animator.StringToHash(s))) { resolved = s; return true; }
            }
        }
        return false;
    }

    /// <summary>安全版 Animator.Play：state 不存在就什麼都不做（回傳 false），不噴警告。</summary>
    public static bool PlaySafe(Animator anim, string requested, float normalizedTime = 0f)
    {
        if (!TryResolve(anim, requested, out string state)) return false;
        anim.Play(state, 0, normalizedTime);
        return true;
    }

    /// <summary>安全版 Animator.CrossFade。</summary>
    public static bool CrossFadeSafe(Animator anim, string requested, float duration)
    {
        if (!TryResolve(anim, requested, out string state)) return false;
        anim.CrossFade(state, duration);
        return true;
    }
}
