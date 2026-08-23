using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Cinemachine;

/// <summary>
/// 天空跳崖極速雲霧/黑屏轉場傳送組件 (Sky Dive Teleport Trigger)
/// 掛在天空城堡跳崖處的 Trigger 碰撞框上：
/// 1. 當主角跳下碰觸時，螢幕極速白霧漸變 (0.2秒)
/// 2. 瞬間將主角與攝影機無縫傳送至下方的墜落起點 (Target Drop Point)
/// 3. 白霧散去，主角直接以高速衝進烏雲墜落通道，無縫銜接音樂與下墜動畫！
/// </summary>
[RequireComponent(typeof(Collider))]
public class SkyDiveTeleportTrigger : MonoBehaviour
{
    [Header("🎯 傳送目標點")]
    [Tooltip("傳送的目的地物件 (請在墜落烏雲背景頂部放一個空物件並拖入此欄位)")]
    public Transform targetDropPoint;

    [Header("🌫️ 轉場視覺設定")]
    [Tooltip("轉場過渡顏色 (預設純白 = 穿透白霧雲海；亦可改為純黑 = 墜入深淵)")]
    public Color transitionColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("進入白霧的淡入時間 (秒，預設 0.2 秒極速過渡)")]
    public float fadeOutDuration = 0.2f;

    [Tooltip("完全盲屏停頓時間 (秒)")]
    public float holdDuration = 0.08f;

    [Tooltip("白霧散去恢復視野時間 (秒)")]
    public float fadeInDuration = 0.35f;

    [Header("🚀 墜落下衝物理")]
    [Tooltip("傳送後給予主角的向下初速度 (負值，預設 -10f 確保下墜感極速流暢)")]
    public float initialDownwardSpeed = -10f;

    [Header("🎵 轉場音效 (可選)")]
    [Tooltip("跳入雲海瞬間播放的呼嘯風聲或破雲音效")]
    public AudioClip transitionSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    private bool _isTeleporting = false;
    private static Canvas _fadeCanvas;
    private static Image _fadeImage;

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
            col.isTrigger = true; // 強制開啟 Trigger
            if (col is BoxCollider box)
            {
                // ★ 關鍵防護：自動給予 30 米 Z 軸厚度，確保在 2.5D 場景中 100% 能捕捉到主角，絕不因 Z 軸微小落差而擦身而過！
                Vector3 size = box.size;
                size.z = Mathf.Max(size.z, 30f);
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
        TryTriggerTeleport(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        TryTriggerTeleport(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryTriggerTeleport(collision.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTriggerTeleport(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryTriggerTeleport(collision.gameObject);
    }

    private void TryTriggerTeleport(GameObject hitObj)
    {
        if (_isTeleporting || hitObj == null) return;

        // 智能識別主角 (支援 Tag、名稱、組件、根物件等多維度搜尋)
        PlayerMovement pm = hitObj.GetComponent<PlayerMovement>();
        if (pm == null) pm = hitObj.GetComponentInParent<PlayerMovement>();
        if (pm == null) pm = hitObj.GetComponentInChildren<PlayerMovement>();
        if (pm == null && (hitObj.CompareTag("Player") || hitObj.name.Contains("Player") || hitObj.transform.root.name.Contains("Player")))
        {
            pm = Object.FindFirstObjectByType<PlayerMovement>();
        }

        if (pm != null)
        {
            if (targetDropPoint != null)
            {
                Debug.Log($"☁️【SkyDiveTeleportTrigger】偵測到主角 '{hitObj.name}' 進入跳崖觸發區！開始極速白霧轉場至 '{targetDropPoint.name}'！");
                StartCoroutine(SkyDiveTeleportRoutine(pm));
            }
            else
            {
                Debug.LogWarning($"⚠️【SkyDiveTeleportTrigger】'{name}' 偵測到主角跳下，但未指定 Target Drop Point (傳送目標點)！請在 Inspector 拖入目的地。");
            }
        }
    }

    private IEnumerator SkyDiveTeleportRoutine(PlayerMovement pm)
    {
        _isTeleporting = true;

        // 確保轉場 UI 畫布存在
        EnsureFadeUI();

        // 播放過渡音效
        if (transitionSFX != null)
        {
            AudioSource.PlayClipAtPoint(transitionSFX, transform.position, sfxVolume);
        }

        // 1. 極速白霧淡入 (螢幕變白)
        _fadeImage.gameObject.SetActive(true);
        float timer = 0f;
        while (timer < fadeOutDuration)
        {
            timer += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(timer / fadeOutDuration);
            _fadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, alpha);
            yield return null;
        }
        _fadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, 1f);

        // 2. 盲屏瞬間：瞬移主角與重設相機
        Vector3 targetPos = targetDropPoint.position;
        pm.WarpTo(targetPos);

        // 更新重生點，防止被判定墜崖
        PlayerRespawnSystem respawnSystem = pm.GetComponent<PlayerRespawnSystem>();
        if (respawnSystem != null)
        {
            respawnSystem.SetSafeGroundPosition(targetPos);
        }

        // 賦予順暢的向下高速下墜動量
        Rigidbody rb = pm.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector3(0f, initialDownwardSpeed, 0f);
        }

        // 瞬移相機防拉扯撕裂
        WarpAllCameras(pm.transform, targetPos);

        // 微小停頓
        if (holdDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(holdDuration);
        }

        // 3. 白霧迅速散去 (恢復視野)
        timer = 0f;
        while (timer < fadeInDuration)
        {
            timer += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(1f - (timer / fadeInDuration));
            _fadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, alpha);
            yield return null;
        }

        _fadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, 0f);
        _fadeImage.gameObject.SetActive(false);

        _isTeleporting = false;
        Debug.Log($"✨【天墜過渡】主角已成功穿透雲霧，傳送至墜落起點：{targetPos}");
    }

    private void WarpAllCameras(Transform target, Vector3 targetPos)
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.transform.position = new Vector3(targetPos.x, targetPos.y, mainCam.transform.position.z);
            CinemachineCameraConfiner3D confiner = mainCam.GetComponent<CinemachineCameraConfiner3D>();
            if (confiner != null)
            {
                confiner.CacheBoundaries();
            }
        }

        GameObject cameraTargetObj = GameObject.Find("PlayerCameraTarget_SmoothY");
        if (cameraTargetObj != null)
        {
            cameraTargetObj.transform.position = targetPos;
        }

        // 新版 CinemachineCamera
        var vcams = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach (var vcam in vcams)
        {
            if (vcam != null)
            {
                vcam.OnTargetObjectWarped(target, targetPos - vcam.transform.position);
                vcam.PreviousStateIsValid = false;
                vcam.transform.position = new Vector3(targetPos.x, targetPos.y, vcam.transform.position.z);
            }
        }

        var vcamsLegacy = Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
        foreach (var vcam in vcamsLegacy)
        {
            if (vcam != null)
            {
                vcam.OnTargetObjectWarped(target, targetPos - vcam.transform.position);
                vcam.PreviousStateIsValid = false;
                vcam.transform.position = new Vector3(targetPos.x, targetPos.y, vcam.transform.position.z);
            }
        }
    }

    private void EnsureFadeUI()
    {
        if (_fadeCanvas == null)
        {
            GameObject canvasObj = new GameObject("[SkyDive_FadeCanvas]");
            DontDestroyOnLoad(canvasObj);

            _fadeCanvas = canvasObj.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fadeCanvas.sortingOrder = 9999; // 最高層級

            canvasObj.AddComponent<CanvasScaler>();

            GameObject imgObj = new GameObject("FadeImage");
            imgObj.transform.SetParent(canvasObj.transform, false);

            _fadeImage = imgObj.AddComponent<Image>();
            _fadeImage.raycastTarget = false;
            _fadeImage.color = new Color(transitionColor.r, transitionColor.g, transitionColor.b, 0f);

            RectTransform rt = _fadeImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            imgObj.SetActive(false);
        }
    }

    // 在 Unity Scene 視窗畫出綠色連線與傳送落點標籤，方便設計師肉眼對齊
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.7f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 1.0f);
        }

        if (targetDropPoint != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawLine(transform.position, targetDropPoint.position);
            Gizmos.DrawWireSphere(targetDropPoint.position, 0.8f);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, "☁️ 跳崖白霧觸發點 (Sky Dive Trigger)");
            UnityEditor.Handles.Label(targetDropPoint.position + Vector3.up * 1.2f, "🎯 墜落起點 (Drop Target)");
            #endif
        }
    }
}
