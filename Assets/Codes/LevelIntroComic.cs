using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 關卡進場開場漫畫控制器 (LevelIntroComic)
/// 
/// 【功能】
///   掛載在任何關卡場景中 (例如 desert, underwater, dark glasses, SampleScene)。
///   當玩家進入此關卡時，在「畫面變亮看到場景前」先播放本關卡的開場漫畫劇情。
///   閱讀完畢後，畫面平滑淡入變亮，交還主角控制權開始遊玩！
/// 
/// 【特色】
///   - 支援「每次進入都播放」或「僅第一次進入時播放 (存檔記錄)」。
///   - 支援無縫黑屏防穿幫保護：場景剛加載時鏡頭與主角保持黑屏靜止，直到漫畫看完才甦醒。
/// </summary>
public class LevelIntroComic : MonoBehaviour
{
    [Header("📖 關卡開場漫畫設定")]
    [Tooltip("是否在進入本關卡時啟用開場漫畫")]
    public bool enableIntroComic = true;

    [Tooltip("本關卡開場漫畫起始頁碼 (0 起算)")]
    public int introStartPage = 0;

    [Tooltip("本關卡開場漫畫結束頁碼")]
    public int introEndPage = 3;

    [Tooltip("是否僅在首次進入本關卡時播放 (使用 PlayerPrefs 記錄)")]
    public bool playOnceOnly = false;

    [Tooltip("漫畫看完後，畫面亮起的平滑淡入時間 (秒)")]
    public float fadeInDuration = 1.0f;

    private static bool _hasPlayedInSession = false;

    private void Awake()
    {
        if (!enableIntroComic) return;

        string currentScene = SceneManager.GetActiveScene().name;
        string key = "IntroComicPlayed_" + currentScene;

        if (playOnceOnly && PlayerPrefs.GetInt(key, 0) == 1)
        {
            return;
        }

        // 若是由轉場管理器主動呼叫進來的，避免重複觸發
        if (BookTransitionManager.hasJustFinishedIntro)
        {
            BookTransitionManager.hasJustFinishedIntro = false;
            return;
        }

        // 若需要播放開場漫畫，立刻將本關卡作為返回目標，中繼至 Book.unity 播放
        if (playOnceOnly)
        {
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }

        BookTransitionManager.hasJustFinishedIntro = true;
        BookTransitionManager.OpenComicTransition(introStartPage, introEndPage, currentScene);
    }
}
