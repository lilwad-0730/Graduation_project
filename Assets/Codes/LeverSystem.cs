using UnityEngine;

/// <summary>
/// 控制拉桿機關解鎖巨石掉落的系統。
/// 支援三種視覺效果：圖片水平翻轉 (FlipX)、旋轉角度 (Rotate) 與圖片切換 (SpriteSwap)。
/// </summary>
public class LeverSystem : MonoBehaviour
{
    [Header("目標物體")]
    [Tooltip("要被解鎖掉落的巨石 Rigidbody (例如 rock-new)")]
    public Rigidbody targetRock;

    [Header("互動設定")]
    [Tooltip("觸發按鍵 (預設為 E 鍵，靠近時按下即可觸發)")]
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("是否靠近就直接觸發 (勾選後，玩家一碰到拉桿就自動拉下，不需按鍵)")]
    public bool triggerOnEnter = false;

    [Header("拉桿視覺效果")]
    [Tooltip("拉桿的 SpriteRenderer (若為空，會嘗試自動在自身或子物件尋找)")]
    public SpriteRenderer leverRenderer;
    [Tooltip("拉桿的 Animator (若有做拉桿動畫，可以拉入此處，並在 Animator 內建立名為 'Pull' 的 Trigger 參數)")]
    public Animator leverAnimator;

    public enum VisualEffectType
    {
        FlipSprite,     // 左右翻轉 (FlipX，最推薦：用一張圖就能做出左右扳動的效果)
        Rotate,         // 旋轉角度 (例如將 Transform Z軸 旋轉 -60 度)
        SpriteSwap,     // 更換圖片 (需要拖入拉動後的圖片)
        PlayAnimation   // 播放動畫 (透過 Animator 播放拉桿動畫)
    }
    [Tooltip("拉動拉桿時的視覺表現方式")]
    public VisualEffectType visualEffect = VisualEffectType.FlipSprite;

    [Tooltip("拉動後的圖片 (僅在視覺效果選為 SpriteSwap 時需要)")]
    public Sprite pulledSprite;
    
    [Tooltip("拉動後的旋轉角度偏移 (僅在視覺效果選為 Rotate 時需要)")]
    public Vector3 pulledRotationOffset = new Vector3(0, 0, -60f);

    [Header("音效設定 (選填)")]
    [Tooltip("拉動拉桿時播放的音效")]
    public AudioClip pullSound;

    private bool isPulled = false;
    private bool isPlayerInZone = false;
    private Sprite originalSprite;
    private Quaternion originalRotation;

    private void Start()
    {
        if (leverRenderer == null) leverRenderer = GetComponent<SpriteRenderer>();
        if (leverRenderer == null) leverRenderer = GetComponentInChildren<SpriteRenderer>();
        
        if (leverRenderer != null)
        {
            originalSprite = leverRenderer.sprite;
            originalRotation = leverRenderer.transform.localRotation;
        }

        // 初始狀態下，確保目標巨石是鎖定的 (Kinematic 鎖死，不受重力影響)
        if (targetRock != null)
        {
            targetRock.isKinematic = true;
        }
    }

    private void Update()
    {
        if (isPulled) return;

        // 如果玩家在互動區域內，且非碰觸即觸發，監聽按鍵
        if (isPlayerInZone && !triggerOnEnter)
        {
            if (Input.GetKeyDown(interactKey))
            {
                PullLever();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>() != null)
        {
            isPlayerInZone = true;
            if (triggerOnEnter && !isPulled)
            {
                PullLever();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>() != null)
        {
            isPlayerInZone = false;
        }
    }

    private void PullLever()
    {
        isPulled = true;
        Debug.Log($"【拉桿系統】拉桿 '{gameObject.name}' 已被拉下！");

        // 1. 解鎖巨石剛體使其掉落
        if (targetRock != null)
        {
            targetRock.isKinematic = false; // 解鎖 Kinematic，使其受重力自然墜落
            targetRock.linearVelocity = Vector3.zero; // 重置速度以防帶有初始衝量
            
            // 如果巨石有滾動程式，將其啟用
            RollingRockVisual rockVisual = targetRock.GetComponent<RollingRockVisual>();
            if (rockVisual != null)
            {
                rockVisual.enabled = true;
            }
            
            Debug.Log($"【拉桿系統】巨石 '{targetRock.gameObject.name}' 已成功解鎖並掉落！");
        }

        // 2. 執行拉桿視覺效果
        if (visualEffect == VisualEffectType.PlayAnimation && leverAnimator != null)
        {
            leverAnimator.SetTrigger("Pull"); // 觸發名為 'Pull' 的 Trigger 播放拉下動畫
            Debug.Log($"【拉桿系統】已向 '{leverAnimator.gameObject.name}' 的 Animator 發送 'Pull' 觸發信號！");
        }
        else if (leverRenderer != null)
        {
            switch (visualEffect)
            {
                case VisualEffectType.FlipSprite:
                    // 左右翻轉：拉桿圖片方向會倒換呈現
                    leverRenderer.flipX = !leverRenderer.flipX;
                    break;
                case VisualEffectType.Rotate:
                    // 轉動角度：繞 Z 軸轉動指定角度
                    leverRenderer.transform.localRotation = Quaternion.Euler(originalRotation.eulerAngles + pulledRotationOffset);
                    break;
                case VisualEffectType.SpriteSwap:
                    // 更換圖片：替換為拉動後的 Sprite
                    if (pulledSprite != null)
                    {
                        leverRenderer.sprite = pulledSprite;
                    }
                    break;
            }
        }

        // 3. 播放音效
        if (pullSound != null)
        {
            AudioSource.PlayClipAtPoint(pullSound, transform.position);
        }
    }
}
