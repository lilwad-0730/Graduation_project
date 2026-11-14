using UnityEngine;

/// <summary>
/// 控制 2D 狼的 Sprite 動態與動畫狀態切換。
/// 直接透過 C# 程式碼呼叫 Play/CrossFade 切換動畫，省去在 Animator 連線與設定參數的步驟。
/// </summary>
public class WolfSpriteAnimator : MonoBehaviour
{
    [Header("元件關聯")]
    [Tooltip("狼的 Animator 組件 (建議手動拖入有 Animator 的物件，若為空會自動搜尋)")]
    public Animator animator;
    
    [Tooltip("狼的 SpriteRenderer 組件 (建議手動拖入有 SpriteRenderer 的物件，若為空會自動搜尋)")]
    public SpriteRenderer spriteRenderer;

    [Header("動畫狀態名稱 (必須與 Animator Controller 中的 State 名字完全相同)")]
    [Tooltip("靜止/Idle 狀態的動畫名稱 (預設使用 backward 動作)")]
    public string idleStateName = "backward";

    [Tooltip("慢走狀態的動畫名稱 (預設使用 walking 動作)")]
    public string walkStateName = "walking";

    [Tooltip("奔跑狀態的動畫名稱 (預設使用 running 動作)")]
    public string runStateName = "running";

    [Tooltip("倒退走狀態的動畫名稱 (預設使用 backward 動作)")]
    public string backwardStateName = "backward";

    [Header("過渡時間")]
    [Tooltip("動畫切換時的平滑過渡時間 (秒，2D Sprite 建議設為 0 或極小如 0.02 以免殘影)")]
    public float crossFadeDuration = 0.02f;

    [Tooltip("除錯用：每次切換動畫狀態時在 Console 印一行。六隻狼同時跑會洗版，預設關閉")]
    public bool logStateChanges = false;

    [Header("★1114 動畫速度跟著移動速度（解決腳底打滑）")]
    [Tooltip("動畫播放速度依狼的實際水平速度調整：跑越快，腿擺越快")]
    public bool syncAnimToSpeed = true;
    [Tooltip("慢走動畫以原速播放時，腳掌著地往後滑的速度（米／秒）。由 sprite sheet 量測：約 1.1")]
    public float walkNaturalSpeed = 1.1f;
    [Tooltip("奔跑動畫以原速播放時的對應速度（米／秒）。由 sprite sheet 量測：約 1.65")]
    public float runNaturalSpeed = 1.65f;
    [Tooltip("倒退走動畫以原速播放時的對應速度（米／秒）。由 sprite sheet 量測：約 1.4")]
    public float backwardNaturalSpeed = 1.4f;
    [Tooltip("同步強度：1＝完全不打滑（高速時腿會擺很快）、0＝固定原速。0.85 是接近貼地又不會太亢奮")]
    [Range(0f, 1f)] public float footSyncStrength = 0.85f;
    [Tooltip("動畫速度下限／上限（倍）")]
    public float minAnimSpeed = 0.6f, maxAnimSpeed = 6f;
    [Tooltip("每隻狼動畫速度的隨機差異（±比例），狼群步伐不會整齊劃一")]
    [Range(0f, 0.2f)] public float animSpeedVariation = 0.06f;

    [Header("★1114 真正的待機（不再用倒退走當待機）")]
    [Tooltip("站著不動、咬住、被打暈時，停在一張站姿上（呼吸、拉扯、後彈等動態由 WolfMotionFX 疊加）")]
    public bool idleUsesStandingPose = true;
    [Tooltip("站姿取自哪個動畫狀態")]
    public string idlePoseState = "walking";
    [Tooltip("站姿在該動畫的位置（0～1）。慢走第 7 格（腳步一半、四腳都在地上）")]
    [Range(0f, 1f)] public float idlePoseNormalizedTime = 0.16f;

    [Header("★1114 動作切換的連貫性")]
    [Tooltip("新狀態要持續這麼久才真的切換（秒）。速度在門檻附近抖動時不會一直換圖閃爍；咬住、暈眩等事件仍立即切換")]
    public float stateSwitchDelay = 0.12f;
    [Tooltip("速度低於 0.1 要持續這麼久才進站姿待機（秒）。往前跑轉往後退時速度會經過 0，不會先閃一下站姿再倒退")]
    public float idleDelay = 0.25f;
    [Tooltip("換動作時，上一張圖留在殘影層上淡出的秒數（交叉溶解，取代 sprite 動畫無法淡入淡出的硬切）。0＝關閉")]
    public float crossDissolveSeconds = 0.15f;

    [Header("★1114 三張 sprite sheet 的色調與比例校正（以奔跑圖為準）")]
    [Tooltip("慢走、倒退走、站姿用的圖比奔跑圖亮約 9%：乘上這個亮度，切換時色調一致")]
    [Range(0.5f, 1.2f)] public float walkSheetBrightness = 0.915f;
    [Tooltip("走路圖的狼畫得比奔跑圖大約 3%：縮放到一致")]
    [Range(0.8f, 1.2f)] public float walkSheetScale = 0.97f;
    [Tooltip("走路圖的腳底比奔跑圖低：往上補（米，外觀物件父層座標），切換時腳底不會跳")]
    public float walkSheetYOffset = 0.029f;

    /// <summary>這一格 sprite 屬於哪張圖、要怎麼校正（奔跑圖不校正）。</summary>
    public void GetSheetCorrection(Sprite s, out float brightness, out float scale, out float yOffset)
    {
        bool run = s != null && s.texture != null && s.texture.name.Contains("running");
        brightness = run || s == null ? 1f : walkSheetBrightness;
        scale = run || s == null ? 1f : walkSheetScale;
        yOffset = run || s == null ? 0f : walkSheetYOffset;
    }

    private string _pendingState;
    private float _pendingTime, _lowSpeedTime;
    private SpriteRenderer _ghost;
    private float _ghostT = -1f, _ghostScale = 1f, _ghostYOffset;
    private WolfMotionFX _motion;

    /// <summary>目前的動畫狀態名稱（待機時是 idlePoseState 前加 "idle:"）。給 WolfMotionFX 讀步伐相位用。</summary>
    public string CurrentState => currentPlayingState;
    /// <summary>目前是否處於「站姿」（凍結的待機）。</summary>
    public bool IsIdlePose => currentPlayingState == IdleKey;
    private string IdleKey => "idle:" + idlePoseState;
    private float _speedVariation = 1f;

    [Header("轉向設定")]
    [Tooltip("如果您的狼原圖朝向是反的 (例如主角在右邊，狼卻看左邊)，請勾選此欄位進行反轉修正")]
    public bool reverseFacingDirection = false;

    [Header("★1031 紅線：狼不露眼、牙、嘴（整隻改成深色剪影）")]
    [Tooltip("開啟後，狼身上所有 SpriteRenderer 的顏色設成下面的剪影色：原圖的眼白、牙、嘴角亮點全部沉進剪影裡，近看也看不到。\n" +
             "繪本與命運之書裡的狼本來就是剪影，遊戲跟著一致。關掉＝回到原圖（有眼睛）。\n" +
             "只改顏色，不動貼圖與動畫；WolfSpawner 的淡入只改 alpha，剪影色會保留。")]
    public bool silhouetteTint = true;

    [Tooltip("剪影色。預設 #2A2D36（深灰藍）：廢墟是灰白底，剪影看得清楚又不是純黑。")]
    public Color silhouetteColor = new Color(0.165f, 0.176f, 0.212f, 1f);

    private Rigidbody rb;
    private WolfEnemy wolfEnemy;
    private Transform playerTransform;
    
    private string currentPlayingState = "";

    private void Start()
    {
        // 強化版組件搜尋：不論腳本掛載在父物件或子物件上，都能自動抓取所需組件
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody>();

        wolfEnemy = GetComponent<WolfEnemy>();
        if (wolfEnemy == null) wolfEnemy = GetComponentInParent<WolfEnemy>();
        if (wolfEnemy == null) wolfEnemy = GetComponentInChildren<WolfEnemy>();

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInParent<SpriteRenderer>();

        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null)
        {
            playerTransform = pObj.transform;
        }

        // ★1031 紅線：整隻狼改成剪影（直接設色，不用乘法：Spawner 複製模板狼時 Start 會再跑一次，乘法會越乘越黑）
        if (silhouetteTint)
        {
            Transform root = (wolfEnemy != null) ? wolfEnemy.transform : transform;
            SpriteRenderer[] all = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                float a = all[i].color.a;
                all[i].color = new Color(silhouetteColor.r, silhouetteColor.g, silhouetteColor.b, a);
            }
        }

        // 防呆警告
        if (rb == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 Rigidbody，無法取得移動速度！");
        if (wolfEnemy == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 WolfEnemy 腳本，無法讀取硬直與附著狀態！");
        if (animator == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 Animator，無法播放動畫！");
        if (spriteRenderer == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 SpriteRenderer，無法翻轉方向！");
    }

    private void Update()
    {
        if (animator == null || rb == null) return;

        // 1. 獲取物理數值與狀態
        float speedX = rb.linearVelocity.x;
        float absSpeedX = Mathf.Abs(speedX);

        // 安全讀取 WolfEnemy 的 private 變數
        bool isStunned = GetWolfEnemyBool("isStunned");
        bool isAttached = GetWolfEnemyBool("isAttached");

        bool isBackingUp = false;
        
        // 2. 轉向與倒退走判定：狼永遠保持朝向正 X 軸 (+X / 面朝右邊)
        float targetScaleX = reverseFacingDirection ? -1f : 1f;

        if (spriteRenderer != null)
        {
            if (spriteRenderer.transform != transform)
            {
                // 1. 如果 SpriteRenderer 在子物件上，固定為正 X 軸縮放
                Vector3 currentScale = spriteRenderer.transform.localScale;
                spriteRenderer.transform.localScale = new Vector3(targetScaleX * Mathf.Abs(currentScale.x), currentScale.y, currentScale.z);
            }
            else
            {
                // 2. 如果 SpriteRenderer 掛在父物件本體上，固定 flipX
                spriteRenderer.flipX = reverseFacingDirection;
            }
        }

        if (playerTransform != null && !isAttached && !isStunned)
        {
            float directionToPlayer = Mathf.Sign(playerTransform.position.x - transform.position.x);

            // 倒退走判定：當狼向左 (-X) 移動、或其速度與玩家方向相反時，觸發專用倒退動畫
            if (absSpeedX > 0.1f)
            {
                isBackingUp = (speedX < -0.1f) || (speedX * directionToPlayer < 0f);
            }
        }

        // 3. 用程式直接控制動畫狀態 (CrossFade)
        string targetState = idleStateName;

        if (isAttached)
        {
            targetState = idleStateName; // 咬住玩家時播放靜止/回頭
        }
        else if (isStunned)
        {
            targetState = idleStateName; // 硬直狀態播放靜止/回頭
        }
        else if (isBackingUp)
        {
            targetState = backwardStateName; // 播放倒退動畫
        }
        else
        {
            // 根據 X 軸速度大小決定播放哪種前進動畫
            if (absSpeedX < 0.1f)
            {
                targetState = idleStateName; // 速度低於 0.1 播放靜止
            }
            else if (absSpeedX > 4.5f)
            {
                targetState = runStateName; // 速度大於 4.5 播放快跑
            }
            else
            {
                targetState = walkStateName; // 速度低於 4.5 播放慢走
            }
        }

        // ★1114 待機（含咬住、暈眩）改成凍結在一張站姿，不再播倒退走
        if (idleUsesStandingPose && targetState == idleStateName && !isBackingUp) targetState = IdleKey;

        // ★1114 防抖：速度要低一陣子才進站姿；其他切換要持續 stateSwitchDelay 才算數。咬住／暈眩立即。
        bool urgent = isAttached || isStunned || string.IsNullOrEmpty(currentPlayingState);
        _lowSpeedTime = absSpeedX < 0.1f ? _lowSpeedTime + Time.deltaTime : 0f;
        if (!urgent && targetState == IdleKey && _lowSpeedTime < idleDelay && currentPlayingState != IdleKey)
            targetState = currentPlayingState;   // 還沒停夠久：維持原動作（例如往前跑轉往後退，中間不閃站姿）
        if (!urgent && targetState != currentPlayingState)
        {
            if (_pendingState != targetState) { _pendingState = targetState; _pendingTime = 0f; }
            _pendingTime += Time.deltaTime;
            if (_pendingTime < stateSwitchDelay) targetState = currentPlayingState;
        }
        else { _pendingState = null; _pendingTime = 0f; }

        // 4. 直接呼叫播放 (過濾重複播放)
        PlayAnimationDirectly(targetState);

        // 5. ★1114 動畫速度跟著實際速度
        if (targetState == IdleKey) animator.speed = 0f;
        else if (syncAnimToSpeed)
        {
            float natural = targetState == runStateName ? runNaturalSpeed
                          : targetState == backwardStateName ? backwardNaturalSpeed : walkNaturalSpeed;
            float ratio = absSpeedX / Mathf.Max(0.1f, natural);
            animator.speed = Mathf.Clamp(Mathf.Lerp(1f, ratio, footSyncStrength), minAnimSpeed, maxAnimSpeed) * _speedVariation;
        }
        else animator.speed = _speedVariation;
    }

    // ★0911：每隻狼自己的動畫起始相位（Awake 取一次，不是每幀取）。
    //   CrossFade / Play 預設都從 normalizedTime = 0 開始播，
    //   所以六隻狼同一瞬間進入 running 就會從同一格開始，看起來完全同步。
    //   給每隻狼一個固定的起始偏移，牠們的腳步就會錯開。
    private float _animPhaseOffset;

    private void Awake()
    {
        _animPhaseOffset = Random.Range(0f, 1f);   // normalizedTime 的單位是「循環的幾成」
        _speedVariation = 1f + Random.Range(-animSpeedVariation, animSpeedVariation);
    }

    private void PlayAnimationDirectly(string stateName)
    {
        if (currentPlayingState == stateName) return;

        string prev = currentPlayingState;
        currentPlayingState = stateName;
        if (!string.IsNullOrEmpty(prev)) BeginCrossDissolve();

        // ★1114 待機：凍結在站姿那一格
        if (stateName == IdleKey)
        {
            animator.Play(idlePoseState, 0, idlePoseNormalizedTime);
            animator.speed = 0f;
            if (logStateChanges) Debug.Log($"【程式碼控制動畫】'{gameObject.name}' 站姿待機");
            return;
        }

        // ★1114 走↔跑↔倒退之間切換：三段動畫都約兩個步伐，沿用目前的步伐位置接著播，腿不會閃一下。
        //   從待機或第一次播才用每隻狼自己的隨機相位（狼群不同步）。sprite 動畫無法淡入淡出，一律直接切。
        float start = _animPhaseOffset;
        bool fromMoving = !string.IsNullOrEmpty(prev) && prev != IdleKey;
        if (fromMoving)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            start = info.normalizedTime - Mathf.Floor(info.normalizedTime);
        }
        animator.Play(stateName, 0, start);

        // 六隻狼各自切狀態的話這行會洗版，預設關掉
        if (logStateChanges) Debug.Log($"【程式碼控制動畫】'{gameObject.name}' 已切換播放狀態為: '{stateName}'");
    }

    /// <summary>★1114 換動作的那一刻，把上一張圖放到殘影層上，之後 crossDissolveSeconds 秒內淡出（交叉溶解）。</summary>
    private void BeginCrossDissolve()
    {
        if (crossDissolveSeconds <= 0.001f || spriteRenderer == null || spriteRenderer.sprite == null) return;
        if (_ghost == null)
        {
            var go = new GameObject("WolfGhost");
            go.transform.SetParent(spriteRenderer.transform, false);
            _ghost = go.AddComponent<SpriteRenderer>();
            _ghost.sharedMaterial = spriteRenderer.sharedMaterial;
        }
        _ghost.sprite = spriteRenderer.sprite;
        _ghost.flipX = spriteRenderer.flipX;
        _ghost.flipY = spriteRenderer.flipY;
        _ghost.sortingLayerID = spriteRenderer.sortingLayerID;
        _ghost.sortingOrder = spriteRenderer.sortingOrder + 1;   // 舊圖疊在新圖上面慢慢淡掉
        GetSheetCorrection(spriteRenderer.sprite, out _, out _ghostScale, out _ghostYOffset);
        _ghost.enabled = true;
        _ghostT = 0f;
    }

    /// <summary>
    /// ★1114 顏色每幀依「這一格來自哪張圖」校正亮度（換圖同一幀就校正，不會有色調跳動），
    /// 再乘上每隻狼的深淺差異（WolfMotionFX）。剪影模式則一律用剪影色。透明度保留（Spawner 淡入只改 alpha）。
    /// </summary>
    private void LateUpdate()
    {
        if (spriteRenderer == null) return;
        if (_motion == null) _motion = GetComponent<WolfMotionFX>();
        float shade = _motion != null ? _motion.Shade : 1f;

        spriteRenderer.color = TintFor(spriteRenderer.sprite, shade, spriteRenderer.color.a);

        if (_ghost != null && _ghostT >= 0f)
        {
            _ghostT += Time.deltaTime;
            float u = Mathf.Clamp01(_ghostT / Mathf.Max(0.01f, crossDissolveSeconds));
            if (u >= 1f) { _ghost.enabled = false; _ghostT = -1f; }
            else
            {
                Color c = TintFor(_ghost.sprite, shade, spriteRenderer.color.a * (1f - u) * (1f - u));
                _ghost.color = c;
                _ghost.sortingOrder = spriteRenderer.sortingOrder + 1;
                // 殘影是外觀物件的子物件（跟著翻轉、起伏走），但外觀物件現在套的是新圖的比例：把舊圖自己的比例補回來
                GetSheetCorrection(spriteRenderer.sprite, out _, out float curScale, out float curY);
                float k = _ghostScale / Mathf.Max(0.01f, curScale);
                _ghost.transform.localScale = new Vector3(k, k, 1f);
                float sy = Mathf.Abs(spriteRenderer.transform.localScale.y) > 1e-4f ? spriteRenderer.transform.localScale.y : 1f;
                _ghost.transform.localPosition = new Vector3(0f, (_ghostYOffset - curY) / sy, 0f);
            }
        }
    }

    private Color TintFor(Sprite s, float shade, float alpha)
    {
        if (silhouetteTint) return new Color(silhouetteColor.r, silhouetteColor.g, silhouetteColor.b, alpha);
        GetSheetCorrection(s, out float b, out _, out _);
        float v = Mathf.Clamp01(b * shade);
        return new Color(v, v, v, alpha);
    }

    // 利用反射安全讀取 WolfEnemy 的 private bool 狀態
    private bool GetWolfEnemyBool(string fieldName)
    {
        if (wolfEnemy == null) return false;
        try
        {
            var field = typeof(WolfEnemy).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                return (bool)field.GetValue(wolfEnemy);
            }
        }
        catch {}
        return false;
    }
}
