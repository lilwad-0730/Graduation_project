using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 主選單總管（★1011 重寫成「單一入口」）。
///
/// 主選單的樣子是美術做的：四個圖片物件（menu_slection_start／_setting／_credits／_quiting），各自有碰撞框，
/// 滑鼠點擊由它們身上原本的元件處理：
///   開始   → StartMenuSceneTransition（EasyTransition 轉場進第一關）
///   設定   → CameraFocusOnClick（鏡頭移到設定頁）
///   製作名單 → CameraFocusOnClick（鏡頭移到名單頁）
///   離開   → QuitGameOnClick
///
/// 以前這支另外幫四個圖片「補 UI Button」並自己走一套碎裂轉場／彈出面板——但圖片不是 UI，
/// 滑鼠根本點不到那些 Button，只有鍵盤 Enter 會觸發，而且走的是另一套轉場，
/// 結果同一個「開始」有兩條路、兩套轉場、兩套卡死旗標。
///
/// 現在：
///   ・每個動作只有一份實作（就是上面那幾個元件），滑鼠點擊與鍵盤（上下／W S 選、Enter／空白鍵確定、Esc 返回）
///     最後都呼叫同一個方法，不會有兩條路。
///   ・進主選單時通知 GameFlow：上一輪留下來的狀態統一在那裡清掉（讓遊戲可以一直循環）。
///   ・舊的 UI 面板（SettingsPanel／CreditsPanel／QuitPanel 那一套）不再由這裡打開，
///     欄位保留只是為了不弄壞場景存檔。
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("（舊）UI 按鈕陣列：已不使用，保留欄位避免弄壞場景存檔")]
    public UnityEngine.UI.Button[] menuButtons;
    public MenuGlitchEffect[] glitchEffects;

    [Header("（舊）子面板：已不由這裡打開，保留欄位避免弄壞場景存檔")]
    public SettingsPanelController settingsPanel;
    public QuitPanelController quitPanel;
    public CreditsPanelController creditsPanel;

    [Header("角色與音效")]
    public MenuCharacterController characterController;
    public AudioClip bgmClip;
    public AudioClip selectSfx;
    public AudioClip confirmSfx;
    public AudioSource sfxAudioSource;

    [Header("轉場目標場景名稱（找不到 StartMenuSceneTransition 時的備用）")]
    public string targetSceneName = "CottonCastle";   // ★1019 第一關是棉花堡（獨立場景）

    // 選單項目（順序：0=開始 1=設定 2=製作名單 3=離開，也就是畫面上由上到下的順序）
    private class Item
    {
        public string key;
        public GameObject go;
        public MenuSpriteHoverEffect hover;
        public System.Action activate;
    }
    private readonly System.Collections.Generic.List<Item> _items = new System.Collections.Generic.List<Item>();
    private int _kbIndex = -1;          // 鍵盤目前選到哪一個（-1＝沒用鍵盤）
    private CameraFocusOnClick _backFocus;   // 名單頁的「返回」，Esc 用

    /// <summary>
    /// 主選單的滑鼠點擊現在由這支統一偵測（見 HandleMouseClick）。
    /// 美術原本的 StartMenuSceneTransition／CameraFocusOnClick／QuitGameOnClick 看到這個是 true 就不再自己處理 OnMouseDown，
    /// 避免同一下點擊觸發兩次；場景裡沒有這支主控時它們維持原本的行為。
    /// </summary>
    public static bool OwnsClicks { get; private set; }

    private void OnDestroy()
    {
        if (OwnsClicks) OwnsClicks = false;
    }

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 在選單初始化之前先清好上一輪的狀態（場景載入事件比 Awake 晚）
        GameFlow.EnterMainMenu();
        OwnsClicks = true;
    }

    private void Start()
    {
        if (AudioManager.Instance != null && bgmClip != null)
        {
            AudioManager.Instance.PlayBGM(bgmClip);
        }

        if (sfxAudioSource == null)
        {
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
        }

        BuildItems();
        ReportHealth();
    }

    /// <summary>
    /// 進主選單的體檢（只印 log）：「玩一輪回來按鈕沒反應」要查的東西都在這裡——
    /// 時間、游標、攝影機、EventSystem，以及上一輪留在 DontDestroyOnLoad 裡沒清掉的物件。
    /// </summary>
    private void ReportHealth()
    {
        var sb = new System.Text.StringBuilder("【主選單】進場體檢\n");
        sb.Append("  timeScale=").Append(Time.timeScale).Append("  游標 lock=").Append(Cursor.lockState).Append(" visible=").Append(Cursor.visible).Append('\n');
        sb.Append("  Camera.main=").Append(Camera.main != null ? Camera.main.name : "null").Append("，啟用中的攝影機 ").Append(Camera.allCamerasCount).Append(" 台\n");
        sb.Append("  EventSystem ").Append(FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Exclude).Length).Append(" 個\n");

        // 取得 DontDestroyOnLoad 場景的根物件：丟一個臨時物件進去，再問它所在的場景
        GameObject probe = new GameObject("[DDOL probe]");
        DontDestroyOnLoad(probe);
        var roots = probe.scene.GetRootGameObjects();
        sb.Append("  DontDestroyOnLoad 根物件 ").Append(roots.Length - 1).Append(" 個：");
        foreach (GameObject r in roots) if (r != probe) sb.Append(r.name).Append("、");
        Destroy(probe);
        Debug.Log(sb.ToString());
    }

    private void BuildItems()
    {
        CollectClickTargets();
        _items.Clear();
        string[] keys = { "start", "setting", "credits", "quiting" };
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (string key in keys)
        {
            GameObject found = null;
            foreach (Transform t in all)
            {
                string n = t.gameObject.name.ToLower();
                if (n.StartsWith("menu_slection_") && n.Contains(key)) { found = t.gameObject; break; }
            }

            if (found == null)
            {
                Debug.LogWarning("【主選單】找不到名稱含「menu_slection_" + key + "」的選單項目，該功能不會有作用。");
                continue;
            }

            Item item = new Item { key = key, go = found, hover = found.GetComponent<MenuSpriteHoverEffect>() };
            switch (key)
            {
                case "start": item.activate = ActivateStart; break;
                case "setting": item.activate = () => FocusOf(found); break;
                case "credits": item.activate = () => FocusOf(found); break;
                case "quiting": item.activate = ActivateQuit; break;
            }
            _items.Add(item);
        }

        // 名單頁的返回（Esc 用）：名字含 (back) 又有 CameraFocusOnClick 的那個
        foreach (CameraFocusOnClick f in FindObjectsByType<CameraFocusOnClick>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (f.gameObject.name.ToLower().Contains("back")) { _backFocus = f; break; }
        }

        Debug.Log("【主選單】選單項目 " + _items.Count + "/4 已接好；點擊由各項目原本的元件處理，鍵盤走同一個方法。");
    }

    private float _nextHeartbeat;

    private void Update()
    {
        HandleMouseClick();
        InputHeartbeat();

        if (_items.Count == 0) return;

        // 滑鼠一動就把鍵盤選取的高亮收掉，避免畫面上同時有兩個被選中
        if (_kbIndex >= 0 && (Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f))
        {
            ClearKeyboardSelection();
        }

        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) MoveSelection(+1);
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) MoveSelection(-1);
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
        {
            if (_kbIndex < 0) MoveSelection(+1);   // 還沒選過就先選第一個，再按一次確定
            else ActivateSelected();
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_backFocus != null) _backFocus.Focus();
        }
    }

    /// <summary>
    /// 進選單後前 20 秒，每 2 秒印一行輸入狀態：遊戲視窗有沒有焦點、滑鼠位置有沒有在動、左鍵有沒有被偵測到。
    /// 「按了沒反應」時，看這幾行就知道是遊戲根本沒收到滑鼠（視窗沒焦點／輸入被擋），還是收到了但沒點到按鈕。
    /// </summary>
    private void InputHeartbeat()
    {
        if (Time.realtimeSinceStartup < _nextHeartbeat) return;
        if (_nextHeartbeat > 0f && Time.timeSinceLevelLoad > 20f) return;
        _nextHeartbeat = Time.realtimeSinceStartup + 2f;
        Debug.Log("【主選單】輸入心跳：遊戲視窗有焦點=" + Application.isFocused + "  滑鼠位置=" + Input.mousePosition + "  左鍵按住=" + Input.GetMouseButton(0) + "  滑鼠存在=" + Input.mousePresent);
    }

    // ── 滑鼠點擊：統一偵測 ─────────────────────────────────────
    // 為什麼不靠各項目自己的 OnMouseDown：那個要 Unity 從攝影機打射線、挑「最前面的碰撞盒」才會送訊息，
    // 只要有別的碰撞盒、別的攝影機、焦點或播放狀態出狀況，就會「看得到按鈕卻按不動」，而且沒有任何 log 可查。
    // 這裡自己在選單的攝影機座標上直接問「滑鼠點的位置有哪些 2D 碰撞盒」，只認選單要的那幾種元件，
    // 別的碰撞盒擋在前面也不影響；每次點擊都印一行 log，沒反應時就知道是沒點到、還是點到了誰。
    private void HandleMouseClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            foreach (Camera c in Camera.allCameras) { if (cam == null || c.depth > cam.depth) cam = c; }
        }
        if (cam == null)
        {
            Debug.LogWarning("【主選單】點擊：找不到任何攝影機，無法判斷點到哪裡。");
            return;
        }

        Vector3 mp = Input.mousePosition;
        Vector3 wp = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, Mathf.Abs(cam.transform.position.z)));

        // 不靠物理引擎：直接用「圖片的範圍（SpriteRenderer.bounds）＋碰撞框範圍」判斷點在哪個選單項目上。
        // 這樣就算碰撞框沒登記、被關掉、位置偏掉，或別的碰撞盒擋在前面，都不影響；
        // 文字（TextMeshPro）是蓋在圖片上面的 UI，也不會擋住這個判斷。
        GameObject best = null;
        float bestArea = float.MaxValue;
        GameObject nearest = null;
        float nearestDist = float.MaxValue;
        foreach (GameObject go in _clickTargets)
        {
            if (go == null || !go.activeInHierarchy) continue;
            if (!TryGetBounds(go, out Bounds b)) continue;

            Vector3 probe = new Vector3(wp.x, wp.y, b.center.z);
            if (b.Contains(probe))
            {
                // 範圍有重疊時，選「點離自己中心最近」的那個（用範圍大小正規化）。
                // 不能比面積：四個按鈕大小幾乎一樣，面積相同時會固定選到清單裡排最前面的，
                // 結果點「設定」跑出「開始」、點「製作名單」跑出「設定」……整排往上錯一格。
                float nx = (probe.x - b.center.x) / Mathf.Max(0.001f, b.extents.x);
                float ny = (probe.y - b.center.y) / Mathf.Max(0.001f, b.extents.y);
                float score = nx * nx + ny * ny;
                if (score < bestArea) { bestArea = score; best = go; }
            }
            else
            {
                float d = Mathf.Sqrt(b.SqrDistance(probe));
                if (d < nearestDist) { nearestDist = d; nearest = go; }
            }
        }

        if (best == null)
        {
            Debug.Log("【主選單】點擊 (" + wp.x.ToString("F1") + ", " + wp.y.ToString("F1") + ")：沒點到選單按鈕"
                      + (nearest != null ? "（最近的是「" + nearest.name + "」，差 " + nearestDist.ToString("F1") + " 單位）" : "（場景裡找不到任何可點的選單項目）"));
            return;
        }

        Debug.Log("【主選單】點擊 (" + wp.x.ToString("F1") + ", " + wp.y.ToString("F1") + ")：命中「" + best.name + "」");
        PlaySfx(confirmSfx, 0.8f);

        StartMenuSceneTransition sst = best.GetComponent<StartMenuSceneTransition>();
        if (sst != null) { sst.BeginTransition(); return; }
        CameraFocusOnClick focus = best.GetComponent<CameraFocusOnClick>();
        if (focus != null) { focus.Focus(); return; }
        QuitGameOnClick quit = best.GetComponent<QuitGameOnClick>();
        if (quit != null) quit.Activate();
    }

    /// <summary>可以點的選單物件（身上有開始／聚焦／離開元件的），進選單時掃一次。</summary>
    private readonly System.Collections.Generic.List<GameObject> _clickTargets = new System.Collections.Generic.List<GameObject>();

    private void CollectClickTargets()
    {
        _clickTargets.Clear();
        foreach (StartMenuSceneTransition c in FindObjectsByType<StartMenuSceneTransition>(FindObjectsInactive.Include)) if (c != null && !_clickTargets.Contains(c.gameObject)) _clickTargets.Add(c.gameObject);
        foreach (CameraFocusOnClick c in FindObjectsByType<CameraFocusOnClick>(FindObjectsInactive.Include)) if (c != null && !_clickTargets.Contains(c.gameObject)) _clickTargets.Add(c.gameObject);
        foreach (QuitGameOnClick c in FindObjectsByType<QuitGameOnClick>(FindObjectsInactive.Include)) if (c != null && !_clickTargets.Contains(c.gameObject)) _clickTargets.Add(c.gameObject);
    }

    private static bool TryGetBounds(GameObject go, out Bounds b)
    {
        b = default;
        bool has = false;
        // 有碰撞框就用碰撞框（美術畫的點擊範圍，剛好框住按鈕）；沒有才用圖片範圍。
        // 圖片本身帶透明邊，範圍會比按鈕大、跟隔壁按鈕重疊。
        Collider2D c2 = go.GetComponent<Collider2D>();
        if (c2 != null && c2.enabled) { b = c2.bounds; has = true; }
        else
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) { b = sr.bounds; has = true; }
        }
        return has;
    }

    private void MoveSelection(int delta)
    {
        int next = _kbIndex < 0 ? (delta > 0 ? 0 : _items.Count - 1) : (_kbIndex + delta + _items.Count) % _items.Count;
        SetKeyboardSelection(next);
        PlaySfx(selectSfx, 0.5f);
    }

    private void SetKeyboardSelection(int index)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].hover != null) _items[i].hover.SetSelected(i == index);
        }
        _kbIndex = index;
    }

    private void ClearKeyboardSelection()
    {
        foreach (Item it in _items) if (it.hover != null) it.hover.SetSelected(false);
        _kbIndex = -1;
    }

    private void ActivateSelected()
    {
        if (_kbIndex < 0 || _kbIndex >= _items.Count) return;
        PlaySfx(confirmSfx, 0.8f);
        _items[_kbIndex].activate?.Invoke();
    }

    // ── 動作：每個動作只有一份實作，就是各項目原本的元件 ──────────────
    private void ActivateStart()
    {
        StartMenuSceneTransition sst = FindFirstObjectByType<StartMenuSceneTransition>();
        if (sst != null)
        {
            sst.BeginTransition();
            return;
        }

        // 備用：場景裡沒有 StartMenuSceneTransition 時，直接開新局並載入第一關
        Debug.LogWarning("【主選單】找不到 StartMenuSceneTransition，改用備用路線直接載入 " + targetSceneName);
        GameFlow.StartNewRun();
        string testerStart = TesterMode.StartScene;   // ★1016 測試模式指定的起始關卡（沒有就照常）
        SceneLoader.Load(!string.IsNullOrEmpty(testerStart) && Application.CanStreamedLevelBeLoaded(testerStart) ? testerStart : targetSceneName);
    }

    private static void FocusOf(GameObject item)
    {
        CameraFocusOnClick f = item.GetComponent<CameraFocusOnClick>();
        if (f != null) f.Focus();
        else Debug.LogWarning("【主選單】" + item.name + " 身上沒有 CameraFocusOnClick，鍵盤無法開啟這一頁。");
    }

    private void ActivateQuit()
    {
        QuitGameOnClick q = FindFirstObjectByType<QuitGameOnClick>();
        if (q != null) q.Activate();
        else GameFlow.QuitApplication();
    }

    private void PlaySfx(AudioClip clip, float volume)
    {
        if (clip != null && sfxAudioSource != null) sfxAudioSource.PlayOneShot(clip, AudioManager.ScaleSfx(volume));
    }

    // ── 舊介面（別的腳本還會呼叫，保留成不做事／轉交）──────────────────
    /// <summary>MenuGlitchEffect 會呼叫。現在選取由 MenuSpriteHoverEffect 處理，這裡只轉給角色動畫。</summary>
    public void OnButtonHoverOrSelect(GameObject buttonGo)
    {
        if (characterController != null) characterController.OnMenuHoverOrSelect();
    }

    /// <summary>舊的子面板（設定／離開／名單 UI 面板）關閉時會呼叫。面板已不由這裡打開，不需要做事。</summary>
    public void OnSubPanelClosed(int returnButtonIndex) { }
}
