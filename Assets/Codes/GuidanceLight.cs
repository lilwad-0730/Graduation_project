using UnityEngine;
using System.Collections;
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

    [Header("等待玩家設定 (預設追逐模式)")]
    [Tooltip("玩家距離超過多少時，精靈停下等待？")]
    public float stopDistance = 12f;
    [Tooltip("精靈停下後，玩家靠近到多少範圍內才繼續飛？")]
    public float resumeDistance = 6f;

    [Header("敘事等待模式設定 (Waypoint_WaitPlayer)")]
    [Tooltip("在此模式下，玩家要靠近到多少距離內，光絮才會飛往下一個點？(通常比追逐模式的距離更近)")]
    public float waitPlayerTriggerDistance = 3f;

    [Header("敘事鎖定延遲 (增加演出感)")]
    [Tooltip("觸發後，光絮在原地停留幾秒鐘才起飛？")]
    public float flyDelay = 0.5f;
    [Tooltip("光絮抵達下一個點後，額外凍結玩家幾秒鐘才放行？")]
    public float unlockDelay = 0.5f;

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

    private int currentWaypointIndex = 0;
    private bool isWaitingForPlayerCatchup = false;
    private Vector3 logicPosition; 
    private bool isLockingPlayer = false; // 是否正在鎖定玩家看動畫

    private SpriteRenderer[] spriteRenderers;
    private Light[] lights;
    private ParticleSystem[] particleSystems;
    private Color[] originalSpriteColors;
    private float[] originalLightIntensities;
    private Coroutine absorbCoroutine;
    private AudioSource hoverAudioSource;
    private CinemachineCamera _cutsceneVcam;      // 演出期間被借走 Follow 的相機
    private Transform _cutsceneOriginalFollow;    // 借走前的 Follow，結束時還回去

    // ── 演出期間的玩家鎖定（剛體、動畫、呼吸）──
    private PlayerMovement _heldPm;
    private Rigidbody _heldRb;
    private RigidbodyConstraints _heldConstraints;
    private bool _heldUseGravity;
    private Animator _heldAnim;
    private float _heldAnimSpeed = 1f;
    private bool _breathHeld;

    /// <summary>演出開始：她不能動、不會沉、動畫定格、氧氣暫停——不然鏡頭跟著光絮飛，她在畫面外沉下去溺斃。</summary>
    private void BeginPlayerHold(PlayerMovement pm)
    {
        if (pm == null || _heldPm != null) return;
        _heldPm = pm;
        pm.isCutsceneFrozen = true;

        _heldRb = pm.GetComponent<Rigidbody>();
        if (_heldRb == null) _heldRb = pm.GetComponentInParent<Rigidbody>();
        if (_heldRb != null)
        {
            _heldConstraints = _heldRb.constraints;
            _heldUseGravity = _heldRb.useGravity;
            _heldRb.linearVelocity = Vector3.zero;
            _heldRb.angularVelocity = Vector3.zero;
            _heldRb.useGravity = false;
            _heldRb.constraints = RigidbodyConstraints.FreezeAll;   // 用約束而不是 kinematic：別的腳本寫速度也不會噴警告
        }

        _heldAnim = pm.animator;
        if (_heldAnim == null) _heldAnim = pm.GetComponentInChildren<Animator>();
        if (_heldAnim != null)
        {
            _heldAnimSpeed = _heldAnim.speed;
            _heldAnim.speed = 0f;
        }

        if (UnderwaterSuffocationEffect.Instance != null && !_breathHeld)
        {
            UnderwaterSuffocationEffect.Instance.SetHold(true);
            _breathHeld = true;
        }
    }

    /// <summary>演出結束：全部還原。</summary>
    private void EndPlayerHold()
    {
        if (_heldRb != null)
        {
            _heldRb.constraints = _heldConstraints;
            _heldRb.useGravity = _heldUseGravity;
            _heldRb = null;
        }
        if (_heldAnim != null)
        {
            _heldAnim.speed = _heldAnimSpeed;
            _heldAnim = null;
        }
        if (_breathHeld)
        {
            if (UnderwaterSuffocationEffect.Instance != null) UnderwaterSuffocationEffect.Instance.SetHold(false);
            _breathHeld = false;
        }
        if (_heldPm != null)
        {
            _heldPm.isCutsceneFrozen = false;
            _heldPm = null;
        }
    }

    void Start()
    {
        logicPosition = transform.position;
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

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
            ApplyBobbing();
            return;
        }

        Transform currentWP = waypoints[currentWaypointIndex];
        string wpTag = currentWP.tag;
        
        float distToPlayer = Vector3.Distance(logicPosition, player.position);
        float distToWaypoint = Vector3.Distance(logicPosition, currentWP.position);

        // 新增規則：被玩家吸收模式 (Waypoint_Absorb)
        if (wpTag == "Waypoint_Absorb")
        {
            if (distToWaypoint > waypointThreshold)
            {
                FlyTowards(currentWP.position); // 先飛到這個點
            }
            else if (distToPlayer <= absorbTriggerDistance) // 等玩家靠近觸發吸收
            {
                StartAbsorbSequence(pm);
            }
        }
        // 規則 3：玩家必須真正碰到光絮 (極短距離)，且光絮不跑
        else if (wpTag == "Waypoint_Touch")
        {
            if (distToWaypoint > waypointThreshold)
            {
                FlyTowards(currentWP.position); // 先飛到這個點
            }
            else if (distToPlayer <= 1.5f) // 等玩家真正碰到
            {
                AdvanceWaypoint(pm, true);
            }
        }
        // 規則 2：允許玩家靠近 (喘氣/敘事)，光絮停在此點不跑
        else if (wpTag == "Waypoint_WaitPlayer")
        {
            if (distToWaypoint > waypointThreshold)
            {
                FlyTowards(currentWP.position); // 先飛到這個點
            }
            else if (distToPlayer <= waitPlayerTriggerDistance) // 使用專屬的等待距離！
            {
                AdvanceWaypoint(pm, true);
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
                    AdvanceWaypoint(pm, false); // 預設模式不鎖定玩家，讓玩家可以邊追邊跑
                }
            }
        }

        ApplyBobbing();
    }

    private void FlyTowards(Vector3 targetPos)
    {
        logicPosition = Vector3.MoveTowards(logicPosition, targetPos, moveSpeed * Time.deltaTime);
    }

    private void AdvanceWaypoint(PlayerMovement pm, bool freezePlayer)
    {
        if (freezePlayer && pm != null && currentWaypointIndex + 1 < waypoints.Length)
        {
            StartCoroutine(CutsceneFlightSequence(pm));
        }
        else
        {
            currentWaypointIndex++;
        }
    }

    private IEnumerator CutsceneFlightSequence(PlayerMovement pm)
    {
        // 1. 立即停止玩家行動（一路鎖到鏡頭回到玩家身上為止）：剛體、動畫、氧氣一起鎖
        BeginPlayerHold(pm);
        isLockingPlayer = true;

        // 2. 鏡頭交給光絮：把場上啟用中的 Cinemachine 相機 Follow 暫時換成光絮。
        //    阻尼維持原設定，鏡頭會平滑地「沿著光絮的飛行路徑」移動，不硬切、不卡頓。
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
            _cutsceneVcam.Follow = transform;
        }

        // 3. 停頓一下 (讓玩家感覺到「觸發了」某件事)
        yield return new WaitForSeconds(flyDelay);

        // 4. 切換目標點，開始飛行（鏡頭全程跟著光絮走）
        currentWaypointIndex++;
        Transform nextWP = waypoints[currentWaypointIndex];

        while (Vector3.Distance(logicPosition, nextWP.position) > waypointThreshold)
        {
            FlyTowards(nextWP.position);
            yield return null; // 等待下一幀
        }

        // 5. 光絮停下後，先讓鏡頭穩穩停在光絮上一小段
        yield return new WaitForSeconds(unlockDelay);

        // 6. 鏡頭還給玩家；等它「真的」回到玩家身上，才解鎖操作
        if (_cutsceneVcam != null)
        {
            if (_cutsceneOriginalFollow != null) _cutsceneVcam.Follow = _cutsceneOriginalFollow;
            else if (player != null) _cutsceneVcam.Follow = player;
            _cutsceneVcam = null;

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
            yield return new WaitForSeconds(0.1f);
        }

        // 7. 解凍玩家（剛體、動畫、氧氣一起還原）
        EndPlayerHold();
        isLockingPlayer = false;
    }

    private void OnDisable()
    {
        // 保險：演出中途被停用／換場景時，把玩家鎖定與鏡頭 Follow 都還回去
        EndPlayerHold();
        if (_cutsceneVcam != null)
        {
            if (_cutsceneOriginalFollow != null) _cutsceneVcam.Follow = _cutsceneOriginalFollow;
            else if (player != null) _cutsceneVcam.Follow = player;
            _cutsceneVcam = null;
        }
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

    private void ApplyBobbing()
    {
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

        logicPosition = targetPosition;
        transform.position = targetPosition;
        isWaitingForPlayerCatchup = false;
        isLockingPlayer = false;

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
}
