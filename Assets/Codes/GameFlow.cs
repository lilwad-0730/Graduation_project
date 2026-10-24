using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1011 遊戲流程管理（讓遊戲可以「主選單 → 玩一輪 → 結局 → 主選單 → 再玩一輪」一直循環）。
///
/// 為什麼需要：
///   原本「一輪遊戲的狀態」散在十幾個 static 欄位與 DontDestroyOnLoad 物件裡，各腳本各自記得要不要歸零。
///   Unity 換場景不會重置 static，所以只要有一個漏掉，第二輪就會出怪事
///   （轉場被打斷的旗標卡住 → 主選單按鈕按了沒反應；結局旗標沒歸零 → 第二輪沒有結局…）。
///
/// 做法：
///   ・一個地方記流程狀態：Boot → MainMenu → Playing → Ending → MainMenu …
///   ・一個地方做「一輪的重置」（ResetRunState）：進主選單、按開始新遊戲時都會跑，
///     每一項各自包 try/catch，一項壞掉不會讓後面的項目被跳過；做了什麼都會印一行 log。
///   ・新增「會跨場景留著的 static」時，把它的重置加進 ResetRunState（或訂閱 OnRunReset），就不會再漏。
///
/// 自動啟動，不用掛在場景上。
/// </summary>
public static class GameFlow
{
    public enum FlowState { Boot, MainMenu, Playing, Ending }

    public const string MenuSceneName = "MainMenuScene";
    const string BookSceneName = "Book";

    /// <summary>目前流程狀態。</summary>
    public static FlowState State { get; private set; } = FlowState.Boot;

    /// <summary>第幾輪（按「開始遊戲」才 +1；在編輯器直接從關卡按 Play 算第 1 輪）。</summary>
    public static int RunIndex { get; private set; }

    /// <summary>每次做「一輪的重置」時通知；其他系統有自己的跨場景狀態，可以訂閱這個自己清。</summary>
    public static event Action OnRunReset;

    static int _lastMenuResetFrame = -1;
    static bool _hooked;

    // 編輯器關閉「重新載入 Domain」時，static 不會自己歸零，這裡保證每次 Play 都從頭開始
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        State = FlowState.Boot;
        RunIndex = 0;
        OnRunReset = null;
        _lastMenuResetFrame = -1;
        _hooked = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!_hooked)
        {
            _hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        ApplySceneState(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        ApplySceneState(scene);
    }

    static void ApplySceneState(Scene scene)
    {
        if (scene.name == MenuSceneName)
        {
            EnterMainMenu();
        }
        else if (scene.name == BookSceneName && EndCredits.EndingMode)
        {
            State = FlowState.Ending;
            Log("進入結局（繪本＋名單）");
        }
        else if (scene.name == BookSceneName && StoryProgress.ReaderMode)
        {
            State = FlowState.MainMenu;   // ★1023 主選單的「故事到目前為止」：還算在主選單，不開新的一輪
            Log("主選單翻繪本（故事到目前為止）");
        }
        else if (State != FlowState.Playing)
        {
            // 從主選單走進第一關，或在編輯器直接從某一關按 Play
            if (State == FlowState.Boot) RunIndex = Mathf.Max(RunIndex, 1);
            State = FlowState.Playing;
            Log("進入遊戲中（場景 " + scene.name + "，第 " + RunIndex + " 輪）");
        }
    }

    /// <summary>
    /// 到了主選單。同一個畫面幀內重複呼叫只做一次
    /// （場景載入事件與 MainMenuController.Awake 都會呼叫，Awake 比較早，確保選單初始化前就是乾淨的）。
    /// </summary>
    public static void EnterMainMenu()
    {
        if (_lastMenuResetFrame == Time.frameCount && State == FlowState.MainMenu) return;
        _lastMenuResetFrame = Time.frameCount;
        State = FlowState.MainMenu;
        ResetRunState("回到主選單");
    }

    /// <summary>按下「開始遊戲」：新的一輪開始，先把上一輪留下來的東西全部清掉再載入第一關。</summary>
    public static void StartNewRun()
    {
        RunIndex++;
        State = FlowState.Playing;
        StoryProgress.ResetForNewRun();   // ★1023 新的一輪：繪本從頭開
        ResetRunState("開始第 " + RunIndex + " 輪");
        Log("新的一輪開始");
    }

    /// <summary>統一的「回主選單」出口（結局名單結束、任何需要退回主選單的地方都走這裡）。</summary>
    public static void ReturnToMainMenu(string reason)
    {
        Log("回主選單：" + reason);
        ResetRunState("離開前清理（" + reason + "）");
        SceneLoader.Load(MenuSceneName);
    }

    /// <summary>離開遊戲（編輯器內停止 Play）。</summary>
    public static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ══════════════════════════════════════════════════════════
    // 一輪的重置
    // ══════════════════════════════════════════════════════════
    public static void ResetRunState(string why)
    {
        int failed = 0;
        void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { failed++; Debug.LogWarning("[GameFlow] 重置項目「" + name + "」失敗：" + e.Message); }
        }

        // 時間／輸入
        Step("時間與游標", () =>
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        });

        // 轉場（兩套都是 DontDestroyOnLoad；只清「卡住很久」的，正常正在收尾的轉場不打斷）
        Step("轉場卡死檢查", () =>
        {
            if (SceneTransitionController.Instance != null) SceneTransitionController.Instance.ResetIfStuck(6f);
            if (PixeLadder.EasyTransition.SceneTransitioner.Instance != null) PixeLadder.EasyTransition.SceneTransitioner.Instance.ResetIfStuck(6f);
        });

        // 流程旗標
        Step("重生與跨場景出生點", () => { PlayerRespawnSystem.IsAnyRespawning = false; PlayerRespawnSystem.ClearPendingSceneSpawn(); });
        Step("漫畫轉場狀態", BookTransitionManager.ResetTransientState);
        Step("結局模式", () => { EndCredits.EndingMode = false; });
        Step("鏡牆演出旗標", () => { MirrorWallAbsorbCutscene.IsAnyCutsceneRunning = false; });
        Step("第一次狼死亡演出", () => { FirstWolfDeathStory.ShownThisRun = false; });

        // 文字卡／日誌
        Step("文字卡與黑幕", () =>
        {
            // 不用 StoryCardPlayer.Instance：它找不到會自己建一個新的，這裡只想清理已經存在的
            StoryCardPlayer scp = UnityEngine.Object.FindFirstObjectByType<StoryCardPlayer>();
            if (scp != null) { scp.ForceStopAll(); scp.ReleaseCurtain(); }
        });
        Step("日誌張數", StoryCardNoteHook.ResetCount);

        // 玩家相關的 static
        Step("玩家抓取鎖", PlayerCutsceneHold.ResetAll);
        Step("跳躍封鎖", () => { PlayerMovement.JumpBlockedByStair = false; PlayerMovement.JumpBlockedByRuinsChase = false; });
        Step("挑戰失敗狼停止旗標", () => { WolfEnemy.ChallengeFailHalt = false; });
        Step("回頭看狼教學旗標", () => { WolfEnemy.PlayerHasLookedBack = false; });   // ★1018 不清的話，同一次開機的下一位玩家看不到廢墟的回頭提示

        // 荒原／鳥群／風
        Step("遮陽傘與掩體", () => { UmbrellaZone.IsPlayerUnderUmbrella = false; WindGustSystem.ClearShelterCount(); });
        Step("鳥群全域壓制", () => { IndividualBirdEnemy.SuppressAllUntil = -1f; IndividualBirdEnemy.WindFreeFromX = float.PositiveInfinity; });

        // 鏡頭旁路
        Step("鏡頭邊界旁路", () =>
        {
            CinemachineCameraConfiner3D.isBypassed = false; CinemachineCameraConfiner3D.customTarget = null;
            SimpleCameraBounds.isBypassed = false; SimpleCameraBounds.customTarget = null;
        });

        Step("OnRunReset 訂閱者", () => { OnRunReset?.Invoke(); });

        Log("一輪重置完成（" + why + "）" + (failed > 0 ? "，有 " + failed + " 項失敗（看上面的警告）" : ""));
    }

    static void Log(string msg)
    {
        Debug.Log("【流程】" + msg + "  [狀態 " + State + " / 第 " + RunIndex + " 輪]");
    }
}
