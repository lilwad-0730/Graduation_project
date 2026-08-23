using UnityEngine;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
public class PlayerRespawnSystem : MonoBehaviour
{
    [Header("擊退重置系統 (純數值偵測)")]
    [Tooltip("當被往左擊飛多遠時判定失敗 (建議 15~20)")]
    public float failKnockbackDistance = 15f;
    [Tooltip("觸發擊飛判定的瞬間向左速度閾值 (小於 -8 代表絕對是被鳥衝撞而不是走路)")]
    public float knockbackVelocityThreshold = -8f;

    [Header("重生點設定")]
    [Tooltip("在場景中建立 Cube 並打勾 IsTrigger，掛上這個 Tag 就可以當作存檔點！")]
    public string respawnPointTag = "RespawnPoint";

    [Header("相機與轉場")]
    public Vector3 cameraOffsetFromPlayer = new Vector3(0, 5, -10f);
    public float fadeDuration = 1.5f;
    public float blackScreenTime = 2.5f;

    // --- 內部狀態追蹤 ---
    private bool _isRespawning = false;
    /// <summary>供外部腳本查詢目前是否正在重生過程中</summary>
    public bool IsRespawning => _isRespawning;
    /// <summary>【Static 全域旗標】不需要物件參考，任何腳本都能直接讀取。重生期間為 true。</summary>
    public static bool IsAnyRespawning = false;
    private bool _isTeleporting = false;
    private Rigidbody _playerRb;
    private Camera _mainCam;
    
    private bool _inKnockbackState = false;
    private float _knockbackStartPosX;
    private Vector3 _lastSafeGroundPos; // 安全踩在地上時的地點
    private Vector3 _initialPlayPos;    // 剛按下 PLAY 時的初始位置
    
    // --- UI 相關 ---
    [Header("🎵 重生與死亡音效 (Death & Respawn SFX)")]
    [Tooltip("角色死亡倒地音效 (例如 主角死亡)")]
    public AudioClip deathSFX;
    [Tooltip("角色復活重生音效 (例如 復活)")]
    public AudioClip respawnSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    [Header("自訂 UI 物件 (若留空，系統會自動幫你生成)")]
    public Image customFadeImage;
    public Text customMessageText;
    public CanvasGroup customMessageCanvasGroup;

    private Text _messageText; 
    private Image _fadeImage;
    private CanvasGroup _messageCanvasGroup;
    private bool _isWaitingForPlayerMove = false;
    private Coroutine _textFadeCoroutine;

    // 將字串改為全英文，避免 Arial 字體在您的 Unity 環境中不支援中文導致整行隱形！
    private string[] encouragements = { 
        "♥ Take a deep breath... ♥", 
        "✿ You're doing great! ✿", 
        "★ It's okay to fall. ★", 
        "♪ You can do this! ♪" 
    };

    private PlayerPetrification GetPetrification()
    {
        PlayerPetrification p = GetComponent<PlayerPetrification>();
        if (p == null) p = GetComponentInChildren<PlayerPetrification>();
        if (p == null) p = GetComponentInParent<PlayerPetrification>();
        return p;
    }

    private PlayerMovement GetMovement()
    {
        PlayerMovement m = GetComponent<PlayerMovement>();
        if (m == null) m = GetComponentInChildren<PlayerMovement>();
        if (m == null) m = GetComponentInParent<PlayerMovement>();
        return m;
    }

    private Rigidbody GetPlayerRigidbody()
    {
        if (_playerRb != null) return _playerRb;
        _playerRb = GetComponent<Rigidbody>();
        if (_playerRb == null) _playerRb = GetComponentInChildren<Rigidbody>();
        if (_playerRb == null) _playerRb = GetComponentInParent<Rigidbody>();
        return _playerRb;
    }

    // 跨場景指定生成點 (由轉場腳本在 LoadScene 前指派)
    public static string NextSceneSpawnTargetName = "";
    public static Vector3? NextSceneCustomSpawnPos = null;

    private Vector3 _activeRespawnPos; // 當前啟用的明確存檔點座標

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // ★ 換場景自動刷新記憶：徹底杜絕跨場景殘留舊存檔點座標！
        StartCoroutine(ApplySceneSpawnPositionRoutine(scene));
    }

    private IEnumerator ApplySceneSpawnPositionRoutine(UnityEngine.SceneManagement.Scene scene)
    {
        yield return null; // 等待 1 幀確保場景內所有物件已完成 Awake 與 Start 初始化

        // 1. 若轉場前有指定特定隱形物件名稱 (如 "SpawnPoint_FromSampleScene")
        if (!string.IsNullOrEmpty(NextSceneSpawnTargetName))
        {
            GameObject targetObj = GameObject.Find(NextSceneSpawnTargetName);
            if (targetObj != null)
            {
                Vector3 targetPos = new Vector3(targetObj.transform.position.x, targetObj.transform.position.y + 0.2f, targetObj.transform.position.z);
                PlayerMovement pm = GetMovement();
                if (pm != null) pm.WarpTo(targetPos);
                else transform.position = targetPos;

                _activeRespawnPos = targetObj.transform.position;
                _initialPlayPos = targetObj.transform.position;

                Debug.Log($"🚩【跨場景出生點】進入新場景 [{scene.name}]，已成功將主角傳送至指定隱形物件 [{NextSceneSpawnTargetName}]：{targetPos}");
                NextSceneSpawnTargetName = "";
                yield break;
            }
            else
            {
                Debug.LogWarning($"⚠️【跨場景出生點】在新場景 [{scene.name}] 中找不到指定的隱形物件 '{NextSceneSpawnTargetName}'，將採用預設位置！");
                NextSceneSpawnTargetName = "";
            }
        }
        // 2. 若轉場前有指定自訂座標
        else if (NextSceneCustomSpawnPos.HasValue)
        {
            Vector3 targetPos = NextSceneCustomSpawnPos.Value;
            PlayerMovement pm = GetMovement();
            if (pm != null) pm.WarpTo(targetPos);
            else transform.position = targetPos;

            _activeRespawnPos = targetPos;
            _initialPlayPos = targetPos;

            Debug.Log($"🚩【跨場景出生點】進入新場景 [{scene.name}]，已成功將主角傳送至自訂座標：{targetPos}");
            NextSceneCustomSpawnPos = null;
            yield break;
        }

        // 3. 預設標準流程：以主角在該場景的起始擺放位置為準
        _activeRespawnPos = transform.position;
        _initialPlayPos = transform.position;
        Debug.Log($"【存檔點系統】進入新場景 [{scene.name}]，重生點記憶已刷新為初始座標：{_activeRespawnPos}");
    }

    private AudioSource _directAudioSource;

    private void PlayDirectSFX(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (_directAudioSource == null)
        {
            _directAudioSource = gameObject.AddComponent<AudioSource>();
            _directAudioSource.playOnAwake = false;
            _directAudioSource.spatialBlend = 0f; // 2D 零衰減直出，保證 100% 清晰響亮
        }
        _directAudioSource.PlayOneShot(clip, volume);
    }

    private void AutoLoadMissingSFX()
    {
        #if UNITY_EDITOR
        if (respawnSFX == null)
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToLower();
            if (sceneName.Contains("underwater"))
            {
                respawnSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/水下/復活.wav");
            }
            else if (sceneName.Contains("desert"))
            {
                respawnSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/復活.wav");
            }
            else
            {
                respawnSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/廢墟/復活.wav");
            }
        }

        if (deathSFX == null)
        {
            deathSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/廢墟/主角死亡（衣服倒下）.mp3");
        }
        #endif
    }

    void Start()
    {
        IsAnyRespawning = false; // 強制重置全域重生靜態旗標，防止舊階段殘留鎖死
        _playerRb = GetPlayerRigidbody();
        _mainCam = Camera.main;

        // 起始點即為最基礎的預設存檔點
        _activeRespawnPos = transform.position;
        _initialPlayPos = transform.position;

        AutoLoadMissingSFX();
        CreatePersistentUI();
    }

    // 允許外部系統 (如手動指定或特製機關) 強制更新存檔點
    public void SetSafeGroundPosition(Vector3 newPos)
    {
        _activeRespawnPos = newPos;
        Debug.Log($"【存檔點系統】外部強制更新重生點至：{_activeRespawnPos}");
    }

    void Update()
    {

        // ===================================
        // 重生點紀錄 (已改為觸發器 Checkpoint 模式)
        // 玩家的重生點現在只會在碰到帶有 RespawnPoint 標籤的方塊時更新
        // 起始點為最一開始的出生點
        // ===================================

        // ===================================
        // 自動墜崖防護系統 (已移除，避免玩家走下坡或下樓梯時誤判死亡)
        // 死亡判定交由 CameraBounds 或其他死亡觸發器來處理！

        // ===================================
        // 擊飛失敗偵測 (距離與速度法)
        // ===================================
        if (_playerRb != null && !_isRespawning)
        {
            // 1. 如果突然承受巨大的向左速度 (小於 -8 代表絕對是被鳥衝撞而不是走路，因走路最快才 -5)
            if (!_inKnockbackState && _playerRb.linearVelocity.x < knockbackVelocityThreshold)
            {
                _inKnockbackState = true;
                _knockbackStartPosX = transform.position.x;
                Debug.Log($"【擊飛偵測】受到撞擊！起始 X：{_knockbackStartPosX}");
            }

            // 2. 在被擊飛的狀態中持續檢查飛行距離
            if (_inKnockbackState)
            {
                float currentDist = _knockbackStartPosX - transform.position.x;
                
                // A. 如果往左飛的距離超過了我們設定的容忍值
                if (currentDist > failKnockbackDistance)
                {
                    Debug.Log($"【擊飛失敗】掉落超過容忍值！({currentDist} > {failKnockbackDistance}) 開始強制轉場！");
                    _inKnockbackState = false;
                    
                    // 重點：傳送到啟用的存檔點
                    StartCoroutine(RespawnSequence(_activeRespawnPos));
                }

                // B. 如何平安解除狀態？當玩家往左的速度幾乎停止、或甚至往右走時，代表安全落地了
                if (_playerRb.linearVelocity.x > -0.5f)
                {
                    _inKnockbackState = false;
                }
            }
        }

        // ===================================
        // 文字消失判定 (偵測玩家左右移動)
        // ===================================
        if (_isWaitingForPlayerMove)
        {
            if (Mathf.Abs(Input.GetAxis("Horizontal")) > 0.1f || (_playerRb != null && Mathf.Abs(_playerRb.linearVelocity.x) > 0.5f))
            {
                _isWaitingForPlayerMove = false;
                if (_textFadeCoroutine != null) StopCoroutine(_textFadeCoroutine);
                _textFadeCoroutine = StartCoroutine(FadeOutText());
            }
        }
    }

    // 碰觸存檔點 (嚴格限制只有 Tag 為 RespawnPoint 的 Trigger 才能設定存檔點)
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(respawnPointTag) || other.CompareTag("RespawnPoint"))
        {
            Vector3 checkpointPos = other.transform.position;

            // ★ 存檔點推進保護：只向前更新最新經過的存檔點，往回走不會倒退回舊存檔點
            if (checkpointPos.x >= _activeRespawnPos.x - 1.0f || Vector3.Distance(checkpointPos, _initialPlayPos) > Vector3.Distance(_activeRespawnPos, _initialPlayPos))
            {
                _activeRespawnPos = checkpointPos;
                Debug.Log($"🚩【存檔點系統】已啟動新存檔點 Tag: RespawnPoint [{other.gameObject.name}]！重生座標更新為：{_activeRespawnPos}");
            }
        }
        // 放坑洞底部的死亡判定區
        else if (other.CompareTag("DeathZone") || other.name.Contains("DeathZone"))
        {
            if (!_isRespawning)
            {
                Debug.Log("【墜崖死亡】玩家碰到 DeathZone，觸發重生！");
                TriggerRespawn();
            }
        }
    }

    // 已經依照要求註解掉！這會避免玩家走到畫面邊緣時被鏡頭框誤判死亡！
    // 如果玩家掉出地圖邊緣需要死亡，請在洞口底部放一個 Tag 為 DeathZone 的 BoxCollider (IsTrigger = true)！
    private void OnTriggerExit(Collider other)
    {
        if (!this.enabled) return; 
        if (_isRespawning) return;

        // if (other.CompareTag("CameraBounds"))
        // {
        //     StopAllCoroutines();
        //     StartCoroutine(RespawnSequence(_lastSafeGroundPos));
        // }
    }

    // 觸發死亡重生轉場 (預設傳送到當前啟用的明確存檔點)
    public void TriggerRespawn()
    {
        this.enabled = true; // 強制開啟，確保重生不會因為被其他腳本停用而死鎖
        if (!_isRespawning)
        {
            Debug.Log($"【重生系統】TriggerRespawn() 正式啟動！將重生至存檔點：{_activeRespawnPos}");
            StartCoroutine(RespawnSequence(_activeRespawnPos));
        }
    }

    // 觸發強制傳送到「指定位置」的重生轉場
    public void TriggerRespawn(Vector3 customSpawnPos)
    {
        this.enabled = true;
        if (!_isRespawning)
        {
            Debug.Log($"【重生系統】TriggerRespawn({customSpawnPos}) 正式啟動！");
            StartCoroutine(RespawnSequence(customSpawnPos));
        }
    }

    // ===================================
    // 原本的轉場演出 (僅傳送，不重載場景)
    // ===================================
    IEnumerator RespawnSequence(Vector3 spawnPos)
    {
        _isRespawning = true;
        IsAnyRespawning = true;  // 全域通知：重生開始
        _isWaitingForPlayerMove = false;

        // 強制確保 UI 一定存在
        if (_fadeImage == null || _messageText == null) CreatePersistentUI();
        
        if (_fadeImage != null)
        {
            _fadeImage.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogError("致命錯誤：無法生成或找到漸黑 UI，轉場將會失去視覺效果！");
        }

        // 強制凍結主角操作，防止漸黑期間按 WASD 破壞重生流程
        PlayerMovement pmCompStart = GetMovement();
        if (pmCompStart != null)
        {
            pmCompStart.isCutsceneFrozen = true;
        }

        // 清除物理動力
        if (_playerRb != null && !_playerRb.isKinematic)
        {
            _playerRb.linearVelocity = Vector3.zero;
            _playerRb.angularVelocity = Vector3.zero;
        }

        // 播放角色死亡音效 (直出播放，保證 100% 聽得到)
        if (deathSFX != null)
        {
            PlayDirectSFX(deathSFX, sfxVolume);
            Debug.Log($"💀【死亡系統】播放死亡音效: {deathSFX.name}");
        }

        // 1. 漸黑
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime; // 使用不受時間暫停影響的真實時間
            if (_fadeImage != null)
            {
                _fadeImage.color = new Color(0, 0, 0, Mathf.Lerp(0f, 1f, timer / fadeDuration));
            }
            if (_playerRb != null && !_playerRb.isKinematic)
            {
                _playerRb.linearVelocity = Vector3.zero;
            }
            yield return null;
        }
        
        if (_fadeImage != null)
            _fadeImage.color = new Color(0, 0, 0, 1f);

        // --- 【重生規則】先清除所有負面效果，再傳送 ---
        PlayerPetrification petrify = GetPetrification();
        if (petrify != null)
        {
            petrify.ClearAllNegativeEffects();
        }

        // --- 呼叫全場景所有 IResettable 物件進行重置 (包含鏡牆演出、光球、黑影怪物、燭火、可破壞地板等) ---
        MonoBehaviour[] allScripts = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var script in allScripts)
        {
            if (script is IResettable resettable && script.gameObject != this.gameObject)
            {
                resettable.ResetToInitialState();
            }
        }

        // --- 傳送至明確存檔點 (稍微抬高 0.2f 確保不陷地) ---
        PlayerMovement pmComponent = GetMovement();
        Vector3 targetPos = new Vector3(spawnPos.x, spawnPos.y + 0.2f, spawnPos.z);
        if (pmComponent != null)
        {
            pmComponent.WarpTo(targetPos);
        }
        else
        {
            transform.position = targetPos;
        }

        if (_mainCam != null)
        {
            GameObject customTarget = GameObject.Find("CameraFollowTarget");
            if (customTarget != null) {
                Vector3 camPos = transform.position + cameraOffsetFromPlayer;
                camPos.y = customTarget.transform.position.y;
                _mainCam.transform.position = camPos;
            } else {
                _mainCam.transform.position = transform.position + cameraOffsetFromPlayer;
            }
            Transform targetFollow = (pmComponent != null) ? pmComponent.GetCameraTarget() : this.transform;

            // 尋找新版 CinemachineCamera
            CinemachineCamera[] vcams3 = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcams3)
            {
                vcam.PreviousStateIsValid = false;
                var t = vcam.Target;
                t.TrackingTarget = targetFollow;
                vcam.Target = t;
                vcam.Follow = targetFollow;
            }

            // 尋找舊版 CinemachineVirtualCamera
            CinemachineVirtualCamera[] vcamsLegacy = FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcamsLegacy)
            {
                vcam.PreviousStateIsValid = false;
                vcam.Follow = targetFollow;
            }

            // 尋找 CinemachineVirtualCameraBase (防呆備用)
            CinemachineVirtualCameraBase[] vcamsBase = FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsSortMode.None);
            foreach (var vcam in vcamsBase)
            {
                vcam.PreviousStateIsValid = false;
                vcam.Follow = targetFollow;
            }
        }

        // --- 強制顯示螢光鼓勵文字 (英文保證顯示) ---
        if (_messageText != null && _messageCanvasGroup != null)
        {
            _messageText.text = encouragements[Random.Range(0, encouragements.Length)];
            _messageCanvasGroup.alpha = 1f;
            _messageText.gameObject.SetActive(true);
        }

        // 2. 黑屏延長維持
        yield return new WaitForSecondsRealtime(blackScreenTime);

        // 3. 漸亮
        timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            if (_fadeImage != null)
            {
                _fadeImage.color = new Color(0, 0, 0, Mathf.Lerp(1f, 0f, timer / fadeDuration));
            }
            yield return null;
        }
        if (_fadeImage != null)
        {
            _fadeImage.color = new Color(0, 0, 0, 0f);
            _fadeImage.gameObject.SetActive(false); 
        }

        // 播放復活重生音效 (直出播放，保證 100% 聽得到)
        if (respawnSFX != null)
        {
            PlayDirectSFX(respawnSFX, sfxVolume);
            Debug.Log($"✨【重生系統】播放復活重生音效: {respawnSFX.name}");
        }

        // ===================================================================
        // 【重生規則最終執行點】
        // 必須在 IsAnyRespawning=false 之前清除，確保旗標解除的瞬間
        // graceTimer 是剛刷新的 5 秒，風無法在該瞬間立即石化玩家。
        // 這是解決「重生後立刻被石化」的根本修復。
        // ===================================================================
        PlayerPetrification petrifyFinal = GetPetrification();
        if (petrifyFinal != null)
        {
            petrifyFinal.ClearAllNegativeEffects(); // graceTimer 刷新為 5 秒
        }

        // 確保 Rigidbody 和 PlayerMovement 正常
        Rigidbody rbComp = GetPlayerRigidbody();
        if (rbComp != null)
        {
            rbComp.isKinematic = false;
            rbComp.useGravity = true;
        }
        PlayerMovement pm = GetMovement();
        if (pm != null)
        {
            pm.enabled = true;
            pm.freezeHorizontal = false;
            pm.isCutsceneFrozen = false;
        }

        // 現在才解除重生旗標（此時 graceTimer=5s，風無法立即石化）
        _isRespawning = false;
        IsAnyRespawning = false;
        _isWaitingForPlayerMove = true;

        // 啟動守護協程，5 秒內每幀主動監控確保玩家可動
        StartCoroutine(PostRespawnGuard());
    }

    /// <summary>
    /// 重生後的安全守護協程。持續 5 秒，每幀監控玩家狀態。
    /// 若偵測到玩家還在石化/鎖定狀態，立刻強制清除。
    /// 這是最後一道防線，確保「重生 = 玩家一定能動」這個規則絕對成立。
    /// </summary>
    private IEnumerator PostRespawnGuard()
    {
        float elapsed = 0f;
        float guardDuration = 5f;

        while (elapsed < guardDuration)
        {
            elapsed += Time.deltaTime;

            // 確保 Rigidbody 不被鎖死
            if (_playerRb != null && _playerRb.isKinematic)
            {
                _playerRb.isKinematic = false;
                Debug.LogWarning("[PostRespawnGuard] 偵測到 isKinematic = true，已強制解除！");
            }

            // 確保 PlayerMovement 是啟用的
            PlayerMovement pm = GetComponent<PlayerMovement>();
            if (pm != null)
            {
                if (!pm.enabled) pm.enabled = true;
                if (pm.freezeHorizontal) pm.freezeHorizontal = false;
                if (pm.isCutsceneFrozen) pm.isCutsceneFrozen = false;
            }

            // 若偵測到玩家被石化，立刻清除
            PlayerPetrification petrify = GetComponent<PlayerPetrification>();
            if (petrify != null && petrify.isPetrified)
            {
                Debug.LogWarning("[PostRespawnGuard] 重生後偵測到石化狀態殘留，強制清除！");
                petrify.ClearAllNegativeEffects();
            }

            yield return null;
        }
    }

    IEnumerator FadeOutText()
    {
        float duration = 1.0f;
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            if (_messageCanvasGroup != null)
                _messageCanvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / duration);
            yield return null;
        }
        if (_messageText != null) _messageText.gameObject.SetActive(false);
    }

    // ===================================
    // UI 建立 (修改為全英文與絕對安全層面)
    // ===================================
    private void CreatePersistentUI()
    {
        try 
        {
            // 如果玩家有拉自訂的 UI 元件，就優先使用它們
            if (customFadeImage != null && customMessageText != null)
            {
                _fadeImage = customFadeImage;
                _messageText = customMessageText;
                _messageCanvasGroup = customMessageCanvasGroup;
                
                // 確保文字一開始是隱藏的
                _messageText.gameObject.SetActive(false);
                return;
            }

            if (GameObject.Find("RespawnCanvas_System") != null)
            {
                // 如果場景中已經有之前程式生成的 Canvas，就抓取下來用
                GameObject existingCanvas = GameObject.Find("RespawnCanvas_System");
                
                // 【關鍵防護】確保 Canvas 絕對位於最外層 (Root)，不繼承任何玩家或其它物件的縮放與旋轉！
                existingCanvas.transform.SetParent(null);
                existingCanvas.transform.localScale = Vector3.one;
                // 將 Canvas 移到極遠的坐標，使其在 Scene 視窗中飛走，不遮擋場景！
                existingCanvas.transform.localPosition = new Vector3(-10000f, -10000f, 0f);
                existingCanvas.transform.localRotation = Quaternion.identity;

                Canvas canvas = existingCanvas.GetComponent<Canvas>();
                if (canvas == null)
                {
                    canvas = existingCanvas.AddComponent<Canvas>();
                }
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 999;

                if (existingCanvas.GetComponent<CanvasScaler>() == null)
                {
                    existingCanvas.AddComponent<CanvasScaler>();
                }
                if (existingCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                {
                    existingCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }

                Transform canvasTrans = existingCanvas.transform;
                Transform fadeTrans = canvasTrans.Find("FadeBlackScreen");
                Transform textTrans = canvasTrans.Find("MessageGlowText");
                if (fadeTrans != null) _fadeImage = fadeTrans.GetComponent<Image>();
                if (textTrans != null) 
                {
                    _messageText = textTrans.GetComponent<Text>();
                    _messageCanvasGroup = textTrans.GetComponent<CanvasGroup>();
                }

                return;
            }

            // --- 以下是自動生成的後備方案 ---
            GameObject canvasObj = new GameObject("RespawnCanvas_System");
            // 將 Canvas 移到極遠的坐標，使其在 Scene 視窗中飛走，不遮擋場景！
            canvasObj.transform.localPosition = new Vector3(-10000f, -10000f, 0f);
            Canvas canvasComp = canvasObj.AddComponent<Canvas>();
            canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComp.sortingOrder = 999; 
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>(); // 【修復】加入圖形射線偵測，使按鈕能被點擊

            GameObject fadeObj = new GameObject("FadeBlackScreen");
            fadeObj.transform.SetParent(canvasObj.transform, false);
            _fadeImage = fadeObj.AddComponent<Image>();
            _fadeImage.color = new Color(0, 0, 0, 0f); 
            
            RectTransform fadeRect = _fadeImage.GetComponent<RectTransform>();
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.sizeDelta = Vector2.zero;
            fadeRect.anchoredPosition = Vector2.zero;
            _fadeImage.gameObject.SetActive(false);

            GameObject textObj = new GameObject("MessageGlowText");
            textObj.transform.SetParent(canvasObj.transform, false);
            _messageCanvasGroup = textObj.AddComponent<CanvasGroup>();
            _messageText = textObj.AddComponent<Text>();
            
            Font fallbackFont = (Font)Resources.GetBuiltinResource(typeof(Font), "LegacyRuntime.ttf");
            if (fallbackFont == null) fallbackFont = Font.CreateDynamicFontFromOSFont("Arial", 50);
            if (fallbackFont != null) _messageText.font = fallbackFont;

            _messageText.fontSize = 55; 
            _messageText.fontStyle = FontStyle.Italic; // 柔和的斜體
            _messageText.color = new Color(1f, 0.6f, 0.8f, 1f); // 溫馨顯眼的粉紅色
            _messageText.alignment = TextAnchor.UpperRight;
            _messageText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _messageText.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform textRect = _messageText.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(1, 1); 
            textRect.anchorMax = new Vector2(1, 1);
            textRect.pivot = new Vector2(1, 1);     
            textRect.sizeDelta = new Vector2(800, 150);
            textRect.anchoredPosition = new Vector2(-50, -50); 
            
            Color glowColor = new Color(1f, 0.4f, 0.6f, 0.5f); // 配合粉紅色的柔和外發光
            Outline glow = textObj.AddComponent<Outline>();
            glow.effectColor = glowColor;
            glow.effectDistance = new Vector2(2, -2);

            _messageText.gameObject.SetActive(false);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("生成 UI 當機: " + ex.Message);
        }
    }

    /// <summary>
    /// 觸發玩家傳送過渡效果 (漸黑 -> 傳送與相機對齊 -> 漸亮)
    /// </summary>
    public void TriggerTeleport(Vector3 destinationPos)
    {
        if (!this.enabled) return;
        if (!_isRespawning && !_isTeleporting)
        {
            StartCoroutine(TeleportSequence(destinationPos));
        }
    }

    private IEnumerator TeleportSequence(Vector3 destPos)
    {
        _isTeleporting = true;

        PlayerMovement pmComponent = GetComponent<PlayerMovement>();
        if (pmComponent != null)
        {
            pmComponent.isCutsceneFrozen = true; // 立即定住玩家，鎖定所有輸入與移動
        }

        // 確保 UI 組件存在
        if (_fadeImage == null || _messageText == null) CreatePersistentUI();

        if (_fadeImage != null)
        {
            _fadeImage.gameObject.SetActive(true);
        }

        // 停止物理慣性
        if (_playerRb != null && !_playerRb.isKinematic)
        {
            _playerRb.linearVelocity = Vector3.zero;
            _playerRb.angularVelocity = Vector3.zero;
        }

        // 1. 畫面漸黑
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            if (_fadeImage != null)
            {
                _fadeImage.color = new Color(0, 0, 0, Mathf.Lerp(0f, 1f, timer / fadeDuration));
            }
            yield return null;
        }
        if (_fadeImage != null)
            _fadeImage.color = new Color(0, 0, 0, 1f);

        // 2. 執行傳送 (同時處理玩家與攝影機跟隨點)
        if (pmComponent != null)
        {
            pmComponent.WarpTo(destPos);
        }
        else
        {
            transform.position = destPos;
        }

        if (_mainCam != null)
        {
            GameObject customTarget = GameObject.Find("CameraFollowTarget");
            if (customTarget != null) {
                Vector3 camPos = transform.position + cameraOffsetFromPlayer;
                camPos.y = customTarget.transform.position.y;
                _mainCam.transform.position = camPos;
            } else {
                _mainCam.transform.position = transform.position + cameraOffsetFromPlayer;
            }
            Transform targetFollow = (pmComponent != null) ? pmComponent.GetCameraTarget() : this.transform;

            // 重置 Cinemachine
            CinemachineCamera[] vcams3 = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcams3)
            {
                vcam.PreviousStateIsValid = false;
                var t = vcam.Target;
                t.TrackingTarget = targetFollow;
                vcam.Target = t;
                vcam.Follow = targetFollow;
            }
            CinemachineVirtualCamera[] vcamsLegacy = FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcamsLegacy)
            {
                vcam.PreviousStateIsValid = false;
                vcam.Follow = targetFollow;
            }
        }

        // 同步更新安全重生點，避免在高空中死掉掉回下層
        SetSafeGroundPosition(destPos);

        // 額外等待 0.2 秒緩衝，讓物理與相機完成置位與防震快取更新
        yield return new WaitForSecondsRealtime(0.2f);

        // 3. 畫面漸亮
        timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            if (_fadeImage != null)
            {
                _fadeImage.color = new Color(0, 0, 0, Mathf.Lerp(1f, 0f, timer / fadeDuration));
            }
            yield return null;
        }
        if (_fadeImage != null)
        {
            _fadeImage.color = new Color(0, 0, 0, 0f);
            _fadeImage.gameObject.SetActive(false);
        }

        // 傳送轉場完成，恢復玩家控制
        if (pmComponent != null)
        {
            pmComponent.isCutsceneFrozen = false;
        }

        _isTeleporting = false;
    }

    // 傳送回剛按下 PLAY 時的初始位置 (測試用按鈕，改為直接重置整個場景)
    public void TriggerResetToStart()
    {
        if (!this.enabled) return;
        if (!_isRespawning)
        {
            Debug.Log("【測試按鈕】玩家點擊回到起點！場景將重新載入！");
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }
}