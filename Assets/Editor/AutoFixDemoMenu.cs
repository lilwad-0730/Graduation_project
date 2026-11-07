#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>展示用選單：Play 中放一個會丟例外的小物件，讓「防崩潰回報 → AI 自動修復」整條路跑一次。</summary>
public static class AutoFixDemoMenu
{
    private const string BuggySource =
@"using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1108 展示「AI 自動修復」用的假錯誤（故意留一個空值 bug）。
/// 用法：Play 中選單 Tools → AutoFix → 示範：製造一個程式錯誤。
/// 被 AI 修好之後，用選單 Tools → AutoFix → 示範：還原示範錯誤 讓 bug 回來，就能重複展示。
/// 正常遊戲裡沒有任何東西會用到這支；展示完不需要的話可以整個刪掉。
/// </summary>
public class AutoFixDemo : MonoBehaviour
{
    private List<int> _scores;   // 故意忘了 new，用到就會 NullReferenceException

    private bool _fired;

    private void Update()
    {
        if (_fired) return;
        _fired = true;
        Debug.Log(""【示範】正在製造一個故意的程式錯誤…"");
        RecordScore(10);
    }

    private void RecordScore(int s)
    {
        _scores.Add(s);
        Debug.Log(""【示範】分數已記錄："" + _scores.Count);
    }
}
";

    [MenuItem("Tools/AutoFix/示範：還原示範錯誤（讓 bug 回來，可重複展示）")]
    private static void RestoreBug()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("請先按 Stop，再還原示範錯誤。"); return; }
        string path = System.IO.Path.Combine(Application.dataPath, "Codes", "AutoFixDemo.cs");
        System.IO.File.WriteAllText(path, BuggySource, new System.Text.UTF8Encoding(false));
        AssetDatabase.Refresh();
        Debug.Log("[AutoFix] 示範錯誤已還原（AutoFixDemo.cs 又有空值 bug 了），編譯完就能再展示一次。");
    }

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
