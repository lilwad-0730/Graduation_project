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
        new GameObject("[AutoFix 示範錯誤]").AddComponent<AutoFixDemo>();
    }
}
#endif
