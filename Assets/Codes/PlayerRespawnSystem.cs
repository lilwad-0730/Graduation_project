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
    private Rigidbody _playerRb;
    private Camera _mainCam;
    
    private bool _inKnockbackState = false;
    private float _knockbackStartPosX;
    private Vector3 _lastSafeGroundPos; // 安全踩在地上時的地點
    
    // --- UI 相關 ---
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

    void Start()
    {
        _playerRb = GetComponent<Rigidbody>();
        _mainCam = Camera.main;

        // 起始點即為最基礎的安全點
        _lastSafeGroundPos = transform.position;

        CreatePersistentUI();
    }

    // 允許外部系統 (如掉落背景切換) 強制更新安全點，避免因為合法長距離掉落而誤判死亡
    public void SetSafeGroundPosition(Vector3 newPos)
    {
        _lastSafeGroundPos = newPos;
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
                    
                    // 重點：傳送到剛剛踩穩的地方
                    StartCoroutine(RespawnSequence(_lastSafeGroundPos));
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

    // 碰觸存檔點 (Cube) 的偵測
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(respawnPointTag) || other.name.Contains("RespawnPoint"))
        {
            // 【超級關鍵】必須記錄「玩家」當下的座標，而不是觸發器的座標！
            // 否則如果觸發器放在半空中，系統會誤以為玩家從半空中掉下來，瞬間觸發墜崖死亡！
            _lastSafeGroundPos = transform.position;
            Debug.Log($"【紀錄存檔點】已更新重生點至 {other.gameObject.name} 的座標：{_lastSafeGroundPos}");

            // 碰過之後就讓這個存檔點失效（關閉），確保玩家不會因為往回走而不小心踩到舊的存檔點！
            other.gameObject.SetActive(false);
        }
        // 【全新功能】：專門用來放坑洞底部的死亡判定區
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

    // 觸發死亡重生轉場 (預設傳送到最後安全點)
    public void TriggerRespawn()
    {
        if (!this.enabled) return;
        if (!_isRespawning)
        {
            StartCoroutine(RespawnSequence(_lastSafeGroundPos));
        }
    }

    // 觸發強制傳送到「指定位置」的重生轉場
    public void TriggerRespawn(Vector3 customSpawnPos)
    {
        if (!this.enabled) return;
        if (!_isRespawning)
        {
            StartCoroutine(RespawnSequence(customSpawnPos));
        }
    }

    // ===================================
    // 原本的轉場演出 (僅傳送，不重載場景)
    // ===================================
    IEnumerator RespawnSequence(Vector3 spawnPos)
    {
        _isRespawning = true;
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

        // 清除物理動力
        if (_playerRb != null)
        {
            _playerRb.linearVelocity = Vector3.zero;
            _playerRb.angularVelocity = Vector3.zero;
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
            yield return null;
        }
        
        if (_fadeImage != null)
            _fadeImage.color = new Color(0, 0, 0, 1f);

        // --- 傳送至最後著陸的安全點 ---
        transform.position = new Vector3(spawnPos.x, spawnPos.y + 2f, spawnPos.z);
        if (_mainCam != null)
        {
            _mainCam.transform.position = transform.position + cameraOffsetFromPlayer;
            // 取得正確的攝影機跟隨目標（相容 Y 軸鎖定設定）
            PlayerMovement pmComponent = GetComponent<PlayerMovement>();
            Transform targetFollow = (pmComponent != null) ? pmComponent.GetCameraTarget() : this.transform;

            // 尋找新版 CinemachineCamera
            CinemachineCamera[] vcams3 = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcams3)
            {
                vcam.PreviousStateIsValid = false;
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

        _isRespawning = false;
        _isWaitingForPlayerMove = true; 

        // 【新增】確保重生後，玩家一定能恢復移動
        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm != null)
        {
            pm.freezeHorizontal = false;
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
                Transform canvasTrans = GameObject.Find("RespawnCanvas_System").transform;
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
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999; 
            canvasObj.AddComponent<CanvasScaler>();

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
}