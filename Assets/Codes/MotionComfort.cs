using UnityEngine;

/// <summary>
/// ★1115 動態強度（減少晃動）：一個全域 0～1 的係數，晃動、鏡頭頓挫、風線這類「會讓人暈」的視覺效果都乘它。
///  1＝照原本設計；0＝完全不晃（效果關掉）。存在 PlayerPrefs，換場景、重開遊戲都記得。
///  目前接上的：墜落晃動（FallSwayFX）、墜落風線（FallWindStreaksFX）、重落地鏡頭頓挫與塵土（RuinsHeavyLandingFX）。
///  之後別的效果要尊重這個設定，乘上 MotionComfort.Scale 就好。
///  還沒有設定畫面的滑桿（要動場景與美術），目前可在程式或 Console 用 MotionComfort.Scale = 0.5f 調整。
/// </summary>
public static class MotionComfort
{
    private const string Key = "MotionComfortScale";
    private static float _scale = -1f;

    /// <summary>0～1，預設 1。</summary>
    public static float Scale
    {
        get
        {
            if (_scale < 0f)
            {
                try { _scale = Mathf.Clamp01(PlayerPrefs.GetFloat(Key, 1f)); }
                catch { _scale = 1f; }
            }
            return _scale;
        }
        set
        {
            _scale = Mathf.Clamp01(value);
            try { PlayerPrefs.SetFloat(Key, _scale); PlayerPrefs.Save(); } catch { }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { _scale = -1f; }
}
