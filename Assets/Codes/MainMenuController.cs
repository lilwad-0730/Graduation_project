using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// 主選單核心流程控制器
/// 管理 4 個選單按鈕 (開始遊戲、設定、離開、製作人員與來源)
/// 預設焦點、鍵盤/手把/滑鼠導覽、音效播放、面板切換與主角狀態連動
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("選單按鈕陣列 (順序: 0=開始遊戲, 1=設定, 2=離開, 3=製作人員與來源)")]
    public Button[] menuButtons;
    public MenuGlitchEffect[] glitchEffects;

    [Header("子面板引用")]
    public SettingsPanelController settingsPanel;
    public QuitPanelController quitPanel;
    public CreditsPanelController creditsPanel;

    [Header("角色與音效")]
    public MenuCharacterController characterController;
    public AudioClip bgmClip;
    public AudioClip selectSfx;
    public AudioClip confirmSfx;
    public AudioSource sfxAudioSource;

    [Header("轉場目標場景名稱")]
    public string targetSceneName = "SampleScene";

    private int _selectedIndex = 0;
    private bool _isInputDisabled = false;
    private GameObject _lastSelectedGameObject;

    private void Awake()
    {
        _isInputDisabled = false;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        PrepareNewGameRun();
    }

    private void Start()
    {
        // 確保 EventSystem 存在且啟用
        if (EventSystem.current != null)
        {
            EventSystem.current.enabled = true;
        }

        // 播放背景音樂
        if (AudioManager.Instance != null && bgmClip != null)
        {
            AudioManager.Instance.PlayBGM(bgmClip);
        }

        if (sfxAudioSource == null)
        {
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
        }

        // 為所有按鈕綁定點擊事件 (加入防呆，防止 Inspector 欄位為空時拋出 NullReferenceException)
        if (menuButtons != null)
        {
            if (menuButtons.Length > 0 && menuButtons[0] != null) menuButtons[0].onClick.AddListener(OnStartGameClicked);
            if (menuButtons.Length > 1 && menuButtons[1] != null) menuButtons[1].onClick.AddListener(OnSettingsClicked);
            if (menuButtons.Length > 2 && menuButtons[2] != null) menuButtons[2].onClick.AddListener(OnQuitClicked);
            if (menuButtons.Length > 3 && menuButtons[3] != null) menuButtons[3].onClick.AddListener(OnCreditsClicked);
        }

        // 預設焦點設為第 0 項「開始遊戲」
        if (menuButtons != null && menuButtons.Length > 0 && menuButtons[0] != null)
        {
            SelectButton(0, true);
        }

        // 列印除錯報告
        PrintDebugStatus();
    }

    private void PrintDebugStatus()
    {
        bool endingMode = EndCredits.EndingMode;
        bool isAnyRespawning = PlayerRespawnSystem.IsAnyRespawning;
        float ts = Time.timeScale;
        EventSystem es = EventSystem.current;
        bool esEnabled = es != null && es.enabled;
        GameObject curSel = es != null ? es.currentSelectedGameObject : null;
        bool startActive = menuButtons != null && menuButtons.Length > 0 && menuButtons[0] != null && menuButtons[0].gameObject.activeInHierarchy;
        bool startInteractable = menuButtons != null && menuButtons.Length > 0 && menuButtons[0] != null && menuButtons[0].interactable;
        var inputModule = es != null ? es.currentInputModule : null;
        bool inputModuleEnabled = inputModule != null && inputModule.enabled;

        Debug.Log("=== MAIN MENU INPUT DEBUG ===\n" +
                  $"EndingMode = {endingMode}\n" +
                  $"IsAnyRespawning = {isAnyRespawning}\n" +
                  $"isCutsceneFrozen = (Gameplay Level Separated)\n" +
                  $"Time.timeScale = {ts}\n" +
                  $"EventSystem.current = {(es != null ? es.gameObject.name : "null")}\n" +
                  $"EventSystem.enabled = {esEnabled}\n" +
                  $"currentSelectedGameObject = {(curSel != null ? curSel.name : "null")}\n" +
                  $"StartButton.interactable = {startInteractable}\n" +
                  $"StartButton.activeInHierarchy = {startActive}\n" +
                  $"UI Input Module enabled = {inputModuleEnabled} ({(inputModule != null ? inputModule.GetType().Name : "None")})\n" +
                  "==============================");

        ReportRaycastBlockers();
    }

    /// <summary>
    /// 診斷「按鈕看得到卻按不動」：
    /// 對第一顆按鈕的位置做一次 UI Raycast，列出擋在最上層的是誰。
    /// 若最上層不是按鈕本身，那個東西就是把點擊吃掉的兇手
    /// (常見是上一個場景留下來的 DontDestroyOnLoad 全螢幕 Canvas，
    ///  例如轉場碎片、黑幕、演出用的遮罩，raycastTarget 沒關掉)。
    /// </summary>
    private void ReportRaycastBlockers()
    {
        if (EventSystem.current == null)
        {
            Debug.LogError("【選單診斷】場景中沒有 EventSystem，所有 UI 點擊都不會生效！");
            return;
        }

        if (menuButtons == null || menuButtons.Length == 0 || menuButtons[0] == null)
        {
            Debug.LogError("【選單診斷】menuButtons 陣列是空的或第 0 顆沒指定！請在 Inspector 補上按鈕。");
            return;
        }

        RectTransform rt = menuButtons[0].GetComponent<RectTransform>();
        if (rt == null) return;

        // 取按鈕中心的螢幕座標
        Canvas canvas = menuButtons[0].GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCam, rt.position);

        PointerEventData ped = new PointerEventData(EventSystem.current) { position = screenPoint };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);

        if (results.Count == 0)
        {
            Debug.LogError($"【選單診斷】在「{menuButtons[0].name}」的位置完全打不到任何 UI！\n" +
                           "可能原因：該按鈕的 Canvas 沒有 GraphicRaycaster、按鈕 raycastTarget 被關掉，或按鈕不在畫面上。");
            return;
        }

        string list = "";
        for (int i = 0; i < results.Count; i++)
        {
            Canvas c = results[i].gameObject.GetComponentInParent<Canvas>();
            list += $"  {i + 1}. {results[i].gameObject.name}" +
                    $"  (Canvas: {(c != null ? c.name + " / sortingOrder " + c.sortingOrder : "無")})\n";
        }

        bool topIsButton = results[0].gameObject == menuButtons[0].gameObject ||
                           results[0].gameObject.transform.IsChildOf(menuButtons[0].transform);

        if (topIsButton)
        {
            Debug.Log($"【選單診斷】✅「{menuButtons[0].name}」位置最上層就是按鈕本身，點擊路徑正常。\n{list}");
        }
        else
        {
            Debug.LogError($"【選單診斷】❌ 點擊被擋住了！最上層是「{results[0].gameObject.name}」，不是按鈕。\n" +
                           $"請找到這個物件把它關掉或把 raycastTarget 取消。完整堆疊：\n{list}");
        }
    }

    private void Update()
    {
        if (_isInputDisabled) return;

        // 檢查 EventSystem 目前選取的物件
        if (EventSystem.current != null)
        {
            GameObject currentSel = EventSystem.current.currentSelectedGameObject;
            if (currentSel != null && currentSel != _lastSelectedGameObject)
            {
                _lastSelectedGameObject = currentSel;
                for (int i = 0; i < menuButtons.Length; i++)
                {
                    if (menuButtons[i] != null && menuButtons[i].gameObject == currentSel)
                    {
                        SelectButton(i, false);
                        break;
                    }
                }
            }
        }
    }

    public void OnButtonHoverOrSelect(GameObject buttonGo)
    {
        if (_isInputDisabled) return;

        for (int i = 0; i < menuButtons.Length; i++)
        {
            if (menuButtons[i] != null && menuButtons[i].gameObject == buttonGo)
            {
                if (_selectedIndex != i)
                {
                    SelectButton(i, false);
                }
                else
                {
                    // 已是目前項目，連動角色
                    if (characterController != null)
                    {
                        characterController.OnMenuHoverOrSelect();
                    }
                }
                break;
            }
        }
    }

    private void SelectButton(int index, bool isInitial)
    {
        if (index < 0 || index >= menuButtons.Length) return;

        _selectedIndex = index;

        // 更新按鈕 Glitch 與高亮狀態
        for (int i = 0; i < glitchEffects.Length; i++)
        {
            if (glitchEffects[i] != null)
            {
                glitchEffects[i].SetSelectedState(i == _selectedIndex);
            }
        }

        // 設定 EventSystem Focus
        if (EventSystem.current != null && menuButtons[_selectedIndex] != null)
        {
            EventSystem.current.SetSelectedGameObject(menuButtons[_selectedIndex].gameObject);
            _lastSelectedGameObject = menuButtons[_selectedIndex].gameObject;
        }

        // 播放選擇音效
        if (!isInitial && selectSfx != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(selectSfx, AudioManager.ScaleSfx(0.5f));
        }

        // 連動角色減速與轉向注視
        if (characterController != null)
        {
            characterController.OnMenuHoverOrSelect();
        }
    }

    // --- 按鈕點擊響應 ---

    private void OnStartGameClicked()
    {
        if (_isInputDisabled) return;
        _isInputDisabled = true;

        PrepareNewGameRun();
        PlayConfirmSfx();

        // 停用選單按鈕 Raycast，防止重複觸發
        DisableMenuButtons();

        // 啟動畫面碎裂後重新組合轉場
        if (SceneTransitionController.Instance == null)
        {
            GameObject transitionGo = new GameObject("SceneTransitionController");
            transitionGo.AddComponent<SceneTransitionController>();
        }

        SceneTransitionController.Instance.TransitionToScene(targetSceneName);
    }

    private void PrepareNewGameRun()
    {
        Time.timeScale = 1f;
        BookTransitionManager.ResetTransientState();
        EndCredits.EndingMode = false;
        PlayerRespawnSystem.IsAnyRespawning = false;
        MirrorWallAbsorbCutscene.IsAnyCutsceneRunning = false;

        StoryCardPlayer storyCardPlayer = FindAnyObjectByType<StoryCardPlayer>();
        if (storyCardPlayer != null)
        {
            storyCardPlayer.ReleaseCurtain();
        }
    }

    private void OnSettingsClicked()
    {
        if (_isInputDisabled) return;
        _isInputDisabled = true;

        PlayConfirmSfx();
        if (settingsPanel != null)
        {
            settingsPanel.OpenPanel();
        }
    }

    private void OnQuitClicked()
    {
        if (_isInputDisabled) return;
        _isInputDisabled = true;

        PlayConfirmSfx();
        if (quitPanel != null)
        {
            quitPanel.OpenPanel();
        }
    }

    private void OnCreditsClicked()
    {
        if (_isInputDisabled) return;
        _isInputDisabled = true;

        PlayConfirmSfx();
        if (creditsPanel != null)
        {
            creditsPanel.OpenPanel();
        }
    }

    public void OnSubPanelClosed(int returnButtonIndex)
    {
        _isInputDisabled = false;
        SelectButton(returnButtonIndex, false);
    }

    private void PlayConfirmSfx()
    {
        if (confirmSfx != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(confirmSfx, AudioManager.ScaleSfx(0.8f));
        }
    }

    private void DisableMenuButtons()
    {
        foreach (var btn in menuButtons)
        {
            if (btn != null) btn.interactable = false;
        }
    }
}
