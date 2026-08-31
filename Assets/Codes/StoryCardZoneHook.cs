using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 我你他　過場文字卡掛鉤（換場景用・零改動版）
///
/// 用在會換場景的卡片 —— 施工單第二節的 M3、M4、M5。
///
/// 【怎麼用】
///   把這支腳本掛在「已經有 SceneTransitionZone 的那個物件」上，cardId 填 M3 / M4 / M5。
///   就這樣。不用改 SceneTransitionZone.cs 一個字。
///
/// 【它做了什麼】
///   Awake 時把同物件上的 SceneTransitionZone 停用，接管轉場：
///       鎖玩家 →（SinkWater 模式先下沉）→ 淡黑 → 播文字卡 → 設出生點 → 載場景
///   目標場景、出生點、淡出時間、下沉參數、音效，全部直接讀原本
///   SceneTransitionZone 上已經填好的值，不用重填。
///
/// 【什麼時候不要用這支】
///   如果你們願意改 SceneTransitionZone.cs 那六行（見《掛接說明》第二節），
///   那條路比較好 —— 它走的是原本測過的轉場流程，
///   剛體約束、IResettable、AudioManager 都照舊。
///   這支是為了「不想動既有程式」而存在的替代方案。
/// </summary>
[RequireComponent(typeof(SceneTransitionZone))]
[DisallowMultipleComponent]
public class StoryCardZoneHook : MonoBehaviour
{
    [Header("要播哪張卡")]
    [Tooltip("M3＝荒原→水下　M4＝水下→玻璃館　M5＝玻璃館→結局")]
    public string cardId = "M4";

    [Header("行為")]
    [Tooltip("勾起來才會接管。取消勾選＝完全不干涉，SceneTransitionZone 照原本跑")]
    public bool takeOver = true;

    private SceneTransitionZone _zone;
    private bool _fired;

    private void Awake()
    {
        _zone = GetComponent<SceneTransitionZone>();
        if (_zone == null)
        {
            Debug.LogWarning("[StoryCardZoneHook] 同一個物件上沒有 SceneTransitionZone，這支不會作用。");
            enabled = false;
            return;
        }

        EnsureColliderSetup();

        if (takeOver)
        {
            // 停用原本的轉場，由這支接管。
            // 停用的 MonoBehaviour 不會收到 OnTriggerEnter，所以不會兩邊同時觸發。
            _zone.enabled = false;
        }
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

    private void OnTriggerEnter(Collider other)
    {
        TryRun(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay(Collider other)
    {
        TryRun(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryRun(collision != null ? collision.gameObject : null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryRun(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryRun(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryRun(collision != null ? collision.gameObject : null);
    }

    private void TryRun(GameObject hitObj)
    {
        if (!takeOver || _fired || _zone == null) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;
        if (!IsPlayer(hitObj)) return;

        _fired = true;
        StartCoroutine(Run(hitObj));
    }

    private static bool IsPlayer(GameObject hitObj)
    {
        if (hitObj == null) return false;
        if (hitObj.CompareTag("Player")) return true;
        if (hitObj.name == "Player") return true;
        if (hitObj.transform.root != null && hitObj.transform.root.name.Contains("Player")) return true;
        return hitObj.GetComponentInParent<PlayerMovement>() != null ||
               hitObj.GetComponentInChildren<PlayerMovement>() != null;
    }

    private IEnumerator Run(GameObject hitObj)
    {
        GameObject playerObj = hitObj;
        PlayerMovement pm = hitObj != null ? hitObj.GetComponentInParent<PlayerMovement>() : null;
        if (pm == null && hitObj != null) pm = hitObj.GetComponentInChildren<PlayerMovement>();
        if (pm != null) playerObj = pm.gameObject;

        Rigidbody rb = playerObj != null ? playerObj.GetComponent<Rigidbody>() : null;

        // 1　鎖玩家輸入與物理
        if (pm != null) pm.isCutsceneFrozen = true;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 2　轉場音效（原本 SceneTransitionZone 上填的那個）
        if (_zone.transitionSFX != null && playerObj != null)
        {
            AudioSource.PlayClipAtPoint(_zone.transitionSFX, playerObj.transform.position, _zone.sfxVolume);
        }

        // 3　沉水模式：先讓她沉下去，再播卡
        if (_zone.transitionMode == SceneTransitionZone.TransitionMode.SinkWater && playerObj != null)
        {
            if (rb != null) rb.useGravity = false;
            yield return Sink(playerObj.transform, _zone.sinkSpeed, _zone.targetSinkDepth);
        }

        // 4　淡黑 → 播文字卡 → 維持全黑
        yield return StoryCardPlayer.Instance.Play(cardId, true, false);

        // 5　跨場景出生點（沿用原本填的值）
        if (!string.IsNullOrEmpty(_zone.targetSpawnPointName))
        {
            PlayerRespawnSystem.QueueNextSceneSpawn(_zone.targetSpawnPointName);
        }

        // 6　載入場景
        //    載完之後 StoryCardPlayer 會自己把黑幕淡掉，這裡不用管
        string targetScene = _zone.nextSceneName != null ? _zone.nextSceneName.Trim() : "";
        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogError("[StoryCardZoneHook] SceneTransitionZone 的 nextSceneName 是空的，無法載入。");
            StoryCardPlayer.Instance.ReleaseCurtain();
            if (pm != null) pm.isCutsceneFrozen = false;
            yield break;
        }

        Debug.Log("[StoryCardZoneHook] 文字卡 " + cardId + " 播完，載入場景 " + targetScene);
        SceneManager.LoadScene(targetScene);
    }

    private IEnumerator Sink(Transform t, float speed, float depth)
    {
        if (t == null) yield break;
        float sunk = 0f;
        float s = speed > 0f ? speed : 1.2f;
        while (sunk < depth)
        {
            float step = s * Time.deltaTime;
            sunk += step;
            t.position = new Vector3(t.position.x, t.position.y - step, t.position.z);
            yield return null;
        }
    }
}
