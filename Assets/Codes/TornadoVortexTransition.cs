using System.Collections;
using UnityEngine;

/// <summary>
/// 暴風吸入式電影轉場組件 (Tornado Vortex Suction Transition)
/// 掛載於暴風/龍捲風轉場 Trigger 碰撞框上：
/// 1. 【自動引力吸入】：玩家一旦踩入邊界，強大暴風引力會平滑將主角身體拉向風暴中心。
/// 2. 【原地掙扎奔跑】：玩家即使狂按方向鍵掙扎，動作依然播放奔跑，但位移被風暴引力完全接管，絕不再被玩家反覆跑出邊界！
/// 3. 【被風暴吞噬轉場】：吸至中心瞬間伴隨風暴咆哮聲，觸發電影級轉場傳送至目的地！
/// </summary>
public class TornadoVortexTransition : MonoBehaviour
{
    [Header("🎯 傳送目的地")]
    [Tooltip("傳送的目的地 Transform (例如 下一關起點 或 下層場景)")]
    public Transform destination;

    [Tooltip("風暴吸入的核心點 (若留空，自動以本物件的 Center 作為核心)")]
    public Transform vortexCenter;

    [Header("🌪️ 風暴吸入演出設定")]
    [Tooltip("吸入過程持續時間 (秒，預設 1.2 秒)")]
    [Range(0.5f, 3.0f)]
    public float suctionDuration = 1.2f;

    [Tooltip("吸入時是否更新重生安全點至目的地")]
    public bool updateRespawnPoint = true;

    [Header("🎵 暴風音效")]
    [Tooltip("進入吸入區時播放的狂風暴風咆哮聲")]
    public AudioClip vortexSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.95f;

    private bool _isTransitioning = false;

    private void Awake()
    {
        EnsureCollider();
    }

    private void Start()
    {
        EnsureCollider();
    }

    private void EnsureCollider()
    {
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            BoxCollider newBox = gameObject.AddComponent<BoxCollider>();
            newBox.size = new Vector3(8f, 15f, 30f);
            newBox.isTrigger = true;
            col = newBox;
        }
        else
        {
            col.isTrigger = true;
            if (col is BoxCollider box)
            {
                // 自動給予 30 米 Z 軸厚度，確保 2.5D 絕不漏碰
                Vector3 size = box.size;
                size.z = Mathf.Max(size.z, 30f);
                box.size = size;

                Vector3 center = box.center;
                center.z = 0f;
                box.center = center;
            }
        }

        Collider2D col2d = GetComponent<Collider2D>();
        if (col2d != null) col2d.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryStartVortex(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        TryStartVortex(other.gameObject);
    }

    private void TryStartVortex(GameObject hitObj)
    {
        if (_isTransitioning || hitObj == null) return;

        PlayerMovement pm = hitObj.GetComponent<PlayerMovement>() ?? 
                           hitObj.GetComponentInParent<PlayerMovement>() ?? 
                           hitObj.GetComponentInChildren<PlayerMovement>();

        if (pm == null && (hitObj.CompareTag("Player") || hitObj.name.Contains("Player")))
        {
            pm = Object.FindFirstObjectByType<PlayerMovement>();
        }

        if (pm != null && destination != null)
        {
            StartCoroutine(VortexSuctionRoutine(pm));
        }
    }

    private IEnumerator VortexSuctionRoutine(PlayerMovement pm)
    {
        _isTransitioning = true;
        Debug.Log($"🌪️【暴風轉場】主角踏入風暴吸入區！開始吸入演出...");

        // 播放狂風咆哮音效
        if (vortexSFX != null)
        {
            AudioSource.PlayClipAtPoint(vortexSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 1. 凍結玩家常規移動，接管位移
        pm.isCutsceneFrozen = true;

        Rigidbody rb = pm.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.useGravity = false;
        }

        // 獲取玩家 Animator
        Animator anim = pm.GetComponentInChildren<Animator>();

        Vector3 startPos = pm.transform.position;
        Vector3 centerPos = vortexCenter != null ? vortexCenter.position : transform.position;
        centerPos.z = startPos.z; // 保持 Z 軸穩定

        // 2. 漸進式強吸入 (Ease-In Acceleration)
        float elapsed = 0f;
        while (elapsed < suctionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / suctionDuration);

            // 指數加速曲線：一開始微吸，越靠近中心吸速越快！
            float suctionCurve = Mathf.Pow(t, 2.2f);
            pm.transform.position = Vector3.Lerp(startPos, centerPos, suctionCurve);

            // ★ 原地跑步/掙扎動畫：保持播放奔跑動作，營造頂風掙扎卻無法阻擋被吸入的臨場感
            if (anim != null)
            {
                anim.SetFloat("Speed", 1.0f);
            }

            yield return null;
        }

        pm.transform.position = centerPos;

        // 3. 抵達風暴中心，觸發電影級轉場傳送
        PlayerRespawnSystem respawnSystem = pm.GetComponent<PlayerRespawnSystem>() ?? pm.GetComponentInParent<PlayerRespawnSystem>();
        if (respawnSystem != null)
        {
            if (updateRespawnPoint)
            {
                respawnSystem.SetSafeGroundPosition(destination.position);
            }

            respawnSystem.TriggerTeleport(destination.position);
        }
        else
        {
            pm.WarpTo(destination.position);
        }

        if (rb != null)
        {
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
        }

        _isTransitioning = false;
        Debug.Log($"✨【暴風轉場完成】主角已被暴風完全吞噬並轉場至 '{destination.name}'！");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.7f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 2.0f);
        }

        Vector3 center = vortexCenter != null ? vortexCenter.position : transform.position;
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.9f);
        Gizmos.DrawWireSphere(center, 0.8f);

        if (destination != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.9f);
            Gizmos.DrawLine(center, destination.position);
            Gizmos.DrawWireSphere(destination.position, 1.0f);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(center + Vector3.up * 1.5f, "🌪️ 風暴吸入引力區 (Vortex Trigger)");
            UnityEditor.Handles.Label(destination.position + Vector3.up * 1.5f, "🎯 暴風吞噬目的地 (Storm Destination)");
            #endif
        }
    }
}
