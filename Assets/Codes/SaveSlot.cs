using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1110 本機多存檔槽（展場多人輪流玩用）。
///
///  存哪裡（Application.persistentDataPath 底下）：
///    一般、預設槽        progress.json                      ← 跟以前完全一樣，不搬不改
///    一般、槽 A          slots/A/progress.json
///    展示、預設槽        exhibit/progress.json
///    展示、槽 A          exhibit/slots/A/progress.json
///  展示與一般分開資料夾，重設展示（ResetExhibit）只會刪 exhibit 資料夾，碰不到一般存檔。
///
///  跟著槽走的：存檔點、安全位置、崩潰紀錄、繪本解鎖頁數、關卡開場漫畫看過沒（後兩個以前存在 PlayerPrefs，會串檔，所以搬進槽裡）。
///  不跟槽走（全機共用）：音量、解析度等設定（PlayerPrefs）。
///  預設槽（非展示）的繪本進度與開場漫畫看過沒，仍然用 PlayerPrefs，行為跟以前一模一樣。
///
///  怎麼指定槽：
///    · 編輯器／Development Build：命令列 -slot A、-exhibit（-exhibitreset 開機時先清掉展示資料夾）。
///    · 正式版：命令列的 -slot／-exhibit 只有在「看門狗重開」（有 -hangrestart）時才認，
///      這樣當機重開會回到同一個槽；玩家與展場人員不會自己誤觸。
///    · 程式裡：SaveSlot.Select("A", false)——只能在主選單呼叫（遊戲中換槽會把進度寫到錯的地方，所以拒絕）。
/// </summary>
public static class SaveSlot
{
    /// <summary>目前的槽名。空字串＝預設槽。固定大寫、只含英數與 _ -。</summary>
    public static string Name { get { EnsureInit(); return _name; } }
    /// <summary>是不是展示模式（存檔放在 exhibit 資料夾）。</summary>
    public static bool Exhibit { get { EnsureInit(); return _exhibit; } }
    /// <summary>目前這個槽用不用舊做法（PlayerPrefs）存繪本進度與開場漫畫：只有「一般＋預設槽」用。</summary>
    public static bool UsesLegacyPrefs { get { EnsureInit(); return _name.Length == 0 && !_exhibit; } }

    private static string _name = "";
    private static bool _exhibit;
    private static bool _init;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _name = ""; _exhibit = false; _init = false; }

    /// <summary>主執行緒呼叫。讀命令列決定槽（只做一次）。</summary>
    internal static void EnsureInit()
    {
        if (_init) return;
        _init = true;
        try
        {
            string[] a = Environment.GetCommandLineArgs();
            bool internalRestart = false;
            for (int i = 0; i < a.Length; i++) if (a[i] == "-hangrestart") internalRestart = true;
            bool allowed = Application.isEditor || Debug.isDebugBuild || internalRestart;
            if (!allowed) return;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == "-slot" && i + 1 < a.Length) _name = Sanitize(a[i + 1]);
                else if (a[i] == "-exhibit") _exhibit = true;
                else if (a[i] == "-exhibitreset" && !internalRestart && (Application.isEditor || Debug.isDebugBuild)) _wipeExhibitOnInit = true;
            }
            if (_wipeExhibitOnInit) { _wipeExhibitOnInit = false; WipeExhibitFolder(); }
            if (_name.Length > 0 || _exhibit) Debug.Log("【存檔槽】本次使用：" + Describe() + " → " + Dir);
        }
        catch (Exception e) { Debug.LogWarning("【存檔槽】讀命令列失敗，用預設槽：" + e.Message); _name = ""; _exhibit = false; }
    }
    private static bool _wipeExhibitOnInit;

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.ToUpperInvariant())
        {
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-') sb.Append(c);
            if (sb.Length >= 16) break;
        }
        return sb.ToString();
    }

    /// <summary>目前槽的資料夾。</summary>
    public static string Dir
    {
        get
        {
            EnsureInit();
            string root = Application.persistentDataPath;
            if (_exhibit) root = Path.Combine(root, "exhibit");
            if (_name.Length > 0) root = Path.Combine(Path.Combine(root, "slots"), _name);
            return root;
        }
    }

    public static string Describe() { EnsureInit(); return (_exhibit ? "展示" : "一般") + "／" + (_name.Length == 0 ? "預設槽" : "槽 " + _name); }

    /// <summary>看門狗重開時要帶的參數（背景執行緒也能叫，不碰 Unity API；請先在主執行緒 EnsureInit 過）。</summary>
    internal static string RelaunchArgs()
    {
        string s = "";
        if (_name.Length > 0) s += " -slot " + _name;
        if (_exhibit) s += " -exhibit";
        return s;
    }

    /// <summary>換槽。只能在主選單（或還沒進關卡時）呼叫；換之前會先把目前槽的進度寫進磁碟。成功回傳 true。</summary>
    public static bool Select(string slotName, bool exhibit)
    {
        EnsureInit();
        string n = Sanitize(slotName);
        if (n == _name && exhibit == _exhibit) return true;
        string scene = SceneManager.GetActiveScene().name;
        if (ProgressSave.IsGameplay(scene) && Application.isPlaying)
        {
            Debug.LogWarning("【存檔槽】遊戲進行中不能換槽（會把進度寫到錯的槽），請先回主選單");
            return false;
        }
        ProgressSave.OnSlotChanging();
        _name = n; _exhibit = exhibit;
        ProgressSave.OnSlotChanged();
        StoryProgress.ReloadFromStore();
        Debug.Log("【存檔槽】換到 " + Describe() + " → " + Dir);
        return true;
    }

    /// <summary>重設展示：只刪 exhibit 資料夾（展示用的所有槽），一般存檔不受影響。若目前正在展示模式，順便把記憶體裡的進度清掉。</summary>
    public static void ResetExhibit()
    {
        EnsureInit();
        if (_exhibit) ProgressSave.OnSlotChanging();
        WipeExhibitFolder();
        if (_exhibit) { ProgressSave.OnSlotChanged(); StoryProgress.ReloadFromStore(); }
        Debug.Log("【存檔槽】展示資料已重設（一般存檔沒動）");
    }

    private static void WipeExhibitFolder()
    {
        try
        {
            string p = Path.Combine(Application.persistentDataPath, "exhibit");
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        catch (Exception e) { Debug.LogWarning("【存檔槽】清展示資料夾失敗：" + e.Message); }
    }

    /// <summary>清空某個「具名槽」（一般或展示）。預設槽不提供清除，避免誤刪原本的存檔。</summary>
    public static void ClearNamedSlot(string slotName, bool exhibit)
    {
        EnsureInit();
        string n = Sanitize(slotName);
        if (n.Length == 0) { Debug.LogWarning("【存檔槽】預設槽不提供清除"); return; }
        bool isCurrent = n == _name && exhibit == _exhibit;
        if (isCurrent) ProgressSave.OnSlotChanging();
        try
        {
            string root = Application.persistentDataPath;
            if (exhibit) root = Path.Combine(root, "exhibit");
            string p = Path.Combine(Path.Combine(root, "slots"), n);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        catch (Exception e) { Debug.LogWarning("【存檔槽】清除失敗：" + e.Message); }
        if (isCurrent) { ProgressSave.OnSlotChanged(); StoryProgress.ReloadFromStore(); }
    }
}
