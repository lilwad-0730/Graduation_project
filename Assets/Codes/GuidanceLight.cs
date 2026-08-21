using UnityEngine;
using System.Collections;

public class GuidanceLight : MonoBehaviour
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
    [Tooltip("光絮懸停等待音效 (例如 玻璃館_光球懸停.wav)")]
    public AudioClip hoverSFX;
    [Tooltip("光絮起飛前往下一個路徑點音效 (例如 玻璃館_光離開_01.wav)")]
    public AudioClip flyAwaySFX;
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

    // 視覺元件快取與原始參數
    private SpriteRenderer[] spriteRenderers;
    private Light[] lights;
    private ParticleSystem[] particleSystems;
    private Color[] originalSpriteColors;
    private float[] originalLightIntensities;
    private Coroutine absorbCoroutine;

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
        // 1. 立即停止玩家行動
        pm.isCutsceneFrozen = true;
        isLockingPlayer = true; 

        // 2. 停頓一下 (讓玩家感覺到「觸發了」某件事)
        yield return new WaitForSeconds(flyDelay);

        // 播放光球起飛音效
        if (flyAwaySFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(flyAwaySFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(flyAwaySFX, transform.position, sfxVolume);
        }

        // 3. 切換目標點
        currentWaypointIndex++;
        Transform nextWP = waypoints[currentWaypointIndex];

        // 4. 開始飛行
        while (Vector3.Distance(logicPosition, nextWP.position) > waypointThreshold)
        {
            FlyTowards(nextWP.position);
            yield return null; // 等待下一幀
        }

        // 5. 到下一個路徑點停頓一下 (讓玩家視角跟上、喘口氣)
        yield return new WaitForSeconds(unlockDelay);

        // 6. 解凍玩家
        pm.isCutsceneFrozen = false;
        isLockingPlayer = false;
    }

    private void StartAbsorbSequence(PlayerMovement pm)
    {
        if (absorbCoroutine != null) StopCoroutine(absorbCoroutine);
        absorbCoroutine = StartCoroutine(AbsorbSequence(pm));
    }

    private IEnumerator AbsorbSequence(PlayerMovement pm)
    {
        IsAbsorbing = true;
        if (pm != null) pm.isCutsceneFrozen = true;

        // 播放光球吸收/合體音效
        if (absorbSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(absorbSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(absorbSFX, transform.position, sfxVolume);
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
        if (pm != null) pm.isCutsceneFrozen = false;
        IsAbsorbing = false;
    }

    private void SetVisualAlpha(float alpha)
    {
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
    public void TeleportLight(Vector3 targetPosition, int newWaypointIndex)
    {
        // 傳送時，如果正在進行吸收協程則將其停止並恢復顯示
        if (absorbCoroutine != null)
        {
            StopCoroutine(absorbCoroutine);
            IsAbsorbing = false;
            RestoreVisuals();
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
