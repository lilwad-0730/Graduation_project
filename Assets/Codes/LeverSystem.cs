using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// 控制拉桿機關解鎖巨石掉落的系統。
/// 支援拉下時奪取鏡頭控制權跟隨巨石墜落、特寫石牆碎裂並平滑回歸主角。
/// 支援三種視覺效果：圖片水平翻轉 (FlipX)、旋轉角度 (Rotate) 與圖片切換 (SpriteSwap)。
/// 支援 IResettable：玩家重生時可完整復原拉桿與巨石初始鎖定狀態及鏡頭。
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

    [Header("🎬 鏡頭特寫演出 (Camera Cutscene)")]
    [Tooltip("是否在拉下拉桿時奪取鏡頭控制權，特寫跟隨巨石掉落與砸碎石牆")]
    public bool enableCameraCutscene = true;

    [Header("🛡️ 場景背景邊界安全鎖定 (Viewport Bounds Clamping)")]
    [Tooltip("鏡頭允許跟隨的最高 Y 座標 (防止鏡頭往上衝破天空露出背景外邊界，預設 -116f)")]
    public float maxCameraY = -116.0f;

    [Tooltip("鏡頭允許跟隨的最低 Y 座標 (確保巨石落地砸牆時鏡頭能完整觸地特寫，預設 -127.5f)")]
    public float minCameraY = -127.5f;

    [Tooltip("鏡頭允許移動的最左 X 座標 (預設 95f)")]
    public float minCameraX = 95.0f;

    [Tooltip("鏡頭允許移動的最右 X 座標 (預設 118f)")]
    public float maxCameraX = 118.0f;

    [Header("⏱️ 演出時長設定")]
    [Tooltip("拉下瞬間，鏡頭 X 軸快速平移對齊巨石中央的時間 (秒)")]
    public float panToRockXDuration = 0.35f;

    [Tooltip("目標被砸碎的石牆 (例如 rock wall_0 上的 Destructible / RuinsDoor，留空會自動搜尋)")]
    public Destructible targetWall;

    [Tooltip("石牆碎裂後，鏡頭停留特寫的時間 (秒)")]
    public float holdAfterShatterDuration = 1.0f;

    [Tooltip("鏡頭由石牆平滑回歸主角的時間 (秒)")]
    public float panBackToPlayerDuration = 0.8f;

    [Tooltip("鏡頭跟隨巨石的最長超時保護時間 (秒)")]
    public float maxCutsceneTimeout = 6.0f;

    [Tooltip("鏡頭中心偏移 (可用於微調巨石在畫面中的中心點，預設 (0,0,0) 正中央)")]
    public Vector3 cameraOffset = Vector3.zero;

    private bool isPulled = false;
    private bool isPlayerInZone = false;
    private Sprite originalSprite;
    private Quaternion originalRotation;
    private Vector3 _rockInitialPosition;
    private Quaternion _rockInitialRotation;
    private Coroutine _cutsceneCoroutine;
    private GameObject _dummyCameraTarget;
    private PlayerMovement _cachedPlayer;

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
            _rockInitialPosition = targetRock.transform.position;
            _rockInitialRotation = targetRock.transform.rotation;
            targetRock.isKinematic = true;
        }

        // 自動搜尋目標石牆 (若未指定)
        if (targetWall == null && targetRock != null)
        {
            FindTargetWall();
        }
    }

    private void FindTargetWall()
    {
        var ruinsDoors = Object.FindObjectsByType<RuinsDoor>(FindObjectsSortMode.None);
        foreach (var door in ruinsDoors)
        {
            if (door.specificDestructionObject == targetRock.gameObject ||
                door.specificDestructionObject == null)
            {
                Destructible dest = door.GetComponent<Destructible>();
                if (dest != null)
                {
                    targetWall = dest;
                    break;
                }
            }
        }

        if (targetWall == null)
        {
            var destructibles = Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None);
            foreach (var dest in destructibles)
            {
                if (dest.name.ToLower().Contains("wall"))
                {
                    targetWall = dest;
                    break;
                }
            }
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

        // 3. 巨石掉落與鏡頭演出
        if (targetRock != null)
        {
            if (enableCameraCutscene)
            {
                if (_cutsceneCoroutine != null) StopCoroutine(_cutsceneCoroutine);
                _cutsceneCoroutine = StartCoroutine(RockDropCameraCutsceneRoutine());
            }
            else
            {
                UnlockRock();
            }
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

    private IEnumerator RockDropCameraCutsceneRoutine()
    {
        // 1. 尋找主角並鎖定操作
        _cachedPlayer = Object.FindFirstObjectByType<PlayerMovement>();
        Transform playerTrans = _cachedPlayer != null ? _cachedPlayer.transform : GameObject.FindGameObjectWithTag("Player")?.transform;

        if (_cachedPlayer != null)
        {
            _cachedPlayer.isCutsceneFrozen = true;
            Rigidbody prb = _cachedPlayer.GetComponent<Rigidbody>();
            if (prb != null)
            {
                prb.linearVelocity = new Vector3(0f, prb.linearVelocity.y, 0f);
            }
        }

        // 旁路全域限制器，完全由本腳本精準的 maxCameraY / minCameraY 與 minCameraX / maxCameraX 掌控
        CinemachineCameraConfiner3D.isBypassed = true;
        SimpleCameraBounds.isBypassed = true;

        if (targetWall == null) FindTargetWall();

        Vector3 playerPos = playerTrans != null ? playerTrans.position : transform.position;

        // 2. 建立平滑過渡 Dummy Target，初始設於主角位置
        if (_dummyCameraTarget == null)
        {
            _dummyCameraTarget = new GameObject("[Lever_RockCameraTarget]");
        }
        _dummyCameraTarget.transform.position = playerPos;

        SetCinemachineTarget(_dummyCameraTarget.transform);

        // 3. 解鎖巨石剛體使其受重力掉落
        UnlockRock();

        // 4. 第一階段：拉下瞬間，鏡頭 X 軸快速平移對齊巨石中央 (X軸置中)，而 Y 軸維持在場景邊界上限 (maxCameraY) 絕不衝破天空
        Vector3 rockInitialPos = targetRock != null ? targetRock.transform.position : playerPos;
        float initialClampedX = Mathf.Clamp(rockInitialPos.x + cameraOffset.x, minCameraX, maxCameraX);
        float initialClampedY = Mathf.Clamp(rockInitialPos.y + cameraOffset.y, minCameraY, maxCameraY);
        Vector3 startAlignPos = _dummyCameraTarget.transform.position;
        Vector3 targetAlignPos = new Vector3(initialClampedX, initialClampedY, startAlignPos.z);

        float panTimer = 0f;
        while (panTimer < panToRockXDuration)
        {
            panTimer += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, panTimer / panToRockXDuration);
            _dummyCameraTarget.transform.position = Vector3.Lerp(startAlignPos, targetAlignPos, t);
            yield return null;
        }

        // 5. 第二階段：動態追隨巨石（X 軸維持鎖定巨石中央，Y 軸在巨石掉落進入場景後一路向下跟隨至最低點 minCameraY 完整觸地）
        bool wallShattered = false;
        System.Action onShatterHandler = () => { wallShattered = true; };
        if (targetWall != null)
        {
            targetWall.OnShattered += onShatterHandler;
            if (targetWall.HasShattered) wallShattered = true;
        }

        float trackingTimer = 0f;
        while (!wallShattered && trackingTimer < maxCutsceneTimeout)
        {
            trackingTimer += Time.deltaTime;

            if (targetWall != null && targetWall.HasShattered)
            {
                wallShattered = true;
                break;
            }

            if (targetRock != null)
            {
                // 嚴格夾緊在場景背景邊界內：
                // 巨石在上空時，Y 軸卡在 maxCameraY (場景頂部不露底)
                // 巨石下落時，Y 軸一路跟隨直到 minCameraY (觸地砸牆特寫)
                float clampedX = Mathf.Clamp(targetRock.transform.position.x + cameraOffset.x, minCameraX, maxCameraX);
                float clampedY = Mathf.Clamp(targetRock.transform.position.y + cameraOffset.y, minCameraY, maxCameraY);
                Vector3 targetFollowPos = new Vector3(clampedX, clampedY, _dummyCameraTarget.transform.position.z);

                _dummyCameraTarget.transform.position = Vector3.Lerp(
                    _dummyCameraTarget.transform.position,
                    targetFollowPos,
                    Time.deltaTime * 14f
                );
            }

            yield return null;
        }

        if (targetWall != null)
        {
            targetWall.OnShattered -= onShatterHandler;
        }

        // 6. 石牆碎裂後特寫停留
        if (holdAfterShatterDuration > 0f)
        {
            yield return new WaitForSeconds(holdAfterShatterDuration);
        }

        // 7. 鏡頭平滑拉回主角
        Vector3 holdEndPos = _dummyCameraTarget.transform.position;
        float returnTimer = 0f;
        while (returnTimer < panBackToPlayerDuration)
        {
            returnTimer += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, returnTimer / panBackToPlayerDuration);
            Vector3 curPlayerPos = playerTrans != null ? playerTrans.position : playerPos;
            _dummyCameraTarget.transform.position = Vector3.Lerp(holdEndPos, curPlayerPos, t);
            yield return null;
        }

        // 8. 還原鏡頭跟隨目標給主角並解除鎖定與邊界旁路
        SetCinemachineTarget(playerTrans);

        if (_dummyCameraTarget != null)
        {
            Destroy(_dummyCameraTarget);
            _dummyCameraTarget = null;
        }

        CinemachineCameraConfiner3D.isBypassed = false;
        SimpleCameraBounds.isBypassed = false;

        if (_cachedPlayer != null)
        {
            _cachedPlayer.isCutsceneFrozen = false;
        }

        _cutsceneCoroutine = null;
        Debug.Log("✨【拉桿系統】巨石砸牆鏡頭演出結束，控制權與鏡頭已順利回歸主角！");
    }

    private void SetCinemachineTarget(Transform target)
    {
        if (target == null) return;

        // Unity 6 Cinemachine 3.x
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

        // Legacy CinemachineVirtualCamera
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

    private System.Collections.IEnumerator PlayCompletedSoundRoutine()
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
        _cutsceneCoroutine = null;

        if (_dummyCameraTarget != null)
        {
            Destroy(_dummyCameraTarget);
            _dummyCameraTarget = null;
        }

        CinemachineCameraConfiner3D.customTarget = null;
        SimpleCameraBounds.customTarget = null;
        CinemachineCameraConfiner3D.isBypassed = false;
        SimpleCameraBounds.isBypassed = false;

        if (_cachedPlayer != null)
        {
            _cachedPlayer.isCutsceneFrozen = false;
            SetCinemachineTarget(_cachedPlayer.transform);
        }
        else
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) SetCinemachineTarget(p.transform);
        }

        isPulled = false;
        isPlayerInZone = false;

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

    private void OnDrawGizmosSelected()
    {
        // 在編輯器中綠色框線繪製鏡頭移動與 Clamp 安全範圍
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
        Vector3 center = new Vector3((minCameraX + maxCameraX) * 0.5f, (minCameraY + maxCameraY) * 0.5f, transform.position.z);
        Vector3 size = new Vector3(Mathf.Abs(maxCameraX - minCameraX), Mathf.Abs(maxCameraY - minCameraY), 1f);
        Gizmos.DrawWireCube(center, size);
    }
}
