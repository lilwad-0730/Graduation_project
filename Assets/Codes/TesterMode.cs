using System.IO;
using UnityEngine;

/// <summary>
/// ★1016 測試模式（給組員與 Claude 測打包版用；展場的電腦不會有這個檔，所以觀眾碰不到）。
///
/// 什麼時候是開的：
///   ・在 Unity 編輯器裡、或 Development Build：一律開（跟以前一樣）。
///   ・正式打包版：只有在下面兩個位置之一放了 WoNiTa_TESTER.txt 才開（檔案裡有一行 off 就當作關）：
///       1. 遊戲 log 的資料夾（Player.log 旁邊）
///          Mac：~/Library/Logs/FKNM/我你他/（FKNM 是 Player Settings 的公司名）
///          Windows：%USERPROFILE%\AppData\LocalLow\FKNM\我你他\
///       2. 遊戲的存檔資料夾（Application.persistentDataPath）
///          Mac：~/Library/Application Support/FKNM/我你他/
///     ★不放在遊戲旁邊：遊戲放在「桌面」「文件」「下載」時，macOS 會跳出「我你他想要取用桌面檔案夾中的檔案」，
///       沒按之前整個遊戲卡住（1016 實測）。上面兩個位置不會跳這個詢問。
///
/// 開了之後可以用：
///   ・F3 或 Ctrl+G：無敵（免疫鳥與風暴石化，原本 PlayerPetrification 的測試開關；★1023 從 F10 改 F3，F10 留給「直接進結局漫畫」）。正式版沒有這個檔就按不出來。
///   ・檔案裡寫一行 start=場景名（例如 start=dark glasses），主選單按「開始遊戲」會直接進那一關。
///     可用的場景名：CottonCastle（棉花堡）、SampleScene（墜落＋廢墟）、desert（荒原）、underwater（水下）、dark glasses（星空玻璃館）。
///     寫錯或沒寫就照常從第一關開始。
///   ・★1020 其他設定（一行一個 名稱=值，用 TesterMode.Option("名稱") 讀）：
///       hints=page　　提示線條畫改成「翻頁」出現（LineHintDirector）
///       hideui=1　　　F11 錄影、F12 截圖時把右上角齒輪與 GOD MODE 字樣藏起來
///       recfps=30　　 F11 錄影的每秒張數（預設 30）
///       recwidth=1920 F11 錄影存下來的寬度（預設 1920）
///       shotscale=1　 F12 截圖放大倍數（預設 1＝螢幕原解析度）
/// </summary>
public static class TesterMode
{
    public const string FileName = "WoNiTa_TESTER.txt";

    private static bool _inited;
    private static bool _enabled;
    private static bool _fileFound;
    private static string _startScene;
    private static string _foundPath;
    private static System.Collections.Generic.Dictionary<string, string> _options =
        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>測試功能是否開啟（編輯器／Development Build 一律開；正式版要有測試檔）。</summary>
    public static bool Enabled { get { Init(); return _enabled; } }

    /// <summary>測試檔指定的起始場景（沒有就是 null）。編輯器裡也一樣要有檔才會跳關。</summary>
    public static string StartScene { get { Init(); return _startScene; } }

    /// <summary>★1020 測試檔裡「名稱=值」那一行的值（沒有測試檔、寫 off、或沒有這一行就是 null）。</summary>
    public static string Option(string key)
    {
        Init();
        if (!_fileFound || string.IsNullOrEmpty(key)) return null;
        string v;
        return _options.TryGetValue(key.Trim(), out v) ? v : null;
    }

    /// <summary>★1020 數字設定（讀不到或不是數字就回傳 fallback）。</summary>
    public static float OptionFloat(string key, float fallback)
    {
        string v = Option(key);
        float f;
        if (!string.IsNullOrEmpty(v) && float.TryParse(v.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) return f;
        return fallback;
    }

    private static void Init()
    {
        if (_inited) return;
        _inited = true;
        _enabled = false;
        _startScene = null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _enabled = true;
#endif
        string[] candidates = CandidatePaths();
        foreach (string path in candidates)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                bool off = false;
                string start = null;
                var opts = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Equals("off", System.StringComparison.OrdinalIgnoreCase)) off = true;
                    else if (line.StartsWith("start=", System.StringComparison.OrdinalIgnoreCase))
                    {
                        string s = line.Substring(6).Trim();
                        if (s.Length > 0) start = s;
                    }
                    else if (!line.StartsWith("#") && line.Contains("="))
                    {
                        int eq = line.IndexOf('=');
                        string k = line.Substring(0, eq).Trim();
                        if (k.Length > 0) opts[k] = line.Substring(eq + 1).Trim();
                    }
                }
                if (off) { Debug.Log("【測試模式】" + path + " 寫著 off，不開"); continue; }
                _fileFound = true;
                _foundPath = path;
                _enabled = true;
                _startScene = start;
                _options = opts;
                break;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("【測試模式】讀測試檔失敗：" + e.Message);
            }
        }

        if (_fileFound)
            Debug.LogWarning($"【測試模式】★1016 找到 {_foundPath}：F3／Ctrl+G 無敵可用、F10 直接進結局漫畫" + (_startScene != null ? $"；開始遊戲直接進「{_startScene}」" : "") + "。展場的電腦不要放這個檔。");
    }

    /// <summary>只看不會觸發 macOS「取用桌面／文件檔案夾」詢問的位置。</summary>
    private static string[] CandidatePaths()
    {
        string logDir = null;
        try
        {
            string log = Application.consoleLogPath;
            if (!string.IsNullOrEmpty(log)) logDir = Path.GetDirectoryName(log);
        }
        catch { }

        string dataDir = null;
        try { dataDir = Application.persistentDataPath; } catch { }

        return new string[]
        {
            string.IsNullOrEmpty(logDir) ? null : Path.Combine(logDir, FileName),
            string.IsNullOrEmpty(dataDir) ? null : Path.Combine(dataDir, FileName),
        };
    }
}
