using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// 通關封鎖巨石組件 (Quest Clear Barrier Rock)
/// 1. 【收集判定】：在 Inspector 拖入所需收集的通關物件 (如日誌 Note Paper)。全部收集完畢時自動觸發通關解鎖。
/// 2. 【鏡頭特寫】：平滑將攝影機移至巨石特寫，凍結玩家操作，確保玩家清楚看見通道開啟。
/// 3. 【消散演出】：巨石微震、播放瓦解音效、粒子微光爆發、縮小塌陷並【100% 徹底消除所有碰撞體】。
/// 4. 【鏡頭回歸】：特寫結束後鏡頭平滑切回主角，解除玩家控制，通關傳送門正式開啟！
/// </summary>
public class QuestClearBarrierRock : MonoBehaviour
{
    [Header("📖 日誌通關條件（企劃定案：收齊日誌 → 巨石碎開）")]
    [Tooltip("開啟後，撿到指定張數的日誌卡（StoryCardNoteHook D1~Dn）巨石就消散；下面的 requiredItems 清單只在關閉時作為備用條件")]
    public bool clearByDiaryCount = true;

    [Tooltip("需要收齊幾張日誌卡")]
    public int requiredDiaryCount = 3;

    [Header("📋 備用：通關所需收集品清單（clearByDiaryCount 關閉時才看這個）")]
    [Tooltip("請將場景中所有需收集的物件 (如 Note Paper、日記等) 拖入此清單")]
    public GameObject[] requiredItems;

    [Header("🎬 鏡頭特寫演出設定")]
    [Tooltip("鏡頭由主角移向巨石的平滑過渡時間 (秒)")]
    public float cameraPanToRockDuration = 1.0f;

    [Tooltip("鏡頭抵達巨石後的特寫停留觀察時間 (秒)")]
    public float cameraHoldDuration = 0.5f;

    [Tooltip("巨石消失後，鏡頭由巨石平滑回歸主角的時間 (秒)")]
    public float cameraPanBackDuration = 1.0f;

    [Tooltip("特寫演出期間是否暫時凍結主角操作 (防止過場時被攻擊或亂跑掉落)")]
    public bool freezePlayerDuringCutscene = true;

    [Header("💥 巨石消散演出設定")]
    [Tooltip("巨石開始消散前的震動時間 (秒)")]
    public float rockShakeDuration = 0.4f;

    [Tooltip("巨石縮小塌陷/漸漸消散的時間 (秒)")]
    public float rockDisappearDuration = 0.8f;

    [Tooltip("巨石消散時播放的音效 (例如 水下_日誌破障.wav 或 瓦解音效)")]
    public AudioClip disappearSFX;
    [Range(0f, 1f)] public float sfxVolume = 1.0f;

    [Tooltip("巨石消散時觸發的碎石/氣泡粒子特效 (選填)")]
    public ParticleSystem disappearParticles;

    private bool _hasTriggered = false;
    private Vector3 _initialLocalScale;
    private Vector3 _initialPosition;

    private void Awake()
    {
        _initialLocalScale = transform.localScale;
        _initialPosition = transform.position;
    }

    private void Update()
    {
        if (_hasTriggered) return;

        bool byDiary = clearByDiaryCount && StoryCardNoteHook.PickedCount >= Mathf.Max(1, requiredDiaryCount);
        bool byItems = !clearByDiaryCount && CheckAllItemsCollected();
        if (byDiary || byItems)
        {
            _hasTriggered = true;
            if (byDiary)
                Debug.Log($"🎉【通關條件達成】日誌已收齊 {StoryCardNoteHook.PickedCount}/{requiredDiaryCount} 張！啟動封鎖巨石 '{name}' 消散特寫演出！");
            else
                Debug.Log($"🎉【通關條件達成】所有收集品已收集完畢！啟動封鎖巨石 '{name}' 消散特寫演出！");
            StartCoroutine(ClearCutsceneRoutine());
        }
    }

    /// <summary>
    /// 檢查清單中的所有通關物件是否皆已被收集 (被銷毀、停用或吸收)
    /// </summary>
    private bool CheckAllItemsCollected()
    {
        if (requiredItems == null || requiredItems.Length == 0) return false;

        foreach (var item in requiredItems)
        {
            if (item != null && item.activeInHierarchy)
            {
                // 若物件依然存在且處於 Active 狀態，代表尚未收集完畢
                return false;
            }
        }

        return true;
    }

    private IEnumerator ClearCutsceneRoutine()
    {
        // 1. 尋找主角
        PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
        Transform playerTrans = pm != null ? pm.transform : GameObject.FindGameObjectWithTag("Player")?.transform;

        if (freezePlayerDuringCutscene && pm != null)
        {
            pm.isCutsceneFrozen = true;
            Rigidbody rb = pm.GetComponent<Rigidbody>();
            if (rb != null) rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        }

        Vector3 rockTargetPos = transform.position;
        Vector3 playerPos = playerTrans != null ? playerTrans.position : rockTargetPos;

        // 2. 建立鏡頭平滑過渡 Dummy Target
        GameObject dummyCameraTarget = new GameObject("[Cutscene_CameraFocusTarget]");
        dummyCameraTarget.transform.position = playerPos;

        // 尋找主相機與 Cinemachine
        SetCinemachineTarget(dummyCameraTarget.transform);

        // 3. 鏡頭平滑從主角移向巨石
        float timer = 0f;
        while (timer < cameraPanToRockDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, timer / cameraPanToRockDuration);
            dummyCameraTarget.transform.position = Vector3.Lerp(playerPos, rockTargetPos, t);
            yield return null;
        }
        dummyCameraTarget.transform.position = rockTargetPos;

        // 4. 特寫停留片刻
        if (cameraHoldDuration > 0f)
        {
            yield return new WaitForSeconds(cameraHoldDuration);
        }

        // 5. 巨石震動與消散演出
        if (disappearSFX != null)
        {
            AudioSource.PlayClipAtPoint(disappearSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        if (disappearParticles != null)
        {
            disappearParticles.Play();
        }

        // 巨石微震
        timer = 0f;
        while (timer < rockShakeDuration)
        {
            timer += Time.deltaTime;
            Vector3 shakeOffset = Random.insideUnitSphere * 0.15f;
            transform.position = _initialPosition + shakeOffset;
            yield return null;
        }
        transform.position = _initialPosition;

        // 立即全面關閉本巨石身上所有碰撞體 (包括 MeshCollider 與 BoxCollider)
        Collider[] allCols = GetComponentsInChildren<Collider>(true);
        foreach (var col in allCols)
        {
            if (col != null) col.enabled = false;
        }

        // 巨石縮小塌陷並漸漸消失
        timer = 0f;
        while (timer < rockDisappearDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / rockDisappearDuration);
            // 指數塌陷 Ease-In
            float scaleT = Mathf.Clamp01(1f - Mathf.Pow(t, 2.5f));
            transform.localScale = _initialLocalScale * scaleT;
            yield return null;
        }

        transform.localScale = Vector3.zero;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        // 6. 鏡頭平滑從巨石拉回主角
        if (playerTrans != null)
        {
            playerPos = playerTrans.position;
            timer = 0f;
            while (timer < cameraPanBackDuration)
            {
                timer += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, timer / cameraPanBackDuration);
                dummyCameraTarget.transform.position = Vector3.Lerp(rockTargetPos, playerPos, t);
                yield return null;
            }
        }

        // 7. 還原鏡頭追蹤目標給主角，並銷毀 Dummy Target
        SetCinemachineTarget(playerTrans);
        Destroy(dummyCameraTarget);

        // 8. 解除主角操作鎖定
        if (freezePlayerDuringCutscene && pm != null)
        {
            pm.isCutsceneFrozen = false;
        }

        // 徹底將此封鎖巨石停用，保證通道 100% 暢通無阻
        gameObject.SetActive(false);

        Debug.Log($"✨【通關解鎖完成】封鎖巨石已完全消除，鏡頭已回歸主角，通道暢通！");
    }

    private void SetCinemachineTarget(Transform target)
    {
        if (target == null) return;

        // 支援 Unity 6 新版 CinemachineCamera (3.x)
        var vcams = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach (var vcam in vcams)
        {
            if (vcam != null)
            {
                var t = vcam.Target;
                t.TrackingTarget = target;
                vcam.Target = t;
                vcam.Follow = target;
            }
        }

        // 支援舊版 CinemachineVirtualCamera
        var vcamsLegacy = Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
        foreach (var vcam in vcamsLegacy)
        {
            if (vcam != null)
            {
                vcam.Follow = target;
                vcam.LookAt = target;
            }
        }
    }

    private void OnDrawGizmos()
    {
        // ★ 緊密貼合巨石 3D 模型線框繪製，絕無多餘方塊外框誤導視野
        Gizmos.color = new Color(0.85f, 0.25f, 1f, 0.85f);
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireMesh(mf.sharedMesh);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 1.0f);
        }

        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, $"🪨 通關封鎖巨石 (剩餘收集品: {(requiredItems != null ? requiredItems.Length : 0)})");
        #endif
    }
}
