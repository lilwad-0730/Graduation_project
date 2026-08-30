using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// 業界標準：橫向捲軸相機跟隨與背景高度精確貼合控制器 (Horizontal X-Follower with Fixed Zone Y & Background Height Fitting)
/// 1. 鏡頭高度大小 (OrthographicSize) 根據場景背景高度精確縮放，上下完全貼齊背景，絕不破圖露底！
/// 2. 左右跟隨主角 (X 軸)，垂直 Y 軸完全鎖死在場景背景中心，跳躍時鏡頭完全不上下晃動！
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
public class CameraTargetXFollower : MonoBehaviour
{
    [Header("跟隨目標")]
    [Tooltip("水平跟隨的目標 (通常為 Player)")]
    public Transform targetToFollow;

    [Header("棉花堡 (天空層) 背景精確貼合")]
    [Tooltip("棉花堡 (天空層) 固定 Y 軸中心高度 (背景正中心)")]
    public float skyZoneFixedY = 4.8f;

    [Tooltip("棉花堡 (天空層) 相機尺寸 Orthographic Size (貼合灰色山脈背景高度 15.6)")]
    public float skyZoneOrthoSize = 7.8f;

    [Header("廢墟層 背景精確貼合")]
    [Tooltip("進入廢墟層的 Y 軸門檻")]
    public float ruinedZoneThresholdY = -60f;

    [Tooltip("廢墟層 固定 Y 軸中心高度 (廢墟背景正中心)")]
    public float ruinedZoneFixedY = -116.5f;

    [Tooltip("廢墟層 相機尺寸 Orthographic Size (貼合廢墟背景高度 22.0)")]
    public float ruinedZoneOrthoSize = 11.0f;

    [Header("荒原沙漠 專用設定")]
    [Tooltip("是否為荒原沙漠場景")]
    public bool isDesertScene = false;

    [Tooltip("荒原沙漠 固定 Y 軸中心高度")]
    public float desertFixedY = 5.29f;

    [Tooltip("荒原沙漠 相機尺寸 Orthographic Size")]
    public float desertOrthoSize = 17.0f;

    [Header("平滑過渡設定")]
    [Tooltip("相機高度與尺寸平滑過渡速度")]
    public float transitionSpeed = 8.0f;

    private float currentTargetY = 4.8f;
    private float currentOrthoSize = 7.8f;
    private CinemachineCamera _vcam;
    private Camera _mainCam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoEnsureInScene()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToLower();
        if (sceneName.Contains("samplescene") || sceneName.Contains("ruin") || sceneName.Contains("desert"))
        {
            GameObject followerObj = GameObject.Find("CameraFollowTarget");
            if (followerObj == null)
            {
                followerObj = new GameObject("CameraFollowTarget");
                var follower = followerObj.AddComponent<CameraTargetXFollower>();

                if (sceneName.Contains("desert"))
                {
                    follower.isDesertScene = true;
                    follower.desertFixedY = 5.29f;
                    follower.desertOrthoSize = 17f;
                }
                else
                {
                    follower.isDesertScene = false;
                    follower.skyZoneFixedY = 4.8f;
                    follower.skyZoneOrthoSize = 7.8f;
                    follower.ruinedZoneFixedY = -116.5f;
                    follower.ruinedZoneOrthoSize = 11.0f;
                }
            }

            var vcams = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            foreach (var v in vcams)
            {
                if (v != null)
                {
                    var t = v.Target;
                    t.TrackingTarget = followerObj.transform;
                    t.LookAtTarget = followerObj.transform;
                    t.CustomLookAtTarget = true;
                    v.Target = t;
                }
            }
        }
    }

    void Awake()
    {
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
        _mainCam = Camera.main;
        _vcam = Object.FindFirstObjectByType<CinemachineCamera>();

        FindTarget();
        ApplyImmediatePosition();
    }

    void OnEnable()
    {
        FindTarget();
        ApplyImmediatePosition();
    }

    void FindTarget()
    {
        if (targetToFollow == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null) player = GameObject.Find("Player");
            if (player != null) targetToFollow = player.transform;
        }
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

        float targetY;
        float targetOrtho;

        if (isDesertScene)
        {
            targetY = desertFixedY;
            targetOrtho = desertOrthoSize;
        }
        else
        {
            float playerY = targetToFollow.position.y;
            if (playerY > ruinedZoneThresholdY)
            {
                targetY = skyZoneFixedY;
                targetOrtho = skyZoneOrthoSize;
            }
            else
            {
                targetY = ruinedZoneFixedY;
                targetOrtho = ruinedZoneOrthoSize;
            }
        }

        currentTargetY = targetY;
        currentOrthoSize = targetOrtho;
        transform.position = new Vector3(targetToFollow.position.x, currentTargetY, 0f);
        ApplyOrthoSize(currentOrthoSize);
    }

    void UpdatePosition()
    {
        if (targetToFollow == null)
        {
            FindTarget();
            if (targetToFollow == null) return;
        }

        Vector3 pos = transform.position;

        // 1. 水平 X 軸：100% 跟隨玩家
        pos.x = targetToFollow.position.x;

        // 2. 垂直 Y 軸與鏡頭大小：100% 貼合背景，跳躍時 Y 軸完全不動
        float desiredY;
        float desiredOrtho;

        if (isDesertScene)
        {
            desiredY = desertFixedY;
            desiredOrtho = desertOrthoSize;
        }
        else
        {
            float playerY = targetToFollow.position.y;

            if (playerY > ruinedZoneThresholdY)
            {
                // 棉花堡層：鏡頭中心 = 4.8，大小 = 7.8 (上下完全貼合灰色背景)
                desiredY = skyZoneFixedY;
                desiredOrtho = skyZoneOrthoSize;
            }
            else if (playerY <= ruinedZoneThresholdY && playerY > ruinedZoneFixedY + 5f)
            {
                // 跳崖墜落深淵中：平滑跟隨下墜
                float t = Mathf.InverseLerp(ruinedZoneThresholdY, ruinedZoneFixedY + 5f, playerY);
                desiredY = Mathf.Lerp(ruinedZoneFixedY, skyZoneFixedY, t);
                desiredOrtho = Mathf.Lerp(ruinedZoneOrthoSize, skyZoneOrthoSize, t);
            }
            else
            {
                // 廢墟層：鏡頭中心 = -116.5，大小 = 11.0 (上下完全貼合廢墟背景)
                desiredY = ruinedZoneFixedY;
                desiredOrtho = ruinedZoneOrthoSize;
            }
        }

        if (Application.isPlaying)
        {
            currentTargetY = Mathf.Lerp(currentTargetY, desiredY, Time.deltaTime * transitionSpeed);
            currentOrthoSize = Mathf.Lerp(currentOrthoSize, desiredOrtho, Time.deltaTime * transitionSpeed);
        }
        else
        {
            currentTargetY = desiredY;
            currentOrthoSize = desiredOrtho;
        }

        pos.y = currentTargetY;
        pos.z = 0f;
        transform.position = pos;

        ApplyOrthoSize(currentOrthoSize);
    }

    private void ApplyOrthoSize(float size)
    {
        if (_vcam == null) _vcam = Object.FindFirstObjectByType<CinemachineCamera>();
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
}
