using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

/// <summary>
/// 鏡牆光球吸入與碎裂演出系統 (Mirror Wall Absorb Cutscene Manager)
/// - 玩家踏入 Trigger 區自動觸發演出並凍結玩家控制。
/// - 支援 IResettable 關卡重置：主角若在任何時刻死亡重生，本系統會立即中止演出並 100% 刷新所有 4 顆光球與鏡牆至初始狀態！
/// - 所有時序參數 (等待時間、飛行時間、消融時長、閃光時長、間隔時間) 皆可在 Inspector 自由調整！
/// - 4 顆光球隨機依序起飛，瞄準鏡牆 Y 軸中心，帶拋物線弧度加速飛入。
/// - 相機精準正中 (Dead Center) Zoom In 聚焦至飛行中的光球。
/// - 抵達鏡牆時先執行【消融縮小被吸收】，完全吸收後再觸發【全螢幕白光閃爍】。
/// - 全部吸入後觸發鏡牆玻璃碎裂特效 (Destructible / GlassShatterFX)，相機平滑還原追蹤主角，恢復玩家控制。
/// </summary>
[RequireComponent(typeof(Collider))]
public class MirrorWallAbsorbCutscene : MonoBehaviour, IResettable
{

    // ══════════════════════════════════════════
    // 結局出口（吸入演出結束後接上結尾）
    // ══════════════════════════════════════════
    [Header("🎬 結局出口")]
    [Tooltip("演出結束後接結尾漫畫與片尾名單（整局的最後一段）")]
    public bool playEndingAfterCutscene = false;   // ★結局已改由「最後一根燭火」觸發，見 ShadowMonsterController
    [Tooltip("接結尾前要播的過場文字卡")]
    public string endingCardId = "M5";
    [Tooltip("結尾漫畫所在的場景名稱")]
    public string endingBookScene = "Book";
    [Header("🎯 目標物件設定")]
    [Tooltip("目標鏡牆物件 (可直接將 'mirror wall_001' 拖入；若為空則自動搜尋)")]
    public GameObject mirrorWall;

    [Tooltip("要飛入的光球陣列 (可直接將 4 顆 FairyLight 拖入；若為空則自動搜尋 FairyLight s 子物件)")]
    public GameObject[] fairyLights;

    [Header("🎬 觸發與玩家控制")]
    [Tooltip("是否只觸發一次 (防止重複觸發演出)")]
    public bool triggerOnce = true;

    [Tooltip("踏入觸發時是否凍結玩家動作")]
    public bool freezePlayerDuringCutscene = true;

    [Header("⏱️ 演出時序設定 (Timing Settings - 自由微調)")]
    [Tooltip("踏入觸發區後，等待幾秒才開始相機 Zoom In 聚焦第一顆光球 (秒，預設 0.3 秒)")]
    public float delayBeforeZoomIn = 0.3f;

    [Tooltip("相機開始 Zoom In 後，等待幾秒才正式啟動第一顆光球起飛 (秒，預設 1.2 秒)")]
    public float delayBeforeFirstFlight = 1.2f;

    [Tooltip("光球觸碰鏡牆時的【消融縮小時長】(秒，營造被吸入鏡內的過渡感，預設 0.45 秒)")]
    public float meltDuration = 0.45f;

    [Tooltip("白光閃屏淡出總時長 (秒，預設 1.8 秒)")]
    public float whiteFlashDuration = 1.8f;

    [Tooltip("上一顆光球閃光結束/開始後，間隔幾秒開始下一顆光球起飛 (秒，預設 0.5 秒)")]
    public float intervalBetweenLights = 0.5f;

    [Tooltip("4 顆光球全吸完後，等待幾秒才觸發鏡牆碎裂 (秒，預設 0.4 秒)")]
    public float delayBeforeShatter = 0.4f;

    [Tooltip("鏡牆碎裂後，等待幾秒才開始還原相機視角與交還玩家控制權 (秒，預設 0.5 秒)")]
    public float delayBeforeRestorePlayer = 0.5f;

    [Header("📷 相機正中聚焦與 Zoom In 設定")]
    [Tooltip("是否開啟相機 Zoom In 聚焦光球")]
    public bool enableCameraZoom = true;

    [Tooltip("Zoom In 時的鏡頭尺寸 (正交相機數值越小畫面越放大，建議 4.0 ~ 5.0；若是透視相機則為 FOV)")]
    public float zoomInLensSize = 4.5f;

    [Tooltip("相機縮放過渡速度")]
    public float zoomTransitionSpeed = 3.5f;

    [Tooltip("是否自動將相機垂直位移 (FollowOffset.y) 歸零以確保光球處於螢幕正正中心")]
    public bool centerCameraOnLight = true;

    [Header("✨ 光球飛行設定")]
    [Tooltip("光球飛行速度")]
    public float flySpeed = 6.0f;

    [Tooltip("拋物線弧度高度 (讓飛行軌跡自然向上微揚再衝入鏡牆)")]
    public float arcHeight = 1.2f;

    [Header("⚡ 白光閃屏細部設定")]
    [Tooltip("白光最高亮度透明度 (0 ~ 1，預設 0.92f)")]
    [Range(0f, 1f)]
    public float maxFlashAlpha = 0.92f;

    [Tooltip("消融完成後白光極速爆亮的時間 (秒，預設 0.08 秒)")]
    public float flashFadeInDuration = 0.08f;

    [Header("💥 結尾碎裂設定 (方案 B)")]
    [Tooltip("4 顆光球全吸完後，是否觸發鏡牆玻璃碎裂特效")]
    public bool triggerShatterAtEnd = true;

    [Header("🎵 鏡牆演出音效 (Cutscene SFX)")]
    [Tooltip("演繹啟動前，玩家靠近光球群時的懸停氛圍音效 (例如 玻璃館_光球懸停.wav)")]
    public AudioClip lightHoverSFX;
    [Tooltip("玩家距離光球多近時開始聽到懸停音效 (米，預設 16 米)")]
    public float hoverHearDistance = 16f;
    [Tooltip("光球起飛衝向鏡牆音效 (例如 玻璃館_光離開_01.wav)")]
    public AudioClip lightTakeoffSFX;
    [Tooltip("光球觸碰鏡牆消融縮小音效 (例如 玻璃館_入鏡慢.wav)")]
    public AudioClip lightEnterMirrorSFX;
    [Tooltip("全螢幕白光閃爍共鳴音效 (例如 玻璃館_冷縫共鳴 / 玻璃館_合體.wav)")]
    public AudioClip flashResonanceSFX;
    [Tooltip("鏡牆玻璃碎裂音效 (例如 玻璃碎裂.mp3 / 玻璃館_破鏡牆.wav)")]
    public AudioClip mirrorShatterSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.95f;

    // 全域靜態旗標 (供其他系統即時查詢)
    public static bool IsAnyCutsceneRunning = false;

    // 內部狀態變數
    private bool hasTriggered = false;
    private bool isCutsceneRunning = false;
    private PlayerMovement cachedPlayer;
    private Transform originalCameraTarget;
    private float originalLensSize = 6.0f;
    private bool isOrthographic = true;

    // 快取光球原始狀態 (供死亡重生 100% 還原)
    private Vector3[] _initialLightPositions;
    private Vector3[] _initialLightScales;

    // Cinemachine 快取與原始參數
    private CinemachineCamera activeVcam3;
    private CinemachineVirtualCamera activeVcamLegacy;
    private CinemachineFollow cmFollow;
    private Vector3 originalFollowOffset = new Vector3(0, 0, -15);
    private bool hasCachedFollowOffset = false;

    // 全螢幕閃白光 UI 系統
    private Canvas flashCanvas;
    private Image flashImage;
    private Coroutine flashCoroutine;
    private Coroutine mainCutsceneCoroutine;

    private void Awake()
    {
        IsAnyCutsceneRunning = false; // 強制重置全域靜態旗標，防止前次測試殘留
        isCutsceneRunning = false;

        // ★ 核心除錯：自動停用場景中殘留的 Timeline PlayableDirector，防止遊戲一開始自動搶播光離開音效
        var directors = Object.FindObjectsByType<UnityEngine.Playables.PlayableDirector>(FindObjectsSortMode.None);
        foreach (var pd in directors)
        {
            if (pd != null && (pd.gameObject.name == "GameObject" || (pd.playableAsset != null && pd.playableAsset.name.Contains("Timeline"))))
            {
                pd.Stop();
                pd.playOnAwake = false;
                pd.enabled = false;
                Debug.Log($"🔇【音效防呆】已成功停用開局誤播音效的 Timeline 物件：{pd.gameObject.name}");
            }
        }

        EnsureColliderSetup();
        InitializeTargets();
        CreateFlashUI();
        CacheInitialLightTransforms();
    }

    private void OnDisable()
    {
        IsAnyCutsceneRunning = false;
        isCutsceneRunning = false;
    }

    private void Start()
    {
        EnsureColliderSetup();
        FindCinemachineCameras();
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

    private AudioSource hoverAudioSource;

    private void Update()
    {
        // ★ 懸停音效管理：僅在演出啟動前、玩家靠近光球時播放；一旦演繹開始即永久停止！
        UpdateHoverAudio();

        // ★ 核心寫死防護：只要演出正在運行，每幀強制鎖死主角移動與剛體速度，絕不允許任何按鍵或外界腳本解除凍結！
        if (isCutsceneRunning && freezePlayerDuringCutscene)
        {
            IsAnyCutsceneRunning = true;
            if (cachedPlayer == null) cachedPlayer = FindFirstObjectByType<PlayerMovement>();
            if (cachedPlayer != null)
            {
                cachedPlayer.isCutsceneFrozen = true;
                Rigidbody prb = cachedPlayer.GetComponent<Rigidbody>();
                if (prb != null)
                {
                    prb.linearVelocity = new Vector3(0f, prb.linearVelocity.y, 0f);
                }
            }
        }
    }

    private void UpdateHoverAudio()
    {
        if (lightHoverSFX == null) return;

        // ★ 一旦演繹啟動或演繹進行中，立即徹底停止懸停音效
        if (hasTriggered || isCutsceneRunning)
        {
            if (hoverAudioSource != null && hoverAudioSource.isPlaying)
            {
                hoverAudioSource.Stop();
            }
            return;
        }

        if (cachedPlayer == null) cachedPlayer = FindFirstObjectByType<PlayerMovement>();
        if (cachedPlayer == null) return;

        Vector3 lightCenter = GetMirrorWallCenter();
        if (fairyLights != null && fairyLights.Length > 0 && fairyLights[0] != null)
        {
            lightCenter = fairyLights[0].transform.position;
        }

        float dist = Vector3.Distance(cachedPlayer.transform.position, lightCenter);

        // 依使用者音訊原味播放，靠近直接播放，遠離直接停止，不另做多餘程式漸變
        if (dist <= hoverHearDistance)
        {
            if (hoverAudioSource == null)
            {
                hoverAudioSource = gameObject.GetComponent<AudioSource>();
                if (hoverAudioSource == null) hoverAudioSource = gameObject.AddComponent<AudioSource>();
                hoverAudioSource.clip = lightHoverSFX;
                hoverAudioSource.loop = true;
                hoverAudioSource.playOnAwake = false;
                hoverAudioSource.spatialBlend = 0.5f;
                hoverAudioSource.minDistance = 4f;
                hoverAudioSource.maxDistance = hoverHearDistance * 1.5f;
            }

            hoverAudioSource.volume = sfxVolume;

            if (!hoverAudioSource.isPlaying)
            {
                hoverAudioSource.Play();
            }
        }
        else
        {
            if (hoverAudioSource != null && hoverAudioSource.isPlaying)
            {
                hoverAudioSource.Stop();
            }
        }
    }

    private void CacheInitialLightTransforms()
    {
        if (fairyLights != null && fairyLights.Length > 0)
        {
            _initialLightPositions = new Vector3[fairyLights.Length];
            _initialLightScales = new Vector3[fairyLights.Length];
            for (int i = 0; i < fairyLights.Length; i++)
            {
                if (fairyLights[i] != null)
                {
                    _initialLightPositions[i] = fairyLights[i].transform.position;
                    _initialLightScales[i] = fairyLights[i].transform.localScale;
                }
            }
        }
    }

    private void InitializeTargets()
    {
        // 1. 自動尋找鏡牆
        if (mirrorWall == null)
        {
            mirrorWall = GameObject.Find("mirror wall_001");
            if (mirrorWall == null) mirrorWall = GameObject.Find("Mirror Wall");
        }

        // 2. 自動尋找光球
        if (fairyLights == null || fairyLights.Length == 0)
        {
            GameObject lightsParent = GameObject.Find("FairyLight s");
            if (lightsParent != null)
            {
                List<GameObject> list = new List<GameObject>();
                for (int i = 0; i < lightsParent.transform.childCount; i++)
                {
                    Transform child = lightsParent.transform.GetChild(i);
                    if (child.gameObject.activeSelf)
                    {
                        list.Add(child.gameObject);
                    }
                }
                fairyLights = list.ToArray();
            }
        }

        // 3. 確保 4 顆鏡牆光球不會被額外的 GuidanceLight 腳本干擾 (徹底消除組件衝突)
        if (fairyLights != null)
        {
            foreach (var lightObj in fairyLights)
            {
                if (lightObj != null)
                {
                    GuidanceLight gl = lightObj.GetComponent<GuidanceLight>();
                    if (gl != null) gl.enabled = false;
                }
            }
        }
    }

    private void FindCinemachineCameras()
    {
        Camera mainCam = Camera.main;
        isOrthographic = (mainCam != null && mainCam.orthographic);

        // 優先抓取 CinemachineCamera (v3)
        activeVcam3 = Object.FindAnyObjectByType<CinemachineCamera>();
        if (activeVcam3 != null)
        {
            originalLensSize = isOrthographic ? activeVcam3.Lens.OrthographicSize : activeVcam3.Lens.FieldOfView;
            originalCameraTarget = activeVcam3.Target.TrackingTarget != null ? activeVcam3.Target.TrackingTarget : activeVcam3.Follow;

            cmFollow = activeVcam3.GetComponent<CinemachineFollow>();
            if (cmFollow != null)
            {
                cmFollow.FollowOffset = new Vector3(cmFollow.FollowOffset.x, 0f, cmFollow.FollowOffset.z);
                originalFollowOffset = cmFollow.FollowOffset;
                hasCachedFollowOffset = true;
            }
            return;
        }

        // 備用抓取 CinemachineVirtualCamera (Legacy)
        activeVcamLegacy = Object.FindAnyObjectByType<CinemachineVirtualCamera>();
        if (activeVcamLegacy != null)
        {
            originalLensSize = isOrthographic ? activeVcamLegacy.m_Lens.OrthographicSize : activeVcamLegacy.m_Lens.FieldOfView;
            originalCameraTarget = activeVcamLegacy.Follow;
        }
    }

    private void CreateFlashUI()
    {
        if (flashCanvas != null) return;

        // 動態建立專屬全螢幕白光 Canvas
        GameObject canvasObj = new GameObject("ScreenWhiteFlash_Canvas");
        flashCanvas = canvasObj.AddComponent<Canvas>();
        flashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        flashCanvas.sortingOrder = 997; // 位於最上層 (比受傷紅邊高，低於最黑轉場)

        canvasObj.AddComponent<CanvasScaler>();

        GameObject imgObj = new GameObject("WhiteFlash_Image");
        imgObj.transform.SetParent(canvasObj.transform, false);
        flashImage = imgObj.AddComponent<Image>();
        flashImage.color = new Color(1f, 1f, 1f, 0f);
        flashImage.raycastTarget = false;

        RectTransform rt = flashImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        DontDestroyOnLoad(canvasObj);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryStartCutsceneFrom(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay(Collider other)
    {
        TryStartCutsceneFrom(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryStartCutsceneFrom(collision != null ? collision.gameObject : null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryStartCutsceneFrom(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryStartCutsceneFrom(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryStartCutsceneFrom(collision != null ? collision.gameObject : null);
    }

    private void TryStartCutsceneFrom(GameObject hitObj)
    {
        if (hasTriggered && triggerOnce) return;
        if (isCutsceneRunning) return;
        if (hitObj == null) return;

        // 判斷是否為玩家踩入
        if (hitObj.CompareTag("Player") ||
            hitObj.name.ToLower().Contains("player") ||
            (hitObj.transform.root != null && hitObj.transform.root.name.ToLower().Contains("player")) ||
            hitObj.GetComponentInParent<PlayerMovement>() != null ||
            hitObj.GetComponentInChildren<PlayerMovement>() != null)
        {
            cachedPlayer = hitObj.GetComponent<PlayerMovement>();
            if (cachedPlayer == null) cachedPlayer = hitObj.GetComponentInParent<PlayerMovement>();
            if (cachedPlayer == null) cachedPlayer = hitObj.GetComponentInChildren<PlayerMovement>();

            StartCutscene();
        }
    }

    /// <summary>
    /// 手動或外部觸發演出
    /// </summary>
    [ContextMenu("手動觸發吸入演出 (Start Cutscene)")]
    public void StartCutscene()
    {
        if (hasTriggered && triggerOnce) return;
        if (isCutsceneRunning) return;

        hasTriggered = true;

        // ★ 演繹開始：立即永久停止懸停氛圍音效！
        if (hoverAudioSource != null && hoverAudioSource.isPlaying)
        {
            hoverAudioSource.Stop();
        }

        if (mainCutsceneCoroutine != null) StopCoroutine(mainCutsceneCoroutine);
        mainCutsceneCoroutine = StartCoroutine(AbsorbSequenceRoutine());
    }

    private IEnumerator AbsorbSequenceRoutine()
    {
        isCutsceneRunning = true;
        Debug.Log("🎬【鏡牆演出啟動】開始執行 4 顆光球依序飛入鏡牆演出！");

        // 1. 凍結玩家操作
        if (cachedPlayer == null) cachedPlayer = FindFirstObjectByType<PlayerMovement>();
        if (cachedPlayer != null && freezePlayerDuringCutscene)
        {
            cachedPlayer.isCutsceneFrozen = true;
            Rigidbody prb = cachedPlayer.GetComponent<Rigidbody>();
            if (prb != null) prb.linearVelocity = Vector3.zero;
        }

        // 2. 刷新相機原始目標
        FindCinemachineCameras();
        if (cachedPlayer != null && originalCameraTarget == null)
        {
            originalCameraTarget = cachedPlayer.transform;
        }

        // 3. 計算鏡牆 Y 軸中心目標點
        Vector3 wallCenter = GetMirrorWallCenter();

        // 4. 準備待飛行的光球佇列 (排除為空或已被關閉的物件)
        List<GameObject> remainingLights = new List<GameObject>();
        if (fairyLights != null)
        {
            foreach (var lightObj in fairyLights)
            {
                if (lightObj != null && lightObj.activeSelf)
                {
                    remainingLights.Add(lightObj);
                }
            }
        }

        if (remainingLights.Count == 0)
        {
            Debug.LogWarning("⚠️【鏡牆演出】找不到任何啟用的光球物件！直接進行收尾。");
            yield return FinishCutsceneRoutine();
            yield break;
        }

        int totalCount = remainingLights.Count;
        int absorbedIndex = 0;

        // ⏱️ Step A: 踩入觸發後，等待 delayBeforeZoomIn 秒才開始鏡頭 Zoom In
        if (delayBeforeZoomIn > 0f)
        {
            yield return new WaitForSeconds(delayBeforeZoomIn);
        }

        // ⏱️ Step B: 開始相機 Zoom In 聚焦第一顆即將起飛的光球
        if (remainingLights.Count > 0 && remainingLights[0] != null)
        {
            SetCameraTarget(remainingLights[0].transform, true);
            if (enableCameraZoom)
            {
                StartCoroutine(SmoothZoomLens(zoomInLensSize, zoomTransitionSpeed));
            }
        }

        // ⏱️ Step C: Zoom In 後等待 delayBeforeFirstFlight 秒才正式啟動第一顆起飛
        if (delayBeforeFirstFlight > 0f)
        {
            yield return new WaitForSeconds(delayBeforeFirstFlight);
        }

        // 5. 隨機逐一飛入光球
        while (remainingLights.Count > 0)
        {
            absorbedIndex++;
            int randomIndex = Random.Range(0, remainingLights.Count);
            GameObject currentLight = remainingLights[randomIndex];
            remainingLights.RemoveAt(randomIndex);

            if (currentLight == null) continue;

            Debug.Log($"✨【光球演出】第 {absorbedIndex}/{totalCount} 顆光球 ({currentLight.name}) 起飛！");

            // A. 將相機目標切換至該光球，並將 FollowOffset 調整至正中 (Y=0)
            SetCameraTarget(currentLight.transform, true);

            // B. 啟動鏡頭平滑 Zoom In
            if (enableCameraZoom)
            {
                StartCoroutine(SmoothZoomLens(zoomInLensSize, zoomTransitionSpeed));
            }

            // C. 執行拋物線飛行至鏡牆中心
            yield return FlyLightToWallRoutine(currentLight, wallCenter);

            // D. 觸碰鏡牆：【先執行消融縮小被吸收動畫】
            yield return MeltAndAbsorbLightRoutine(currentLight);

            // E. 【吸收完全結束後，再觸發全螢幕白光閃爍】
            TriggerWhiteScreenFlash(whiteFlashDuration);

            // F. ⏱️ 每個光球間隔 intervalBetweenLights 秒後再啟動下一顆光球
            if (remainingLights.Count > 0)
            {
                yield return new WaitForSeconds(intervalBetweenLights);
            }
        }

        // 6. 全數吸入完成後的結尾碎裂與相機還原
        yield return FinishCutsceneRoutine();
    }

    private AudioSource sfxAudioSource;

    private void PlayDirectSFX(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        if (sfxAudioSource == null)
        {
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.spatialBlend = 0f; // 2D 全螢幕立體聲，零衰減保證 100% 清晰播放
        }
        sfxAudioSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// 拋物線飛行協程 (Quadratic Bezier Arc)
    /// </summary>
    private IEnumerator FlyLightToWallRoutine(GameObject lightObj, Vector3 targetPos)
    {
        Vector3 startPos = lightObj.transform.position;
        Vector3 midControlPoint = (startPos + targetPos) * 0.5f + Vector3.up * arcHeight;

        // ★ 核心：每一顆光球起飛瞬間必定播放一次光離開音效！
        if (lightTakeoffSFX != null)
        {
            PlayDirectSFX(lightTakeoffSFX, sfxVolume);
            Debug.Log($"🔊【鏡牆演出】光球 ({lightObj.name}) 起飛！成功播放光離開音效！");
        }

        float distance = Vector3.Distance(startPos, targetPos);
        float duration = Mathf.Max(0.4f, distance / flySpeed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easeT = t * t * (3f - 2f * t);

            Vector3 currentPos = (1f - easeT) * (1f - easeT) * startPos +
                                 2f * (1f - easeT) * easeT * midControlPoint +
                                 easeT * easeT * targetPos;

            if (lightObj != null)
            {
                lightObj.transform.position = currentPos;
            }

            yield return null;
        }

        if (lightObj != null)
        {
            lightObj.transform.position = targetPos;
        }
    }

    /// <summary>
    /// 光球接觸鏡牆後的【消融縮小與吸收】過渡動畫
    /// </summary>
    private IEnumerator MeltAndAbsorbLightRoutine(GameObject lightObj)
    {
        if (lightObj == null) yield break;

        // 播放光球入鏡消融音效
        if (lightEnterMirrorSFX != null)
        {
            PlayDirectSFX(lightEnterMirrorSFX, sfxVolume);
        }

        Vector3 originalScale = lightObj.transform.localScale;
        SpriteRenderer[] srs = lightObj.GetComponentsInChildren<SpriteRenderer>(true);
        ParticleSystem[] pss = lightObj.GetComponentsInChildren<ParticleSystem>(true);
        Light[] lights = lightObj.GetComponentsInChildren<Light>(true);

        foreach (var ps in pss)
        {
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        float timer = 0f;
        while (timer < meltDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / meltDuration);
            float scaleFactor = Mathf.Lerp(1f, 0f, t * t);
            if (lightObj != null)
            {
                lightObj.transform.localScale = originalScale * scaleFactor;
            }

            foreach (var sr in srs)
            {
                if (sr != null)
                {
                    Color c = sr.color;
                    c.a = Mathf.Lerp(1f, 0f, t);
                    sr.color = c;
                }
            }

            foreach (var l in lights)
            {
                if (l != null)
                {
                    l.intensity = Mathf.Lerp(l.intensity, 0f, t);
                }
            }

            yield return null;
        }

        if (lightObj != null)
        {
            lightObj.SetActive(false);
            lightObj.transform.localScale = originalScale;
        }
    }

    private AudioSource flashAudioSource;

    private void TriggerWhiteScreenFlash(float totalDuration)
    {
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(WhiteFlashRoutine(totalDuration));
    }

    private IEnumerator WhiteFlashRoutine(float totalDuration)
    {
        if (flashImage == null) yield break;

        // ★ 啟動白光共鳴音效：即使音效檔案未經剪輯，也精準配合白光淡入淡出並在閃光結束時即刻停用
        if (flashResonanceSFX != null)
        {
            if (flashAudioSource == null)
            {
                flashAudioSource = gameObject.AddComponent<AudioSource>();
                flashAudioSource.playOnAwake = false;
                flashAudioSource.spatialBlend = 0f; // 2D 全螢幕音效
            }
            flashAudioSource.clip = flashResonanceSFX;
            flashAudioSource.volume = 0f;
            flashAudioSource.time = 0f;
            flashAudioSource.Play();
        }

        float inTimer = 0f;
        while (inTimer < flashFadeInDuration)
        {
            inTimer += Time.deltaTime;
            float norm = Mathf.Clamp01(inTimer / flashFadeInDuration);
            float a = Mathf.Lerp(0f, maxFlashAlpha, norm);
            flashImage.color = new Color(1f, 1f, 1f, a);

            if (flashAudioSource != null && flashAudioSource.isPlaying)
            {
                flashAudioSource.volume = sfxVolume * (a / Mathf.Max(0.01f, maxFlashAlpha));
            }

            yield return null;
        }

        flashImage.color = new Color(1f, 1f, 1f, maxFlashAlpha);
        if (flashAudioSource != null && flashAudioSource.isPlaying)
        {
            flashAudioSource.volume = sfxVolume;
        }

        float outDuration = Mathf.Max(0.5f, totalDuration - flashFadeInDuration);
        float outTimer = 0f;
        while (outTimer < outDuration)
        {
            outTimer += Time.deltaTime;
            float norm = Mathf.Clamp01(outTimer / outDuration);
            float a = Mathf.Lerp(maxFlashAlpha, 0f, norm);
            flashImage.color = new Color(1f, 1f, 1f, a);

            if (flashAudioSource != null && flashAudioSource.isPlaying)
            {
                flashAudioSource.volume = sfxVolume * (a / Mathf.Max(0.01f, maxFlashAlpha));
            }

            yield return null;
        }

        flashImage.color = new Color(1f, 1f, 1f, 0f);

        // ★ 白光閃爍結束瞬間：立即徹底停止音效，絕不拖尾或超時！
        if (flashAudioSource != null && flashAudioSource.isPlaying)
        {
            flashAudioSource.Stop();
        }
    }

    private IEnumerator SmoothZoomLens(float targetSize, float speed)
    {
        float timer = 0f;
        while (timer < 1.0f)
        {
            timer += Time.deltaTime * speed;

            if (activeVcam3 != null)
            {
                var lens = activeVcam3.Lens;
                if (isOrthographic)
                {
                    lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, targetSize, Time.deltaTime * speed * 3f);
                }
                else
                {
                    lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetSize, Time.deltaTime * speed * 3f);
                }
                activeVcam3.Lens = lens;
            }
            else if (activeVcamLegacy != null)
            {
                var lens = activeVcamLegacy.m_Lens;
                if (isOrthographic)
                {
                    lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, targetSize, Time.deltaTime * speed * 3f);
                }
                else
                {
                    lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetSize, Time.deltaTime * speed * 3f);
                }
                activeVcamLegacy.m_Lens = lens;
            }

            yield return null;
        }
    }

    private void SetCameraTarget(Transform target, bool isFocusingLight = false)
    {
        if (target == null) return;

        if (activeVcam3 != null)
        {
            var t = activeVcam3.Target;
            t.TrackingTarget = target;
            activeVcam3.Target = t;
            activeVcam3.Follow = target;

            if (cmFollow != null)
            {
                cmFollow.FollowOffset = new Vector3(cmFollow.FollowOffset.x, 0f, cmFollow.FollowOffset.z);
            }
        }

        if (activeVcamLegacy != null)
        {
            activeVcamLegacy.Follow = target;
        }
    }

    private IEnumerator FinishCutsceneRoutine()
    {
        if (delayBeforeShatter > 0f)
        {
            yield return new WaitForSeconds(delayBeforeShatter);
        }

        // 觸發鏡牆玻璃碎裂特效 (方案 B)
        if (triggerShatterAtEnd && mirrorWall != null)
        {
            Debug.Log("💥【鏡牆演出】4 顆光球充能完畢，觸發鏡牆玻璃碎裂特效！");

            // 播放鏡牆碎裂音效
            if (mirrorShatterSFX != null)
            {
                PlayDirectSFX(mirrorShatterSFX, sfxVolume);
            }

            Destructible dest = mirrorWall.GetComponent<Destructible>();
            if (dest == null) dest = mirrorWall.GetComponentInChildren<Destructible>();

            GlassShatterFX gfx = mirrorWall.GetComponent<GlassShatterFX>();
            if (gfx == null) gfx = mirrorWall.GetComponentInChildren<GlassShatterFX>();

            if (dest != null)
            {
                dest.Shatter();
            }
            else if (gfx != null)
            {
                gfx.ExecuteShatter();
            }
            else
            {
                mirrorWall.SetActive(false);
            }
        }

        if (delayBeforeRestorePlayer > 0f)
        {
            yield return new WaitForSeconds(delayBeforeRestorePlayer);
        }

        // 還原相機追蹤主角
        if (originalCameraTarget != null)
        {
            SetCameraTarget(originalCameraTarget, false);
        }
        else if (cachedPlayer != null)
        {
            SetCameraTarget(cachedPlayer.transform, false);
        }

        if (enableCameraZoom)
        {
            yield return StartCoroutine(SmoothZoomLens(originalLensSize, zoomTransitionSpeed * 0.8f));
        }

        if (cachedPlayer != null)
        {
            cachedPlayer.isCutsceneFrozen = false;
        }

        isCutsceneRunning = false;
        IsAnyCutsceneRunning = false;
        Debug.Log("✅【鏡牆演出結束】相機已還原追蹤主角，玩家控制權已恢復！");

        // ★【結局】鏡牆吸入演完 → 過場文字 M5 →（維持全黑）→ 結尾漫畫 → 片尾名單 → 主選單
        if (playEndingAfterCutscene)
        {
            if (cachedPlayer != null) cachedPlayer.isCutsceneFrozen = true;

            if (StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.HasCard(endingCardId))
            {
                yield return StoryCardPlayer.Instance.Play(endingCardId, true, false);
            }

            EndCredits.EndingMode = true;
            Debug.Log("🎬【結局】載入結尾漫畫場景：" + endingBookScene);
            UnityEngine.SceneManagement.SceneManager.LoadScene(endingBookScene);
        }
    }

    /// <summary>
    /// 【IResettable 實作】：主角死亡重生時，強制中止演出並 100% 刷新所有光球、鏡牆與相機狀態！
    /// </summary>
    public void ResetToInitialState()
    {
        Debug.Log("🔄【鏡牆演出】收到重生重置訊號！全面中止演出並刷新 4 顆光球與鏡牆...");

        // 1. 中止所有演出協程
        StopAllCoroutines();
        mainCutsceneCoroutine = null;
        flashCoroutine = null;
        isCutsceneRunning = false;
        IsAnyCutsceneRunning = false;
        hasTriggered = false;

        if (hoverAudioSource != null)
        {
            hoverAudioSource.Stop();
        }

        if (flashAudioSource != null)
        {
            flashAudioSource.Stop();
        }

        // 2. 隱藏白光閃爍畫面
        if (flashImage != null)
        {
            flashImage.color = new Color(1f, 1f, 1f, 0f);
        }

        // 3. 100% 還原所有 4 顆光球的位置、大小、顯示狀態與粒子
        if (fairyLights != null)
        {
            for (int i = 0; i < fairyLights.Length; i++)
            {
                GameObject lightObj = fairyLights[i];
                if (lightObj != null)
                {
                    lightObj.SetActive(true);
                    if (_initialLightPositions != null && i < _initialLightPositions.Length)
                    {
                        lightObj.transform.position = _initialLightPositions[i];
                    }
                    if (_initialLightScales != null && i < _initialLightScales.Length)
                    {
                        lightObj.transform.localScale = _initialLightScales[i];
                    }

                    // 還原 SpriteRenderer 透明度
                    SpriteRenderer[] srs = lightObj.GetComponentsInChildren<SpriteRenderer>(true);
                    foreach (var sr in srs)
                    {
                        if (sr != null)
                        {
                            Color c = sr.color;
                            c.a = 1f;
                            sr.color = c;
                        }
                    }

                    // 重新啟動 ParticleSystem 噴發
                    ParticleSystem[] pss = lightObj.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var ps in pss)
                    {
                        if (ps != null)
                        {
                            ps.Clear();
                            ps.Play();
                        }
                    }
                }
            }
        }

        // 4. 100% 還原鏡牆與碎裂特效
        if (mirrorWall != null)
        {
            mirrorWall.SetActive(true);
            Destructible dest = mirrorWall.GetComponent<Destructible>();
            if (dest == null) dest = mirrorWall.GetComponentInChildren<Destructible>();
            if (dest != null) dest.ResetToInitialState();

            GlassShatterFX gfx = mirrorWall.GetComponent<GlassShatterFX>();
            if (gfx == null) gfx = mirrorWall.GetComponentInChildren<GlassShatterFX>();
            if (gfx != null) gfx.ResetToInitialState();
        }

        // 5. 還原相機鏡頭尺寸與目標
        FindCinemachineCameras();
        if (cachedPlayer == null) cachedPlayer = FindFirstObjectByType<PlayerMovement>();
        if (cachedPlayer != null)
        {
            SetCameraTarget(cachedPlayer.transform, false);
            cachedPlayer.isCutsceneFrozen = false;
        }
        else if (originalCameraTarget != null)
        {
            SetCameraTarget(originalCameraTarget, false);
        }

        if (activeVcam3 != null)
        {
            var lens = activeVcam3.Lens;
            if (isOrthographic) lens.OrthographicSize = originalLensSize;
            else lens.FieldOfView = originalLensSize;
            activeVcam3.Lens = lens;
        }
        else if (activeVcamLegacy != null)
        {
            var lens = activeVcamLegacy.m_Lens;
            if (isOrthographic) lens.OrthographicSize = originalLensSize;
            else lens.FieldOfView = originalLensSize;
            activeVcamLegacy.m_Lens = lens;
        }

        Debug.Log("✅【鏡牆演出】光球、鏡牆與相機已全數刷新重置完畢！");
    }

    private Vector3 GetMirrorWallCenter()
    {
        if (mirrorWall == null) return transform.position;

        Collider col = mirrorWall.GetComponent<Collider>();
        if (col != null) return col.bounds.center;

        SpriteRenderer sr = mirrorWall.GetComponent<SpriteRenderer>();
        if (sr != null) return sr.bounds.center;

        return mirrorWall.transform.position;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }

        if (mirrorWall != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 center = GetMirrorWallCenter();
            Gizmos.DrawWireSphere(center, 0.5f);
            Gizmos.DrawLine(transform.position, center);
        }
    }
}
