using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// 業界標準：常駐 Mario-style 橫向捲軸相機控制器 + 垂直高速墜落 Falling Mode + 暫時性 Camera Override 系統
/// 1. 【常駐核心】：永遠存在，負責 Player X 軸跟隨、Y 軸鎖定在背景世界中心、正交尺寸自動貼合背景高度、整條背景世界左右邊界 Clamp。
/// 2. 【自適應背景世界 (Background World)】：自動辨識同層整排相連背景的總 Bounds，不寫死單張大小，動態計算 Center Y、Height 與 OrthoSize。
/// 3. 【Falling Mode (垂直高速墜落模式)】：進入 FallingBackground 時，X 軸鎖定在通道中心 (玩家左右移動相機 X 不晃動)，Y 軸高速垂直緊隨玩家，離開時立即無縫還原 Mario Mode！
/// 4. 【暫時性 Override 支援】：劇情/特寫時可暫時讓出控制權 (SetCameraOverride)，結束時 (ClearCameraOverride) 瞬間無縫無痛還原 Mario 鏡頭！
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
public class CameraTargetXFollower : MonoBehaviour
{
    public static CameraTargetXFollower Instance { get; private set; }

    public enum CameraMode
    {
        Mario,              // 正常橫向捲軸模式 (X跟隨Player, Y鎖死背景中心)
        Falling,            // 垂直高速墜落模式 (X鎖定FallingBackground中心, Y高速跟隨Player)
        CinematicOverride   // 暫時性劇情特寫覆寫
    }

    public enum BackgroundZone
    {
        None,
        UpperCastle,
        Sky,
        Ruins,
        Desert
    }

    [Header("跟隨目標")]
    [Tooltip("水平跟隨的目標 (通常為 Player)")]
    public Transform targetToFollow;

    [Header("Cinemachine 鏡頭指定")]
    [Tooltip("要控制的主要遊戲鏡頭。指定後不會改動其他過場或特殊用途的虛擬鏡頭。")]
    public CinemachineCamera cameraToControl;

    [Tooltip("未指定主要鏡頭時，是否將所有 Cinemachine 鏡頭改為追蹤此目標（舊場景相容用）。")]
    public bool retargetAllCinemachineCameras = true;

    [Header("🏰 城堡高空層 (最上層) 背景貼合設定")]
    public float upperZoneThresholdY = 25.0f;
    public float upperZoneFixedY = 46.3f;
    public float upperZoneOrthoSize = 16.42f;

    [Header("☁️ 棉花堡 (天空層) 背景貼合設定")]
    public float skyZoneFixedY = 4.58f;
    public float skyZoneOrthoSize = 15.0f;

    [Header("🏛️ 廢墟層 背景貼合設定")]
    public float ruinedZoneThresholdY = -60f;
    public float ruinedZoneFixedY = -120.7f;
    public float ruinedZoneOrthoSize = 11.1f;

    [Header("🏜️ 荒原沙漠 專用設定")]
    public bool isDesertScene = false;
    public float desertFixedY = 5.29f;
    public float desertOrthoSize = 17.0f;

    [Header("平滑過渡設定")]
    public float transitionSpeed = 8.0f;
    public bool useGradualZoneTransition = false;

    [Header("🛡️ 整排背景世界邊界 Clamp 防護")]
    public bool enableHorizontalBoundaryClamp = true;

    [Header("🕳️ Falling Mode (垂直高速墜落模式)")]
    [Tooltip("當前運行的相機模式")]
    public CameraMode currentMode = CameraMode.Mario;

    [Tooltip("Falling Mode 下相機相對於玩家的 Y 軸偏移量 (正數往上，負數往下，預設 0)")]
    public float fallingCameraYOffset = 0f;

    [Tooltip("Falling Mode 下 Y 軸垂直追隨基礎速度 (高速墜落時會自動加速跟隨)")]
    public float fallingFollowSpeed = 18f;

    [Header("🎬 暫時性 Camera Override 狀態 (劇情/特寫用)")]
    public bool isOverridden = false;
    public Transform overrideTarget;
    public float overrideOrthoSize = -1f;

    // 快取的整排背景世界合併邊界 (Compound Row Bounds)
    private float _upperMinX = float.MinValue;
    private float _upperMaxX = float.MaxValue;
    private float _skyMinX = float.MinValue;
    private float _skyMaxX = float.MaxValue;
    private float _ruinMinX = float.MinValue;
    private float _ruinMaxX = float.MaxValue;
    private float _desertMinX = float.MinValue;
    private float _desertMaxX = float.MaxValue;

    // 快取的 FallingBackground 邊界清單
    private List<Bounds> _fallingBoundsList = new List<Bounds>();

    private float currentTargetY = 4.58f;
    private float currentOrthoSize = 15.0f;
    private Vector3 _lastPlayerPos;
    private bool _hasLastPlayerPos = false;
    private BackgroundZone _lastZone = BackgroundZone.None;
    private CinemachineCamera _vcam;
    private Camera _mainCam;
    private ParallaxGroup _ruinsParallaxGroup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoEnsureInScene()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToLower();
        if (sceneName.Contains("samplescene") || sceneName.Contains("ruin") || sceneName.Contains("desert"))
        {
            GameObject followerObj = GameObject.Find("CameraFollowTarget");
            CameraTargetXFollower follower;
            if (followerObj == null)
            {
                followerObj = new GameObject("CameraFollowTarget");
                follower = followerObj.AddComponent<CameraTargetXFollower>();

                if (sceneName.Contains("desert"))
                {
                    follower.isDesertScene = true;
                    follower.desertFixedY = 5.29f;
                    follower.desertOrthoSize = 17f;
                }
                else
                {
                    follower.isDesertScene = false;
                    follower.upperZoneThresholdY = 25.0f;
                    follower.upperZoneFixedY = 46.3f;
                    follower.upperZoneOrthoSize = 16.42f;
                    follower.skyZoneFixedY = 4.58f;
                    follower.skyZoneOrthoSize = 15.0f;
                    follower.ruinedZoneFixedY = -120.7f;
                    follower.ruinedZoneOrthoSize = 11.1f;
                    follower.ruinedZoneThresholdY = -60f;
                    follower.useGradualZoneTransition = false;
                }
            }
            else
            {
                follower = followerObj.GetComponent<CameraTargetXFollower>();
                if (follower == null) follower = followerObj.AddComponent<CameraTargetXFollower>();
                if (!sceneName.Contains("desert"))
                {
                    follower.upperZoneThresholdY = 25.0f;
                    follower.upperZoneFixedY = 46.3f;
                    follower.upperZoneOrthoSize = 16.42f;
                    follower.skyZoneFixedY = 4.58f;
                    follower.skyZoneOrthoSize = 15.0f;
                    follower.ruinedZoneFixedY = -120.7f;
                    follower.ruinedZoneOrthoSize = 11.1f;
                    follower.ruinedZoneThresholdY = -60f;
                    follower.useGradualZoneTransition = false;
                }
            }

            follower.RetargetCinemachineCameras();
        }
    }

    void Awake()
    {
        Instance = this;
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToLower();
        if (sceneName.Contains("desert"))
        {
            isDesertScene = true;
        }
        else
        {
            isDesertScene = false;
        }
    }

    void Start()
    {
        Instance = this;
        _mainCam = Camera.main;
        _vcam = cameraToControl != null
            ? cameraToControl
            : Object.FindFirstObjectByType<CinemachineCamera>();

        FindTarget();
        CalculateUnifiedBackgroundBounds();
        RetargetCinemachineCameras();
        ApplyImmediatePosition();
    }

    void OnEnable()
    {
        Instance = this;
        FindTarget();
        CalculateUnifiedBackgroundBounds();
        ApplyImmediatePosition();
    }

    #region 🎬 暫時性 Camera Override API (支援劇情/特寫無縫切換與還原)

    public static void SetCameraOverride(Transform customFocusTarget, float? customOrthoSize = null)
    {
        if (Instance == null) Instance = Object.FindFirstObjectByType<CameraTargetXFollower>();
        if (Instance == null) return;

        Instance.isOverridden = true;
        Instance.overrideTarget = customFocusTarget;
        Instance.overrideOrthoSize = customOrthoSize ?? -1f;
        Debug.Log($"🎬【Camera Override 啟用】聚焦目標: {customFocusTarget?.name}");
    }

    public static void ClearCameraOverride()
    {
        if (Instance == null) Instance = Object.FindFirstObjectByType<CameraTargetXFollower>();
        if (Instance == null) return;

        ReacquireCamera();
        Debug.Log("🎬【Camera Override 結束】已無縫還原為常駐相機模式！");
    }

    #endregion

    void FindTarget()
    {
        if (targetToFollow == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null) player = GameObject.Find("Player");
            if (player != null) targetToFollow = player.transform;
        }
    }

    public void CalculateUnifiedBackgroundBounds()
    {
        float uMinX = float.MaxValue, uMaxX = float.MinValue;
        float sMinX = float.MaxValue, sMaxX = float.MinValue;
        float rMinX = float.MaxValue, rMaxX = float.MinValue;
        float dMinX = float.MaxValue, dMaxX = float.MinValue;

        _fallingBoundsList.Clear();

        SpriteRenderer[] srs = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        foreach (var sr in srs)
        {
            if (sr == null || !sr.enabled) continue;
            string n = sr.gameObject.name.ToLower();
            string t = sr.gameObject.tag;

            if (t == "FallingBackground" || n.Contains("falling") || n.Contains("connect_"))
            {
                Bounds fb = sr.bounds;
                if (fb.size.x > 0.5f && fb.size.y > 0.5f) _fallingBoundsList.Add(fb);
            }

            bool isVisualBg = (t == "Background" || t == "RuinedBackground" || n.Contains("background") || n.Contains("sky") || n.Contains("split"))
                              && t != "Floor" && t != "Ground" && !n.Contains("ground") && !n.Contains("floor") && !n.Contains("furniture");

            if (isVisualBg)
            {
                Bounds b = sr.bounds;
                if (b.size.x < 0.5f || b.size.y < 0.5f) continue;

                if (isDesertScene)
                {
                    dMinX = Mathf.Min(dMinX, b.min.x);
                    dMaxX = Mathf.Max(dMaxX, b.max.x);
                }
                else
                {
                    if (b.center.y >= upperZoneThresholdY)
                    {
                        uMinX = Mathf.Min(uMinX, b.min.x);
                        uMaxX = Mathf.Max(uMaxX, b.max.x);
                    }
                    else if (b.center.y > ruinedZoneThresholdY)
                    {
                        sMinX = Mathf.Min(sMinX, b.min.x);
                        sMaxX = Mathf.Max(sMaxX, b.max.x);
                    }
                    else
                    {
                        rMinX = Mathf.Min(rMinX, b.min.x);
                        rMaxX = Mathf.Max(rMaxX, b.max.x);
                    }
                }
            }
        }

        Collider[] cols = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (var c in cols)
        {
            if (c == null || !c.enabled) continue;
            string n = c.gameObject.name.ToLower();
            string t = c.gameObject.tag;

            if (t == "FallingBackground" || n.Contains("falling") || n.Contains("connect_"))
            {
                Bounds fb = c.bounds;
                if (fb.size.x > 0.5f && fb.size.y > 0.5f) _fallingBoundsList.Add(fb);
            }

            if (t == "Background" || t == "RuinedBackground" || t == "CameraBoundary" || n.Contains("background"))
            {
                Bounds b = c.bounds;
                if (b.size.x < 0.5f || b.size.y < 0.5f) continue;

                if (isDesertScene)
                {
                    dMinX = Mathf.Min(dMinX, b.min.x);
                    dMaxX = Mathf.Max(dMaxX, b.max.x);
                }
                else
                {
                    if (b.center.y >= upperZoneThresholdY)
                    {
                        uMinX = Mathf.Min(uMinX, b.min.x);
                        uMaxX = Mathf.Max(uMaxX, b.max.x);
                    }
                    else if (b.center.y > ruinedZoneThresholdY)
                    {
                        sMinX = Mathf.Min(sMinX, b.min.x);
                        sMaxX = Mathf.Max(sMaxX, b.max.x);
                    }
                    else
                    {
                        rMinX = Mathf.Min(rMinX, b.min.x);
                        rMaxX = Mathf.Max(rMaxX, b.max.x);
                    }
                }
            }
        }

        if (uMinX < uMaxX) { _upperMinX = uMinX; _upperMaxX = uMaxX; }
        if (sMinX < sMaxX) { _skyMinX = sMinX; _skyMaxX = sMaxX; }
        if (rMinX < rMaxX) { _ruinMinX = rMinX; _ruinMaxX = rMaxX; }
        if (dMinX < dMaxX) { _desertMinX = dMinX; _desertMaxX = dMaxX; }

        Debug.Log($"【相機邊界自動合併】\n" +
                  $" - 城堡高空層整排邊界 X: [{_upperMinX:F1} ~ {_upperMaxX:F1}]\n" +
                  $" - 天空層整排邊界 X: [{_skyMinX:F1} ~ {_skyMaxX:F1}]\n" +
                  $" - 廢墟層整排邊界 X: [{_ruinMinX:F1} ~ {_ruinMaxX:F1}]\n" +
                  $" - 荒原層整排邊界 X: [{_desertMinX:F1} ~ {_desertMaxX:F1}]\n" +
                  $" - 快取 FallingBackground 數量: {_fallingBoundsList.Count}");
    }

    public BackgroundZone GetCurrentZone(float playerY)
    {
        if (isDesertScene) return BackgroundZone.Desert;
        if (playerY >= upperZoneThresholdY) return BackgroundZone.UpperCastle;
        if (playerY > ruinedZoneThresholdY) return BackgroundZone.Sky;
        return BackgroundZone.Ruins;
    }

    void Update()
    {
        UpdatePosition();
    }

    void LateUpdate()
    {
        UpdatePosition();
    }

    private void ApplyImmediatePosition()
    {
        if (targetToFollow == null) FindTarget();
        if (targetToFollow == null) return;

        Bounds? activeFB = GetActiveFallingBounds(targetToFollow.position);
        if (activeFB.HasValue)
        {
            currentMode = CameraMode.Falling;
            ApplyFallingModePosition(activeFB.Value, targetToFollow.position, true);
        }
        else
        {
            currentMode = CameraMode.Mario;
            BackgroundZone currentZone = GetCurrentZone(targetToFollow.position.y);
            _lastZone = currentZone;
            ReinitializeCameraForZone(currentZone, targetToFollow.position);
        }
    }

    public Bounds? GetActiveFallingBounds(Vector3 playerPos)
    {
        if (_fallingBoundsList == null || _fallingBoundsList.Count == 0) return null;
        for (int i = 0; i < _fallingBoundsList.Count; i++)
        {
            Bounds b = _fallingBoundsList[i];
            if (playerPos.x >= b.min.x - 3f && playerPos.x <= b.max.x + 3f &&
                playerPos.y >= b.min.y - 1f && playerPos.y <= b.max.y + 1f)
            {
                return b;
            }
        }
        return null;
    }

    public void ReinitializeCameraForZone(BackgroundZone zone, Vector3 playerPos)
    {
        switch (zone)
        {
            case BackgroundZone.UpperCastle:
                currentTargetY = upperZoneFixedY;
                currentOrthoSize = upperZoneOrthoSize;
                break;
            case BackgroundZone.Sky:
                currentTargetY = skyZoneFixedY;
                currentOrthoSize = skyZoneOrthoSize;
                break;
            case BackgroundZone.Ruins:
                currentTargetY = ruinedZoneFixedY;
                currentOrthoSize = ruinedZoneOrthoSize;
                break;
            case BackgroundZone.Desert:
                currentTargetY = desertFixedY;
                currentOrthoSize = desertOrthoSize;
                break;
        }

        CalculateUnifiedBackgroundBounds();

        float clampedX = GetClampedX(playerPos.x, currentOrthoSize, playerPos.y);
        transform.position = new Vector3(clampedX, currentTargetY, 0f);
        _lastPlayerPos = playerPos;
        _hasLastPlayerPos = true;

        ApplyOrthoSize(currentOrthoSize);

        RetargetCinemachineCameras();
        if (_vcam != null)
        {
            _vcam.PreviousStateIsValid = false;
        }

        if (_mainCam != null)
        {
            _mainCam.transform.position = new Vector3(clampedX, currentTargetY, _mainCam.transform.position.z);
        }

        var confiner = Object.FindFirstObjectByType<CinemachineCameraConfiner3D>();
        if (confiner != null)
        {
            confiner.CacheBoundaries();
        }

        Debug.Log($"🎯【Camera 區域切換 Setup 完成】當前區域: {zone}, 固定 Y: {currentTargetY}, OrthoSize: {currentOrthoSize}");
    }

    public void ReacquireMarioCamera(Vector3 playerPos)
    {
        currentMode = CameraMode.Mario;
        BackgroundZone currentZone = GetCurrentZone(playerPos.y);
        _lastZone = currentZone;
        ReinitializeCameraForZone(currentZone, playerPos);
        Debug.Log("🎮【恢復 Mario Mode】已無縫切換回 Mario 橫向相機模式！");
    }

    public static void ReacquireCamera()
    {
        if (Instance == null) Instance = Object.FindFirstObjectByType<CameraTargetXFollower>();
        if (Instance == null) return;

        Instance.isOverridden = false;
        Instance.overrideTarget = null;
        Instance.overrideOrthoSize = -1f;

        if (Instance.targetToFollow != null)
        {
            Vector3 pPos = Instance.targetToFollow.position;
            Bounds? activeFB = Instance.GetActiveFallingBounds(pPos);
            if (activeFB.HasValue)
            {
                Instance.currentMode = CameraMode.Falling;
                if (Instance._vcam != null) Instance._vcam.PreviousStateIsValid = false;
                Instance.ApplyFallingModePosition(activeFB.Value, pPos, true);
            }
            else
            {
                Instance.ReacquireMarioCamera(pPos);
            }
        }
    }

    void UpdatePosition()
    {
        if (targetToFollow == null)
        {
            FindTarget();
            if (targetToFollow == null) return;
        }

        Vector3 playerPos = targetToFollow.position;
        float playerY = playerPos.y;

        Bounds? activeFallingBounds = GetActiveFallingBounds(playerPos);
        CameraMode targetMode;

        if (isOverridden && overrideTarget != null)
        {
            targetMode = CameraMode.CinematicOverride;
        }
        else if (activeFallingBounds.HasValue)
        {
            targetMode = CameraMode.Falling;
        }
        else
        {
            targetMode = CameraMode.Mario;
        }

        if (targetMode != currentMode)
        {
            currentMode = targetMode;

            if (targetMode == CameraMode.Mario)
            {
                ReacquireMarioCamera(playerPos);
                return;
            }
            else if (targetMode == CameraMode.Falling)
            {
                if (_vcam != null) _vcam.PreviousStateIsValid = false;
                ApplyFallingModePosition(activeFallingBounds.Value, playerPos, true);
                return;
            }
        }

        switch (currentMode)
        {
            case CameraMode.CinematicOverride:
                Vector3 overridePos = overrideTarget.position;
                transform.position = new Vector3(overridePos.x, overridePos.y, 0f);
                if (overrideOrthoSize > 0f) ApplyOrthoSize(overrideOrthoSize);
                break;

            case CameraMode.Falling:
                if (activeFallingBounds.HasValue)
                {
                    ApplyFallingModePosition(activeFallingBounds.Value, playerPos, false);
                }
                break;

            case CameraMode.Mario:
            default:
                BackgroundZone currentZone = GetCurrentZone(playerY);

                if (currentZone != _lastZone)
                {
                    _lastZone = currentZone;
                    ReinitializeCameraForZone(currentZone, playerPos);
                    return;
                }

                if (_hasLastPlayerPos)
                {
                    float distMoved = Vector3.Distance(playerPos, _lastPlayerPos);
                    if (distMoved > 10f)
                    {
                        ReinitializeCameraForZone(currentZone, playerPos);
                        return;
                    }
                }
                _lastPlayerPos = playerPos;
                _hasLastPlayerPos = true;

                float clampedX = GetClampedX(playerPos.x, currentOrthoSize, playerY);
                transform.position = new Vector3(clampedX, currentTargetY, 0f);

                ApplyOrthoSize(currentOrthoSize);
                break;
        }
    }

    private void ApplyFallingModePosition(Bounds fb, Vector3 playerPos, bool instant)
    {
        float camX = fb.center.x;

        float aspect = (_mainCam != null && _mainCam.aspect > 0.1f) ? _mainCam.aspect : (16f / 9f);
        float shaftHalfWidth = fb.extents.x;
        float desiredOrtho = Mathf.Clamp(shaftHalfWidth / aspect, 10f, 18f);

        if (instant || !Application.isPlaying)
        {
            currentOrthoSize = desiredOrtho;
        }
        else
        {
            currentOrthoSize = Mathf.Lerp(currentOrthoSize, desiredOrtho, Time.deltaTime * 6f);
        }

        float targetY = playerPos.y + fallingCameraYOffset;
        float halfHeight = currentOrthoSize;
        float minY = fb.min.y + halfHeight;
        float maxY = fb.max.y - halfHeight;

        if (minY <= maxY)
        {
            targetY = Mathf.Clamp(targetY, minY, maxY);
        }
        else
        {
            targetY = fb.center.y;
        }

        if (instant || !Application.isPlaying)
        {
            currentTargetY = targetY;
        }
        else
        {
            float verticalDiff = Mathf.Abs(playerPos.y - currentTargetY);
            float currentSpeed = (verticalDiff > 4f) ? 28f : fallingFollowSpeed;
            currentTargetY = Mathf.Lerp(currentTargetY, targetY, Time.deltaTime * currentSpeed);
        }

        transform.position = new Vector3(camX, currentTargetY, 0f);
        _lastPlayerPos = playerPos;
        _hasLastPlayerPos = true;

        ApplyOrthoSize(currentOrthoSize);
    }

    private float GetClampedX(float rawX, float orthoSize, float playerY)
    {
        if (!enableHorizontalBoundaryClamp) return rawX;

        float minBoundX = float.MinValue;
        float maxBoundX = float.MaxValue;

        if (isDesertScene)
        {
            minBoundX = _desertMinX;
            maxBoundX = _desertMaxX;
        }
        else
        {
            if (playerY >= upperZoneThresholdY)
            {
                minBoundX = _upperMinX;
                maxBoundX = _upperMaxX;
            }
            else if (playerY > ruinedZoneThresholdY)
            {
                minBoundX = _skyMinX;
                maxBoundX = _skyMaxX;
            }
            else
            {
                // 廢墟層：使用穩定的全關卡背景邊界（與 Parallax 即時位移解耦）
                minBoundX = _ruinMinX;
                maxBoundX = _ruinMaxX;
            }
        }

        if (minBoundX >= maxBoundX || minBoundX <= float.MinValue + 100f)
        {
            CalculateUnifiedBackgroundBounds();
            return rawX;
        }

        float aspect = (_mainCam != null && _mainCam.aspect > 0.1f) ? _mainCam.aspect : (16f / 9f);
        float halfWidth = orthoSize * aspect;

        float minCamX = minBoundX + halfWidth;
        float maxCamX = maxBoundX - halfWidth;

        float finalCameraX = rawX;
        if (minCamX <= maxCamX)
        {
            finalCameraX = Mathf.Clamp(rawX, minCamX, maxCamX);
        }
        else
        {
            finalCameraX = Mathf.Clamp(rawX, minBoundX, maxBoundX);
        }

        #if UNITY_EDITOR
        if (!isDesertScene && playerY <= ruinedZoneThresholdY && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[RuinsCamera]\nPlayerX = {rawX:F2}\nRawCameraX = {rawX:F2}\nBackgroundMinX = {minBoundX:F2}\nBackgroundMaxX = {maxBoundX:F2}\nHalfWidth = {halfWidth:F2}\nMinCameraX = {minCamX:F2}\nMaxCameraX = {maxCamX:F2}\nFinalCameraX = {finalCameraX:F2}");
        }
        #endif

        return finalCameraX;
    }

    private void ApplyOrthoSize(float size)
    {
        if (cameraToControl != null) _vcam = cameraToControl;
        else if (_vcam == null) _vcam = Object.FindFirstObjectByType<CinemachineCamera>();
        if (_vcam != null)
        {
            _vcam.Lens.OrthographicSize = size;
        }

        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam != null)
        {
            _mainCam.orthographicSize = size;
        }
    }

    private void RetargetCinemachineCameras()
    {
        if (cameraToControl != null)
        {
            SetTrackingTarget(cameraToControl);
            return;
        }

        if (!retargetAllCinemachineCameras) return;

        var vcams = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach (var vcam in vcams)
        {
            SetTrackingTarget(vcam);
        }
    }

    private void SetTrackingTarget(CinemachineCamera vcam)
    {
        if (vcam == null) return;

        var target = vcam.Target;
        target.TrackingTarget = transform;
        target.LookAtTarget = transform;
        target.CustomLookAtTarget = true;
        vcam.Target = target;
    }
}
