using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1016 命運之書（24 章提案、25 章廢墟頁規格、26 章研究）：全片只有一本早就畫好的書，每一關翻到一頁。
/// 她走進場景時整頁是鉛筆稿；事情發生，那一格才上墨；死掉的那一格本來就在，第一次死上墨，之後每死一次在格角加一道刻痕。
///
/// 這裡只記「狀態」：哪一頁哪幾格已經上墨、這一頁死了幾次、進場頁／結束頁播過沒。
/// 狀態跨場景保留（一輪遊戲裡），回到主選單（下一位玩家）全部清掉——和 FirstWolfDeathStory.ShownThisRun 同一個規則。
/// 畫面在 FateBookPage，廢墟的觸發在 FateBookRuins，第一次死亡接在 FirstWolfDeathStory。
///
/// 開關：FateBookState.Enabled = false ＝整包關掉（進場、死亡、結束都回到原本的流程）。
/// 每一頁、每一格的開關在 Resources/FateBook/&lt;頁&gt;/page_layout.json（enabled）。
/// </summary>
public static class FateBookState
{
    /// <summary>整包開關。</summary>
    public static bool Enabled = true;

    /// <summary>印每一次上墨、翻頁的紀錄到 Console。</summary>
    public static bool LogEvents = true;

    private class PageState
    {
        public readonly HashSet<string> inked = new HashSet<string>();
        public int deaths;            // 這一頁（這一關）被狼群追擊而死的次數（第一次＝上墨，之後每次＋一道刻痕）
        public bool enterShown;       // 進場頁播過（一輪只播一次）
        public bool endShown;         // 結束頁播過
    }

    private static readonly Dictionary<string, PageState> _pages = new Dictionary<string, PageState>();

    private static PageState Page(string pageId)
    {
        if (string.IsNullOrEmpty(pageId)) pageId = "?";
        PageState p;
        if (!_pages.TryGetValue(pageId, out p)) { p = new PageState(); _pages[pageId] = p; }
        return p;
    }

    public static bool IsInked(string pageId, string panel) { return Page(pageId).inked.Contains(panel); }

    /// <summary>把這一格記成已上墨。回傳 true＝這次才上的（之前沒上過）。</summary>
    public static bool Ink(string pageId, string panel, string why = null)
    {
        if (string.IsNullOrEmpty(panel)) return false;
        bool fresh = Page(pageId).inked.Add(panel);
        if (fresh && LogEvents) Debug.Log("[命運之書] " + pageId + " 頁・格 " + panel + " 上墨" + (string.IsNullOrEmpty(why) ? "" : "（" + why + "）"));
        return fresh;
    }

    public static IEnumerable<string> InkedPanels(string pageId) { return Page(pageId).inked; }
    public static int InkedCount(string pageId) { return Page(pageId).inked.Count; }
    public static bool AnyInked(string pageId) { return _pages.ContainsKey(pageId) && _pages[pageId].inked.Count > 0; }

    public static int Deaths(string pageId) { return Page(pageId).deaths; }
    public static int AddDeath(string pageId)
    {
        PageState p = Page(pageId);
        p.deaths++;
        if (LogEvents) Debug.Log("[命運之書] " + pageId + " 頁・第 " + p.deaths + " 次死");
        return p.deaths;
    }

    public static bool EnterShown(string pageId) { return Page(pageId).enterShown; }
    public static void MarkEnterShown(string pageId) { Page(pageId).enterShown = true; }
    public static bool EndShown(string pageId) { return Page(pageId).endShown; }
    public static void MarkEndShown(string pageId) { Page(pageId).endShown = true; }

    /// <summary>新的一輪：墨跡、刻痕全清（回主選單時自動呼叫；測試選單也可以手動叫）。</summary>
    public static void ResetRun()
    {
        if (LogEvents && _pages.Count > 0) Debug.Log("[命運之書] 新的一輪：墨跡與刻痕全部清掉");
        _pages.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Boot()
    {
        _pages.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 回到主選單＝這一輪結束（和 FirstWolfDeathStory、GameFlow 同一個判斷：場景名含 MainMenu）
        if (scene.name.IndexOf("MainMenu", System.StringComparison.OrdinalIgnoreCase) >= 0) ResetRun();
    }
}
