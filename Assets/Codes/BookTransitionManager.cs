using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 漫畫繪本跨關卡轉場管理器 (BookTransitionManager)
/// 
/// 【功能】
///   1. 靜態跨場景參數傳遞：記錄章節開始頁、結束頁、下一關卡場景名稱、自訂出生點。
///   2. 單行代碼觸發漫畫轉場：BookTransitionManager.OpenComicTransition(startPage, endPage, nextSceneName);
///   3. 閱讀完畢後無縫載入下一關卡（支援 SceneTransitionController 碎裂轉場或原生非同步載入）。
/// </summary>
public static class BookTransitionManager
{
    // 是否處於章節轉場模式 (若為 false 則為完整繪本自由翻閱模式)
    public static bool isChapterMode = false;

    // 本次轉場要播放的章節起始頁碼 (0 起算)
    public static int chapterStartPage = 0;

    // 本次轉場要播放的章節結束頁碼 (翻到此頁後提示進入下一關)
    public static int chapterEndPage = -1;

    // 翻完漫畫後要載入的目標關卡名稱 (例如 desert, underwater, dark glasses)
    public static string nextSceneAfterBook = "";

    // 進入新關卡後的自訂重生點名稱
    public static string targetSpawnPointName = "";

    // 標記是否剛剛看完進場開場漫畫（防止重複循環觸發）
    public static bool hasJustFinishedIntro = false;

    /// <summary>
    /// 從任何關卡呼叫此方法，開啟特定章節的漫畫轉場
    /// </summary>
    /// <param name="startPage">漫畫起始頁 (0 起算)</param>
    /// <param name="endPage">漫畫結束頁</param>
    /// <param name="nextSceneName">翻完後進入的下一關卡場景名稱</param>
    /// <param name="spawnPointName">下一關自訂出生點 (選填)</param>
    public static void OpenComicTransition(int startPage, int endPage, string nextSceneName, string spawnPointName = "")
    {
        isChapterMode = true;
        chapterStartPage = startPage;
        chapterEndPage = endPage;
        nextSceneAfterBook = nextSceneName;
        targetSpawnPointName = spawnPointName;

        Debug.Log($"📖【漫畫轉場】啟動漫畫章節轉場！起始頁: {startPage} -> 結束頁: {endPage}，下一關: {nextSceneName}");

        if (SceneTransitionController.Instance != null)
        {
            SceneTransitionController.Instance.TransitionToScene("Book");
        }
        else
        {
            SceneManager.LoadScene("Book");
        }
    }

    /// <summary>
    /// 閱讀完該章節最後一頁時呼叫，自動轉場載入下一關卡
    /// </summary>
    public static void CompleteChapterAndLoadNext()
    {
        string targetScene = nextSceneAfterBook;
        string spawnPoint = targetSpawnPointName;

        // 清除章節狀態
        isChapterMode = false;
        hasJustFinishedIntro = true;
        nextSceneAfterBook = "";
        targetSpawnPointName = "";

        if (string.IsNullOrEmpty(targetScene))
        {
            targetScene = "MainMenuScene";
        }

        Debug.Log($"✨【漫畫轉場】章節閱讀完畢，轉場載入目標關卡: '{targetScene}'");

        // 若有指定出生點，寫入 PlayerPrefs
        if (!string.IsNullOrEmpty(spawnPoint))
        {
            PlayerPrefs.SetString("TargetSpawnPoint", spawnPoint);
        }

        if (SceneTransitionController.Instance != null)
        {
            SceneTransitionController.Instance.TransitionToScene(targetScene);
        }
        else
        {
            SceneManager.LoadScene(targetScene);
        }
    }
}
