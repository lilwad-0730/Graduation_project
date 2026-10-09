using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

public class GuidanceLight : MonoBehaviour, IResettable
{
    [Header("目標設定")]
    [Tooltip("玩家物件 (程式會自動透過 Tag 尋找)")]
    public Transform player;
    [Tooltip("精靈要飛過的路徑點 (請在場景建立多個空物件，並拉進這個陣列中)")]
    public Transform[] waypoints;

    [Header("飛行屬性")]
    [Tooltip("精靈飛行的速度")]
    public float moveSpeed = 4f;
    [Tooltip("距離路徑點多近算抵達？")]
    public float waypointThreshold = 0.5f;

    [Tooltip("光絮貼到路徑點的收斂精度：距離小於此值才算真正停在點上。\n" +
             "waypointThreshold 只決定「可以開始互動了」，光絮仍會一路收斂到點上，\n" +
             "避免停在離路徑點很遠的地方讓玩家碰不到")]
    public float waypointSnapEpsilon = 0.05f;

    [Tooltip("【Waypoint_Touch】玩家要靠多近才算碰到光絮 (原本寫死 1.5，現在可調)")]
    public float touchTriggerDistance = 1.5f;

    [Header("🔍 Scene 視窗可視化")]
    [Tooltip("是否在 Scene 視窗畫出每個路徑點的觸發範圍與行為標示")]
    public bool drawWaypointGizmos = true;

    [Header("等待玩家設定 (預設追逐模式)")]
    [Tooltip("玩家距離超過多少時，精靈停下等待？")]
    public float stopDistance = 12f;
    [Tooltip("精靈停下後，玩家靠近到多少範圍內才繼續飛？")]
    public float resumeDistance = 6f;

    [Header("敘事等待模式設定 (Waypoint_WaitPlayer)")]
    [Tooltip("在此模式下，玩家要靠近到多少距離內，光絮才會飛往下一個點？(通常比追逐模式的距離更近)")]
    public float waitPlayerTriggerDistance = 3f;

    [Header("★1009 棉花堡：光球移動時，鏡頭跟著光球（1008 會議 1a）")]
    [Tooltip("開著＝沒有標籤的路徑點改成：光球停在點上等她，她走近（或走過）且站在地上 → 她不能動、鏡頭跟著光球飛到下一個點，停一下再回到她身上。\n有標籤的點照各自規則（Waypoint_TouchCloseup＝碰到＋特寫鏡頭、Waypoint_Touch＝碰到才前進）。\n關掉＝沒標籤的點回到原本「跑給她追」")]
    public bool cameraFollowsOrbOnPlainWaypoints = true;
    [Tooltip("只在這個場景用（空白＝所有場景）。水下、玻璃館的光球照舊")]
    public string cameraFollowsOrbScene = "SampleScene";
    [Tooltip("她跟光球的水平距離在這以內（或她已經走過光球），而且站在地上，就開始演出")]
    public float plainCutsceneTriggerX = 3.5f;
    [Tooltip("她跟光球的高度差在這以內，才算走近（棉花堡有幾個點懸在地面上 10～13 米：P2、P5、P10）")]
    public float plainCutsceneTriggerY = 14f;
    [Tooltip("下一個點比這個點「高」超過這個值就不飛：下層到上層由最後一階的傳送帶過去。往下的（例如最後飛到跳崖處）照飛")]
    public float plainCutsceneMaxRise = 15f;
    [Tooltip("演出時光球飛的速度（跑給她追的速度 moveSpeed 不變）")]
    public float cutsceneFlySpeed = 14f;

    [Header("敘事鎖定延遲 (增加演出感)")]
    [Tooltip("觸發後，光絮在原地停留幾秒鐘才起飛？")]
    public float flyDelay = 0.5f;
    [Tooltip("光絮抵達下一個點後，額外凍結玩家幾秒鐘才放行？")]
    public float unlockDelay = 0.5f;

    [Header("鏡頭特寫模式 (Waypoint_TouchCloseup)")]
    [Tooltip("Waypoint 設成 Waypoint_TouchCloseup 時，光絮移動期間暫時拉近 Cinemachine 鏡頭。正交鏡頭＝Orthographic Size；透視鏡頭＝Field Of View")]
    public float closeupLensSize = 10f;
    [Tooltip("鏡頭拉近/還原速度。數字越大越快")]
    public float closeupLensLerpSpeed = 6f;

    [Header("動畫效果 (呼吸浮動)")]
    [Tooltip("上下浮動的幅度")]
    public float bobHeight = 0.3f;
    [Tooltip("上下浮動的速度")]
    public float bobSpeed = 3f;

    [Header("吸收模式設定 (Waypoint_Absorb)")]
    [Tooltip("玩家要靠近到多少距離內才會觸發吸收？（建議設為 0.8 左右，代表實際碰到時才觸發）")]
    public float absorbTriggerDistance = 0.8f;
    [Tooltip("吸收過程淡出時間")]
    public float fadeOutDuration = 1.0f;
    [Tooltip("淡入還原時間")]
    public float fadeInDuration = 1.0f;
    [Header("🎵 光絮音效 (Guidance SFX)")]
    [Tooltip("光絮被吸收/合體音效 (例如 玻璃館_合體.wav / 玻璃館_解體_03.wav)")]
    public AudioClip absorbSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    // 狀態屬性，便於外部偵測
    public bool IsAbsorbing { get; private set; } = false;

    // 吸收完成的事件委派，供後續加成效果偵測
    public event System.Action OnAbsorbed;

    /// <summary>
    /// 當光球抵達並完成所有路徑點時觸發的事件 (供拉桿或其他機關解鎖監聽)
    /// </summary>
    public event System.Action OnAllWaypointsCompleted;

    private bool _hasCompletedAllWaypoints = false;

    /// <summary>
    /// 是否已經完成所有路徑點 (以光球程式內的 waypoints 陣列為準)
    /// </summary>
    public bool IsAllWaypointsCompleted
    {
        get
        {
            if (waypoints == null || waypoints.Length == 0) return true;
            if (_hasCompletedAllWaypoints) return true;
            if (currentWaypointIndex >= waypoints.Length) return true;
            if (currentWaypointIndex == waypoints.Length - 1 && waypoints[currentWaypointIndex] != null)
            {
                float d = Vector3.Distance(transform.position, waypoints[currentWaypointIndex].position);
                if (d <= waypointThreshold) return true;
            }
            return false;
        }
    }

    /// <summary>目前進行中/已抵達的路徑點索引 (0 ~ waypoints.Length)</summary>
    public int CurrentWaypointIndex => Mathf.Clamp(Mathf.Max(currentWaypointIndex, maxWaypointReached), 0, TotalWaypointsCount);

    /// <summary>全部路徑點總數</summary>
    public int TotalWaypointsCount => (waypoints != null) ? waypoints.Length : 0;

    private int currentWaypointIndex = 0;
    private int maxWaypointReached = 0;
    private bool isWaitingForPlayerCatchup = false;
    private Vector3 logicPosition; 
    private bool isLockingPlayer = false; // 是否正在鎖定玩家看動畫
    private Coroutine _cutsceneFlightCoroutine;
    private Transform _touchCooldownWaypoint;

    private SpriteRenderer[] spriteRenderers;
    private Light[] lights;
    private ParticleSystem[] particleSystems;
    private Color[] originalSpriteColors;
    private float[] originalLightIntensities;
    private Coroutine absorbCoroutine;
    private AudioSource hoverAudioSource;
    private Collider _playerCollider;
    private CinemachineCamera _cutsceneVcam;      // 演出期間被借走 Follow 的相機
    private Transform _cutsceneOriginalFollow;    // 借走前的 Follow，結束時還回去
    private Transform _cutsceneOriginalTrackingTarget;
    private CinemachineFollow _cutsceneFollow;
    private Vector3 _cutsceneOriginalFollowOffset;
    private bool _cutsceneHasSavedFollowOffset;
    private bool _cutsceneHasSavedLens;
    private float _cutsceneOriginalOrthoSize;
    private float _cutsceneOriginalFieldOfView;
    private bool _usingCameraTargetOverride;
    private PlayerMovement _flightLockedPlayer;
    private Rigidbody _flightLockedRb;
    private RigidbodyConstraints _flightSavedConstraints;
    private bool _flightSavedUseGravity;

    // ★1009 棉花堡演出：鏡頭跟光球改走 CameraTargetXFollower 的跟隨目標（保留區域固定高度與左右邊界）
    private Transform _camProxy;                       // 代替她被鏡頭跟的小點，跟著光球走、再走回她身上
    private CameraTargetXFollower _camProxyOwner;
    private Transform _camProxyOriginalTarget;

    // ── 演出期間的玩家鎖定（剛體、動畫、呼吸）──
    // ★0909：剛體約束與動畫速度的存檔／還原改由 PlayerCutsceneHold 統一管。
    //   原本這裡自己存自己還原，跟 QuestClearBarrierRock 那套一模一樣，
    //   兩段演出一重疊（收完最後一張日誌時光絮演出跟巨石消散前後腳發生）
    //   後放手的那個會把「FreezeAll」當成正常狀態還原回去，玩家從此不能動。
    private bool _holdingPlayer;
    private bool _breathHeld;

    /// <summary>演出開始：她不能動、不會沉、動畫定格、氧氣暫停——不然鏡頭跟著光絮飛，她在畫面外沉下去溺斃。</summary>
    private void BeginPlayerHold(PlayerMovement pm)
    {
        if (pm == null || _holdingPlayer) return;
        _holdingPlayer = true;
        PlayerCutsceneHold.Acquire(pm, freezeAnimator: true);

        if (UnderwaterSuffocationEffect.Instance != null && !_breathHeld)
        {
            UnderwaterSuffocationEffect.Instance.SetHold(true);
            _breathHeld = true;
        }
    }

    /// <summary>演出結束：全部還原。</summary>
    private void EndPlayerHold()
    {
        if (_holdingPlayer)
        {
            _holdingPlayer = false;
            PlayerCutsceneHold.Release();
        }
        if (_breathHeld)
        {
            if (UnderwaterSuffocationEffect.Instance != null) UnderwaterSuffocationEffect.Instance.SetHold(false);
            _breathHeld = false;
        }
    }

    /// <summary>光球帶路時只鎖操作與水平位移，Y 軸交給重力，避免玩家在空中被定住。</summary>
    private void BeginPlayerFlightLock(PlayerMovement pm)
    {
        if (pm == null || _flightLockedPlayer != null) return;

        _flightLockedPlayer = pm;
        _flightLockedPlayer.isCutsceneFrozen = true;

        _flightLockedRb = pm.GetComponent<Rigidbody>();
        if (_flightLockedRb == null) _flightLockedRb = pm.GetComponentInParent<Rigidbody>();
        if (_flightLockedRb != null)
        {
            _flightSavedConstraints = _flightLockedRb.constraints;
            _flightSavedUseGravity = _flightLockedRb.useGravity;
            Vector3 v = _flightLockedRb.linearVelocity;
            _flightLockedRb.linearVelocity = new Vector3(0f, v.y, 0f);
            _flightLockedRb.angularVelocity = Vector3.zero;
            _flightLockedRb.useGravity = true;
            _flightLockedRb.constraints = RigidbodyConstraints.FreezeRotation
                                          | RigidbodyConstraints.FreezePositionX
                                          | RigidbodyConstraints.FreezePositionZ;
        }
    }

    private void EndPlayerFlightLock()
    {
        if (_flightLockedRb != null)
        {
            _flightLockedRb.constraints = _flightSavedConstraints;
            _flightLockedRb.useGravity = _flightSavedUseGravity;
        }

        if (_flightLockedPlayer != null)
        {
            if (!PlayerCutsceneHold.IsHeld) _flightLockedPlayer.isCutsceneFrozen = false;
        }

        _flightLockedPlayer = null;
        _flightLockedRb = null;
    }

    void Start()
    {
        logicPosition = transform.position;
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        CachePlayerCollider();

        // 快取視覺元件與其原始數值以供漸變控制
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        lights = GetComponentsInChildren<Light>(true);
        particleSystems = GetComponentsInChildren<ParticleSystem>(true);

        originalSpriteColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            originalSpriteColors[i] = spriteRenderers[i].color;
        }

        originalLightIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            originalLightIntensities[i] = lights[i].intensity;
        }
    }

    void Update()
    {
        // 鏡牆演出進行中時，完全交由 MirrorWallCutsceneManager 主控發聲與動態
        if (MirrorWallAbsorbCutscene.IsAnyCutsceneRunning)
        {
            return;
        }

        if (waypoints == null || waypoints.Length == 0 || player == null) return;
        
        PlayerMovement pm = player.GetComponent<PlayerMovement>();

        // 如果正在演出「被吸收」狀態，Update 只負責上下浮動
        if (IsAbsorbing)
        {
            ApplyBobbing();
            return;
        }

        // 規則 4：如果正在演出「飛往下一個點」的劇情鎖定狀態，Update 只負責上下浮動
        if (isLockingPlayer)
        {
            ApplyBobbing();
            return;
        }

        // 如果已經抵達最後一個點，就在原地浮動
        if (currentWaypointIndex >= waypoints.Length)
        {
            if (!_hasCompletedAllWaypoints)
            {
                _hasCompletedAllWaypoints = true;
                OnAllWaypointsCompleted?.Invoke();
            }
            ApplyBobbing();
            return;
        }

        Transform currentWP = waypoints[currentWaypointIndex];
        if (currentWP == null)
        {
            currentWaypointIndex++;
            maxWaypointReached = Mathf.Max(maxWaypointReached, currentWaypointIndex);
            return;
        }
        string wpTag = currentWP.tag;
        
        float distToPlayer = Vector3.Distance(logicPosition, player.position);
        float touchDistToPlayer = GetPlayerDistanceToVisibleLight();
        float distToWaypoint = Vector3.Distance(logicPosition, currentWP.position);
        if (_touchCooldownWaypoint != null)
        {
            if (_touchCooldownWaypoint != currentWP || touchDistToPlayer > touchTriggerDistance + 0.25f)
            {
                _touchCooldownWaypoint = null;
            }
        }

        // ★ 光絮要真正停在路徑點「上」。
        //   原本三種模式都是 distToWaypoint > waypointThreshold 才飛，一旦進入門檻內就完全不動，
        //   而場景把 waypointThreshold 設成 5，等於光絮可能停在點外 5 單位就不走了；
        //   玩家跑到路徑點上，卻還離光絮 5 單位，構不到 absorbTriggerDistance (1.5) 而觸發不了。
        //   改成持續收斂到點上，waypointThreshold 只用來判斷「可以開始互動了」。
        bool arrivedAtWaypoint = distToWaypoint <= waypointThreshold;
        bool isTouchWaypoint = wpTag == "Waypoint_Touch" || wpTag == "Waypoint_TouchCloseup";
        bool useCloseup = wpTag == "Waypoint_TouchCloseup";

        if (wpTag == "Waypoint_Absorb" || isTouchWaypoint || wpTag == "Waypoint_WaitPlayer")
        {
            if (distToWaypoint > waypointSnapEpsilon)
            {
                FlyTowards(currentWP.position);
            }
        }

        // 新增規則：被玩家吸收模式 (Waypoint_Absorb)
        if (wpTag == "Waypoint_Absorb")
        {
            if (arrivedAtWaypoint && touchDistToPlayer <= absorbTriggerDistance) // 等玩家靠近觸發吸收
            {
                StartAbsorbSequence(pm);
            }
        }
        // 規則 3：玩家必須真正碰到光絮 (極短距離)，且光絮不跑
        else if (isTouchWaypoint)
        {
            if (arrivedAtWaypoint && touchDistToPlayer <= touchTriggerDistance) // 等玩家真正碰到
            {
                if (_touchCooldownWaypoint != currentWP)
                {
                    AdvanceWaypoint(pm, true, useCloseup);
                }
            }
        }
        // 規則 2：允許玩家靠近 (喘氣/敘事)，光絮停在此點不跑
        else if (wpTag == "Waypoint_WaitPlayer")
        {
            if (arrivedAtWaypoint && distToPlayer <= waitPlayerTriggerDistance) // 使用專屬的等待距離！
            {
                AdvanceWaypoint(pm, true, false);
            }
        }
        // ★1009 規則 1b：棉花堡（沒有標籤的點）＝停在點上等她，她走近才演出「光球飛、鏡頭跟、她不能動」
        else if (UsePlainCutscene())
        {
            float dx = Mathf.Abs(player.position.x - logicPosition.x);
            float dy = Mathf.Abs(player.position.y - logicPosition.y);
            bool reachedX = dx <= plainCutsceneTriggerX || player.position.x > logicPosition.x;   // 跳過光球也算（棉花堡一路往右走）
            bool grounded = pm == null || pm.isGrounded;                                         // 落地才演出，不在半空中定住
            bool near = reachedX && dy <= plainCutsceneTriggerY && grounded;
            if (distToWaypoint > 3f)
            {
                // 光球還不在這一點上（開場、重生、或最後一階把它帶到上層之後）：她走近才演出飛過去，不自己先飛走
                if (near && pm != null && _cutsceneFlightCoroutine == null)
                    _cutsceneFlightCoroutine = StartCoroutine(CutsceneFlightSequence(pm, false, cutsceneFlySpeed, false));
            }
            else
            {
                if (distToWaypoint > waypointSnapEpsilon)
                {
                    FlyTowards(currentWP.position);
                }
                if (arrivedAtWaypoint && near)
                {
                    int next = currentWaypointIndex + 1;
                    bool nextTooFar = next < waypoints.Length && waypoints[next] != null
                                      && (waypoints[next].position.y - currentWP.position.y) > plainCutsceneMaxRise;   // 只看往上
                    if (!nextTooFar)
                    {
                        AdvanceWaypoint(pm, true, false, cutsceneFlySpeed);
                    }
                    // nextTooFar：在這裡等，最後一階的傳送（TeleportTrigger）會把光球一起帶到上層
                }
            }
        }
        // 規則 1：預設模式 (跟玩家保持距離，跑給玩家追)
        else
        {
            if (!isWaitingForPlayerCatchup && distToPlayer > stopDistance)
            {
                isWaitingForPlayerCatchup = true;
            }
            else if (isWaitingForPlayerCatchup && distToPlayer <= resumeDistance)
            {
                isWaitingForPlayerCatchup = false;
            }

            if (!isWaitingForPlayerCatchup)
            {
                FlyTowards(currentWP.position);
                if (distToWaypoint < waypointThreshold)
                {
                    AdvanceWaypoint(pm, false, false); // 預設模式不鎖定玩家，讓玩家可以邊追邊跑
                }
            }
        }

        ApplyBobbing();
    }

    private void FlyTowards(Vector3 targetPos)
    {
        logicPosition = Vector3.MoveTowards(logicPosition, targetPos, moveSpeed * Time.deltaTime);
    }

    private void CachePlayerCollider()
    {
        _playerCollider = null;
        if (player == null) return;

        _playerCollider = player.GetComponent<Collider>();
        if (_playerCollider == null) _playerCollider = player.GetComponentInChildren<Collider>();
        if (_playerCollider == null) _playerCollider = player.GetComponentInParent<Collider>();
    }

    private float GetPlayerDistanceToVisibleLight()
    {
        if (player == null) return float.MaxValue;
        if (_playerCollider == null) CachePlayerCollider();

        Vector3 lightPos = transform.position;
        if (_playerCollider != null)
        {
            return Vector3.Distance(_playerCollider.ClosestPoint(lightPos), lightPos);
        }
        return Vector3.Distance(player.position, lightPos);
    }

    /// <summary>★1009 這個場景、這個點要不要用「棉花堡演出」（沒有標籤的點才算；有標籤的照原本規則）</summary>
    private bool UsePlainCutscene()
    {
        if (!cameraFollowsOrbOnPlainWaypoints) return false;
        if (string.IsNullOrEmpty(cameraFollowsOrbScene)) return true;
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == cameraFollowsOrbScene;
    }

    /// <param name="flySpeed">★1009 演出時的飛行速度（-1＝原本的 moveSpeed；有值＝棉花堡演出，鏡頭借跟隨目標跟光球）</param>
    private void AdvanceWaypoint(PlayerMovement pm, bool freezePlayer, bool useCloseup, float flySpeed = -1f)
    {
        if (_cutsceneFlightCoroutine != null) return;

        if (freezePlayer && pm != null && currentWaypointIndex + 1 < waypoints.Length)
        {
            _cutsceneFlightCoroutine = StartCoroutine(CutsceneFlightSequence(pm, useCloseup, flySpeed));
        }
        else
        {
            currentWaypointIndex++;
            maxWaypointReached = Mathf.Max(maxWaypointReached, currentWaypointIndex);
        }
    }

    /// <param name="flySpeed">★1009 有值＝棉花堡演出：用這個速度飛，鏡頭借 CameraTargetXFollower 的跟隨目標跟光球（只左右跟）</param>
    /// <param name="advance">★1009 true＝飛往下一個點（原本）；false＝飛往目前這個點（光球還沒到點上時）</param>
    private IEnumerator CutsceneFlightSequence(PlayerMovement pm, bool useCloseup, float flySpeed = -1f, bool advance = true)
    {
        Transform nextWP = null;
        bool useProxy = false;

        try
        {
            // 1. 立即鎖住玩家操作，但保留重力，讓她在空中會自然落下。
            BeginPlayerFlightLock(pm);
            isLockingPlayer = true;

            // 2. 只有 Waypoint_TouchCloseup 才借走鏡頭；Waypoint_Touch 只讓光絮前進，不做鏡頭跟隨。
            _usingCameraTargetOverride = useCloseup && Object.FindFirstObjectByType<CameraTargetXFollower>() != null;
            if (_usingCameraTargetOverride)
            {
                CameraTargetXFollower.SetCameraOverride(transform, closeupLensSize);
            }

            // ★1009 棉花堡沒標籤的點：鏡頭也跟光球，但不拉近、高度照區域固定值（借跟隨目標，不用特寫）
            if (!useCloseup && flySpeed > 0f) useProxy = BeginCamProxy();

            // 保留直接控制 Cinemachine 的 fallback：場景沒有 CameraTargetXFollower 時仍可運作。
            if (useCloseup && !_usingCameraTargetOverride)
            {
                _cutsceneVcam = null;
                _cutsceneOriginalFollow = null;
                foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
                {
                    if (v != null && v.isActiveAndEnabled)
                    {
                        _cutsceneVcam = v;
                        break;
                    }
                }
                if (_cutsceneVcam != null)
                {
                    _cutsceneOriginalFollow = _cutsceneVcam.Follow;
                    var camTarget = _cutsceneVcam.Target;
                    _cutsceneOriginalTrackingTarget = camTarget.TrackingTarget;
                    camTarget.TrackingTarget = transform;
                    _cutsceneVcam.Target = camTarget;

                    _cutsceneFollow = _cutsceneVcam.GetComponent<CinemachineFollow>();
                    if (_cutsceneFollow != null)
                    {
                        _cutsceneOriginalFollowOffset = _cutsceneFollow.FollowOffset;
                        _cutsceneHasSavedFollowOffset = true;
                        _cutsceneFollow.FollowOffset = new Vector3(_cutsceneFollow.FollowOffset.x, 0f, _cutsceneFollow.FollowOffset.z);
                    }

                    SaveCutsceneLens();
                    _cutsceneVcam.Follow = transform;
                }
            }

            // 3. 停頓一下 (讓玩家感覺到「觸發了」某件事)
            if (flyDelay > 0f)
            {
                if (useProxy) yield return WaitWithCamProxy(flyDelay);
                else yield return new WaitForSeconds(flyDelay);
            }

            // 4. 切換目標點，開始飛行（鏡頭全程跟著光絮走）
            if (advance)   // ★1009 false＝飛往目前這個點
            {
                currentWaypointIndex++;
                maxWaypointReached = Mathf.Max(maxWaypointReached, currentWaypointIndex);
            }
            if (currentWaypointIndex >= waypoints.Length) yield break;

            nextWP = waypoints[currentWaypointIndex];
            if (nextWP == null) yield break;

            float speedThisFlight = flySpeed > 0f ? Mathf.Max(moveSpeed, flySpeed) : moveSpeed;   // ★1009 棉花堡演出飛快一點
            float startDistance = Vector3.Distance(logicPosition, nextWP.position);
            float safeMoveSpeed = Mathf.Max(0.01f, speedThisFlight);
            float timeout = Mathf.Max(3f, startDistance / safeMoveSpeed + 3f);
            float timer = 0f;

            while (Vector3.Distance(logicPosition, nextWP.position) > waypointSnapEpsilon && timer < timeout)
            {
                timer += Time.deltaTime;
                logicPosition = Vector3.MoveTowards(logicPosition, nextWP.position, speedThisFlight * Time.deltaTime);
                ApplyBobbing();
                if (useProxy) StepCamProxy(logicPosition, speedThisFlight);
                if (useCloseup && !_usingCameraTargetOverride) SmoothCutsceneLens(closeupLensSize);
                yield return null; // 等待下一幀
            }

            logicPosition = nextWP.position;
            ApplyBobbing();
            if (useCloseup && !_usingCameraTargetOverride) SetCutsceneLens(closeupLensSize);

            // 剛抵達的點如果也是 Waypoint_Touch，不能因為玩家還在範圍裡就立刻連續觸發下一段。
            _touchCooldownWaypoint = nextWP;

            // 5. 光絮停下後，先讓鏡頭穩穩停在光絮上一小段
            if (unlockDelay > 0f)
            {
                float holdTimer = 0f;
                while (holdTimer < unlockDelay)
                {
                    holdTimer += Time.deltaTime;
                    if (useProxy) StepCamProxy(logicPosition, speedThisFlight);
                    if (useCloseup && !_usingCameraTargetOverride) SmoothCutsceneLens(closeupLensSize);
                    yield return null;
                }
            }

            // 6'. ★1009 棉花堡演出：小點從光球走回她身上（鏡頭平順地搖回來，不硬切），再把跟隨目標還給她
            if (useProxy)
            {
                float back = 0f;
                while (_camProxy != null && _camProxyOriginalTarget != null && back < 4f
                       && Vector3.Distance(_camProxy.position, _camProxyOriginalTarget.position) > 0.15f)
                {
                    back += Time.deltaTime;
                    _camProxy.position = Vector3.MoveTowards(_camProxy.position, _camProxyOriginalTarget.position, Mathf.Max(24f, speedThisFlight * 1.6f) * Time.deltaTime);
                    yield return null;
                }
                Transform followNow = _camProxyOwner != null ? _camProxyOwner.transform : null;
                EndCamProxy();
                float waitStart2 = Time.unscaledTime;
                while (followNow != null && Time.unscaledTime - waitStart2 < 3f)
                {
                    Camera cam = Camera.main;
                    if (cam == null) break;
                    if (Mathf.Abs(cam.transform.position.x - followNow.position.x) < 1.5f) break;
                    yield return null;
                }
            }

            // 6. 鏡頭還給玩家；等它「真的」回到玩家身上，才解鎖操作
            if (_usingCameraTargetOverride)
            {
                CameraTargetXFollower.ClearCameraOverride();
                _usingCameraTargetOverride = false;
            }
            else if (useCloseup)
            {
                yield return StartCoroutine(SmoothRestoreCutsceneLens());
            }
            else
            {
                RestoreCutsceneLens();
            }
            RestoreCutsceneCameraFollow();
            if (!useProxy && IsCameraReturningToPlayer())   // ★1009 棉花堡演出上面已經等過鏡頭回到跟隨目標（關卡邊緣鏡頭被夾住時對不上她本人）
            {
                float waitStart = Time.unscaledTime;
                while (Time.unscaledTime - waitStart < 4f)   // 最多等 4 秒，防呆不卡死
                {
                    Camera cam = Camera.main;
                    if (cam == null || player == null) break;
                    float dx = Mathf.Abs(cam.transform.position.x - player.position.x);
                    float dy = Mathf.Abs(cam.transform.position.y - player.position.y);
                    if (dx < 2.5f && dy < 7f) break;   // 垂直方向本來就有取景偏移，放寬判定
                    yield return null;
                }
            }
            yield return new WaitForSeconds(0.1f);
        }
        finally
        {
            if (_usingCameraTargetOverride)
            {
                CameraTargetXFollower.ClearCameraOverride();
                _usingCameraTargetOverride = false;
            }
            RestoreCutsceneLens();
            RestoreCutsceneCameraFollow();
            EndCamProxy();   // ★1009
            EndPlayerFlightLock();
            isLockingPlayer = false;
            _cutsceneFlightCoroutine = null;
        }
    }

    private void RestoreCutsceneCameraFollow()
    {
        if (_cutsceneVcam == null) return;

        if (_cutsceneHasSavedFollowOffset && _cutsceneFollow != null)
        {
            _cutsceneFollow.FollowOffset = _cutsceneOriginalFollowOffset;
        }

        var camTarget = _cutsceneVcam.Target;
        camTarget.TrackingTarget = _cutsceneOriginalTrackingTarget != null ? _cutsceneOriginalTrackingTarget : _cutsceneOriginalFollow;
        _cutsceneVcam.Target = camTarget;

        if (_cutsceneOriginalFollow != null) _cutsceneVcam.Follow = _cutsceneOriginalFollow;
        else if (player != null) _cutsceneVcam.Follow = player;

        _cutsceneOriginalTrackingTarget = null;
        _cutsceneFollow = null;
        _cutsceneHasSavedFollowOffset = false;
        _cutsceneVcam = null;
    }

    private void SaveCutsceneLens()
    {
        if (_cutsceneVcam == null || _cutsceneHasSavedLens) return;

        _cutsceneOriginalOrthoSize = _cutsceneVcam.Lens.OrthographicSize;
        _cutsceneOriginalFieldOfView = _cutsceneVcam.Lens.FieldOfView;
        _cutsceneHasSavedLens = true;
    }

    private void SetCutsceneLens(float targetSize)
    {
        if (_cutsceneVcam == null) return;

        var lens = _cutsceneVcam.Lens;
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic) lens.OrthographicSize = Mathf.Max(0.1f, targetSize);
        else lens.FieldOfView = Mathf.Max(1f, targetSize);
        _cutsceneVcam.Lens = lens;
    }

    private void SmoothCutsceneLens(float targetSize)
    {
        if (_cutsceneVcam == null) return;

        var lens = _cutsceneVcam.Lens;
        Camera cam = Camera.main;
        float t = Mathf.Clamp01(Time.deltaTime * Mathf.Max(0.01f, closeupLensLerpSpeed) * 3f);
        if (cam != null && cam.orthographic)
        {
            lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, Mathf.Max(0.1f, targetSize), t);
        }
        else
        {
            lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, Mathf.Max(1f, targetSize), t);
        }
        _cutsceneVcam.Lens = lens;
    }

    private IEnumerator SmoothRestoreCutsceneLens()
    {
        if (_cutsceneVcam == null || !_cutsceneHasSavedLens) yield break;

        Camera cam = Camera.main;
        float target = (cam != null && cam.orthographic) ? _cutsceneOriginalOrthoSize : _cutsceneOriginalFieldOfView;
        float timer = 0f;
        while (_cutsceneVcam != null && timer < 1f)
        {
            timer += Time.deltaTime * Mathf.Max(0.01f, closeupLensLerpSpeed);
            SmoothCutsceneLens(target);
            yield return null;
        }

        RestoreCutsceneLens();
    }

    private void RestoreCutsceneLens()
    {
        if (_cutsceneVcam == null || !_cutsceneHasSavedLens) return;

        var lens = _cutsceneVcam.Lens;
        lens.OrthographicSize = _cutsceneOriginalOrthoSize;
        lens.FieldOfView = _cutsceneOriginalFieldOfView;
        _cutsceneVcam.Lens = lens;
        _cutsceneHasSavedLens = false;
    }

    private bool IsCameraReturningToPlayer()
    {
        if (_cutsceneOriginalFollow == null || player == null) return true;
        return _cutsceneOriginalFollow == player || _cutsceneOriginalFollow.IsChildOf(player);
    }

    private void OnDisable()
    {
        // 保險：演出中途被停用／換場景時，把玩家鎖定與鏡頭 Follow 都還回去
        if (_usingCameraTargetOverride)
        {
            CameraTargetXFollower.ClearCameraOverride();
            _usingCameraTargetOverride = false;
        }
        EndPlayerFlightLock();
        EndPlayerHold();
        EndCamProxy();   // ★1009
        if (_cutsceneVcam != null)
        {
            RestoreCutsceneLens();
            if (_cutsceneOriginalFollow != null) _cutsceneVcam.Follow = _cutsceneOriginalFollow;
            else if (player != null) _cutsceneVcam.Follow = player;
            _cutsceneVcam = null;
        }
    }

    // ── ★1009 棉花堡演出的鏡頭小點 ──────────────────────────
    /// <summary>把 CameraTargetXFollower 的跟隨目標暫時換成一個小點（從她身上出發），鏡頭就會沿用原本的高度與邊界去跟光球。</summary>
    private bool BeginCamProxy()
    {
        CameraTargetXFollower ctx = CameraTargetXFollower.Instance != null ? CameraTargetXFollower.Instance : FindAnyObjectByType<CameraTargetXFollower>();
        if (ctx == null || !ctx.isActiveAndEnabled || player == null) return false;
        if (_camProxy == null) _camProxy = new GameObject("[光球演出鏡頭小點]").transform;
        _camProxyOwner = ctx;
        _camProxyOriginalTarget = ctx.targetToFollow != null && ctx.targetToFollow != _camProxy ? ctx.targetToFollow : player;
        _camProxy.position = _camProxyOriginalTarget.position;
        ctx.targetToFollow = _camProxy;
        return true;
    }

    /// <summary>小點往光球走（一幀最多走幾十公分，鏡頭那邊不會誤判成瞬移而硬切）。</summary>
    private void StepCamProxy(Vector3 target, float orbSpeed)
    {
        if (_camProxy == null) return;
        _camProxy.position = Vector3.MoveTowards(_camProxy.position, target, Mathf.Max(24f, orbSpeed * 1.6f) * Time.deltaTime);
    }

    private IEnumerator WaitWithCamProxy(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            StepCamProxy(logicPosition, moveSpeed);
            yield return null;
        }
    }

    private void EndCamProxy()
    {
        if (_camProxyOwner != null && _camProxyOwner.targetToFollow == _camProxy)
            _camProxyOwner.targetToFollow = _camProxyOriginalTarget != null ? _camProxyOriginalTarget : player;
        _camProxyOwner = null;
        _camProxyOriginalTarget = null;
    }

    private void StartAbsorbSequence(PlayerMovement pm)
    {
        if (absorbCoroutine != null) StopCoroutine(absorbCoroutine);
        absorbCoroutine = StartCoroutine(AbsorbSequence(pm));
    }

    private IEnumerator AbsorbSequence(PlayerMovement pm)
    {
        IsAbsorbing = true;
        BeginPlayerHold(pm);   // 吸收期間同樣不沉、不扣氧氣

        // 播放光球吸收/合體音效
        if (absorbSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(absorbSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(absorbSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 1. 停止粒子發射
        foreach (var ps in particleSystems)
        {
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // 2. 漸漸淡出 (降低透明度與光源強度)
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeOutDuration);
            float alpha = Mathf.Lerp(1f, 0f, t);

            SetVisualAlpha(alpha);
            yield return null;
        }

        // 確保完全透明/關閉
        SetVisualAlpha(0f);

        // 3. 觸發吸收完成事件 (供外部/主角加成偵測使用)
        OnAbsorbed?.Invoke();
        Debug.Log("【光絮】已被玩家吸收！觸發 OnAbsorbed 事件。");

        // 光球＝氧氣（0805 企劃定案）：吸收光絮＝補一大口氣，呼吸光圈擴回來
        if (UnderwaterSuffocationEffect.Instance != null)
        {
            UnderwaterSuffocationEffect.Instance.RestoreBreath(0.45f);
        }
        UnderwaterCheckpoint.MarkHere(this, "吸收光絮");   // 只在水下作用

        // 4. 切換到下一個路徑點
        if (currentWaypointIndex + 1 < waypoints.Length)
        {
            currentWaypointIndex++;
            Transform nextWP = waypoints[currentWaypointIndex];

            // 瞬間跳到下一個點的邏輯位置與世界位置
            logicPosition = nextWP.position;
            transform.position = logicPosition;
            Debug.Log($"【光絮】瞬間傳送至下一個路徑點：{nextWP.name}");

            // 5. 播放粒子並漸漸淡入
            foreach (var ps in particleSystems)
            {
                if (ps != null)
                {
                    ps.Clear();
                    ps.Play();
                }
            }

            elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeInDuration);
                float alpha = Mathf.Lerp(0f, 1f, t);

                SetVisualAlpha(alpha);
                yield return null;
            }

            // 恢復原始狀態
            RestoreVisuals();
        }
        else
        {
            // 如果已經是最後一個點，我們在原地淡入回來以維持指引
            elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeInDuration);
                float alpha = Mathf.Lerp(0f, 1f, t);

                SetVisualAlpha(alpha);
                yield return null;
            }
            RestoreVisuals();
        }

        // 6. 解凍玩家與結束狀態
        EndPlayerHold();
        IsAbsorbing = false;
    }

    private void SetVisualAlpha(float alpha)
    {
        if (hoverAudioSource != null)
        {
            hoverAudioSource.volume = AudioManager.ScaleSfx(sfxVolume * alpha);
        }

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                Color c = originalSpriteColors[i];
                c.a = originalSpriteColors[i].a * alpha;
                spriteRenderers[i].color = c;
            }
        }
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null)
            {
                lights[i].intensity = originalLightIntensities[i] * alpha;
            }
        }
    }

    private void RestoreVisuals()
    {
        if (hoverAudioSource != null)
        {
            hoverAudioSource.volume = AudioManager.ScaleSfx(sfxVolume);
            if (!hoverAudioSource.isPlaying) hoverAudioSource.Play();
        }

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].color = originalSpriteColors[i];
            }
        }
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null)
            {
                lights[i].intensity = originalLightIntensities[i];
            }
        }
        foreach (var ps in particleSystems)
        {
            if (ps != null && !ps.isPlaying) ps.Play();
        }
    }

    /// <summary>
    /// Scene 視窗可視化：畫出每個路徑點的觸發範圍與它的行為模式。
    /// 綠＝Waypoint_WaitPlayer(靠近就前進)、青＝Waypoint_Absorb(靠近吸收＋補氧)、
    /// 黃＝Waypoint_Touch(要碰到才前進)、灰＝未標記(跑給玩家追)。
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!drawWaypointGizmos || waypoints == null) return;

        for (int i = 0; i < waypoints.Length; i++)
        {
            Transform wp = waypoints[i];
            if (wp == null) continue;

            string tag = "";
            try { tag = wp.tag; } catch { tag = ""; }

            Color color;
            float radius;
            string behaviour;

            switch (tag)
            {
                case "Waypoint_Absorb":
                    color = new Color(0.2f, 0.9f, 1f, 0.9f);
                    radius = absorbTriggerDistance;
                    behaviour = "吸收＋補氧";
                    break;
                case "Waypoint_Touch":
                    color = new Color(1f, 0.85f, 0.2f, 0.9f);
                    radius = touchTriggerDistance;
                    behaviour = "碰到才前進";
                    break;
                case "Waypoint_TouchCloseup":
                    color = new Color(1f, 0.45f, 0.1f, 0.95f);
                    radius = touchTriggerDistance;
                    behaviour = "碰到＋鏡頭特寫";
                    break;
                case "Waypoint_WaitPlayer":
                    color = new Color(0.3f, 1f, 0.4f, 0.9f);
                    radius = waitPlayerTriggerDistance;
                    behaviour = "靠近就前進";
                    break;
                default:
                    color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
                    radius = stopDistance;
                    behaviour = "跑給玩家追";
                    break;
            }

            // 觸發範圍
            Gizmos.color = color;
            Gizmos.DrawWireSphere(wp.position, radius);

            // 光絮「算抵達」的範圍 (waypointThreshold)：只決定可否開始互動
            Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
            Gizmos.DrawWireSphere(wp.position, waypointThreshold);

            // 路徑連線
            if (i + 1 < waypoints.Length && waypoints[i + 1] != null)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                Gizmos.DrawLine(wp.position, waypoints[i + 1].position);
            }

#if UNITY_EDITOR
            string tagLabel = string.IsNullOrEmpty(tag) || tag == "Untagged" ? "(未標記)" : tag;
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(wp.position + Vector3.up * (radius + 0.4f),
                $"WP{i} {tagLabel}\n{behaviour}：{radius:F1}m");
#endif
        }

        // 光絮自己目前的位置
        Gizmos.color = new Color(1f, 1f, 0.6f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }

    // ---- 廢墟光球範圍限制 ----
    // 光球飛行途中不能飛出廢墟背景，否則被夾在背景裡的鏡頭拍不到它。
    // 只認 tag = RuinedBackground 的面板（雲、視差層不算），每幀只讀 bounds。
    private static readonly List<SpriteRenderer> _ruinPanels = new List<SpriteRenderer>();
    private const float RuinBoundsMargin = 1.5f;

    private void RefreshRuinPanels()
    {
        _ruinPanels.Clear();
        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr != null && sr.enabled && sr.gameObject.CompareTag("RuinedBackground") && sr.bounds.size.x > 0.5f)
                _ruinPanels.Add(sr);
        }
    }

    /// <summary>把位置夾進廢墟背景範圍。不在廢墟高度、或沒有廢墟面板時原樣回傳。</summary>
    private Vector3 ClampToRuinBackground(Vector3 p)
    {
        if (_ruinPanels.Count == 0 || _ruinPanels[0] == null) RefreshRuinPanels();
        if (_ruinPanels.Count == 0) return p;

        float allMinX = float.MaxValue, allMaxX = float.MinValue, allMinY = float.MaxValue, allMaxY = float.MinValue;
        foreach (var sr in _ruinPanels)
        {
            if (sr == null) continue;
            Bounds b = sr.bounds;
            allMinX = Mathf.Min(allMinX, b.min.x); allMaxX = Mathf.Max(allMaxX, b.max.x);
            allMinY = Mathf.Min(allMinY, b.min.y); allMaxY = Mathf.Max(allMaxY, b.max.y);
        }
        if (allMinX >= allMaxX) return p;

        // 光球不在廢墟那一帶（天空層等）就完全不碰
        if (p.y < allMinY - 8f || p.y > allMaxY + 8f) return p;

        float x = Mathf.Clamp(p.x, allMinX + RuinBoundsMargin, allMaxX - RuinBoundsMargin);

        // Y：取蓋到這個 X 的面板交集（交界處以矮的為準）
        float minY = float.MinValue, maxY = float.MaxValue;
        bool found = false;
        foreach (var sr in _ruinPanels)
        {
            if (sr == null) continue;
            Bounds b = sr.bounds;
            if (x < b.min.x || x > b.max.x) continue;
            minY = Mathf.Max(minY, b.min.y); maxY = Mathf.Min(maxY, b.max.y);
            found = true;
        }
        float y = p.y;
        if (found && minY + RuinBoundsMargin < maxY - RuinBoundsMargin)
            y = Mathf.Clamp(p.y, minY + RuinBoundsMargin, maxY - RuinBoundsMargin);

        return new Vector3(x, y, p.z);
    }

    private void ApplyBobbing()
    {
        // 目標路徑點本身如果在範圍外（企劃刻意放的），就不限制，免得光球永遠到不了
        if (waypoints != null && currentWaypointIndex >= 0 && currentWaypointIndex < waypoints.Length && waypoints[currentWaypointIndex] != null)
        {
            Vector3 wpPos = waypoints[currentWaypointIndex].position;
            Vector3 clampedWp = ClampToRuinBackground(wpPos);
            if ((clampedWp - wpPos).sqrMagnitude < 0.0001f) logicPosition = ClampToRuinBackground(logicPosition);
        }
        else
        {
            logicPosition = ClampToRuinBackground(logicPosition);
        }

        float newY = logicPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(logicPosition.x, newY, logicPosition.z);
    }

    /// <summary>
    /// 供傳送點 (TeleportTrigger) 呼叫：瞬間將光絮傳送到新區域並更新下一個目標路徑點
    /// </summary>
    /// <param name="targetPosition">光絮要傳送到的 3D 位置</param>
    /// <param name="newWaypointIndex">下一個路徑點的索引值，-1 代表自動尋找最近點</param>
    /// <summary>
    /// IResettable：玩家死亡重生時把光絮拉回身邊。
    /// 原本沒有實作這個介面 (但 PlayerRespawnSystem 的註解卻寫著會「重置光球」)，
    /// 玩家重生回上一個存檔點後，光絮仍留在前方很遠的路徑點上，
    /// 距離超過 stopDistance 就會停在遠處不動，玩家身邊完全沒有指引與照明。
    /// 這裡把光絮傳送到「離玩家最近的路徑點」，並從那一點繼續帶路。
    /// </summary>
    public void ResetToInitialState()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        // ★ 要用「重生點」而不是玩家當下的位置：
        //   PlayerRespawnSystem 是先跑完所有 IResettable，之後才把玩家傳送到存檔點，
        //   此刻讀 player.position 拿到的還是死亡當下的位置。
        Vector3 referencePos;
        Vector3 respawnPos = PlayerRespawnSystem.ActiveRespawnPosition;
        if (respawnPos != Vector3.zero)
        {
            referencePos = respawnPos;
        }
        else
        {
            Transform target = player;
            if (target == null)
            {
                PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
                if (pm != null) target = pm.transform;
            }
            if (target == null) return;
            referencePos = target.position;
        }

        // 找離重生點最近的路徑點
        int nearestIndex = 0;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            float d = Vector3.Distance(waypoints[i].position, referencePos);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearestIndex = i;
            }
        }

        if (waypoints[nearestIndex] == null) return;
        TeleportLight(waypoints[nearestIndex].position, nearestIndex);
        Debug.Log($"💡【光絮重置】玩家重生，光絮已回到最近的路徑點 {nearestIndex} ({waypoints[nearestIndex].name})");
    }

    public void TeleportLight(Vector3 targetPosition, int newWaypointIndex)
    {
        // 傳送時，如果正在進行吸收協程則將其停止並恢復顯示
        if (absorbCoroutine != null)
        {
            StopCoroutine(absorbCoroutine);
            IsAbsorbing = false;
            RestoreVisuals();
            EndPlayerHold();   // 吸收被中斷也要把玩家還原，別讓她卡在定格
        }
        if (_cutsceneFlightCoroutine != null)
        {
            StopCoroutine(_cutsceneFlightCoroutine);
            _cutsceneFlightCoroutine = null;
            if (_usingCameraTargetOverride)
            {
                CameraTargetXFollower.ClearCameraOverride();
                _usingCameraTargetOverride = false;
            }
            RestoreCutsceneLens();
            RestoreCutsceneCameraFollow();
            EndCamProxy();   // ★1009
            EndPlayerFlightLock();
        }

        logicPosition = targetPosition;
        transform.position = targetPosition;
        CachePlayerCollider();
        isWaitingForPlayerCatchup = false;
        isLockingPlayer = false;
        _touchCooldownWaypoint = null;

        if (waypoints == null || waypoints.Length == 0) return;

        if (newWaypointIndex >= 0 && newWaypointIndex < waypoints.Length)
        {
            currentWaypointIndex = newWaypointIndex;
            Debug.Log($"【光絮】已同步傳送至 {targetPosition}，下一個目標點索引設定為：{newWaypointIndex}");
        }
        else
        {
            // 自動搜尋距離傳送目的地最近的路徑點
            float minDistance = float.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                float dist = Vector3.Distance(waypoints[i].position, targetPosition);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestIndex = i;
                }
            }
            currentWaypointIndex = bestIndex;
            Debug.Log($"【光絮】已同步傳送至 {targetPosition}，自動匹配最近的目標點索引：{bestIndex}");
        }
    }

    /// <summary>
    /// 把光球直接傳到指定名稱的路徑點（跨背景用，不慢慢飛）。
    /// 先在 waypoints 陣列找；陣列裡沒有就退而求其次用場景物件名稱找，位置一定會到，索引再取最近的路徑點。
    /// </summary>
    public bool TeleportToWaypointName(string waypointName)
    {
        if (string.IsNullOrEmpty(waypointName)) return false;

        if (waypoints != null)
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform wp = waypoints[i];
                if (wp == null || wp.name != waypointName) continue;

                gameObject.SetActive(true);
                enabled = true;
                TeleportLight(wp.position, i);
                Debug.Log($"【光絮】跨背景同步傳送到 {waypointName}，路徑索引={i}。");
                return true;
            }
        }

        GameObject found = GameObject.Find(waypointName);
        if (found == null) return false;

        gameObject.SetActive(true);
        enabled = true;
        TeleportLight(found.transform.position, -1);
        Debug.LogWarning($"【光絮】waypoints 陣列裡沒有 {waypointName}，改用場景物件位置傳送；請把它補進 GuidanceLight.waypoints。");
        return true;
    }

    /// <summary>光球目前是否在指定名稱的路徑點附近（容許上下浮動）。</summary>
    public bool IsNearWaypointName(string waypointName, float tolerance = 2.5f)
    {
        if (!isActiveAndEnabled) return false;

        Transform target = null;
        if (waypoints != null)
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] != null && waypoints[i].name == waypointName) { target = waypoints[i]; break; }
            }
        }
        if (target == null)
        {
            GameObject found = GameObject.Find(waypointName);
            if (found != null) target = found.transform;
        }
        if (target == null) return true;   // 找不到目標就沒得比對，別一直補傳

        return Vector3.Distance(transform.position, target.position) <= tolerance;
    }
}
