#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>展示用選單：Play 中放一個會丟例外的小物件，讓「防崩潰回報 → AI 自動修復」整條路跑一次。</summary>
public static class AutoFixDemoMenu
{
    [MenuItem("Tools/AutoFix/示範：製造一個程式錯誤（Play 中使用）")]
    private static void Make()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("請先按 Play，再選這個選單。"); return; }
        // 展示要能重複做：清掉「這個錯誤已經試修過」的記憶，不然第二次會被略過（實測發生過）
        SessionState.SetString("AutoFix.Attempted", "");
        SessionState.SetInt("AutoFix.RunCount", 0);
        SessionState.SetFloat("AutoFix.LastRunTime", -9999f);
        SessionState.SetFloat("AutoFix.RetryAfter", 0f);
        new GameObject("[AutoFix 示範錯誤]").AddComponent<AutoFixDemo>();
    }
}
#endif
