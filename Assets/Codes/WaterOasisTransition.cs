using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 掛載於綠洲水面 Trigger 物件。
/// 當玩家踩入綠洲水面時，接管控制使玩家緩慢沉入水底，並轉場載入水下關卡。
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaterOasisTransition : MonoBehaviour, IResettable
{
    [Header("轉場與場景設定")]
    [Tooltip("要載入的水下關卡場景名稱 (預設為 underwater)")]
    public string nextSceneName = "underwater";

    [Tooltip("進入目標場景 (underwater) 後，要指定重生的隱形物件/重生點名稱 (若留空則使用該場景預設位置)")]
    public string targetSpawnPointName = "SpawnPoint_FromDesert";

    [Tooltip("玩家沉入水中的速度 (單位/秒)")]
    public float sinkSpeed = 1.0f;

    [Tooltip("需要下沉多少深度才啟動場景載入 (單位，預設 3)")]
    public float targetSinkDepth = 3.0f;

    [Tooltip("漸黑轉場淡出的時間長度 (秒，預設 1.5)")]
    public float fadeDuration = 1.5f;

    private bool isTransitioning = false;
    private Transform playerTransform;
    private Rigidbody playerRb;
    private PlayerMovement playerMovement;
    private UnityEngine.UI.Image fadeImage;

    private void Awake()
    {
        EnsureColliderSetup();
    }

    private void Start()
    {
        EnsureColliderSetup();
    }

    private void EnsureColliderSetup()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
            if (col is BoxCollider box)
            {
                float lossyZ = transform.lossyScale.z != 0f ? Mathf.Abs(transform.lossyScale.z) : 1f;
                Vector3 size = box.size;
                size.z = Mathf.Max(size.z, 30f / lossyZ);
                box.size = size;

                Vector3 center = box.center;
                center.z = 0f;
                box.center = center;
            }
        }

        Collider2D col2d = GetComponent<Collider2D>();
        if (col2d != null)
        {
            col2d.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay(Collider other)
    {
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryStartTransition(collision != null ? collision.gameObject : null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryStartTransition(collision != null ? collision.gameObject : null);
    }

    private void TryStartTransition(GameObject hitObj)
    {
        if (isTransitioning || hitObj == null) return;

        PlayerMovement pm = hitObj.GetComponent<PlayerMovement>();
        if (pm == null) pm = hitObj.GetComponentInParent<PlayerMovement>();
        if (pm == null) pm = hitObj.GetComponentInChildren<PlayerMovement>();

        if (pm != null)
        {
            StartTransition(pm.gameObject);
            return;
        }

        if (hitObj.CompareTag("Player") || hitObj.name.Contains("Player") || (hitObj.transform.root != null && hitObj.transform.root.name.Contains("Player")))
        {
            StartTransition(hitObj);
        }
    }

    private void StartTransition(GameObject playerObj)
    {
        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogError("[WaterOasisTransition] nextSceneName 是空的，無法轉場。", this);
            return;
        }

        isTransitioning = true;
        playerTransform = playerObj.transform;
        playerRb = playerObj.GetComponent<Rigidbody>();
        playerMovement = playerObj.GetComponent<PlayerMovement>();

        Debug.Log("【綠洲轉場】玩家觸碰水面，開始沉入水底...");
        StartCoroutine(SinkAndTransitionSequence());
    }

    private IEnumerator SinkAndTransitionSequence()
    {
        // 1. 鎖定玩家控制與物理狀態
        if (playerMovement != null)
        {
            playerMovement.isCutsceneFrozen = true; // 鎖定鍵盤輸入
        }

        if (playerRb != null)
        {
            playerRb.useGravity = false;
            playerRb.linearVelocity = Vector3.zero;
            playerRb.angularVelocity = Vector3.zero;
            // 物理防呆：鎖定 X 與 Z 軸，只允許 Y 軸下沉，防止下沉中被強風推走
            playerRb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        }

        // 2. 動態生成全螢幕漸黑圖片進行淡出
        CreateFadeImage();

        float startY = playerTransform.position.y;
        float targetY = startY - targetSinkDepth;

        // ★1024 沉入水裡的表現：入水點起水波、身體越沉越偏水藍變暗（像被水吞掉，不是穿過地面）、起步慢慢加速
        BeginSubmergeFx();

        // 3. 執行平滑下沉與漸黑效果
        float fadeTimer = 0f;
        while (playerTransform.position.y > targetY + 0.05f)
        {
            float depthT = Mathf.Clamp01((startY - playerTransform.position.y) / Mathf.Max(0.1f, targetSinkDepth));
            float ease = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, depthT / 0.35f));   // 剛入水慢、約三分之一深度後全速
            UpdateSubmergeFx(depthT);

            // 移動玩家座標
            playerTransform.position = Vector3.MoveTowards(
                playerTransform.position,
                new Vector3(playerTransform.position.x, targetY, playerTransform.position.z),
                sinkSpeed * ease * Time.deltaTime
            );

            // 淡出至全黑
            if (fadeImage != null && fadeTimer < fadeDuration)
            {
                fadeTimer += Time.deltaTime;
                fadeImage.color = new Color(0, 0, 0, Mathf.Lerp(0f, 1f, fadeTimer / fadeDuration));
            }

            yield return null;
        }

        // 4. 強制完全黑屏
        if (fadeImage != null)
        {
            fadeImage.color = new Color(0, 0, 0, 1f);
        }
        yield return new WaitForSeconds(0.2f);

        // 5. 設定跨場景指定出生點並載入下一關卡場景
        if (!string.IsNullOrEmpty(targetSpawnPointName))
        {
            PlayerRespawnSystem.QueueNextSceneSpawn(targetSpawnPointName);
        }

        string targetScene = nextSceneName.Trim();
        Debug.Log($"【綠洲轉場】下沉完畢，開始載入場景: '{targetScene}'，目標出生點物件為：'{targetSpawnPointName}'");
        // ★改用非同步載入：水下場景很大（上百顆石頭、近 300MB 貼圖），同步載入會讓視窗整個卡住、
        //   Windows 跳出「沒有回應」（exe 才會發生，編輯器裡資源已在記憶體所以沒感覺）。
        //   非同步載入時視窗持續有回應，畫面維持全黑直到新場景好。
        AsyncOperation load = SceneLoader.Load(targetScene);   // 取不到非同步操作時 SceneLoader 已經退回同步載入
        if (load == null) yield break;
        while (!load.isDone) yield return null;
    }

    // ───────── ★1024 沉入水裡的表現（水波＋身體入水變色）─────────
    // 原本只是把主角往下平移，後面沒有東西「蓋住」她，看起來是穿過地面。沒辦法改場景圖的情況下用兩件事補：
    //   1. 入水點起三圈水波（程式產生的柔邊橢圓環，不需要圖檔），讓視線停在「水面」上
    //   2. 越沉越偏水藍、變暗（MaterialPropertyBlock，不動材質本體，重置時還原），像被水吞進去
    [Header("沉入水裡的表現（★1024）")]
    [Tooltip("入水點起水波、身體入水後逐漸偏水藍。取消＝回到原本只往下移動")]
    public bool submergeFx = true;
    [Tooltip("身體完全沉下去時的顏色（跟原本顏色混合）")]
    public Color submergeTint = new Color(0.18f, 0.5f, 0.6f, 1f);
    [Tooltip("水波最大寬度（公尺）")]
    public float rippleWidth = 3.2f;

    private Renderer[] _fxRenderers;
    private MaterialPropertyBlock _fxBlock;
    private Color[] _fxBaseColors;
    private SpriteRenderer[] _ripples;
    private float _fxStartTime;
    private static Sprite _rippleSprite;

    private void BeginSubmergeFx()
    {
        if (!submergeFx || playerTransform == null) return;
        _fxStartTime = Time.time;

        _fxRenderers = playerTransform.GetComponentsInChildren<Renderer>(true);
        _fxBlock = new MaterialPropertyBlock();
        _fxBaseColors = new Color[_fxRenderers.Length];
        for (int i = 0; i < _fxRenderers.Length; i++)
        {
            Renderer r = _fxRenderers[i];
            Color c = Color.white;
            if (r != null && r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty("_BaseColor")) c = r.sharedMaterial.GetColor("_BaseColor");
                else if (r.sharedMaterial.HasProperty("_Color")) c = r.sharedMaterial.GetColor("_Color");
            }
            _fxBaseColors[i] = c;
        }

        // 水波：放在主角腳下的入水點，比主角稍微靠後（被身體蓋住），比地面靠前
        Collider pc = playerTransform.GetComponent<Collider>();
        float feetY = pc != null ? pc.bounds.min.y : playerTransform.position.y - 1f;
        Vector3 origin = new Vector3(playerTransform.position.x, feetY + 0.05f, playerTransform.position.z + 0.05f);
        if (_rippleSprite == null) _rippleSprite = MakeRippleSprite();
        _ripples = new SpriteRenderer[3];
        for (int i = 0; i < _ripples.Length; i++)
        {
            GameObject go = new GameObject("[OasisRipple] " + i);
            go.transform.position = origin;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _rippleSprite;
            sr.sortingOrder = 50;
            sr.color = new Color(0.8f, 0.95f, 1f, 0f);
            _ripples[i] = sr;
        }
    }

    private void UpdateSubmergeFx(float depthT)
    {
        if (!submergeFx || _fxRenderers == null) return;

        // 身體顏色：入水後（深度 15% 起）慢慢偏水藍變暗
        float tint = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depthT - 0.15f) / 0.85f));
        for (int i = 0; i < _fxRenderers.Length; i++)
        {
            Renderer r = _fxRenderers[i];
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            Color c = Color.Lerp(_fxBaseColors[i], _fxBaseColors[i] * submergeTint, tint);
            r.GetPropertyBlock(_fxBlock);
            _fxBlock.SetColor("_BaseColor", c);
            _fxBlock.SetColor("_Color", c);
            r.SetPropertyBlock(_fxBlock);
        }

        // 水波：三圈錯開 0.35 秒，各自 1.4 秒內擴大並淡掉
        if (_ripples == null) return;
        float t0 = Time.time - _fxStartTime;
        for (int i = 0; i < _ripples.Length; i++)
        {
            SpriteRenderer sr = _ripples[i];
            if (sr == null) continue;
            float k = (t0 - i * 0.35f) / 1.4f;
            if (k < 0f || k > 1f) { sr.color = new Color(0.8f, 0.95f, 1f, 0f); continue; }
            float w = Mathf.Lerp(0.5f, rippleWidth, 1f - (1f - k) * (1f - k));
            sr.transform.localScale = new Vector3(w, w * 0.28f, 1f);
            sr.color = new Color(0.8f, 0.95f, 1f, 0.7f * (1f - k));
        }
    }

    private void EndSubmergeFx()
    {
        if (_fxRenderers != null)
        {
            foreach (Renderer r in _fxRenderers) if (r != null) r.SetPropertyBlock(null);
        }
        _fxRenderers = null;
        if (_ripples != null)
        {
            foreach (SpriteRenderer sr in _ripples) if (sr != null) Destroy(sr.gameObject);
        }
        _ripples = null;
    }

    /// <summary>程式產生的柔邊空心圓（1 單位寬），不需要圖檔。</summary>
    private static Sprite MakeRippleSprite()
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "OasisRippleTex" };
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.14f);   // 半徑 0.8 附近的一圈，邊緣柔
                px[y * size + x] = new Color32(255, 255, 255, (byte)(ring * ring * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private void CreateFadeImage()
    {
        // 嘗試抓取系統的重生 Canvas，若沒有則動態生成一個
        GameObject canvasObj = GameObject.Find("RespawnCanvas_System");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("TransitionCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        }

        GameObject fadeObj = new GameObject("OasisFadeScreen");
        fadeObj.transform.SetParent(canvasObj.transform, false);
        fadeImage = fadeObj.AddComponent<UnityEngine.UI.Image>();
        fadeImage.color = new Color(0, 0, 0, 0f);

        RectTransform rect = fadeImage.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isTransitioning = false;
        EndSubmergeFx();   // ★1024 還原身體顏色、清掉水波

        if (fadeImage != null)
        {
            Destroy(fadeImage.gameObject);
        }

        // 重設玩家的物理與移動狀態（防呆）
        if (playerRb != null)
        {
            playerRb.useGravity = true;
            playerRb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
        }
        if (playerMovement != null)
        {
            playerMovement.isCutsceneFrozen = false;
        }
    }
}
