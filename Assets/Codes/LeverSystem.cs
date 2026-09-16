using System.Collections;
using UnityEngine;

/// <summary>
/// 控制拉桿機關解鎖巨石掉落的系統。
/// 支援三種視覺效果：圖片水平翻轉 (FlipX)、旋轉角度 (Rotate) 與圖片切換 (SpriteSwap)。
/// 支援 IResettable：玩家重生時可完整復原拉桿與巨石初始鎖定狀態。
/// </summary>
public class LeverSystem : MonoBehaviour, IResettable
{
    [Header("目標物體")]
    [Tooltip("要被解鎖掉落的巨石 Rigidbody (例如 rock-new)")]
    public Rigidbody targetRock;

    [Header("互動設定")]
    [Tooltip("觸發按鍵 (預設為 E 鍵，靠近時按下即可觸發)")]
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("是否靠近就直接觸發 (勾選後，玩家一碰到拉桿就自動拉下，不需按鍵)")]
    public bool triggerOnEnter = false;

    [Header("光球路徑點解鎖設定")]
    [Tooltip("是否需要以光球程式 (GuidanceLight) 的路徑點作為解鎖條件")]
    public bool lockByGuidanceLight = true;

    [Tooltip("要監聽的光球物件 (GuidanceLight)；留空時會在遊戲開始時自動搜尋場景中的 GuidanceLight")]
    public GuidanceLight targetGuidanceLight;

    [Tooltip("解鎖拉桿需要完成幾個路徑點？(0 代表全部走完；若大於 0 則只需達到此點數即可解鎖，例如 6 代表光球走到第 6 個點就解鎖)")]
    public int requiredWaypointsCount = 0;

    [Tooltip("光球走完所有路徑點解鎖一次後，是否永久解鎖 (重生後不會再次被鎖住)")]
    public bool unlockOnceForever = true;

    [Tooltip("拉桿鎖住拉不動時播放的卡住悶響音效 (選填，留空會自動以較低音量播放拉動音效提示)")]
    public AudioClip stuckSound;

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

    [Header("🎵 音效設定 (選填)")]
    [Tooltip("拉動拉桿時播放的音效 (例如 拉桿（拉動）)")]
    public AudioClip pullSound;
    [Tooltip("拉桿到達底端鎖定完成時播放的音效 (例如 拉桿（完成拉動）)")]
    public AudioClip completedSound;
    [Range(0f, 1f)] public float soundVolume = 0.9f;

    private bool isPulled = false;
    private bool isPlayerInZone = false;
    private bool isUnlockedOnce = false;
    private Coroutine _stuckShakeCoroutine;
    private Sprite originalSprite;
    private Quaternion originalRotation;
    private Vector3 _rockInitialPosition;
    private Quaternion _rockInitialRotation;

    /// <summary>
    /// 拉桿當前是否處於鎖定狀態（以光球程式的 waypoints 為準）
    /// </summary>
    public bool IsLeverLocked
    {
        get
        {
            if (isPulled) return false;
            if (!lockByGuidanceLight) return false;
            if (unlockOnceForever && isUnlockedOnce) return false;

            // 以光球程式 (GuidanceLight) 內部的 waypoints 物件為準判定
            if (targetGuidanceLight != null)
            {
                int req = requiredWaypointsCount > 0 ? requiredWaypointsCount : targetGuidanceLight.TotalWaypointsCount;
                if (targetGuidanceLight.CurrentWaypointIndex >= req || targetGuidanceLight.IsAllWaypointsCompleted)
                {
                    isUnlockedOnce = true;
                    return false;
                }
                return true; // 光球尚未走完全部路徑點，拉桿鎖定中
            }

            return false;
        }
    }

    private void Start()
    {
        if (leverRenderer == null) leverRenderer = GetComponent<SpriteRenderer>();
        if (leverRenderer == null) leverRenderer = GetComponentInChildren<SpriteRenderer>();
        
        if (leverRenderer != null)
        {
            originalSprite = leverRenderer.sprite;
            originalRotation = leverRenderer.transform.localRotation;
        }

        // 自動搜尋場景中的光球程式 (GuidanceLight)
        if (lockByGuidanceLight && targetGuidanceLight == null)
        {
            targetGuidanceLight = FindFirstObjectByType<GuidanceLight>();
            if (targetGuidanceLight != null)
            {
                Debug.Log($"【拉桿系統】已自動鎖定光球程式：'{targetGuidanceLight.gameObject.name}' (共有 {targetGuidanceLight.TotalWaypointsCount} 個路徑點)");
            }
            else
            {
                Debug.LogWarning("【拉桿系統】場景中未找到任何 GuidanceLight，拉桿將不被光球鎖定。");
            }
        }

        // 初始狀態下，確保目標巨石是鎖定的 (Kinematic 鎖死，不受重力影響)
        if (targetRock != null)
        {
            _rockInitialPosition = targetRock.transform.position;
            _rockInitialRotation = targetRock.transform.rotation;
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
                if (IsLeverLocked)
                {
                    NotifyLeverStuck();
                }
                else
                {
                    PullLever();
                }
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
                if (IsLeverLocked)
                {
                    NotifyLeverStuck();
                }
                else
                {
                    PullLever();
                }
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

    /// <summary>
    /// 光球未走完所有路徑點時按拉桿的回饋：晃動拉桿＋播放提示音效
    /// </summary>
    private void NotifyLeverStuck()
    {
        // 1. 播放卡住音效
        if (stuckSound != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFXAt(stuckSound, transform.position, soundVolume);
        }
        else if (pullSound != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFXAt(pullSound, transform.position, soundVolume * 0.4f);
        }

        // 2. 晃動拉桿視覺回饋 (Shake)
        if (_stuckShakeCoroutine != null) StopCoroutine(_stuckShakeCoroutine);
        _stuckShakeCoroutine = StartCoroutine(ShakeRoutine());

        // 3. Console 提示進度
        int current = targetGuidanceLight != null ? targetGuidanceLight.CurrentWaypointIndex : 0;
        int total = targetGuidanceLight != null ? targetGuidanceLight.TotalWaypointsCount : 0;
        int req = requiredWaypointsCount > 0 ? requiredWaypointsCount : total;
        Debug.Log($"【拉桿系統】拉桿尚未解鎖！需要光球程式前進至路徑點 (目前進度: {current}/{req})");
    }

    private IEnumerator ShakeRoutine()
    {
        Transform t = leverRenderer != null ? leverRenderer.transform : transform;
        Quaternion baseRot = originalRotation;
        float dur = 0.35f, el = 0f;
        while (el < dur)
        {
            el += Time.deltaTime;
            float p = el / dur;
            float ang = Mathf.Sin(p * Mathf.PI * 5f) * 4f * (1f - p);
            t.localRotation = baseRot * Quaternion.Euler(0f, 0f, ang);
            yield return null;
        }
        t.localRotation = baseRot;
        _stuckShakeCoroutine = null;
    }

    private void PullLever()
    {
        isPulled = true;
        Debug.Log($"【拉桿系統】拉桿 '{gameObject.name}' 已被拉下！");

        // 1. 執行拉桿視覺效果
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

        // 2. 播放拉桿音效
        if (pullSound != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFXAt(pullSound, transform.position, soundVolume);
        }

        if (completedSound != null)
        {
            StartCoroutine(PlayCompletedSoundRoutine());
        }

        // 3. 巨石掉落
        if (targetRock != null)
        {
            UnlockRock();
        }
    }

    private void UnlockRock()
    {
        if (targetRock != null)
        {
            targetRock.isKinematic = false;
            targetRock.linearVelocity = Vector3.zero;
            
            RollingRockVisual rockVisual = targetRock.GetComponent<RollingRockVisual>();
            if (rockVisual != null)
            {
                rockVisual.enabled = true;
            }
            
            Debug.Log($"【拉桿系統】巨石 '{targetRock.gameObject.name}' 已成功解鎖並掉落！");
        }
    }

    private IEnumerator PlayCompletedSoundRoutine()
    {
        yield return new WaitForSeconds(0.28f);
        if (AudioManager.Instance != null && completedSound != null)
        {
            AudioManager.Instance.PlaySFXAt(completedSound, transform.position, soundVolume);
        }
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        _stuckShakeCoroutine = null;

        isPulled = false;
        isPlayerInZone = false;
        if (!unlockOnceForever)
        {
            isUnlockedOnce = false;
        }

        if (targetRock != null)
        {
            targetRock.isKinematic = true;
            targetRock.linearVelocity = Vector3.zero;
            targetRock.angularVelocity = Vector3.zero;
            if (_rockInitialPosition != Vector3.zero)
            {
                targetRock.transform.position = _rockInitialPosition;
                targetRock.transform.rotation = _rockInitialRotation;
            }
        }

        if (leverRenderer != null)
        {
            leverRenderer.flipX = false;
            leverRenderer.transform.localRotation = originalRotation;
            if (originalSprite != null)
            {
                leverRenderer.sprite = originalSprite;
            }
        }

        if (leverAnimator != null)
        {
            leverAnimator.Rebind();
            leverAnimator.Update(0f);
        }
    }
}
