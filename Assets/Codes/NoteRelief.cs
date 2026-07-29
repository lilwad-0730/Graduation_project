using UnityEngine;

/// <summary>
/// 掛在 Note Paper 物件（或其 Parent）上
/// 當玩家接觸到此物件時：
/// 1. 觸發吸收動畫 (Animator 若存在)
/// 2. 通知 UnderwaterSuffocationEffect 執行 20% 緩解
/// 3. 禁用本物件 (被「吸收」消失)
/// </summary>
public class NoteRelief : MonoBehaviour
{
    [Header("【物件設定】")]
    [Tooltip("這張紙條的顯示名稱 (純描述用，方便辨識)")]
    public string noteName = "Note Paper";

    [Header("【緩解設定】")]
    [Tooltip("是否覆蓋主效果的 reliefAmount？關閉則使用主效果的預設值")]
    public bool overrideReliefAmount = false;

    [Tooltip("此特定紙條的緩解量 (overrideReliefAmount = true 時有效)")]
    [Range(0f, 1f)]
    public float customReliefAmount = 0.2f;

    [Header("【吸收動畫】")]
    [Tooltip("觸發吸收動畫的 Animator (留空則自動搜尋)")]
    public Animator noteAnimator;

    [Tooltip("吸收動畫的 Trigger 參數名稱")]
    public string absorbTriggerName = "Absorb";

    [Tooltip("動畫播完後幾秒物件消失 (秒)")]
    public float disappearDelay = 0.6f;

    [Header("【偵測設定】")]
    [Tooltip("偵測玩家的 Tag")]
    public string playerTag = "Player";

    [Tooltip("是否只能被吃一次 (防止重複觸發)")]
    public bool consumeOnce = true;

    private bool consumed = false;

    private void Start()
    {
        if (noteAnimator == null)
            noteAnimator = GetComponentInChildren<Animator>();
        if (noteAnimator == null)
            noteAnimator = GetComponent<Animator>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Absorb();
    }

    // 也支援 2D 碰撞
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;
        Absorb();
    }

    // 亦支援直接碰撞 (非 Trigger)
    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag(playerTag)) return;
        Absorb();
    }

    private void Absorb()
    {
        if (consumeOnce && consumed) return;
        consumed = true;

        Debug.Log($"[NoteRelief] 玩家吸收了「{noteName}」！觸發窒息緩解效果。");

        // 1. 播放吸收動畫
        if (noteAnimator != null)
        {
            noteAnimator.SetTrigger(absorbTriggerName);
        }

        // 2. 通知窒息效果系統
        if (UnderwaterSuffocationEffect.Instance != null)
        {
            if (overrideReliefAmount)
            {
                // 暫時替換緩解量再呼叫
                float original = UnderwaterSuffocationEffect.Instance.reliefAmount;
                UnderwaterSuffocationEffect.Instance.reliefAmount = customReliefAmount;
                UnderwaterSuffocationEffect.Instance.TriggerRelief();
                UnderwaterSuffocationEffect.Instance.reliefAmount = original;
            }
            else
            {
                UnderwaterSuffocationEffect.Instance.TriggerRelief();
            }
        }
        else
        {
            Debug.LogWarning("[NoteRelief] 找不到 UnderwaterSuffocationEffect！請確認水下場景中有掛載此腳本。");
        }

        // 3. 延遲後隱藏/銷毀物件
        Destroy(gameObject, disappearDelay);
    }

    // Editor Gizmo：在 Scene 視窗顯示偵測範圍提示
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }

        // 標示文字
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.7f, $"📄 {noteName}");
        #endif
    }
}
