using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 轉場階梯傳送觸發組件 (Teleport Trigger)
/// 1. 【夢幻白色轉場】：玩家踩到最後階梯時，畫面快速泛白 (0.12s) ➔ 瞬移至上層城堡 ➔ 白色散去 (0.20s)，絕無掉落感！
/// 2. 【堅固物理碰撞】：保持階梯實體地面碰撞 (isTrigger=false)，杜絕玩家因碰撞器被關閉而下墜穿模。
/// 3. 【即時定身鎖定】：觸發瞬間立即凍結玩家輸入 (isCutsceneFrozen=true) 並將速度歸零，維持站立姿態。
/// 4. 【光絮與存檔聯動】：光絮同步瞬移，安全存檔點無縫推進至 Castle Destination。
/// 5. 【防跳過/防逃課規則】：若玩家跳得太高太遠未踩中階梯，水平越過紅線自動安全重生。
/// </summary>
public class TeleportTrigger : MonoBehaviour
{
    [Header("🎯 傳送目的地設定")]
    [Tooltip("傳送的目的地 Transform (Castle Destination)")]
    public Transform destination;

    [Tooltip("傳送時是否將玩家速度歸零，避免帶著原本跑跳的慣性衝出平台")]
    public bool resetVelocity = true;

    [Tooltip("是否同時將玩家的「重生安全點」更新到目的地？(確保在上層城堡失足時在上層重生)")]
    public bool updateRespawnPoint = true;

    [Header("🌫️ 夢幻白色轉場設定")]
    [Tooltip("轉場過渡顏色 (天空場景預設純白，營造夢幻明亮氛圍)")]
    public Color transitionColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("畫面泛白淡入時間 (秒，預設 0.12s 極速泛白)")]
    [Range(0.05f, 0.5f)] public float whiteFadeInDuration = 0.12f;

    [Tooltip("完全全白停頓時間 (秒，在此全白期間執行 WarpTo 瞬移與相機對齊)")]
    [Range(0.02f, 0.3f)] public float whiteHoldDuration = 0.08f;

    [Tooltip("白色散去恢復視野時間 (秒，預設 0.20s)")]
    [Range(0.05f, 0.5f)] public float whiteFadeOutDuration = 0.20f;

    [Header("🎵 轉場音效 (可選)")]
    [Tooltip("踩上階梯泛白瞬間播放的夢幻音效或微風聲")]
    public AudioClip teleportSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    [Header("🛡️ 防跳過 / 防越界重生判定 (Fail-Safe)")]
    [Tooltip("是否啟用防跳過規則：若玩家 X 軸超過階梯位置且未踩到階梯觸發傳送，自動判定為墜落虛空並重生")]
    public bool enableBypassDeathCheck = true;

    [Tooltip("防跳過警戒線相對於「階梯最右側邊界」的外推距離 (米，預設 0.5f)")]
    public float bypassOffsetX = 0.5f;

    [Tooltip("防跳過警戒線【向下延伸長度】(米，預設 8m，可直接在 Inspector 自由增減)")]
    public float checkYDown = 8f;

    [Tooltip("防跳過警戒線【向上延伸高度】(米，預設 30m，可直接在 Inspector 自由增減)")]
    public float checkYUp = 30f;

    [Header("✨ 光絮 (GuidanceLight) 聯動設定")]
    [Tooltip("要聯動的光絮 (留空的話，程式會自動在場景中尋找)")]
    public GuidanceLight guidanceLight;

    [Tooltip("傳送後，光絮要切換到哪一個路徑點 (Waypoint) 的索引？設為 -1 代表自動搜尋最靠近傳送點的 Waypoint")]
    public int targetWaypointIndex = -1;

    private bool _isTeleporting = false;
    private bool _teleportTriggered = false;
    private PlayerMovement _cachedPlayer;

    private static Canvas _whiteFadeCanvas;
    private static Image _whiteFadeImage;

    private void Awake()
    {
        EnsureColliderSetup();
    }

    private void Start()
    {
        EnsureColliderSetup();
        FindPlayer();
    }

    /// <summary>
    /// 確保階梯擁有堅固的地面碰撞（isTrigger = false），同時具備 30m Z 軸深度的 Trigger 觸發區
    /// </summary>
    private void EnsureColliderSetup()
    {
        // 1. 保留原本階梯的實體碰撞，玩家踩上去有堅硬的地面支撐，絕不下掉！
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = false;
        }

        Collider2D col2d = GetComponent<Collider2D>();
        if (col2d != null)
        {
            col2d.isTrigger = false;
        }

        // 2. 建立或配置專屬 Trigger 子物件，擁有 30m Z 軸深度保證必定捕捉主角
        Transform existingZone = transform.Find("Teleport_TriggerZone");
        GameObject zoneGo;
        if (existingZone != null)
        {
            zoneGo = existingZone.gameObject;
        }
        else
        {
            zoneGo = new GameObject("Teleport_TriggerZone");
            zoneGo.transform.SetParent(transform, false);
            zoneGo.transform.localPosition = Vector3.zero;
        }

        BoxCollider zoneBox = zoneGo.GetComponent<BoxCollider>();
        if (zoneBox == null) zoneBox = zoneGo.AddComponent<BoxCollider>();
        zoneBox.isTrigger = true;

        if (col is BoxCollider mainBox)
        {
            float lossyZ = transform.lossyScale.z != 0f ? Mathf.Abs(transform.lossyScale.z) : 1f;
            Vector3 size = mainBox.size;
            size.z = Mathf.Max(size.z, 30f / lossyZ);
            size.y = Mathf.Max(size.y + 0.5f, 1.5f); // 向上略微延伸，踏上瞬間零延遲觸發
            zoneBox.size = size;
            zoneBox.center = mainBox.center + Vector3.up * 0.2f;
        }
        else
        {
            zoneBox.size = new Vector3(3f, 2f, 30f);
            zoneBox.center = Vector3.up * 0.5f;
        }
    }

    private void FindPlayer()
    {
        if (_cachedPlayer == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null) p = GameObject.Find("Player");
            if (p != null) _cachedPlayer = p.GetComponent<PlayerMovement>() ?? p.GetComponentInChildren<PlayerMovement>() ?? p.GetComponentInParent<PlayerMovement>();
            if (_cachedPlayer == null) _cachedPlayer = Object.FindFirstObjectByType<PlayerMovement>();
        }
    }

    private void Update()
    {
        // 若已觸發傳送，嚴格禁止 Fail-Safe Red Line 再次判定重生，避免狀態衝突
        if (_teleportTriggered || _isTeleporting) return;

        // -------------------------------------------------------------
        // 【防跳過階梯/越界規則 (Fail-Safe)】
        // 玩家如果跳得又高又遠，直接從空中飛過階梯上方/右側而沒有踩到傳送階梯，
        // 一旦 X 軸越過階梯防線，立刻判定墜落死亡，強制重生回最近存檔點！
        // -------------------------------------------------------------
        if (enableBypassDeathCheck)
        {
            if (_cachedPlayer == null) FindPlayer();
            if (_cachedPlayer != null)
            {
                Vector3 playerPos = _cachedPlayer.transform.position;

                // 檢查是否在當前階梯的高度區間內
                float stairY = transform.position.y;
                if (playerPos.y >= (stairY - checkYDown) && playerPos.y <= (stairY + checkYUp))
                {
                    // 計算防跳過警戒線 X
                    float failSafeLineX = GetRightEdgeX();

                    // 若玩家 X 軸已經徹底越過階梯右側防線，且未踩中階梯
                    if (playerPos.x > failSafeLineX)
                    {
                        PlayerRespawnSystem respawnSystem = _cachedPlayer.GetComponent<PlayerRespawnSystem>()
                            ?? _cachedPlayer.GetComponentInParent<PlayerRespawnSystem>()
                            ?? Object.FindFirstObjectByType<PlayerRespawnSystem>();

                        if (respawnSystem != null && !respawnSystem.IsRespawning)
                        {
                            Debug.LogWarning($"⚠️【階梯防越界判定】玩家 (X:{playerPos.x:F2}, Y:{playerPos.y:F2}) 徹底越過了階梯最右側防線 (X:{failSafeLineX:F2}) 且未踩中階梯！立即重生回最近存檔點！");
                            respawnSystem.TriggerRespawn();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 計算階梯物件的「最右側世界邊緣 X 座標」
    /// </summary>
    public float GetRightEdgeX()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds.max.x + bypassOffsetX;
        }

        Collider2D col2d = GetComponent<Collider2D>();
        if (col2d != null)
        {
            return col2d.bounds.max.x + bypassOffsetX;
        }

        SpriteRenderer sr = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            return sr.bounds.max.x + bypassOffsetX;
        }

        return transform.position.x + (transform.lossyScale.x * 0.5f) + bypassOffsetX;
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleTeleport(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleTeleport(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleTeleport(collision.gameObject);
    }

    private void OnCollisionStay(Collision collision)
    {
        HandleTeleport(collision.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleTeleport(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleTeleport(collision.gameObject);
    }

    /// <summary>
    /// 核心傳送觸發邏輯：立即定住玩家、保持地面碰撞、極速白色夢幻轉場
    /// </summary>
    private void HandleTeleport(GameObject targetObj)
    {
        if (_isTeleporting || _teleportTriggered || targetObj == null) return;

        // 偵測是否為玩家
        PlayerMovement player = targetObj.GetComponent<PlayerMovement>()
            ?? targetObj.GetComponentInParent<PlayerMovement>()
            ?? targetObj.GetComponentInChildren<PlayerMovement>();

        if (player == null && (targetObj.CompareTag("Player") || targetObj.name.Contains("Player") || targetObj.transform.root.name.Contains("Player")))
        {
            player = Object.FindFirstObjectByType<PlayerMovement>();
        }

        if (player != null && destination != null)
        {
            _isTeleporting = true;
            _teleportTriggered = true;

            // ① 立即凍結玩家所有輸入與物理移動（保持當前站立姿態，絕不下掉）
            player.isCutsceneFrozen = true;
            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Debug.Log($"✨【階梯白色轉場】主角踩上最後階梯 '{name}'！立即鎖定角色，開始白色夢幻過渡至 '{destination.name}' ({destination.position})");

            // ② 啟動白色夢幻極速轉場協程
            StartCoroutine(WhiteFadeTeleportRoutine(player, rb));
        }
    }

    private IEnumerator WhiteFadeTeleportRoutine(PlayerMovement player, Rigidbody rb)
    {
        EnsureWhiteFadeUI();

        // 播放轉場音效
        if (teleportSFX != null)
        {
            AudioSource.PlayClipAtPoint(teleportSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 光絮聯動：同步瞬移至目的地
        GuidanceLight targetLight = guidanceLight != null ? guidanceLight : Object.FindFirstObjectByType<GuidanceLight>();
        if (targetLight != null)
        {
            Vector3 lightDest = destination.position + Vector3.up * 1.5f;
            targetLight.TeleportLight(lightDest, targetWaypointIndex);
        }

        // 更新安全存檔點至上層城堡
        if (updateRespawnPoint && player != null)
        {
            PlayerRespawnSystem respawnSystem = player.GetComponent<PlayerRespawnSystem>()
                ?? player.GetComponentInParent<PlayerRespawnSystem>()
                ?? Object.FindFirstObjectByType<PlayerRespawnSystem>();

            if (respawnSystem != null)
            {
                respawnSystem.SetSafeGroundPosition(destination.position);
            }
        }

        // ---------------------------------------------------------
        // 階段 1：白色快速泛白淡入 (White Fade In: 0.12s)
        // ---------------------------------------------------------
        _whiteFadeImage.gameObject.SetActive(true);
        float timer = 0f;
        while (timer < whiteFadeInDuration)
        {
            timer += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(timer / whiteFadeInDuration);
            _whiteFadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, alpha);

            // 確保轉場期間玩家絕對維持靜止
            if (player != null) player.isCutsceneFrozen = true;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            yield return null;
        }
        _whiteFadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, 1f);

        // ---------------------------------------------------------
        // 階段 2：完全白屏期間執行 Warp (0.08s)
        // ---------------------------------------------------------
        if (whiteHoldDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(whiteHoldDuration);
        }

        // 執行玩家與相機座標瞬間遷移
        if (player != null)
        {
            player.WarpTo(destination.position);
            // WarpTo 內部可能重置 isCutsceneFrozen，在此強制保持凍結直到白幕散去
            player.isCutsceneFrozen = true;

            if (rb != null && resetVelocity)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // 等待一幀確保物理引擎與 Cinemachine 相機在城堡新位置完全穩定
        yield return null;

        // ---------------------------------------------------------
        // 階段 3：白色淡出，視野逐漸清晰 (White Fade Out: 0.20s)
        // ---------------------------------------------------------
        timer = 0f;
        while (timer < whiteFadeOutDuration)
        {
            timer += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(1f - (timer / whiteFadeOutDuration));
            _whiteFadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, alpha);
            yield return null;
        }

        _whiteFadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, 0f);
        _whiteFadeImage.gameObject.SetActive(false);

        // ---------------------------------------------------------
        // 階段 4：完全解除凍結，無縫進入城堡遊玩
        // ---------------------------------------------------------
        if (player != null)
        {
            player.isCutsceneFrozen = false;
            if (rb != null && resetVelocity)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        _isTeleporting = false;
    }

    /// <summary>
    /// 確保全域常駐的白色轉場畫布存在
    /// </summary>
    private static void EnsureWhiteFadeUI()
    {
        if (_whiteFadeCanvas == null)
        {
            GameObject canvasObj = new GameObject("[Stair_WhiteFadeCanvas]");
            DontDestroyOnLoad(canvasObj);

            _whiteFadeCanvas = canvasObj.AddComponent<Canvas>();
            _whiteFadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _whiteFadeCanvas.sortingOrder = 9999;

            canvasObj.AddComponent<CanvasScaler>();

            GameObject imgObj = new GameObject("WhiteFadeImage");
            imgObj.transform.SetParent(canvasObj.transform, false);

            _whiteFadeImage = imgObj.AddComponent<Image>();
            _whiteFadeImage.raycastTarget = false; // 絕不阻擋點擊
            _whiteFadeImage.color = new Color(1f, 1f, 1f, 0f);

            RectTransform rt = _whiteFadeImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            imgObj.SetActive(false);
        }
    }

    // 在 Unity Scene 視窗畫出傳送連線與防越界警戒線
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }

        // 畫出防跳過警戒線 (紅色)
        if (enableBypassDeathCheck)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.85f);
            float lineX = GetRightEdgeX();
            Vector3 top = new Vector3(lineX, transform.position.y + checkYUp, transform.position.z);
            Vector3 bot = new Vector3(lineX, transform.position.y - checkYDown, transform.position.z);
            Gizmos.DrawLine(top, bot);
            Gizmos.DrawWireSphere(top, 0.35f);
            Gizmos.DrawWireSphere(bot, 0.35f);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(new Vector3(lineX, transform.position.y + 1.5f, transform.position.z), $"⛔ 防跳過警戒線 [下:-{checkYDown:F0}m, 上:+{checkYUp:F0}m]");
            #endif
        }

        if (destination != null)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Gizmos.DrawLine(transform.position, destination.position);
            Gizmos.DrawWireSphere(destination.position, 0.8f);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, "🪜 階梯傳送起點 (Stair Trigger)");
            UnityEditor.Handles.Label(destination.position + Vector3.up * 1f, "🏰 上層城堡目的地 (Castle Destination)");
            #endif
        }
    }
}

