using System.Collections;
using UnityEngine;

/// <summary>
/// 可碎裂重生的平台腳本 (適用於 3D 物理：3D Rigidbody + 3D Collider)
/// 視覺設計：保留平台原圖為本體，並在上面疊加裂痕覆蓋層 (Overlay)。
/// 流程：玩家踩上 -> 短暫延遲 -> 平台震動 + 原圖上疊加裂痕 -> 掉落 -> 一段時間後重生
/// 同時實現 IResettable 介面，支援區域重置系統。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class BreakablePlatform : MonoBehaviour, IResettable
{
    public enum BreakState
    {
        Intact,    // 完好狀態
        Delaying,  // 觸發延遲中
        Shaking,   // 震動裂開中
        Falling,   // 掉落中
        Broken     // 已碎裂隱藏 (等待重生)
    }

    [Header("時間與延遲參數 (秒)")]
    [SerializeField]
    [Tooltip("踩上平台後的初次短暫延遲 (秒)")]
    private float initialDelay = 0.2f;

    [SerializeField]
    [Tooltip("平台震動與出現裂痕的持續時間 (秒)")]
    private float shakeDuration = 1.0f;

    [SerializeField]
    [Tooltip("掉落後到平台隱藏的持續時間 (秒)")]
    private float fallDuration = 1.5f;

    [SerializeField]
    [Tooltip("平台隱藏後到重新引導刷新的時間 (秒)")]
    private float respawnDelay = 3.0f;

    [Header("震動參數")]
    [SerializeField]
    [Tooltip("震動強度/振幅")]
    private float shakeIntensity = 0.08f;

    [Header("視覺效果與裂痕疊加設定")]
    [SerializeField]
    [Tooltip("疊加在平台原圖上的裂痕 Sprite (例如玻璃裂紋貼圖)")]
    private Sprite crackedOverlaySprite;

    [SerializeField]
    [Tooltip("震動時主平台的著色顏色 (警示色)")]
    private Color mainColorTint = new Color(0.95f, 0.7f, 0.7f, 1f);

    [SerializeField]
    [Tooltip("裂痕覆蓋圖層的透明度 (0~1)")]
    private float overlayMaxAlpha = 0.85f;

    [Header("玩家判定")]
    [SerializeField]
    [Tooltip("判定玩家的 Layer Mask。若設為 Nothing 則使用 Tag 判定。")]
    private LayerMask playerLayer;

    [SerializeField]
    [Tooltip("判定玩家的 Tag (預設為 'Player')")]
    private string playerTag = "Player";

    [SerializeField]
    [Tooltip("頂部觸碰法線 Y 門檻 (法線朝下於此數值表示玩家站在平台上)")]
    private float topCollisionNormalThreshold = -0.5f;

    // --- 內部狀態與快取 ---
    private Rigidbody rb;
    private Collider col;
    private SpriteRenderer mainSr;

    // 裂痕覆蓋圖層
    private GameObject overlayObj;
    private SpriteRenderer overlaySr;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Sprite originalSprite;
    private Color originalColor;

    private BreakState currentState = BreakState.Intact;
    private Coroutine breakCoroutine;

    public BreakState CurrentState => currentState;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;

        col = GetComponent<Collider>();
        mainSr = GetComponent<SpriteRenderer>();

        // 紀錄初始位置與狀態
        initialPosition = transform.position;
        initialRotation = transform.rotation;

        if (mainSr != null)
        {
            originalSprite = mainSr.sprite;
            originalColor = mainSr.color;
        }

        // 自動建立/設定子物件裂痕圖層 (Crack Overlay)
        SetupCrackOverlay();
    }

    /// <summary>
    /// 初始化裂痕覆蓋子物件，使其精準疊加在平台原圖上
    /// </summary>
    private void SetupCrackOverlay()
    {
        Transform overlayTrans = transform.Find("CrackOverlay");
        if (overlayTrans == null)
        {
            overlayObj = new GameObject("CrackOverlay");
            overlayObj.transform.SetParent(transform, false);
            overlayObj.transform.localPosition = new Vector3(0f, 0f, -0.01f); // 稍微在前方
            overlayObj.transform.localRotation = Quaternion.identity;
            overlayObj.transform.localScale = Vector3.one;
        }
        else
        {
            overlayObj = overlayTrans.gameObject;
        }

        overlaySr = overlayObj.GetComponent<SpriteRenderer>();
        if (overlaySr == null)
        {
            overlaySr = overlayObj.AddComponent<SpriteRenderer>();
        }

        if (mainSr != null)
        {
            overlaySr.sortingLayerID = mainSr.sortingLayerID;
            overlaySr.sortingOrder = mainSr.sortingOrder + 1; // 確保在主貼圖前層繪製
        }

        if (crackedOverlaySprite != null)
        {
            overlaySr.sprite = crackedOverlaySprite;
        }

        // 初始設為不顯示 (透明)
        overlaySr.color = new Color(1f, 1f, 1f, 0f);
        overlayObj.SetActive(false);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (currentState != BreakState.Intact) return;

        if (IsPlayerObject(collision.gameObject))
        {
            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.normal.y < topCollisionNormalThreshold)
                {
                    StartBreakSequence();
                    break;
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (currentState != BreakState.Intact) return;

        if (IsPlayerObject(other.gameObject))
        {
            StartBreakSequence();
        }
    }

    public void StartBreakSequence()
    {
        if (currentState != BreakState.Intact) return;

        if (breakCoroutine != null)
        {
            StopCoroutine(breakCoroutine);
        }
        breakCoroutine = StartCoroutine(BreakRoutine());
    }

    private IEnumerator BreakRoutine()
    {
        // Step 1: 踩上短暫延遲
        currentState = BreakState.Delaying;
        if (initialDelay > 0f)
        {
            yield return new WaitForSeconds(initialDelay);
        }

        // Step 2: 平台震動 & 原圖上疊加顯示裂痕
        currentState = BreakState.Shaking;

        if (overlayObj != null && overlaySr != null)
        {
            if (crackedOverlaySprite != null)
            {
                overlaySr.sprite = crackedOverlaySprite;
            }
            overlayObj.SetActive(true);
        }

        float timer = 0f;
        while (timer < shakeDuration)
        {
            timer += Time.deltaTime;

            // 隨機微幅位移產生震動與晃動效果
            Vector3 randomOffset = Random.insideUnitSphere * shakeIntensity;
            transform.position = initialPosition + randomOffset;

            // 裂痕覆蓋層透明度逐步顯現與微幅閃爍
            float progress = Mathf.Clamp01(timer / shakeDuration);
            float pulse = Mathf.PingPong(timer * 10f, 0.2f);
            float alpha = Mathf.Clamp01((progress * overlayMaxAlpha) + pulse);

            if (overlaySr != null)
            {
                overlaySr.color = new Color(1f, 1f, 1f, alpha);
            }

            // 原圖主體微調變暗/變紅警示
            if (mainSr != null)
            {
                mainSr.color = Color.Lerp(originalColor, mainColorTint, progress);
            }

            yield return null;
        }

        // 震動結束，回歸準確位置準備掉落
        transform.position = initialPosition;
        if (overlaySr != null)
        {
            overlaySr.color = new Color(1f, 1f, 1f, overlayMaxAlpha);
        }

        // Step 3: 掉落 (開啟物理重力)
        currentState = BreakState.Falling;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = Vector3.zero;

        // 掉落短暫延遲後關閉碰撞，讓玩家落入下方
        yield return new WaitForSeconds(0.2f);
        if (col != null)
        {
            col.enabled = false;
        }

        // 等待掉落歷程結束
        yield return new WaitForSeconds(Mathf.Max(0.1f, fallDuration - 0.2f));

        // Step 4: 隱藏平台與裂痕 (進入已破裂等待狀態)
        currentState = BreakState.Broken;
        SetPlatformVisible(false);

        // Step 5: 等待重生時間
        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);

            // 重生刷新
            ResetToInitialState();
        }
    }

    private void SetPlatformVisible(bool visible)
    {
        if (mainSr != null) mainSr.enabled = visible;
        if (overlayObj != null) overlayObj.SetActive(visible);
        if (col != null) col.enabled = visible;

        if (!visible && rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    public void ResetToInitialState()
    {
        if (breakCoroutine != null)
        {
            StopCoroutine(breakCoroutine);
            breakCoroutine = null;
        }

        // 重置物理 transform
        transform.position = initialPosition;
        transform.rotation = initialRotation;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 恢復原版主貼圖
        if (mainSr != null)
        {
            if (originalSprite != null) mainSr.sprite = originalSprite;
            mainSr.color = originalColor;
            mainSr.enabled = true;
        }

        // 隱藏/重置裂痕覆蓋層
        if (overlaySr != null)
        {
            overlaySr.color = new Color(1f, 1f, 1f, 0f);
        }
        if (overlayObj != null)
        {
            overlayObj.SetActive(false);
        }

        // 恢復碰撞體
        if (col != null)
        {
            col.enabled = true;
        }

        currentState = BreakState.Intact;
    }

    private bool IsPlayerObject(GameObject go)
    {
        if (go == null) return false;

        if (playerLayer.value != 0)
        {
            if (((1 << go.layer) & playerLayer.value) != 0)
                return true;
        }

        if (!string.IsNullOrEmpty(playerTag) && go.CompareTag(playerTag))
        {
            return true;
        }

        return false;
    }
}
