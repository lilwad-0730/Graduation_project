using UnityEngine;
using Unity.Cinemachine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("基本移動設定")]
    public float baseSpeed = 5f;       
    [Tooltip("抓取/拉動物件 (LeftShift) 的偵測距離 (可在 Inspector 自由調整)")]
    public float pullRange = 2.5f;
    [Tooltip("往後拉動物件時的速度倍率：數值越小拉得越慢、物件感覺越重（1 = 不額外減速）")]
    [Range(0.1f, 1f)]
    public float pullSpeedMultiplier = 0.65f;
    [Tooltip("跳躍高度：數值越大跳越高（預設 5）")]
    public float jumpForce = 5f;
    
    private Rigidbody rb;
    private GameObject pulledObject;
    private Vector3 facingDirection = Vector3.right; 
    public Vector3 FacingDirection => facingDirection;

    [Header("動畫控制")]
    [Tooltip("請直接把有 PlayerAnimator Controller 的模型子物件拖曳到這裡！")]
    public Animator animator; 
    public float CurrentMoveInput { get; private set; }
    public float BaseSpeed => baseSpeed;
    /// <summary>★0917 唯讀：這一幀腳下地面的坡度（度，沒踩地＝0）。給狼的追擊速度決策對照玩家實際移動狀態用，不影響玩家自己。</summary>
    public float GroundSlopeAngle { get; private set; }
    /// <summary>★0920 唯讀：這一幀腳下地面的法線（沒踩地＝Vector3.up）。只給視覺貼坡用。</summary>
    public Vector3 GroundNormal { get; private set; } = Vector3.up;

    [Header("★0920 斜坡視覺貼合（只轉外觀，剛體不轉）")]
    [Tooltip("站在斜坡上時，讓角色的外觀跟著坡面傾斜。關掉就是原本永遠正立的樣子")]
    public bool enableSlopeVisualTilt = true;
    [Tooltip("視覺最多傾斜幾度（實際坡度超過也只會傾到這個角度）")]
    [Range(0f, 45f)] public float maxSlopeVisualAngle = 30f;
    [Tooltip("傾斜與回正的平滑速度，越大越快跟上坡面")]
    [Range(1f, 30f)] public float slopeVisualTiltSpeed = 10f;
    private float _slopeVisualAngle;   // 目前已套用的視覺傾角（度）

    [Tooltip("★1018 上坡推石時，保留多少比例的「跟著坡面傾斜」。1＝照舊；0＝推石時身體站直（推石動畫本身就會前傾）。\n" +
             "原因：坡面往右上升時，跟著坡面傾斜 = 頭往左（遠離石頭）倒最多 30°，3.4 米高的身體頭部離石頭多出約 1.7 米，是推坡時「石頭離身體很遠」的主要來源之一。")]
    [Range(0f, 1f)] public float pushingSlopeTiltKeep = 0.2f;

    [Tooltip("★1001 斜坡視覺空隙補償：0 ＝ 完全關閉，1 ＝ 完整幾何補償（預設）。\n" +
             "成因是純幾何、不是物理穿透：剛體鎖了 FreezeRotation，碰撞盒永遠保持水平，\n" +
             "放在斜坡上時只有下坡側的底角碰得到坡面，底面中心必然懸空「半寬 × tan(坡度)」。\n" +
             "廢墟山坡 34.648°、玩家世界半寬 1.064 → 空隙約 0.736 公尺（角色身高的 21%）。\n" +
             "這裡只把外觀子物件往下移該補償量，碰撞盒／剛體／地面偵測一律不動；\n" +
             "平地、跳躍、離地、水下、坡度過小一律為 0，並沿用 Slope Visual Tilt Speed 平滑。\n" +
             "（狼的 WolfEnemy.slopeVisualYOffset 是同一個問題的既有解法，這裡沿用同一個模式）")]
    [Range(0f, 1f)] public float slopeVisualGapFactor = 1f;

    // 外觀子物件與它的原始 local Y（在 Start 快取，補償量以此為基準往下疊）
    private Transform _visualRoot;
    private float _visualBaseLocalY;
    private bool _hasVisualBase = false;

    private Collider playerCollider;
    private string currentAnimState = ""; 
    public bool isGrounded = false;
    private bool isJumping = false;
    
    private float initialZ;
    
    [Header("最高層級防破圖與鎖死系統")]
    [HideInInspector] public bool isStrictLockingX = false;
    [HideInInspector] public float lockedXValue = 0f;
    private Vector3 _lastFramePos;

    // FallingBackground 防重複觸發 flag
    private bool _fallingBGEntered = false;

    [Header("廢墟墜落落地鎖定")]
    [Tooltip("進入廢墟背景後，直到玩家真正落到此 Y 以下的地板才恢復操作。")]
    public float ruinsLandingUnlockY = -85f;

    [Tooltip("玩家進入廢墟背景時，鏡頭暫時特寫到這個名稱的光球/路徑點。")]
    public string ruinsIntroLightName = "P12";

    [Tooltip("廢墟第一光球特寫的 Orthographic Size。數字越小越近。")]
    public float ruinsIntroCloseupLensSize = 10f;

    private bool _waitingForRuinsLanding = false;
    private bool _ruinsIntroLightSynced = false;
    private bool _ruinsIntroCloseupStarted = false;
    private Coroutine _ruinsIntroCameraCoroutine;
    private Coroutine _ruinsIntroLightGuardCoroutine;


    [Header("狼群減速狀態 (可調整)")]
    [Tooltip("幾隻狼能讓玩家完全停下（同時也是死亡門檻）。0910 企劃定案：6。\n" +
             "減速是自動按比例算的：速度 = baseSpeed × (1 − 咬住數 / maxWolvesToStop)\n" +
             "  設 6 → 1隻 83%、2隻 67%、3隻 50%、4隻 33%、5隻 17%、6隻 0%\n" +
             "  設 4 → 1隻 75%、2隻 50%、3隻 25%、4隻 0%\n" +
             "改這一個數字就好，不用改程式，也沒有第二個要同步的參數")]
    [Range(1f, 12f)]
    public float maxWolvesToStop = 6f;

    [Tooltip("咬住數達到 Wolves To Respawn 時是否觸發重生。關掉的話玩家只會被減速，不會死")]
    public bool respawnWhenMaxWolves = true;

    [Tooltip("★1001 咬住幾隻狼就觸發重生（可自由調整）。\n" +
             "刻意與 maxWolvesToStop 分開成兩個數字，因為它們是兩件事：\n" +
             "  ・maxWolvesToStop  ＝ 減速曲線的分母（速度 = baseSpeed × (1 − 咬住數 / 它)），維持 6 不動\n" +
             "  ・wolvesToRespawn  ＝ 死亡門檻\n" +
             "原本兩者共用 maxWolvesToStop = 6，但場上最多只會同時存在 5 隻狼\n" +
             "（場景那隻 WOLF 是 5 個 WolfSpawner 的複製模板，開場就被 SetActive(false)），\n" +
             "所以 6 這個門檻永遠達不到＝狼永遠咬不死人。拆開之後門檻才真的會成立。")]
    [Range(1, 12)]
    public int wolvesToRespawn = 3;

    [Tooltip("★1008 狼群全部咬上來也算死：在場的狼（已出場、正在追或咬住）比 Wolves To Respawn 少的時候，改用在場的狼數當門檻，最少 Pack Kill Min Wolves 隻。\n" +
             "拉桿前只有兩隻狼，原本永遠咬不死；開著的話兩隻都咬上來就重生（廢墟第一次會播重生漫畫）。\n" +
             "推巨石時被第一口咬到仍由 BoulderChallengeController 接手，跟這個無關。\n" +
             "★1010 預設關：02 #35 定案「狼咬三次就死」，這個做法作廢（修毅 10-10 已把 wolvesToRespawn 改成 3）；打勾才回到 10-08 的做法")]
    public bool packKillWhenAllBite = false;
    [Tooltip("★1008 狼群全部咬上來才算死的最少隻數：一隻狼單獨咬住只減速，不死")]
    [Range(1, 6)]
    public int packKillMinWolves = 2;

    [Tooltip("★1010 02 #35「狼咬三次就死」：這一條命累計被咬幾口（咬完鬆口也算）達到 Wolves To Respawn 就死，重生後歸零。\n" +
             "狼現在咬一口就退後（WolfEnemy.biteAndRetreat），不會一直掛在身上，所以要算累計口數。取消＝回到「同時咬住幾隻才死」")]
    public bool countBitesCumulative = true;

    /// <summary>★1010 這一條命累計被咬了幾口（重生、傳送 WarpTo 歸零）。</summary>
    public int BitesTaken { get; private set; }

    /// <summary>★1010 判斷「咬滿」用的數：累計口數（countBitesCumulative）或同時咬住的隻數。FirstWolfDeathStory 也用這個。</summary>
    public int WolfBiteCountForDeath => countBitesCumulative ? BitesTaken : attachedWolvesCount;

    [Header("🐺 狼咬住的向下拖曳負載（只作用在斜坡上）")]
    [Tooltip("每一隻咬住的狼，沿坡面往下拖曳的速度 (公尺/秒)。設 0 ＝ 整個機制關閉，行為完全回到修改前。\n" +
             "為什麼不用 AddForce：斜坡分支會關掉重力並且每幀整個覆寫 rb.linearVelocity，\n" +
             "任何加在剛體上的力都會被下一次 Update 抹掉，所以改成直接參與坡面速度合成。")]
    [Range(0f, 3f)]
    public float wolfDragDownPerWolf = 0.6f;

    [Tooltip("所有咬住的狼加起來的拖曳速度上限 (公尺/秒)。預設 1.8 ≒ 三隻狼的量，之後再多咬也不會更強")]
    [Range(0f, 6f)]
    public float wolfDragDownMax = 1.8f;

    [Tooltip("保護上限：拖曳速度最多只能吃掉玩家「當下沿坡速度」的這個比例。\n" +
             "0.5 ＝ 永遠保留一半的爬坡能力，數學上保證她不會被狼拖著倒退下坡。\n" +
             "注意：這只保證玩家自己還在往上走，不等於巨石一定推得上去（巨石有自己的目標速度邏輯）")]
    [Range(0f, 0.9f)]
    public float wolfDragDownMaxSpeedFraction = 0.5f;

    /// <summary>★1001 唯讀診斷：這一幀實際套用的沿坡向下拖曳速度（不在斜坡上＝0）。</summary>
    public float WolfDragDownSpeed { get; private set; }

    /// <summary>
    /// ★1001 唯讀：這一幀 PlayerMovement 實際寫進剛體的「世界水平速度」（指令值）。
    ///
    /// 這是唯一同時反映下列所有因素的數字，所以也是外部要「跟著玩家目前有效速度」時該用的來源：
    ///   ・狼咬住的等比減速（currentSpeed＝baseSpeed × (1 − 咬住數 / maxWolvesToStop)）
    ///   ・狼咬住的沿坡向下拖曳（WolfDragDownSpeed）
    ///   ・斜坡把沿坡速度折算成世界水平的 cos(坡度)
    ///   ・逆風推力、水下速度倍率、被拉動物件的重量折扣
    ///
    /// 注意不要改用 baseSpeed 或 currentSpeed：
    ///   baseSpeed    固定 6，完全不理任何減速
    ///   currentSpeed 只含「咬住幾隻狼」的等比減速，不含拖曳、不含坡度折算、不含風
    /// 移動平台的速度刻意不計入——那是平台載著她走，不是她自己的推進力。
    /// </summary>
    public float CommandedHorizontalSpeed { get; private set; }
    
    [Header("觀察用 (不要手動改)")]
    public int attachedWolvesCount = 0; 
    [HideInInspector] public float currentSpeed;      
    [HideInInspector] public bool freezeHorizontal = false;
    [HideInInspector] public bool isCutsceneFrozen = false; // 用於劇情鎖定 (例如光絮移動時)

    private float _lastGroundContactTime = -999f;
    private Vector3 _lastGroundContactNormal = Vector3.up;
    private Vector3 _lastGroundContactPoint = Vector3.zero;
    private const float GroundContactGraceSeconds = 0.12f;

    private static float _hardLockSince = -1f;

    /// <summary>安全網：演出旗標若卡住超過這麼久，強制放開，免得玩家永遠不能動。</summary>
    public const float HardLockSafetySeconds = 45f;

    /// <summary>
    /// 全域演出鎖定狀態查詢 (包含重生、鏡牆演出、過場文字卡、黑影怪物特寫等所有相機被接管的情境)
    /// </summary>
    public static bool IsHardCutsceneLocked
    {
        get
        {
            bool locked =
                PlayerRespawnSystem.IsAnyRespawning
                || MirrorWallAbsorbCutscene.IsAnyCutsceneRunning
                || ShadowMonsterController.IsAnyRevealRunning
                || (StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying);

            if (!locked) { _hardLockSince = -1f; return false; }

            // ★因為拿掉了「按鍵就自動解鎖」的逃生門，這裡補一個時間上限。
            //   任何演出旗標忘了關（協程被 StopCoroutine 砍掉之類），
            //   最多鎖 45 秒就放人。正常的卡片與運鏡都遠短於這個。
            if (_hardLockSince < 0f) _hardLockSince = Time.unscaledTime;
            if (Time.unscaledTime - _hardLockSince > HardLockSafetySeconds) return false;

            return true;
        }
    }

    public static bool IsAnyCutsceneActive => IsHardCutsceneLocked;

    /// <summary>
    /// 當前主角是否處於完全鎖定操作狀態
    /// </summary>
    public bool IsControlLocked =>
        _candleLightLocked ||
        isCutsceneFrozen ||
        freezeHorizontal ||
        IsHardCutsceneLocked;

    [Header("📦 推動物件機制 (Pushing Object System)")]
    [Tooltip("推動物件偵測距離 (向前檢測 Pushable Tag 物件)")]
    public float pushDetectionDistance = 0.35f;

    [Tooltip("除錯用：被風推卻推不動時，在 Console 印出診斷訊息 (確認防抖判定有沒有生效)")]
    public bool logWindBlockDebug = false;

    [HideInInspector] public bool isPushing = false;
    private float _collisionPushableTimer = 0f;

    [Header("🕯️ 點燃燭火動作")]
    [Tooltip("PlayerAnimator 裡點燃動作的 State 名稱")]
    public string lightCandleAnimState = "LightCandle";
    [Tooltip("點燃動作統一在幾秒內播完（會自動加速）；點燃時間比這長的話，多出來的時間停在最後一格")]
    public float lightAnimPlaySeconds = 1.5f;
    [Tooltip("點燃時身體往鏡頭轉的角度（面向右時是順時針）。只在沒勾 aimLightHandAtCandle 時使用")]
    public float lightAnimYawOffset = 15f;
    [Tooltip("點燃時轉向燭台的速度（度/秒）。300 ≈ 轉 180 度約 0.6 秒，數字越小轉越慢")]
    public float lightTurnDegreesPerSecond = 300f;
    private bool _candleLightAnim = false;
    private bool _candleLightHold = false;
    private bool _candleLightLocked = false;   // 點燃中鎖住移動，點完或中斷才放開
    private const string LightCandleSpeedParam = "LightCandleSpeed";

    [Header("🕯️ 點燃轉向：讓點火的手最靠近燭火")]
    [Tooltip("勾選＝以燭火為圓心、玩家到燭火的距離為半徑，算出讓手最靠近燭火的轉向角度；不勾＝只左右轉向燭台再加上面的固定角度")]
    public bool aimLightHandAtCandle = true;
    [Tooltip("點火的手（指尖）")]
    public HumanBodyBones lightHandBone = HumanBodyBones.LeftIndexDistal;
    [Tooltip("點燃動作最後一格時，手相對外觀模型的位置（模型自己的座標）。第一次點完會用實際骨頭位置自動校正")]
    public Vector3 lightHandLocalOffset = new Vector3(-0.157f, 0.521f, 0.684f);
    [Tooltip("瞄準時燭火深度（Z）的額外位移。0＝用燭台在 3D 世界裡的實際深度：玩家越接近站在鏡頭和燭台之間，就越轉身背對鏡頭去點")]
    public float candleAimDepthOffset = 0f;
    [Tooltip("好幾個角度一樣近時（差距在這個距離內），挑身體最面向燭台的那個，讓動作合常理")]
    public float lightAimFacingTieTolerance = 0.05f;
    private bool _hasCandleAimYaw = false;
    private float _candleAimYaw = 0f;
    private bool _lightHandCalibrated = false;

    /// <summary>開始播點燃動作（不循環，播完停在最後一格），並轉向燭火。flamePos＝燭火位置。</summary>
    public void BeginCandleLightAnim(float duration, Vector3 flamePos)
    {
        _candleLightLocked = true;
        if (animator == null) return;
        float dx = flamePos.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.05f) facingDirection = dx > 0f ? Vector3.right : Vector3.left;

        _hasCandleAimYaw = false;
        if (aimLightHandAtCandle && animator.transform != transform)
        {
            // 用燭火在 3D 世界的實際位置（含深度）：以燭火為圓心、玩家到燭火的水平距離為半徑
            Vector3 target = new Vector3(flamePos.x, flamePos.y, flamePos.z + candleAimDepthOffset);
            _candleAimYaw = FindBestLightYaw(target);
            _hasCandleAimYaw = true;
            Vector3 fwd = Quaternion.Euler(0f, _candleAimYaw, 0f) * Vector3.forward;
            if (Mathf.Abs(fwd.x) > 0.01f) facingDirection = fwd.x > 0f ? Vector3.right : Vector3.left;
        }

        float speed = 1f;
        // 動作統一在 lightAnimPlaySeconds 內播完；點燃時間比較短的話就跟著點燃時間播完
        float playSeconds = lightAnimPlaySeconds > 0.01f ? lightAnimPlaySeconds : duration;
        if (duration > 0.01f) playSeconds = Mathf.Min(playSeconds, duration);
        float clipLength = GetLightClipLength();
        if (clipLength > 0.01f && playSeconds > 0.01f) speed = clipLength / playSeconds;
        foreach (var p in animator.parameters)
            if (p.name == LightCandleSpeedParam) { animator.SetFloat(LightCandleSpeedParam, speed); break; }

        _candleLightAnim = true;
        _candleLightHold = false;
        try
        {
            animator.Play(lightCandleAnimState, 0, 0f);
            currentAnimState = lightCandleAnimState;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[點燃動作] 找不到 State '{lightCandleAnimState}'：{e.Message}");
            _candleLightAnim = false;
        }
    }

    /// <summary>結束點燃動作。holdPose＝true 時停在最後一格，直到玩家移動才回到一般動作。</summary>
    public void EndCandleLightAnim(bool holdPose)
    {
        _candleLightLocked = false;
        if (!_candleLightAnim) return;
        if (holdPose) _candleLightHold = true;
        else { _candleLightAnim = false; _candleLightHold = false; }
    }

    /// <summary>
    /// 以燭火為圓心、玩家到燭火的水平距離為半徑：每 1 度試一個轉向，
    /// 算出點燃動作最後一格時手會在哪，挑手離燭火最近的角度（一樣近就挑身體最面向燭台的）。回傳世界 Y 軸角度。
    /// </summary>
    private float FindBestLightYaw(Vector3 target)
    {
        Transform visual = animator.transform;
        Transform parent = visual.parent;
        Matrix4x4 parentM = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
        Quaternion parentRotInv = parent != null ? Quaternion.Inverse(parent.rotation) : Quaternion.identity;
        Vector3 localPos = visual.localPosition;
        Vector3 localScale = visual.localScale;

        var dist = new float[360];
        float best = float.MaxValue;
        for (int deg = 0; deg < 360; deg++)
        {
            Quaternion world = Quaternion.Euler(0f, deg, 0f);
            Matrix4x4 m = parentM * Matrix4x4.TRS(localPos, parentRotInv * world, localScale);
            Vector3 hand = m.MultiplyPoint3x4(lightHandLocalOffset);
            Vector2 d = new Vector2(hand.x - target.x, hand.z - target.z);
            dist[deg] = d.magnitude;
            if (dist[deg] < best) best = dist[deg];
        }

        // 一樣近的角度裡挑身體最面向燭台的，動作才合常理（不會側身或背對燭台去點）
        Vector3 toTarget = target - visual.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) toTarget = facingDirection;
        toTarget.Normalize();

        int pick = 0;
        float bestFacing = float.MinValue;
        for (int deg = 0; deg < 360; deg++)
        {
            if (dist[deg] > best + lightAimFacingTieTolerance) continue;
            float facing = Vector3.Dot(Quaternion.Euler(0f, deg, 0f) * Vector3.forward, toTarget);
            if (facing > bestFacing) { bestFacing = facing; pick = deg; }
        }
        return pick;
    }

    /// <summary>第一次停在點燃動作最後一格時，用實際的手指骨頭位置校正 lightHandLocalOffset。</summary>
    private void CalibrateLightHandIfReady()
    {
        if (_lightHandCalibrated || !_candleLightAnim || animator == null || !animator.isHuman) return;
        if (animator.IsInTransition(0)) return;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(lightCandleAnimState) || info.normalizedTime < 1f) return;
        Transform bone = animator.GetBoneTransform(lightHandBone);
        if (bone == null) { _lightHandCalibrated = true; return; }
        lightHandLocalOffset = animator.transform.InverseTransformPoint(bone.position);
        _lightHandCalibrated = true;
    }

    [Tooltip("點燃動作的動畫片段名稱（用來算播放速度）")]
    public string lightCandleClipName = "Light Candle";
    private float _cachedLightClipLength = -1f;

    private float GetLightClipLength()
    {
        if (_cachedLightClipLength > 0f) return _cachedLightClipLength;
        if (animator == null || animator.runtimeAnimatorController == null) return 0f;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
            if (clip != null && clip.name == lightCandleClipName) { _cachedLightClipLength = clip.length; break; }
        return Mathf.Max(0f, _cachedLightClipLength);
    }

    [Header("🌪️ 風暴吸入物理牽引 (Wind Suction - 地形與斜坡適應)")]
    [HideInInspector] public bool isWindSuctionActive = false;
    [HideInInspector] public float windSuctionTargetX = 0f;
    [HideInInspector] public float windSuctionSpeed = 3.0f;
    /// <summary>★1011 牽引走路時，按 A／D 也不改面向（台階走兩步用：面向一直朝著走的方向）。StopWindSuction 會關掉。</summary>
    [HideInInspector] public bool windSuctionIgnoreInputFacing = false;

    // ★1011 跳躍封鎖：每個來源各用一個旗標，互相不會蓋掉對方。場景載入時各自負責放開。
    /// <summary>最後一階台階走兩步的期間不能跳（TeleportTrigger 管）。</summary>
    public static bool JumpBlockedByStair = false;
    /// <summary>廢墟：石牆砸碎後推巨石被狼追的階段不能跳（RuinsChaseNoJump 管）。</summary>
    public static bool JumpBlockedByRuinsChase = false;
    public static bool IsJumpBlocked => JumpBlockedByStair || JumpBlockedByRuinsChase;

    public void StartWindSuction(float targetX, float speed)
    {
        isWindSuctionActive = true;
        windSuctionTargetX = targetX;
        windSuctionSpeed = speed;
        isCutsceneFrozen = false;
        freezeHorizontal = false;
        isStrictLockingX = false;
    }

    public void StopWindSuction()
    {
        isWindSuctionActive = false;
        windSuctionIgnoreInputFacing = false;
    }

    [Header("攝影機緩衝與防震設定 (取代原本的 Y 軸鎖死)")]
    [Tooltip("開啟此選項，會讓攝影機平滑跟隨玩家的上下跳躍 (減震效果，防止跳躍時畫面跟著狂震)")]
    public bool smoothCameraY = true;
    
    [Tooltip("Y 軸追蹤的平滑時間 (數值越大越慢跟上，0.2 ~ 0.5 最佳)")]
    public float cameraYDamping = 0.25f;

    [Tooltip("是否將鏡頭高度嚴格限制在 SkyBackground 範圍內 (設為 false 則直接跟隨主角)")]
    public bool clampSkyBackgroundHeight = false;

    [Header("🎵 陸地動作音效設定 (Land Action SFX)")]
    [Tooltip("地面跑步/走路音效 (例如 Ruined_running / 跑步，自動在移動期間持續 Loop 播放)")]
    public AudioClip footstepSFX;
    [Tooltip("起跳蹬地音效 (例如 起跳)")]
    public AudioClip jumpSFX;
    [Tooltip("普通落地音效 (例如 落地1)")]
    public AudioClip landSoftSFX;
    [Tooltip("高處墜落重著地回聲音效 (例如 落地_回聲)")]
    public AudioClip landHardSFX;
    [Tooltip("滯空時間大於幾秒時，著地改播重著地回聲音效 (秒，預設 0.75)")]
    public float hardLandAirTimeThreshold = 0.75f;

    [Header("🌊 水下游泳音效設定 (Underwater SFX)")]
    [Tooltip("水下游泳/划水音效 (例如 水下_游動_01.wav，水下移動時自動 Loop 播放)")]
    public AudioClip swimSFX;
    [Tooltip("水下單發氣泡音效 (例如 水下_氣泡單發_01, 02, 03，游泳時隨機播放)")]
    public AudioClip[] underwaterBubbleSFX;
    
    [Range(0f, 1f)] public float sfxVolume = 0.85f;

    private AudioSource _footstepSource;
    private AudioSource _swimSource;
    private float _lastAirTime = 0f;
    private bool _wasGroundedLastFrame = true;
    
    private Transform cameraTarget;
    private float _smoothYVelocity;

    [Header("跳躍與墜落動畫微調")]
    [Tooltip("當角色懸空且【向下掉落的速度】大於這個數值時，才播放 Falling 動畫 (設為 0 代表只要往下掉就播，負數代表掉落有一定速度才播)")]
    public float fallVelocityThreshold = -1.0f;
    
    [Tooltip("角色離開地面後，延遲幾秒才允許播放 Falling 動畫 (避免走過小顛簸時一直閃爍掉落動畫)")]
    public float fallAnimationDelay = 0.15f;

    [Tooltip("★1011 跳起來之後，落下動畫要比『一般跳躍從起跳到落地的時間』再晚幾秒才出現（這個時間由跳躍力、重力、阻力自動算出來）。0＝不特別延後，照 fallAnimationDelay")]
    public float jumpFallAnimExtraDelay = 0.3f;

    [Tooltip("★1011 落下動畫的條件要連續成立多久才切過去（秒，防止速度在門檻上下抖動時閃一下）")]
    public float fallAnimDebounceSeconds = 0.1f;
    [Tooltip("★1011 進入／離開落下動畫的淡入淡出時間（秒，0＝硬切）")]
    public float fallAnimBlendSeconds = 0.12f;
    [Tooltip("★1014 水下放開方向鍵：游泳 (身體平躺) → 踩水 (身體直立) 的淡入淡出時間 (秒)。越大身體抬起來越慢")]
    public float swimToTreadBlendSeconds = 0.8f;
    [Tooltip("★1014 水下開始移動：踩水 → 游泳 的淡入淡出時間 (秒)")]
    public float treadToSwimBlendSeconds = 0.3f;
    [Tooltip("停下來：奔跑 → 站立 的淡入淡出時間 (秒，0＝硬切)。越大收腳越慢越柔")]
    public float runToIdleBlendSeconds = 0.25f;
    [Tooltip("起跑：站立 → 奔跑 的淡入淡出時間 (秒，0＝硬切)")]
    public float idleToRunBlendSeconds = 0.15f;
    private float _fallCandidateTime = 0f;

    // 一般（平地）跳躍的滯空時間：用跳躍力、重力、剛體阻力模擬出來，數值沒變就不重算
    private float _cachedJumpAirTime;
    private float _cachedJumpKeyForce = -1f, _cachedJumpKeyGravity = -1f, _cachedJumpKeyDamping = -1f;

    private float GetExpectedJumpAirTime()
    {
        float g = Mathf.Abs(Physics.gravity.y);
        float damping = rb != null ? rb.linearDamping : 0f;
        if (Mathf.Approximately(_cachedJumpKeyForce, jumpForce) && Mathf.Approximately(_cachedJumpKeyGravity, g) && Mathf.Approximately(_cachedJumpKeyDamping, damping))
            return _cachedJumpAirTime;

        float dt = Mathf.Max(0.005f, Time.fixedDeltaTime);
        float v = jumpForce, y = 0f, t = 0f;
        for (int i = 0; i < 1000; i++)
        {
            v -= g * dt;
            v *= Mathf.Clamp01(1f - damping * dt);
            y += v * dt;
            t += dt;
            if (i > 2 && y <= 0f) break;
        }
        _cachedJumpKeyForce = jumpForce; _cachedJumpKeyGravity = g; _cachedJumpKeyDamping = damping;
        _cachedJumpAirTime = t;
        return t;
    }

    // ════════════════════════════════════════════════════════
    // ★1012 天空掉到廢墟：固定速度曲線（每次掉下去的時間都一樣）
    // 原本是物理自由落體：起始速度取決於「傳送當下」與「碰到墜落通道那一刻」誰先誰後
    // （SkyDive 給 -10，FallingBackground 又取 Min(目前, -8)），再加上剛體阻力 0.5、重力每個固定步長積分，
    // 每次的終端速度與抵達時間都有落差；而且 Update 裡「位移超過 5 米＝瞬間移動」的判斷在
    // 高速下掉時，只要卡一幀（>0.25 秒）就會誤判成傳送、把墜落鎖解掉。
    // 做法：墜落期間速度完全由這裡決定——固定起始速度 → 固定加速度 → 固定終端速度，
    // 接近地面時固定放慢到落地速度；關掉重力與阻力、打開內插，畫面才不會一格一格跳。
    // ════════════════════════════════════════════════════════
    [Header("★1012 廢墟墜落（固定速度曲線）")]
    [Tooltip("天空掉到廢墟那段用固定速度曲線，每次墜落時間一樣。關掉＝回到原本的物理自由落體")]
    public bool controlledRuinsFall = true;
    [Tooltip("起始向下速度（米／秒），跟 SkyDiveTeleportTrigger 的 initialDownwardSpeed 對齊")]
    public float ruinsFallStartSpeed = 9f;
    [Tooltip("終端速度（米／秒）")]
    public float ruinsFallTerminalSpeed = 17f;
    [Tooltip("加速度（米／秒²）")]
    public float ruinsFallAcceleration = 12f;
    [Tooltip("離地面這麼近（米）開始放慢，落地才不會砸")]
    public float ruinsFallBrakeDistance = 9f;
    [Tooltip("落地前的速度（米／秒）")]
    public float ruinsFallLandingSpeed = 6f;

    private bool _controlledFall;
    private float _controlledFallVy;
    private float _savedDamping;
    private RigidbodyInterpolation _savedInterpolation;
    private bool _savedUseGravity = true;

    /// <summary>★1015 正在固定速度墜落中（給中途傳送用）。</summary>
    public bool IsInControlledFall => _controlledFall;
    private float _controlledFallStartTime;
    /// <summary>★1018 這一次固定速度墜落已經掉了幾秒（沒在墜落＝0）。DeathZone 免死用，超過上限就不再保護，免得掉出地圖永遠不死。</summary>
    public float ControlledFallSeconds => _controlledFall ? Time.time - _controlledFallStartTime : 0f;

    /// <summary>
    /// ★1015 墜落途中傳送（天空底 → 廢墟頂，白霧遮住的那一瞬間用）：
    /// 位置與相機搬走，但固定速度曲線不中斷、橫向鎖不解開，傳過去繼續用同一條速度曲線掉到地面。
    /// （一般的 WarpTo 會把墜落結束、速度歸零，之後變回物理自由落體，速度就不一致了。）
    /// </summary>
    public void RelayWarpDuringFall(Vector3 position)
    {
        // ★1018 墜落時 LateUpdate 每一幀都會把 X 拉回 lockedXValue、Z 拉回 initialZ（isStrictLockingX）。
        //   Root Cause（Editor.log）：接力傳送點的 X 跟第一段墜落的 X 不一樣時（搬了墜落圖之後），傳過去下一幀就被拉回舊的 X，
        //   玩家看起來「傳送失敗、一直往下掉」（log：傳到 (50.3,-108.3)，之後玩家一直在 (-25.1, …)）。
        //   傳送時把鎖定的 X 一起換成新位置，Z 用玩家原本的 Z。
        position.z = initialZ;
        if (isStrictLockingX) lockedXValue = position.x;
        transform.position = position;
        if (rb != null)
        {
            rb.position = position;
            rb.linearVelocity = new Vector3(0f, _controlledFall ? _controlledFallVy : rb.linearVelocity.y, 0f);
        }
        if (cameraTarget != null) cameraTarget.position = position;
        _lastFramePos = position;
        _smoothYVelocity = 0f;
        foreach (CinemachineCamera v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None)) v.PreviousStateIsValid = false;
    }

    private void BeginControlledFall()
    {
        if (!controlledRuinsFall || rb == null || rb.isKinematic || _controlledFall) return;
        _controlledFall = true;
        _controlledFallVy = -Mathf.Abs(ruinsFallStartSpeed);
        _controlledFallStartTime = Time.time;
        _savedDamping = rb.linearDamping;
        _savedInterpolation = rb.interpolation;
        _savedUseGravity = rb.useGravity;
        rb.linearDamping = 0f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;   // 渲染時內插，高速下掉才平順
        rb.useGravity = false;
        rb.linearVelocity = new Vector3(0f, _controlledFallVy, 0f);
        Debug.Log($"【墜落】開始固定速度墜落：起始 {ruinsFallStartSpeed:F0} → 終端 {ruinsFallTerminalSpeed:F0} 米／秒，位置 {transform.position.ToString("F1")}");
    }

    private void EndControlledFall(string why)
    {
        if (!_controlledFall) return;
        _controlledFall = false;
        if (rb != null)
        {
            rb.linearDamping = _savedDamping;
            rb.interpolation = _savedInterpolation;
            rb.useGravity = true;
        }
        Debug.Log($"【墜落】結束固定速度墜落（{why}），位置 {transform.position.ToString("F1")}");
    }

    private void ControlledFallStep()
    {
        if (rb == null || rb.isKinematic) { EndControlledFall("剛體不可用"); return; }
        // 墜落旗標被別的流程清掉了（重生、傳送…）就交還物理
        if (!_waitingForRuinsLanding) { EndControlledFall("墜落旗標已解除"); return; }

        float dt = Time.fixedDeltaTime;
        float target = -Mathf.Abs(ruinsFallTerminalSpeed);

        // 離地面近就固定放慢到落地速度
        Collider col = playerCollider != null ? playerCollider : GetComponent<Collider>();
        if (col != null)
        {
            Bounds b = col.bounds;
            float reach = ruinsFallBrakeDistance + b.extents.y + 0.5f;
            if (Physics.Raycast(b.center, Vector3.down, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider != col && !hit.collider.transform.IsChildOf(transform))
            {
                float dist = Mathf.Max(0f, hit.distance - b.extents.y);
                float k = Mathf.Clamp01(dist / Mathf.Max(0.1f, ruinsFallBrakeDistance));
                target = -Mathf.Lerp(Mathf.Abs(ruinsFallLandingSpeed), Mathf.Abs(ruinsFallTerminalSpeed), k);
            }
        }

        float rate = target < _controlledFallVy ? ruinsFallAcceleration : ruinsFallAcceleration * 2.5f;   // 加速慢慢來、減速稍快
        _controlledFallVy = Mathf.MoveTowards(_controlledFallVy, target, rate * dt);
        rb.useGravity = false;
        rb.linearVelocity = new Vector3(0f, _controlledFallVy, 0f);
    }

    [Header("★1011 廢墟墜落特寫還給玩家")]
    [Tooltip("落地後，鏡頭從光球特寫慢慢移回玩家身上要幾秒（0＝瞬間切回）")]
    public float ruinsIntroHandBackSeconds = 1.0f;
    [Tooltip("★1012 墜落中鏡頭從玩家移到光球特寫的過渡時間（秒）")]
    public float ruinsIntroCloseupBlendSeconds = 1.0f;
    
    private float currentAirTime = 0f;

    public enum WaterDetectionMode { Auto, ForceOn, ForceOff }

    [Header("水下物理與浮力設定 (Underwater Physics)")]
    [Tooltip("水中狀態偵測模式：Auto(自動偵測場景名稱包含 underwater 或進入水體 Trigger)、ForceOn(強制開啟水下物理)、ForceOff(強制關閉)")]
    public WaterDetectionMode waterDetectionMode = WaterDetectionMode.Auto;

    [Tooltip("目前是否處於水中狀態 (觀察用 / 腳本唯讀與觸發)")]
    public bool isUnderwater = false;

    [Tooltip("水中重力縮放比例 (0 ~ 1，數值越小浮力越強、掉落越慢，建議 0.25)")]
    public float underwaterGravityScale = 0.25f;

    [Tooltip("水中跳躍 / 撥水推進推力 (預設 3.5f，比陸地的跳躍 5.0 更平緩)")]
    public float underwaterJumpForce = 3.5f;

    [Tooltip("水中最大沉降速度限制 (單位/秒，設為 -2.0f 讓沉降不會太快，產生滯空浮動感)")]
    public float underwaterMaxFallSpeed = -2.0f;

    [Tooltip("水中垂直向上/向下移動的阻力衰減率 (數值越大衝速減慢越快，呈現滑順水阻)")]
    public float underwaterVerticalDrag = 2.0f;

    [Tooltip("水中水平移動速度倍率 (預設 0.85f，模擬水中行走/游泳的微水阻)")]
    public float underwaterHorizontalSpeedMultiplier = 0.85f;

    // ★1016 水下斜石修正：陸地的「站在斜坡上」分支會每幀把速度歸零、關掉重力，
    //   水下站在斜的石頭上時，FixedUpdate 划水給的往上速度下一幀就被蓋掉（按 W 只抬 0.1 米），也不會往下沉。
    //   打開＝水下不走斜坡分支，划水、下沉照水下物理；關掉＝原本。
    [Tooltip("★1016 水下站在斜的石頭上也能划水往上、會順著水下物理下沉（關掉＝原本：斜石上定住、按 W 只抬一點點）")]
    public bool underwaterIgnoreSlopeStick = true;

    [Tooltip("是否允許在水中未著地時連按 W 向上撥水游泳")]
    public bool allowContinuousSwimming = true;

    [Tooltip("連點往上鍵時，兩次划水的最短間隔 (秒)")]
    public float swimCooldown = 0.18f;

    [Tooltip("按住往上鍵不放時，每隔幾秒自動再划一下 (划一下 → 停頓 → 再划一下)，直到體力耗盡；耗盡後要放開再按才會繼續")]
    public float swimHoldRepeatInterval = 0.6f;
    private float lastSwimTime = -999f;
    private bool isTriggerUnderwater = false;
    private HorizontalMovingPlatform activeMovingPlatform;

    [Tooltip("水中按 W 向上游泳時的模型仰角傾斜 (預設 15 度)")]
    public float underwaterSwimUpTiltAngle = 15f;

    [Header("真實水下波義耳定律負浮力機制 (Depth Buoyancy Physics)")]
    [Tooltip("是否開啟水下深度波義耳定律負浮力機制 (越深水壓越大壓縮體積，超過中性浮力深度後加速沉降)")]
    public bool enableDepthBuoyancyPhysics = true;

    [Tooltip("中性浮力 Y 軸座標 (Neutral Buoyancy Y Level)。高於此處為正浮力，低於此處水壓壓縮空氣產生負浮力加速沉降")]
    public float neutralBuoyancyY = -5f;

    [Tooltip("水壓氣體體積壓縮率 (Depth Compression Rate)。每向下滑行 1 單位深度時增加的沉降加速度比率 (建議 0.1 ~ 0.4)")]
    public float depthCompressionRate = 0.2f;

    [Tooltip("水下深度負浮力最大額外下沉推力上限 (避免極深處掉落太快)")]
    public float maxNegativeBuoyancyExtraForce = 8.0f;

    [Header("隱形水下游泳體力與適時斷開機制 (Underwater Stamina & Limit)")]
    [Tooltip("連續向上游泳的最大隱形體力上限 (秒，預設 3.0)。控制按住 W 最多能連續向上游多久")]
    public float maxSwimStamina = 5.0f;

    [Tooltip("隱形體力恢復速率 (倍率，預設 1.5)。當放開按鍵或著地時體力恢復速度")]
    public float swimStaminaRegenRate = 1.5f;

    [Tooltip("體力耗盡力竭冷卻時間 (秒，預設 1.0)。體力扣完斷開推力後，需冷卻多久才能再次向上游泳")]
    public float swimExhaustionCooldown = 1.0f;

    [Header("★1014 瑪利歐式划水 (Swim Stroke)")]
    [Tooltip("每按一下往上鍵 (W / Space / ↑) 往上升的高度 (單位)。按住不會持續上升，要再按一次才會再划一下；兩次划水的最短間隔見 Swim Cooldown")]
    public float swimStrokeHeight = 1.6f;

    [Tooltip("★1021 蛙式：每划一下「蹬腿推水」加速到最高速要多久 (秒)。越長越慢慢起步")]
    public float swimStrokePushTime = 0.45f;

    [Tooltip("★1021 蛙式：推完之後滑行的水阻減速度 (單位/秒²)。越小滑越久、速度越柔和；最高速會自動算，讓每划一下仍升 Swim Stroke Height")]
    public float swimStrokeGlideDecel = 2.5f;

    [Tooltip("每划一下消耗的隱形體力 (秒，體力上限見 Max Swim Stamina)")]
    public float swimStrokeStaminaCost = 0.4f;

    [Tooltip("放開往上鍵且停止划水多久之後，體力開始回復 (秒)。按住往上鍵時不回復")]
    public float swimStaminaRegenDelay = 0.5f;

    [Tooltip("按 S / ↓ 主動下潛的最低速度 (單位/秒)")]
    [UnityEngine.Serialization.FormerlySerializedAs("underwaterSwimUpSpeed")]
    public float underwaterDiveSpeed = 4.0f;

    [Tooltip("按 S / ↓ 下潛時，速度至少是當下自然下沉上限的幾倍 (確保任何深度按 S 都比不按沉得快)")]
    public float underwaterDiveFallMultiplier = 2.6f;

    [Tooltip("按 S / ↓ 下潛的加速度")]
    public float underwaterDiveAccel = 12f;

    private bool _swimStrokeQueued;
    private float _strokePushStart = -99f;   // ★1021 蛙式推水開始時間 (fixedTime)
    private float _strokePushFromVy;         // 推水開始時的垂直速度
    private float _strokePeakVy;             // 這一划的最高上升速度
    private float _lastSwimWantTime = -99f;
    private bool _swimHoldBlocked;   // 按住划水到力竭後，要放開往上鍵才會恢復按住連划

    [Header("★1013 光球帶路時的水中定位（別沉太多游不回來）")]
    [Tooltip("光球帶路鎖住玩家時，下沉速度上限（公尺/秒）")]
    public float floatHoldSinkSpeed = 0.3f;
    [Tooltip("光球帶路鎖住玩家期間，最多允許比一開始再往下多少公尺")]
    public float floatHoldMaxSink = 0.6f;
    private bool _floatHold;
    private float _floatHoldStartY;

    [Header("★1016 剛進水下／水下重生：先漂著，等她動了才開始沉")]
    [Tooltip("一進水下關（或在水下重生）她就開始往下沉，越深沉越快；還沒搞懂怎麼游的人，想幾秒就沉了幾十公尺。\n" +
             "打開＝進水下（或水下重生完成）後先幾乎原地漂浮，直到她按任何移動／划水鍵，或可以操作後過了 underwaterEntryFloatSeconds 秒。\n" +
             "關掉＝原本（一進來就沉）")]
    public bool underwaterEntryFloat = true;
    [Tooltip("可以操作之後，最多漂幾秒就恢復下沉（秒）")]
    public float underwaterEntryFloatSeconds = 8f;
    private bool _entryFloat;
    private float _entryFloatStartY;
    private float _entryFloatRemaining;
    private bool _entryWasUnderwater;

    /// <summary>★1016 每幀更新「剛入水漂浮」：入水、水下重生完成時開始；按鍵或時間到就結束。</summary>
    private void UpdateUnderwaterEntryFloat()
    {
        bool respawning = PlayerRespawnSystem.IsAnyRespawning;
        // 剛入水：開始漂。水下重生：黑幕期間一路漂著（每幀以當下高度為基準，傳送到重生點後也不會往下掉），重生完再開始算時間
        if (underwaterEntryFloat && isUnderwater && (!_entryWasUnderwater || respawning))
        {
            _entryFloat = true;
            _entryFloatStartY = transform.position.y;
            _entryFloatRemaining = Mathf.Max(0f, underwaterEntryFloatSeconds);
        }
        _entryWasUnderwater = isUnderwater;

        if (!_entryFloat) return;
        if (!underwaterEntryFloat || !isUnderwater) { _entryFloat = false; return; }
        if (IsControlLocked || respawning) return;   // 演出、重生黑幕中不算時間

        bool anyKey = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S)
                   || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)
                   || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow);
        _entryFloatRemaining -= Time.deltaTime;
        if (anyKey || _entryFloatRemaining <= 0f)
        {
            _entryFloat = false;
            Debug.Log($"【水下】★1016 入水漂浮結束（{(anyKey ? "她按鍵了" : "時間到")}）：漂浮期間下沉 {(_entryFloatStartY - transform.position.y):F2} 米");
        }
    }

    /// <summary>光球演出開始／結束時呼叫：水中時讓她幾乎原地漂浮。</summary>
    public void SetFloatHold(bool on)
    {
        _floatHold = on;
        _floatHoldStartY = transform.position.y;
    }

    [Header("穿模防護 (Depenetration)")]
    [Tooltip("陸地用的推出速度。壓低是為了消除斜坡夾角的抖動，不要隨意調高")]
    public float landDepenetrationVelocity = 2.0f;

    [Tooltip("水下推出速度 = 當下沉降速度 x 這個倍率。\n" +
             "必須大於 1，否則物理推玩家出石頭的速度會輸給下沉速度，玩家會陷進石頭裡穿出去")]
    public float underwaterDepenetrationFactor = 2.0f;

    [Tooltip("underwater 以外的場景：前方障礙物在這個距離內就停止往它推擠 (防止強風/推力把玩家頂進石柱造成抖動)")]
    public float landObstacleLookahead = 0.25f;

    [Tooltip("只在 underwater 場景使用的前方障礙物偵測距離 (其餘場景一律用 Land Obstacle Lookahead)。\n" +
             "設得太大時，玩家會在離石頭還有一段距離的地方就被擋下來，看起來像隔著一道空氣牆")]
    public float underwaterObstacleLookahead = 0f;

    [HideInInspector] public float currentSwimStamina;
    [HideInInspector] public bool isSwimExhausted = false;
    private float exhaustionTimer = 0f;

    // 動畫接軌預留標籤
    [HideInInspector] public bool isSwimming = false;

    // 天空背景邊界快取 (避免每幀 FindObjectsByType)
    private float _nextSkyBoundsCheckTime = 0f;
    private GameObject _followTargetObj;          // 快取的 CameraFollowTarget（LateUpdate 用）
    private float _nextFollowTargetFind = 0f;
    private Bounds _cachedSkyBounds = new Bounds();
    private bool _hasCachedSkyBounds = false;

    private void OnEnable()
    {
        isCutsceneFrozen = false;
        freezeHorizontal = false;
        isStrictLockingX = false;
        PlayerRespawnSystem.IsAnyRespawning = false;
        MirrorWallAbsorbCutscene.IsAnyCutsceneRunning = false;
    }

    private void OnDisable()
    {
        if (_footstepSource != null)
        {
            _footstepSource.Stop();
            _footstepSource.volume = 0f;
        }

        if (_swimSource != null)
        {
            _swimSource.Stop();
            _swimSource.volume = 0f;
        }
    }

    void Start()
    {
        // ★ 自動停用場景中殘留的 Timeline PlayableDirector，防止開局自動播放光離開音效
        var directors = Object.FindObjectsByType<UnityEngine.Playables.PlayableDirector>(FindObjectsSortMode.None);
        foreach (var pd in directors)
        {
            if (pd != null && (pd.gameObject.name == "GameObject" || (pd.playableAsset != null && pd.playableAsset.name.Contains("Timeline"))))
            {
                pd.Stop();
                pd.playOnAwake = false;
                pd.enabled = false;
            }
        }

        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();
        
        // 儲存初始 Z 軸位置，作為 2.5D 移動的基準線
        initialZ = transform.position.z;
        
        // 強化物理設定，鎖定旋轉與 Z 軸移動，避免被撞飛、偏移或穿模
        rb.interpolation = RigidbodyInterpolation.Interpolate; // 啟用物理內插，消除攝影機與物理刷新率不同步之畫面抖動
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ; 
        rb.mass = 10f; // 增加玩家質量，才不會被輕易推動
        rb.maxDepenetrationVelocity = landDepenetrationVelocity; // ★ 消除斜坡/平地夾角被強力彈開反彈引發的劇烈抖動
        rb.solverIterations = 16;
        rb.solverVelocityIterations = 16;

        // 賦予無摩擦力物理材質，避免卡在牆壁、物件邊緣
        PhysicsMaterial noFriction = new PhysicsMaterial("NoFrictionMaterial");
        noFriction.dynamicFriction = 0f;
        noFriction.staticFriction = 0f;
        noFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
        noFriction.bounciness = 0f;
        noFriction.bounceCombine = PhysicsMaterialCombine.Minimum;

        if (playerCollider != null)
        {
            playerCollider.material = noFriction;
        }

        // ★ 水下場景 Z 軸精確居中校準與岩石深度全角度密封 (水下岩石洞穴中心為 Z = -0.4f)
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("underwater"))
        {
            initialZ = -0.4f;
            transform.position = new Vector3(transform.position.x, transform.position.y, -0.4f);
            UnderwaterRockColliderHelper.SealUnderwaterRockGaps();
        }

        // ==========================================
        // 攝影機鎖定主角初始化 (確保 Cinemachine 100% 跟隨主角)
        // ==========================================
        SetCameraFollow(this.transform);

        // 如果沒有手動設定，才嘗試自動搜尋 (備用)
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null)
        {
            animator.applyRootMotion = false; // 強制關閉 Root Motion！防止 3D/FBX 動畫拖走或鎖死 Transform 導致玩家在水下卡住不能動！
            animator.speed = 1.0f;

            // ★1001 斜坡視覺空隙補償：快取外觀子物件與它的原始 local Y。
            //   ★必須確認它不是玩家根物件——根物件上掛著 BoxCollider 與 Rigidbody，
            //     移動根物件會連碰撞盒一起移動，那就不是「只改視覺」了。
            if (animator.transform != transform)
            {
                _visualRoot = animator.transform;
                _visualBaseLocalY = _visualRoot.localPosition.y;
                _hasVisualBase = true;
            }
            else
            {
                _hasVisualBase = false;
                Debug.LogWarning("[斜坡視覺補償] animator 就掛在玩家根物件上，為了不動到碰撞盒，已停用視覺空隙補償。");
            }
        }
        
        // 強制重置所有狀態，避免卡死
        JumpBlockedByStair = false;       // ★1011 static 會跨場景／跨播放留著，進場景先放開
        JumpBlockedByRuinsChase = false;
        freezeHorizontal = false; 
        isStrictLockingX = false;
        isCutsceneFrozen = false; // 確保起始未被鎖定
        attachedWolvesCount = 0;
        BitesTaken = 0;   // ★1010
        currentSpeed = baseSpeed;
        _lastFramePos = transform.position;
        currentSwimStamina = maxSwimStamina;
        isSwimExhausted = false;

        // 【修復轉場殘留鎖定】：強制重置 Rigidbody 的 constraints 與重力狀態
        // WaterOasisTransition 轉場時會鎖定 FreezePositionX 和關閉 useGravity，
        // 場景切換後這些狀態會殘留在 Player 身上導致無法移動，這裡強制還原！
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero; // 清除殘留速度
        }

        // =========================================
        // 【超強力啟動診斷】幫助排查卡住問題
        // =========================================
        Debug.Log($"========== 【PlayerMovement 啟動診斷】 ==========\n" +
                  $" 場景名稱: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n" +
                  $" 座標 Position: {transform.position}\n" +
                  $" Rigidbody: {(rb != null ? "有" : "❌ NULL！")}\n" +
                  $"   - isKinematic: {(rb != null ? rb.isKinematic.ToString() : "N/A")}\n" +
                  $"   - useGravity: {(rb != null ? rb.useGravity.ToString() : "N/A")}\n" +
                  $"   - constraints: {(rb != null ? rb.constraints.ToString() : "N/A")}\n" +
                  $"   - mass: {(rb != null ? rb.mass.ToString() : "N/A")}\n" +
                  $" Animator: {(animator != null ? animator.name : "❌ NULL！動畫不會播放！")}\n" +
                  $" Collider: {(playerCollider != null ? playerCollider.GetType().Name : "❌ NULL！")}\n" +
                  $" PlayerMovement.enabled: {this.enabled}\n" +
                  $" isCutsceneFrozen: {isCutsceneFrozen}\n" +
                  $" freezeHorizontal: {freezeHorizontal}\n" +
                  $" isStrictLockingX: {isStrictLockingX}\n" +
                  $" isUnderwater: {isUnderwater}\n" +
                  $" baseSpeed: {baseSpeed} | currentSpeed: {currentSpeed}\n" +
                  $"==============================================");
    }

    void Update()
    {
        // 刷新水下狀態 (三層判定)
        UpdateUnderwaterState();
        UpdateUnderwaterEntryFloat();   // ★1016 剛進水下／水下重生先漂著

        // ==========================================
        // 【防卡死】偵測瞬間移動 (例如：重生系統觸發)
        // ==========================================
        if (!_controlledFall && Vector3.Distance(transform.position, _lastFramePos) > 5f)   // ★1012 高速墜落一幀就可能 >5 米，不是傳送
        {
            Debug.Log("【重生防卡死】偵測到玩家瞬間移動，強制解除所有 X 軸與墜落鎖定！");
            freezeHorizontal = false;
            isStrictLockingX = false;
        }
        _lastFramePos = transform.position;

        // 補強組件檢索
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>();
        if (playerCollider == null) playerCollider = GetComponentInParent<Collider>();

        // 1. 偵測地面狀態與斜坡角度 (使用多點射線確保 BoxCollider 任何邊緣觸地皆能精確穩定感應)
        RaycastHit groundHit;
        float currentSlopeAngle = 0f;
        bool preliminaryGrounded = CheckGrounded(out groundHit, out currentSlopeAngle);
        GroundSlopeAngle = preliminaryGrounded ? currentSlopeAngle : 0f;
        GroundNormal = (preliminaryGrounded && groundHit.normal.sqrMagnitude > 0.001f) ? groundHit.normal : Vector3.up;

        if (preliminaryGrounded && groundHit.collider != null)
        {
            activeMovingPlatform = groundHit.collider.GetComponentInParent<HorizontalMovingPlatform>();
            if (activeMovingPlatform == null) activeMovingPlatform = groundHit.collider.GetComponent<HorizontalMovingPlatform>();
        }
        else
        {
            activeMovingPlatform = null;
        }

        // ==========================================
        // 處理水平移動與最高層級墜落鎖定
        // ==========================================
        bool isInDropZone = freezeHorizontal;
        bool actuallyFreeze = isInDropZone && !preliminaryGrounded;
        
        // 讀取玩家輸入
        float rawInput = Input.GetAxis("Horizontal");
        if (rawInput == 0f)
        {
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) rawInput = 1f;
            else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) rawInput = -1f;
        }

        float moveInput = 0f;

        if (isWindSuctionActive)
        {
            // ★ 風暴吸入模式：位移由風暴引力牽引，但允許玩家按 A/D 自由左右轉身面向 (原地跑掙扎演出)
            float diffX = windSuctionTargetX - transform.position.x;
            if (Mathf.Abs(diffX) > 0.05f)
            {
                moveInput = Mathf.Sign(diffX);
            }
            else
            {
                moveInput = 0f;
            }

            // 玩家按鍵時優先面向玩家按鍵方向，沒按時面向吸入方向
            if (!windSuctionIgnoreInputFacing && rawInput > 0.1f) facingDirection = Vector3.right;
            else if (!windSuctionIgnoreInputFacing && rawInput < -0.1f) facingDirection = Vector3.left;
            else if (moveInput > 0.05f) facingDirection = Vector3.right;
            else if (moveInput < -0.05f) facingDirection = Vector3.left;
        }
        else
        {
            // 當處於重生中 (IsAnyRespawning) 或 劇情演出鎖定 (IsControlLocked) 時，嚴格禁止玩家移動
            if (IsControlLocked)
            {
                rawInput = 0f;
                moveInput = 0f;
                if (rb != null && !rb.isKinematic)
                {
                    rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                }
            }
            else
            {
                // 若無任何全局演出正在跑，玩家主動按鍵即視為正常操作，自動解鎖。
                bool wantsToMove = Mathf.Abs(rawInput) > 0.1f
                                   || Input.GetKeyDown(KeyCode.W)
                                   || Input.GetKeyDown(KeyCode.Space)
                                   || Input.GetKeyDown(KeyCode.UpArrow);
                if (wantsToMove && !_waitingForRuinsLanding)
                {
                    // 若非演出/重生狀態且玩家主動按鍵，才解除掉落鎖死
                    isStrictLockingX = false;
                    freezeHorizontal = false;
                }

                moveInput = (actuallyFreeze || isCutsceneFrozen || IsHardCutsceneLocked) ? 0f : rawInput;

                // 抓取物件中 (LeftShift) 鎖定面向：往後拉時保持面向物件倒退走，不轉身
                bool isHoldingObject = pulledObject != null;
                if (moveInput > 0.1f && !isStrictLockingX && !isHoldingObject) facingDirection = Vector3.right;
                if (moveInput < -0.1f && !isStrictLockingX && !isHoldingObject) facingDirection = Vector3.left;
            }
        }

        CurrentMoveInput = moveInput;

        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.W) || 
            Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            PlayerPetrification petr = GetComponent<PlayerPetrification>();
            if (petr == null) petr = GetComponentInChildren<PlayerPetrification>();
            Debug.Log($"【移動診斷 LOG】玩家按下按鍵！\n" +
                      $" - 座標 Position: {transform.position}\n" +
                      $" - Rigidbody.isKinematic: {(rb != null ? rb.isKinematic.ToString() : "NULL")}\n" +
                      $" - PlayerMovement.enabled: {this.enabled}\n" +
                      $" - isGrounded: {preliminaryGrounded}\n" +
                      $" - freezeHorizontal: {freezeHorizontal}\n" +
                      $" - isCutsceneFrozen: {isCutsceneFrozen}\n" +
                      $" - isStrictLockingX: {isStrictLockingX}\n" +
                      $" - isJumping: {isJumping}\n" +
                      $" - isUnderwater: {isUnderwater}（水下就不能跳）\n" +
                      $" - IsHardCutsceneLocked: {IsHardCutsceneLocked}"
                      + $"（重生中:{PlayerRespawnSystem.IsAnyRespawning}"
                      + $" 鏡牆演出:{MirrorWallAbsorbCutscene.IsAnyCutsceneRunning}"
                      + $" 怪物登場:{ShadowMonsterController.IsRevealRunning}"
                      + $" 文字卡:{(StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying)}）\n" +
                      $" - 石化狀態 isPetrified: {(petr != null ? petr.isPetrified.ToString() : "無石化組件")}\n" +
                      $" - 當前速度 currentSpeed: {currentSpeed}\n" +
                      $" - 剛體速度 velocity: {(rb != null ? rb.linearVelocity.ToString() : "NULL")}");
        }

        // ==========================================
        // 1. 地面狀態、推動判定與著地/腳步音效判定
        // ==========================================
        isGrounded = preliminaryGrounded;

        // 偵測是否正在推動 Pushable 物件
        UpdatePushingDetection(moveInput);

        if (isGrounded)
        {
            // 剛從空中落地的瞬間
            if (!_wasGroundedLastFrame && _lastAirTime > 0.15f)
            {
                if (_lastAirTime >= hardLandAirTimeThreshold && landHardSFX != null)
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(landHardSFX, sfxVolume);
                }
                else if (landSoftSFX != null)
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(landSoftSFX, sfxVolume);
                }
            }

            _lastAirTime = 0f;
            currentAirTime = 0f;

            // 如果落地且沒有明顯向上的速度，代表跳躍結束
            if (rb.linearVelocity.y <= 0.1f) isJumping = false;

            // ★★★ 關鍵修正：只要玩家落地踩到任何地面，自動解除掉落鎖定，恢復自由控制！
            if (freezeHorizontal)
            {
                if (!_waitingForRuinsLanding || IsRuinsLandingGround(groundHit))
                {
                    UnlockFallingControl("玩家已著地");
                }
            }
        }
        else
        {
            currentAirTime += Time.deltaTime;
            _lastAirTime = currentAirTime;
        }

        // ==========================================
        // 陸地跑步 / 推動物件音效 Loop 控制 (微包絡淡入淡出，消除波形硬切雜音與延遲)
        // ==========================================
        if (footstepSFX != null && !isUnderwater)
        {
            if (_footstepSource == null)
            {
                _footstepSource = gameObject.AddComponent<AudioSource>();
                _footstepSource.clip = footstepSFX;
                _footstepSource.loop = true;
                _footstepSource.playOnAwake = false;
                _footstepSource.volume = 0f;
            }
            else if (_footstepSource.clip != footstepSFX)
            {
                _footstepSource.clip = footstepSFX;
            }

            bool shouldPlayFootstep = isGrounded && !isJumping && !IsControlLocked && (Mathf.Abs(moveInput) > 0.1f || isPushing);
            float maxFootstepVol = AudioManager.ScaleSfx(sfxVolume * 0.75f);
            float targetFootstepVol = shouldPlayFootstep ? maxFootstepVol : 0f;

            // 50ms 快速微淡入淡出，保證無延遲且絕無波形截斷雜音
            _footstepSource.volume = Mathf.MoveTowards(_footstepSource.volume, targetFootstepVol, Time.deltaTime * (maxFootstepVol / 0.05f));

            if (_footstepSource.volume > 0.001f)
            {
                if (!_footstepSource.isPlaying) _footstepSource.Play();
            }
            else if (_footstepSource.isPlaying && !shouldPlayFootstep)
            {
                _footstepSource.Stop();
            }
        }
        else if (_footstepSource != null && _footstepSource.isPlaying)
        {
            _footstepSource.Stop();
            _footstepSource.volume = 0f;
        }

        // ==========================================
        // 水下游泳音效 Loop 控制 (只要在水下且有移動/上浮按鍵就播放)
        // ==========================================
        if (isUnderwater && swimSFX != null)
        {
            if (_swimSource == null)
            {
                _swimSource = gameObject.AddComponent<AudioSource>();
                _swimSource.clip = swimSFX;
                _swimSource.loop = true;
                _swimSource.playOnAwake = false;
                _swimSource.volume = sfxVolume * 0.85f;
            }
            else if (_swimSource.clip != swimSFX)
            {
                _swimSource.clip = swimSFX;
            }

            _swimSource.volume = AudioManager.ScaleSfx(sfxVolume * 0.85f);

            // 只有當玩家主動按下 WASD、方向鍵或空白鍵時才判定為正在划水移動
            bool hasActiveSwimInput = (Mathf.Abs(moveInput) > 0.1f)
                                   || Input.GetKey(KeyCode.W) 
                                   || Input.GetKey(KeyCode.S) 
                                   || Input.GetKey(KeyCode.A) 
                                   || Input.GetKey(KeyCode.D) 
                                   || Input.GetKey(KeyCode.Space) 
                                   || Input.GetKey(KeyCode.UpArrow) 
                                   || Input.GetKey(KeyCode.DownArrow) 
                                   || Input.GetKey(KeyCode.LeftArrow) 
                                   || Input.GetKey(KeyCode.RightArrow);

            bool isMovingInWater = hasActiveSwimInput && !IsControlLocked;
            if (isMovingInWater)
            {
                if (!_swimSource.isPlaying) _swimSource.Play();
            }
            else
            {
                if (_swimSource.isPlaying) _swimSource.Stop();
            }
        }
        else if (_swimSource != null && _swimSource.isPlaying)
        {
            _swimSource.Stop();
        }

        _wasGroundedLastFrame = isGrounded;

        // ==========================================
        // 2. 動畫強行控制 (直接程式碼接管播放)
        // ==========================================
        if (animator != null)
        {
            animator.applyRootMotion = false; // 每幀維持關閉 Root Motion，確保玩家速度由程式與 Physics 主導

            // 自動為未來的游泳/推動 Animator 傳遞狀態標籤 (若 Animator Controller 有對應 Parameter 則更新)
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == "isUnderwater") animator.SetBool("isUnderwater", isUnderwater);
                if (param.name == "isSwimming") animator.SetBool("isSwimming", isSwimming);
                if (param.name == "isPushing") animator.SetBool("isPushing", isPushing);
                if (param.name == "Pushing") animator.SetBool("Pushing", isPushing);
            }

            // 控制角色外觀模型轉向與水下按 W 向上 15 度仰角傾斜
            if (facingDirection != Vector3.zero)
            {
                // 點燃燭火且算過「手最靠近燭火」的角度：用那個角度；否則照左右朝向
                bool useCandleAim = _candleLightAnim && _hasCandleAimYaw && !isUnderwater;
                Quaternion baseRotation = useCandleAim
                    ? Quaternion.Euler(0f, _candleAimYaw, 0f)
                    : Quaternion.LookRotation(facingDirection);
                CalibrateLightHandIfReady();

                // ★0920 斜坡視覺貼合：站在坡上時外觀跟著坡面傾斜，離地平滑回正。
                //   ・只轉 animator 這個外觀子物件，剛體的 FreezeRotation 完全不動
                //   ・傾角繞畫面的 Z 軸、而且是「左乘」＝在世界空間疊加，所以左右朝向翻面時方向仍然正確
                //     （右乘會被 LookRotation 的 180° 轉向鏡射成反方向）
                //   ・水下維持原本的仰角演出，不疊坡度
                float targetTilt = 0f;
                if (enableSlopeVisualTilt && !isUnderwater && isGrounded && !isJumping)
                {
                    Vector3 n = GroundNormal;
                    targetTilt = Mathf.Clamp(Mathf.Atan2(-n.x, n.y) * Mathf.Rad2Deg,
                                             -maxSlopeVisualAngle, maxSlopeVisualAngle);
                    // ★1018 正在推巨石：身體不要跟著坡面往後倒（頭離石頭遠），站直，前傾交給推石動畫
                    if (isPushing && RollingRockVisual.ActivelyPushed != null) targetTilt *= pushingSlopeTiltKeep;
                }
                _slopeVisualAngle = Mathf.LerpAngle(_slopeVisualAngle, targetTilt,
                                                    1f - Mathf.Exp(-slopeVisualTiltSpeed * Time.deltaTime));
                if (Mathf.Abs(_slopeVisualAngle) > 0.01f)
                {
                    baseRotation = Quaternion.Euler(0f, 0f, _slopeVisualAngle) * baseRotation;
                }

                // 點燃燭火時身體轉向鏡頭一點，讓動作看得清楚
                //   面向右：順時針（從上往下看）轉；面向左：鏡射成逆時針，兩邊都是朝鏡頭轉
                if (_candleLightAnim && !useCandleAim && Mathf.Abs(lightAnimYawOffset) > 0.01f)
                {
                    float yaw = facingDirection.x >= 0f ? lightAnimYawOffset : -lightAnimYawOffset;
                    baseRotation = Quaternion.Euler(0f, yaw, 0f) * baseRotation;
                }

                // ★1014 划水式游泳：按住不代表在往上，只看實際是否正在上升
                bool isSwimmingUpward = isUnderwater && rb.linearVelocity.y > 0.3f;

                if (isSwimmingUpward)
                {
                    // 局部 X 軸旋轉 -underwaterSwimUpTiltAngle (-15度) 會將模型的頭部/朝向平滑抬高仰角
                    Quaternion targetRotation = baseRotation * Quaternion.Euler(-underwaterSwimUpTiltAngle, 0f, 0f);
                    animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation, targetRotation, Time.deltaTime * 12f);
                }
                else
                {
                    // 陸地移動（包含斜坡）：保持正立左右朝向，絕不往 Z 軸深處傾倒穿透背景貼圖
                    if (_candleLightAnim && lightTurnDegreesPerSecond > 0.01f)
                    {
                        // 點燃燭火時等速轉身，轉向燭台的過程看得出來，不會像瞬間翻面
                        animator.transform.rotation = Quaternion.RotateTowards(animator.transform.rotation, baseRotation, lightTurnDegreesPerSecond * Time.deltaTime);
                    }
                    else
                    {
                        animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation, baseRotation, Time.deltaTime * 12f);
                    }
                }
            }

            // ★1001 斜坡視覺空隙補償（純視覺，與上面的傾斜是同一組表現）
            UpdateSlopeVisualGapCompensation();

            string targetAnim = "Idle";

            // 判斷是否應該播放墜落動畫 (加入了延遲時間與掉落速度的容錯閥值)
            // ★1011 跳起來的那一段（isJumping）：落下動畫要比「一般跳躍從起跳到落地的時間」再晚一點才出現，
            //   所以普通跳躍落地前不會突然切成落下動畫；從高處掉下去、滯空更久時才會播。
            //   沒跳（走下平台邊緣）照 fallAnimationDelay。
            float fallDelay = fallAnimationDelay;
            if (isJumping && jumpFallAnimExtraDelay > 0f)
            {
                fallDelay = Mathf.Max(fallDelay, GetExpectedJumpAirTime() + jumpFallAnimExtraDelay);
            }
            bool rawFalling = !isGrounded && (currentAirTime >= fallDelay) && (rb.linearVelocity.y < fallVelocityThreshold);

            // ★1011 防抖：條件要連續成立 fallAnimDebounceSeconds 秒才真的切成落下動畫（速度剛好在門檻上下抖動時不會閃一下）；
            //   一落地（或條件消失）馬上放掉，落地不拖。
            _fallCandidateTime = rawFalling ? _fallCandidateTime + Time.deltaTime : 0f;
            bool isFalling = rawFalling && _fallCandidateTime >= fallAnimDebounceSeconds;

            // 判斷是否有水平速度或輸入
            bool hasHorizontalSpeed = Mathf.Abs(rb.linearVelocity.x) > 0.1f || Mathf.Abs(moveInput) > 0.1f;

            // 判斷鍵盤是否有按下任何移動按鍵 (A, D, W, Space, 方向鍵)
            bool hasKeyboardInput = Mathf.Abs(moveInput) > 0.05f 
                                 || Input.GetKey(KeyCode.W) 
                                 || Input.GetKey(KeyCode.Space) 
                                 || Input.GetKey(KeyCode.A) 
                                 || Input.GetKey(KeyCode.D) 
                                 || Input.GetKey(KeyCode.LeftArrow) 
                                 || Input.GetKey(KeyCode.RightArrow)
                                 || Input.GetKey(KeyCode.UpArrow);

            if (isUnderwater)
            {
                // 【水下絕對邏輯】：只要鍵盤沒有按鍵在動 ➔ 100% 播放 Treading Water；鍵盤有按鍵在動 ➔ 才播放 Swimming
                if (hasKeyboardInput)
                {
                    targetAnim = "Swimming";
                }
                else
                {
                    targetAnim = "Treading Water";
                }
                _candleLightAnim = false;
                _candleLightHold = false;
            }
            else
            {
                // 【陸地動畫邏輯】
                if (isFalling)
                {
                    targetAnim = "Falling";
                }
                else if (isPushing)
                {
                    targetAnim = "Pushing";
                }
                else
                {
                    targetAnim = hasHorizontalSpeed ? "Run" : "Idle";
                }

                // 點燃燭火：點燃中播點燃動作；點完停在最後一格，玩家一移動才放掉
                if (_candleLightAnim && !isFalling && !isPushing)
                {
                    if (_candleLightHold && (hasKeyboardInput || hasHorizontalSpeed))
                    {
                        _candleLightAnim = false;
                        _candleLightHold = false;
                    }
                    else
                    {
                        targetAnim = lightCandleAnimState;
                    }
                }
            }

            // 【最高層級】：如果正在播 Falling 動畫，且位於掉落區，啟動絕對 X 軸鎖死
            if (isFalling && isInDropZone)
            {
                if (!isStrictLockingX)
                {
                    isStrictLockingX = true;
                    lockedXValue = transform.position.x;
                    Debug.Log($"【最高規則生效】開始絕對鎖死 X 座標於: {lockedXValue}");
                }
            }
            else if (isGrounded)
            {
                // 落地解除鎖死
                if (isStrictLockingX) Debug.Log("【最高規則解除】已落地，解除 X 軸鎖定。");
                isStrictLockingX = false;
            }

            // 【強制接管播放】使用 Play 直接切換指定 State (加入安全保護)
            if (currentAnimState != targetAnim)
            {
                try
                {
                    // ★1011 進出落下動畫用淡入淡出（原本是硬切，姿勢瞬間跳一下，看起來像抽筋）；其他動畫維持原本的直接切換
                    bool involvesFalling = targetAnim == "Falling" || currentAnimState == "Falling";
                    // ★1014 水下 游泳 ↔ 踩水 也用淡入淡出：放開方向鍵時身體從平躺 (~73°) 慢慢抬回直立踩水 (~34°)，不再像兩張 GIF 硬切
                    float blend = 0f;
                    if (currentAnimState == "Swimming" && targetAnim == "Treading Water") blend = swimToTreadBlendSeconds;
                    else if (currentAnimState == "Treading Water" && targetAnim == "Swimming") blend = treadToSwimBlendSeconds;
                    // 奔跑 ↔ 站立 也用淡入淡出，停下來時不會像換圖片一樣瞬間跳成站姿
                    else if (currentAnimState == "Run" && targetAnim == "Idle") blend = runToIdleBlendSeconds;
                    else if (currentAnimState == "Idle" && targetAnim == "Run") blend = idleToRunBlendSeconds;

                    if (blend > 0.001f)
                    {
                        animator.CrossFadeInFixedTime(targetAnim, blend, 0);
                    }
                    else if (involvesFalling && fallAnimBlendSeconds > 0.001f)
                    {
                        if (targetAnim == "Falling")
                            Debug.Log("【落下動畫】開始：滯空 " + currentAirTime.ToString("F2") + " 秒、垂直速度 " + rb.linearVelocity.y.ToString("F1") + "、起跳過=" + isJumping + "、位置 " + transform.position.ToString("F1"));
                        animator.CrossFadeInFixedTime(targetAnim, fallAnimBlendSeconds, 0);
                    }
                    else
                    {
                        animator.Play(targetAnim);
                    }
                    currentAnimState = targetAnim;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[動畫安全保護] 找不到動畫 State '{targetAnim}'，跳過切換。Error: {e.Message}");
                }
            }
        }
        else
        {
            Debug.LogError("[動畫診斷] animator 是 NULL！請確認皮套子物件上有 Animator 組件，或在 Inspector 手動指定！");
        }

        // ==========================================
        // 3. 處理抓取與移動 (恢復原本的邏輯)
        // ==========================================
        if (Input.GetKeyDown(KeyCode.LeftShift)) TryGrabObject();
        if (Input.GetKeyUp(KeyCode.LeftShift)) ReleaseObject();

        // 防呆校正：沒被狼咬的時候，速度一律回到 baseSpeed
        // ★0910：原本的條件是「attachedWolvesCount == 0 || currentSpeed <= 0f」。
        //   那個 currentSpeed <= 0f 會把「被咬滿速度歸零」也當成異常救回來——
        //   第 6 隻狼咬住讓速度變 0，下一幀就被這行重設成 baseSpeed，
        //   玩家身上掛著 6 隻狼卻用全速跑，減速機制等於整個失效。
        //   速度 0 在滿狼時是正確狀態，不是壞掉，所以只留「沒狼才重置」。
        if (attachedWolvesCount == 0)
        {
            currentSpeed = baseSpeed;
        }

        float finalSpeed = currentSpeed * (isUnderwater ? underwaterHorizontalSpeedMultiplier : 1.0f);
        if (isWindSuctionActive)
        {
            finalSpeed = windSuctionSpeed;
        }
        if (pulledObject != null)
        {
            // 強化型剛體搜尋：防呆，以防推拉的碰撞器 (Collider) 與剛體 (Rigidbody) 不在同一個物件層級上
            Rigidbody pulledRb = pulledObject.GetComponent<Rigidbody>();
            if (pulledRb == null) pulledRb = pulledObject.GetComponentInParent<Rigidbody>();
            if (pulledRb == null) pulledRb = pulledObject.GetComponentInChildren<Rigidbody>();

            if (pulledRb != null)
            {
                // 動態重量比率：主角自身重量為 10f。拉動物體越重，速度越慢。
                // 速度比例 = 10f / (10f + 物體質量)。
                // 例如：物體質量 10f -> 速度減半；物體質量 90f -> 速度變 1/10；物體質量 500f -> 幾乎拉不動。
                float weightFactor = 10f / (10f + pulledRb.mass);
                finalSpeed = finalSpeed * weightFactor;
            }
            else
            {
                finalSpeed = finalSpeed / 2f; // 備用方案
            }

            // 往後拉 (移動方向與面向相反) 時額外減速，凸顯物件重量
            if (Mathf.Abs(moveInput) > 0.05f && moveInput * facingDirection.x < 0f)
            {
                finalSpeed *= pullSpeedMultiplier;
            }
        }

        if (actuallyFreeze || isStrictLockingX)
        {
            rb.useGravity = true;
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, rb.linearVelocity.z);
        }
        else
        {
            bool isOnSlope = isGrounded && !isJumping && (currentSlopeAngle > 0.5f && currentSlopeAngle < 60f)
                             && !(isUnderwater && underwaterIgnoreSlopeStick);   // ★1016 水下不走斜坡定住分支

            // ★1001 狼拖曳負載：先歸零，只有真正走到斜坡＋有輸入那條路徑才會被填值。
            //   這樣平地、空中、水下、演出鎖定一律保證是 0，不會有殘留的 Y 漂移。
            WolfDragDownSpeed = 0f;

            // 計算逆風推力偏移量
            float windOffset = 0f;
            if (_windPushTimer > 0f)
            {
                _windPushTimer -= Time.deltaTime;
                windOffset = _windPushVelocityX;
            }

            // ★ 風推卡住偵測 (不依賴射線，撞到什麼都有效)：
            //   被風推著、卻連續數幀幾乎沒有實際位移 → 一定是被石柱/牆面擋住了。
            //   此時若繼續每幀把速度灌進障礙物裡，PhysX 會不斷把玩家擠出來，那就是抖動的來源。
            //   直接把風推視為 0，玩家就穩穩貼著障礙物不動。
            if (IsWindPushBlocked(windOffset))
            {
                windOffset = 0f;
            }

            if (isOnSlope)
            {
                // ★★★ 核心修復：在斜坡上關閉 Unity PhysX 重力！
                rb.useGravity = false;

                if (Mathf.Abs(moveInput) > 0.05f)
                {
                    // 玩家主動按鍵移動：貼合斜坡移動並疊加逆風阻力
                    Vector3 moveDir = new Vector3(Mathf.Sign(moveInput), 0, 0);
                    Vector3 slopeDir = Vector3.ProjectOnPlane(moveDir, groundHit.normal).normalized;
                    float slopeSpeed = finalSpeed * Mathf.Abs(moveInput);

                    // ★1001 狼咬住的向下拖曳：沿「坡面往下」的方向疊加一段速度。
                    //   ・方向用 world down 投影到坡面，所以是真正的順坡向下，不是粗暴的世界 −Y
                    //   ・平地時這個投影長度為 0（而且平地根本進不到 isOnSlope 這條分支），雙重保險
                    //   ・大小由 ComputeWolfDragDown 夾三道上限：每狼量、總上限、當下沿坡速度的比例
                    Vector3 dragVec = Vector3.zero;
                    Vector3 downSlope = Vector3.ProjectOnPlane(Vector3.down, groundHit.normal);
                    if (downSlope.sqrMagnitude > 0.000001f)
                    {
                        float drag = ComputeWolfDragDown(slopeSpeed);
                        WolfDragDownSpeed = drag;
                        if (drag > 0f) dragVec = downSlope.normalized * drag;
                    }

                    Vector3 targetVelocity = new Vector3(
                        slopeDir.x * slopeSpeed + windOffset + dragVec.x,
                        slopeDir.y * slopeSpeed + dragVec.y,
                        0f
                    );

                    // ★1001 在加上移動平台之前記錄：這是她自己的推進力（含減速／拖曳／坡度折算／風）
                    CommandedHorizontalSpeed = targetVelocity.x;

                    if (activeMovingPlatform != null)
                    {
                        targetVelocity.x += activeMovingPlatform.Velocity.x;
                    }

                    rb.linearVelocity = targetVelocity;
                }
                else if (_externalPushTimer > 0f)
                {
                    _externalPushTimer -= Time.deltaTime;
                    Vector3 pushSlopeDir = Vector3.ProjectOnPlane(_externalPushVelocity, groundHit.normal);
                    float pushX = pushSlopeDir.x + windOffset;

                    // 斜坡上同樣要防止被外力/風推進石柱裡造成抖動
                    if (IsBlockedByObstacle(pushX))
                    {
                        rb.linearVelocity = new Vector3(0f, 0f, 0f);
                    }
                    else
                    {
                        rb.linearVelocity = new Vector3(pushX, pushSlopeDir.y, 0f);
                    }
                }
                else if (Mathf.Abs(windOffset) > 0.01f)
                {
                    // 在斜坡上受到風吹：順著斜坡向後平滑滑動
                    // ★ 若後方緊貼不可推的石柱/牆面，停止繼續施加風推速度，否則 PhysX 每幀把玩家擠出來就是抖動
                    if (IsBlockedByObstacle(windOffset))
                    {
                        rb.linearVelocity = Vector3.zero;
                    }
                    else
                    {
                        Vector3 windDir = new Vector3(Mathf.Sign(windOffset), 0, 0);
                        Vector3 windSlopeDir = Vector3.ProjectOnPlane(windDir, groundHit.normal).normalized;
                        rb.linearVelocity = windSlopeDir * Mathf.Abs(windOffset);
                    }
                }
                else
                {
                    // 靜止站在斜坡上：完全消除所有軸向速度，剛體穩固靜止於斜坡上
                    Vector3 targetVelocity = Vector3.zero;
                    if (activeMovingPlatform != null)
                    {
                        targetVelocity.x += activeMovingPlatform.Velocity.x;
                    }
                    rb.linearVelocity = targetVelocity;
                }
            }
            else
            {
                // 平地、空中或水下：開啟正常重力
                rb.useGravity = true;

                float targetX = (moveInput * finalSpeed) + windOffset;

                // 夾角推移緩衝：如果玩家在平地受到巨石撞擊且未按移動鍵，順應巨石推力平滑滑動
                if (Mathf.Abs(moveInput) <= 0.05f && _externalPushTimer > 0f)
                {
                    _externalPushTimer -= Time.deltaTime;
                    targetX = (_externalPushVelocity.x * 0.85f) + windOffset;
                }

                // ★★★ 實體障礙物/石柱防穿透夾角緩衝判定 (防止強風或推力把玩家頂進不可推動石柱牆面造成的 PhysX 每幀穿透反彈劇烈抖動)
                if (IsBlockedByObstacle(targetX))
                {
                    targetX = 0f; // 緊貼非可推石柱/障礙物時停止繼續施加擠壓速度，完全消除抖動！
                }

                Vector3 targetVelocity = new Vector3(targetX, rb.linearVelocity.y, rb.linearVelocity.z);

                // ★1001 平地／空中／水下也記錄同一個語意的值（見 CommandedHorizontalSpeed 的說明）
                CommandedHorizontalSpeed = targetVelocity.x;

                if (isGrounded && !isJumping && !isUnderwater && Mathf.Abs(moveInput) <= 0.05f && _externalPushTimer <= 0f && Mathf.Abs(windOffset) <= 0.01f && Mathf.Abs(rb.linearVelocity.y) < 0.1f)
                {
                    targetVelocity.y = 0f;
                }

                if (activeMovingPlatform != null)
                {
                    targetVelocity.x += activeMovingPlatform.Velocity.x;
                }

                rb.linearVelocity = targetVelocity;
            }
        }

        // ★1014 水下划水 (實際推進在 FixedUpdate)：
        //   按一下 → 划一下；按住 → 每隔 swimHoldRepeatInterval 自動再划一下，直到體力耗盡
        bool swimUpDown = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow);
        bool swimUpHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow);
        if (!swimUpHeld) _swimHoldBlocked = false;
        if (!IsControlLocked && isUnderwater)
        {
            if (swimUpDown)
            {
                _swimStrokeQueued = true;
                _swimHoldBlocked = false;
            }
            else if (swimUpHeld && !_swimHoldBlocked && Time.time - lastSwimTime >= swimHoldRepeatInterval)
            {
                _swimStrokeQueued = true;
            }
        }

        // 水下隱形體力：每划一下扣一次 (在 FixedUpdate 划出去時扣)，停止划水一段時間後才回復
        if (isSwimExhausted)
        {
            exhaustionTimer -= Time.deltaTime;
            if (exhaustionTimer <= 0f)
            {
                isSwimExhausted = false;
                Debug.Log("【水下體力機制】力竭冷卻結束，恢復向上游泳機能！");
            }
        }
        else if (isGrounded || (!swimUpHeld && Time.time - lastSwimTime >= swimStaminaRegenDelay))
        {
            currentSwimStamina = Mathf.Min(maxSwimStamina, currentSwimStamina + Time.deltaTime * swimStaminaRegenRate);
        }

        // ★1013 力竭提示：黑圈邊緣亮一小圈紅
        if (UnderwaterSuffocationEffect.Instance != null)
        {
            // ★1015 紅光只在「力竭、而且她還想往上游」時亮（按著或剛按過往上鍵）。
            //   以前力竭一發生就亮，划到一半紅光突然冒出來，看起來像無緣無故的警示。
            bool wantsUp = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow);
            if (wantsUp) _lastSwimWantTime = Time.time;
            bool tryingWhileExhausted = isUnderwater && isSwimExhausted && (Time.time - _lastSwimWantTime) < 0.4f;
            UnderwaterSuffocationEffect.Instance.SetSwimExhausted(tryingWhileExhausted);
        }

        // 陸地跳躍 (非水下、非演出中、非重生中)
        bool _jumpPressed = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space);

        // ★按了跳躍卻沒起跳時，直接指名是哪個條件擋住的
        // (水下按 W 是划水，不是跳躍，不必警告)
        if (_jumpPressed && !isUnderwater && (IsControlLocked || !isGrounded))
        {
            // 一行寫完，Console 不用展開就看得到
            Debug.LogWarning("🚫跳不了 froz=" + (isCutsceneFrozen ? "T" : "F")
                + " hard=" + (IsHardCutsceneLocked ? "T" : "F")
                + "(生" + (PlayerRespawnSystem.IsAnyRespawning ? "T" : "F")
                + " 鏡" + (MirrorWallAbsorbCutscene.IsAnyCutsceneRunning ? "T" : "F")
                + " 怪" + (ShadowMonsterController.IsAnyRevealRunning ? "T" : "F")
                + " 卡" + ((StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying) ? "T" : "F") + ")"
                + " 水=" + (isUnderwater ? "T" : "F")
                + " 地=" + (isGrounded ? "T" : "F")
                + " 跳中=" + (isJumping ? "T" : "F")
                + " kine=" + ((rb != null && rb.isKinematic) ? "T" : "F")
                + " y=" + (rb != null ? rb.linearVelocity.y.ToString("F1") : "-")
                + " x=" + transform.position.x.ToString("F1"));
        }

        if (!IsControlLocked && !isUnderwater && _jumpPressed && isGrounded && !IsJumpBlocked)
        {
            // 陸地標準跳躍
            isJumping = true;
            rb.useGravity = true; // 跳躍時立即還原重力
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);

            // ★ 起跳瞬間零延遲切斷腳步聲音量，完美銜接起跳音效！
            if (_footstepSource != null)
            {
                _footstepSource.volume = 0f;
                _footstepSource.Stop();
            }

            // 播放起跳音效
            if (jumpSFX != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX(jumpSFX, sfxVolume);
            }
        }
    }


    void LateUpdate()
    {
        // ==========================================
        // 【最高層級防破圖系統】確保玩家絕對不出界
        // ==========================================
        if (isStrictLockingX)
        {
            Vector3 finalPos = transform.position;
            finalPos.x = lockedXValue;
            finalPos.z = initialZ;
            transform.position = finalPos;
        }
        else if (Mathf.Abs(transform.position.z - initialZ) > 0.01f)
        {
            Vector3 finalPos = transform.position;
            finalPos.z = initialZ;
            transform.position = finalPos;
        }

        // ==========================================
        // 攝影機 Y 軸避震系統
        // ==========================================
        if (smoothCameraY && cameraTarget != null)
        {
            // 效能：GameObject.Find 是整個場景比對名字，原本每幀都跑。快取起來，找不到時每秒才再找一次
            if (_followTargetObj == null && Time.time >= _nextFollowTargetFind)
            {
                _nextFollowTargetFind = Time.time + 1f;
                _followTargetObj = GameObject.Find("CameraFollowTarget");
            }
            GameObject customTarget = _followTargetObj;
            if (customTarget != null)
            {
                cameraTarget.position = customTarget.transform.position;
            }
            else
            {
                // ★ X 和 Z 軸始終跟緊玩家真實位置（包含 FallingBackground 墜落中）
                float targetX = transform.position.x;
                float targetZ = transform.position.z;

                float currentDamping;
                if (freezeHorizontal && !isGrounded)
                {
                    // FallingBackground 墜落中：Y 軸也幾乎無延遲，確保攝影機不掉隊
                    currentDamping = 0.01f;
                }
                else
                {
                    currentDamping = cameraYDamping;
                }

                // Y 軸 SmoothDamp（吸收跳躍碎震）；X/Z 無延遲死死跟著玩家
                float newY = Mathf.SmoothDamp(cameraTarget.position.y, transform.position.y, ref _smoothYVelocity, currentDamping);

                if (clampSkyBackgroundHeight)
                {
                    // 限制在 SkyBackground 高度範圍內 (不論主角上下移動，鏡頭視野絕不超出 SkyBackground 頂部與底部)
                    // 快取計算 SkyBackground 包圍盒，避免每幀執行全場景 FindObjectsByType 造成卡頓
                    if (!_hasCachedSkyBounds || Time.time >= _nextSkyBoundsCheckTime)
                    {
                        // 效能：這一掃是全場景所有物件，原本每 2 秒掃一次會週期性頓一下。
                        //   天空背景不會動，找到後 30 秒才再確認一次；沒找到也放慢到 10 秒。
                        _nextSkyBoundsCheckTime = Time.time + (_hasCachedSkyBounds ? 30.0f : 10.0f);
                        _cachedSkyBounds = new Bounds();
                        _hasCachedSkyBounds = false;

                        GameObject[] skyGos = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                        foreach (var go in skyGos)
                        {
                            if (go != null && go.name.Contains("SkyBackground") && go.activeInHierarchy)
                            {
                                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                                Collider col = go.GetComponent<Collider>();
                                Bounds b = new Bounds();
                                if (sr != null && sr.sprite != null) b = sr.bounds;
                                else if (col != null) b = col.bounds;

                                if (b.size.sqrMagnitude > 0.1f)
                                {
                                    if (!_hasCachedSkyBounds) { _cachedSkyBounds = b; _hasCachedSkyBounds = true; }
                                    else { _cachedSkyBounds.Encapsulate(b); }
                                }
                            }
                        }
                    }

                    if (_hasCachedSkyBounds)
                    {
                        Camera mainCam = Camera.main;
                        float halfHeight = (mainCam != null && mainCam.orthographic) ? mainCam.orthographicSize : 10f;
                        float skyMinY = _cachedSkyBounds.min.y + halfHeight;
                        float skyMaxY = _cachedSkyBounds.max.y - halfHeight;
                        if (skyMinY <= skyMaxY) newY = Mathf.Clamp(newY, skyMinY, skyMaxY);
                        else newY = _cachedSkyBounds.center.y;
                    }
                }

                cameraTarget.position = new Vector3(targetX, newY, targetZ);
            }
        }


    }

    // ==========================================
    // 狼群減速邏輯
    // ==========================================
    /// <summary>★1008 現在咬住幾隻就重生：wolvesToRespawn；開了 packKillWhenAllBite 時，在場（已出場、正在追或咬住，
    /// 含硬直中）的狼比它少就以在場數為門檻，最少 packKillMinWolves 隻。FirstWolfDeathStory 也用這個判定「被狼咬滿」。</summary>
    public int WolfKillThreshold
    {
        get
        {
            int killW = Mathf.Max(1, wolvesToRespawn);
            if (!packKillWhenAllBite) return killW;
            int pack = 0;
            foreach (WolfEnemy w in FindObjectsByType<WolfEnemy>(FindObjectsInactive.Exclude))
            {
                if (w != null && w.enabled) pack++;   // 出生點生出來但還沒啟動追擊的狼 enabled＝false，不算在場
            }
            if (pack <= 0) return killW;
            return Mathf.Max(Mathf.Max(1, packKillMinWolves), Mathf.Min(killW, pack));
        }
    }

    public void AddWolf()
    {
        attachedWolvesCount++;
        BitesTaken++;   // ★1010 累計口數
        CalculateSpeed();

        int maxW = Mathf.Max(1, Mathf.RoundToInt(maxWolvesToStop));
        int killW = WolfKillThreshold;   // ★1001 死亡門檻改用獨立欄位；★1008 在場的狼全部咬上來也算（見 WolfKillThreshold）
        float pct = baseSpeed > 0.001f ? (currentSpeed / baseSpeed) * 100f : 0f;
        Debug.Log($"狼咬！第 {BitesTaken} 口（{(countBitesCumulative ? "累計口數" : "同時咬住")}算死，門檻 {killW}），目前身上有 {attachedWolvesCount} 隻狼（減速分母 {maxW}），玩家速度：{currentSpeed:F2} ({pct:F0}%)");

        // ★1002 巨石挑戰進行中：第一隻咬到只啟動巨石壓迫，狼咬計數仍繼續累積。
        //   控制器沒設定好、或挑戰還沒開始（石頭還沒解鎖）時，仍完全走原本狼咬流程。
        if (BoulderChallengeController.NotifyWolfBite(this)) return;

        // 咬滿就死。TriggerRespawn 內部有 _isRespawning 防重入，不會重複觸發，這裡不另外做旗標。
        // ★1010 改看 WolfBiteCountForDeath：預設是累計口數（咬三口死），不是同時咬住的隻數
        if (respawnWhenMaxWolves && WolfBiteCountForDeath >= killW)
        {
            PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = GetComponentInParent<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = GetComponentInChildren<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = FindAnyObjectByType<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = FindFirstObjectByType<PlayerRespawnSystem>();
            #pragma warning disable CS0618
            if (respawnSystem == null) respawnSystem = (PlayerRespawnSystem)FindObjectOfType(typeof(PlayerRespawnSystem));
            #pragma warning restore CS0618

            if (respawnSystem != null)
            {
                Debug.Log("【狼咬致死】累積狼咬達到上限，觸發重生系統！");
                respawnSystem.TriggerRespawn();
            }
            else
            {
                Debug.LogError("【狼咬致死】找不到 PlayerRespawnSystem 組件！無法觸發重生！請確認場景中是否有任何物件掛載了 PlayerRespawnSystem 腳本。");
            }
        }
    }

    public void RemoveWolf()
    {
        attachedWolvesCount--;
        if (attachedWolvesCount < 0) attachedWolvesCount = 0;   // 夾在 0：重複呼叫不會扣成負的
        CalculateSpeed();

        float pct = baseSpeed > 0.001f ? (currentSpeed / baseSpeed) * 100f : 0f;
        Debug.Log($"狼鬆口！目前身上有 {attachedWolvesCount} 隻狼，玩家速度恢復為：{currentSpeed:F2} ({pct:F0}%)");
    }

    /// <summary>
    /// 依「咬住幾隻狼」等比例算出玩家速度。這是唯一的速度懲罰來源，
    /// 也是唯一的死亡門檻依據，沒有第二套計數。
    ///     速度 = baseSpeed × (1 − attachedWolvesCount / maxWolvesToStop)
    /// 所以 maxWolvesToStop 改成幾，比例就自動跟著換，不用改程式。
    /// </summary>
    private void CalculateSpeed()
    {
        float maxWolves = Mathf.Max(1f, maxWolvesToStop);   // 防除以 0：Inspector 被填成 0 會炸
        float ratio = 1f - (attachedWolvesCount / maxWolves);
        currentSpeed = Mathf.Max(0f, baseSpeed * ratio);    // 最低就是 0，不會倒退
    }

    /// <summary>
    /// ★1001 這一幀要沿坡面往下拖曳多少（公尺/秒，永遠 ≥ 0，方向由呼叫端補上）。
    ///
    /// 三道夾制，順序就是設計上的優先權：
    ///   1. 每狼量：wolfDragDownPerWolf × 咬住的狼數
    ///   2. 總上限：wolfDragDownMax（狼再多也不會更強）
    ///   3. 保護上限：當下沿坡速度 × wolfDragDownMaxSpeedFraction
    ///      → 這一道保證「拖曳永遠小於她自己的速度」，所以她絕對不會被拖著倒退下坡，
    ///        也不會出現「被咬住就永遠爬不上去」。
    ///
    /// 這裡刻意直接讀既有的 attachedWolvesCount，不另外做一套計數：
    /// 咬住／鬆口／重生歸零全部沿用 AddWolf／RemoveWolf／WarpTo 既有的流程，
    /// 所以狼一鬆口就自動歸零，也不會在沒有狼的時候殘留。
    /// </summary>
    private float ComputeWolfDragDown(float currentSlopeSpeed)
    {
        if (attachedWolvesCount <= 0 || wolfDragDownPerWolf <= 0f) return 0f;

        float raw = wolfDragDownPerWolf * attachedWolvesCount;
        float capped = Mathf.Min(raw, wolfDragDownMax);
        float protectionCap = Mathf.Max(0f, currentSlopeSpeed) * wolfDragDownMaxSpeedFraction;
        return Mathf.Min(capped, protectionCap);
    }

    /// <summary>
    /// ★1001 斜坡視覺空隙補償（純視覺，一行都不碰物理）。
    ///
    /// Root Cause：剛體鎖了 FreezeRotation，BoxCollider 永遠保持水平。水平的盒子放在斜坡上
    /// 只有下坡側的底角碰得到坡面，所以底面中心必然懸在坡面上方「半寬 × tan(坡度)」。
    /// 廢墟山坡 34.648°、玩家世界半寬 1.064 → 約 0.736 公尺。這不是穿透、也不是懸空 bug，
    /// 碰撞是成立的（角接觸），只是外觀看起來浮著。
    ///
    /// 修法沿用 WolfEnemy.slopeVisualYOffset 的既有模式：把外觀子物件往下移該補償量。
    /// 差別是這裡直接用碰撞盒實際半寬與當下坡度算，不寫死數字，所以換場景（玩家 scale 不同）自動適應。
    /// </summary>
    private void UpdateSlopeVisualGapCompensation()
    {
        if (!_hasVisualBase || _visualRoot == null) return;

        float targetLocalY = _visualBaseLocalY;

        if (slopeVisualGapFactor > 0.0001f && !isUnderwater && isGrounded && !isJumping && playerCollider != null)
        {
            float angle = GroundSlopeAngle;
            // 坡度範圍與 isOnSlope／視覺傾斜完全一致；平地與過陡（視為牆）都不補償
            if (angle > 0.5f && angle < 60f)
            {
                float worldGap = playerCollider.bounds.extents.x
                               * Mathf.Tan(angle * Mathf.Deg2Rad)
                               * slopeVisualGapFactor;

                // ★座標空間：localPosition 是父物件的空間，世界單位不能直接當 local 用，
                //   必須除掉父物件累積的 Y 縮放（玩家根物件在 SampleScene 是 1.776、荒原是 1.506）。
                float parentScaleY = (_visualRoot.parent != null) ? _visualRoot.parent.lossyScale.y : 1f;
                if (Mathf.Abs(parentScaleY) > 0.0001f)
                {
                    targetLocalY = _visualBaseLocalY - (worldGap / parentScaleY);
                }
            }
        }

        // 沿用視覺傾斜同一組平滑參數，不瞬間跳位置
        Vector3 lp = _visualRoot.localPosition;
        lp.y = Mathf.Lerp(lp.y, targetLocalY, 1f - Mathf.Exp(-slopeVisualTiltSpeed * Time.deltaTime));
        _visualRoot.localPosition = lp;
    }

    // ==========================================
    // 抓取物件邏輯 (Pull Range 可在 Inspector 自訂調整)
    // ==========================================
    void TryGrabObject()
    {
        Vector3 center = (playerCollider != null) ? playerCollider.bounds.center : transform.position;
        Vector3 extents = (playerCollider != null) ? playerCollider.bounds.extents : new Vector3(0.6f, 1.0f, 0.6f);
        Vector3 rayOrigin = center + facingDirection * (extents.x * 0.75f);

        Debug.DrawRay(rayOrigin, facingDirection * pullRange, Color.red, 2f);
        // ★1014 原本用半徑 0.4 的 SphereCast，只掃玩家中心那一個 z 深度。
        //   玩家站在 z≈-0.9、箱子在 z=0 而且很薄 (±0.1)，球從箱子前面擦過去 → 抓不到，
        //   結果 Shift 只剩「身體頂著往前推」，不能往後拉。改用涵蓋玩家整個 z 厚度的 BoxCast。
        Vector3 halfExtents = new Vector3(0.3f, extents.y * 0.7f, Mathf.Max(0.4f, extents.z));
        RaycastHit[] hits = Physics.BoxCastAll(rayOrigin, halfExtents, facingDirection, Quaternion.identity, pullRange);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.gameObject == this.gameObject || hit.collider.transform.IsChildOf(transform)) continue;
            if (IsPushableObject(hit.collider.gameObject, hit.collider))
            {
                // 優先抓取帶有 Rigidbody 的根物件
                GameObject targetObj = hit.collider.gameObject;
                if (targetObj.GetComponent<Rigidbody>() == null && targetObj.GetComponentInParent<Rigidbody>() != null)
                {
                    targetObj = targetObj.GetComponentInParent<Rigidbody>().gameObject;
                }

                pulledObject = targetObj;
                pulledObject.transform.SetParent(this.transform);
                Rigidbody objRb = pulledObject.GetComponent<Rigidbody>();
                if (objRb != null)
                {
                    objRb.isKinematic = true;
                }
                return; 
            }
        }
    }

    public void ReleaseObject()
    {
        if (pulledObject != null)
        {
            pulledObject.transform.SetParent(null);
            Rigidbody objRb = pulledObject.GetComponent<Rigidbody>();
            if (objRb != null)
            {
                objRb.isKinematic = false;
            }
            pulledObject = null;
        }
    }

    private Vector3 _externalPushVelocity = Vector3.zero;
    private float _externalPushTimer = 0f;
    private float _windPushVelocityX = 0f;
    private float _windPushTimer = 0f;

    /// <summary>
    /// 接收來自荒漠強風的逆風推力，平滑融入玩家運動學速度，徹底消除物理 AddForce 產生的抽搐與抖動！
    /// </summary>
    public void ApplyWindPush(float pushSpeedX)
    {
        _windPushVelocityX = pushSpeedX;
        _windPushTimer = 0.15f; // 短暫維持緩衝
    }

    /// <summary>
    /// 前方是否貼著「不可推動的實體障礙物」(石柱、牆面)。
    /// 用於在被風/外力推向障礙物時停止繼續施加擠壓速度，避免 PhysX 每幀穿透反彈造成抖動。
    /// ★ 射線長度改用碰撞體實際 bounds 寬度 (原本用 GetPlayerRadius() 在膠囊/子物件縮放時會算得太短而射不到)。
    /// </summary>
    private bool IsBlockedByObstacle(float directionX)
    {
        if (Mathf.Abs(directionX) < 0.01f) return false;
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        if (playerCollider == null) return false;

        int obstacleMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI");
        Bounds b = playerCollider.bounds;
        Vector3 centerPos = b.center;

        Vector3 dir = directionX < 0f ? Vector3.left : Vector3.right;
        float normalSignNeeded = directionX < 0f ? 1f : -1f;
        // 只有 underwater 場景用較短的偵測距離 (石頭密集，0.25 會讓玩家像被空氣牆擋住)；其餘場景維持原本的距離
        bool isUnderwaterScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            .IndexOf("underwater", System.StringComparison.OrdinalIgnoreCase) >= 0;
        float rayLen = b.extents.x + (isUnderwaterScene ? underwaterObstacleLookahead : landObstacleLookahead);

        // 多點高度取樣：石柱造型不規則，只掃兩點很容易剛好射空
        float[] heights = { -0.45f, -0.15f, 0.15f, 0.45f };

        foreach (float h in heights)
        {
            Vector3 origin = centerPos + Vector3.up * (b.extents.y * h);
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, rayLen, obstacleMask, QueryTriggerInteraction.Ignore)) continue;
            if (hit.collider == null || hit.collider.isTrigger) continue;
            if (IsPushableObject(hit.collider.gameObject, hit.collider)) continue;
            if (hit.normal.x * normalSignNeeded > 0.15f) return true;
        }

        return false;
    }

    // ── 風推卡住偵測 ───────────────────────────────────────────────
    /// <summary>
    /// 重置游泳體力。重生時必須呼叫：
    /// 體力原本只在 Start() 設定一次，若玩家是在力竭狀態下溺斃，重生後仍然是力竭，
    /// 一浮上來就掉下去，很容易直接進入「重生 → 沉底 → 再溺斃」的死亡迴圈。
    /// </summary>
    public void ResetSwimStamina()
    {
        currentSwimStamina = maxSwimStamina;
        isSwimExhausted = false;
        exhaustionTimer = 0f;
        isSwimming = false;
    }

    private float _windStuckLastX;
    private float _windStuckTimer;
    private bool _windStuckInitialized;

    /// <summary>
    /// 被風推著但實際上幾乎沒有位移，代表被實體障礙物擋住了。
    /// 不依賴射線，撞到石柱、牆面、任何造型的碰撞體都有效。
    /// </summary>
    private bool IsWindPushBlocked(float windOffset)
    {
        if (Mathf.Abs(windOffset) < 0.01f)
        {
            _windStuckTimer = 0f;
            _windStuckLastX = transform.position.x;
            _windStuckInitialized = true;
            return false;
        }

        if (!_windStuckInitialized)
        {
            _windStuckLastX = transform.position.x;
            _windStuckInitialized = true;
            return false;
        }

        float movedX = Mathf.Abs(transform.position.x - _windStuckLastX);
        _windStuckLastX = transform.position.x;

        // 這一幀理論上應該被推動的距離
        float expectedX = Mathf.Abs(windOffset) * Time.deltaTime;

        if (expectedX > 0.0001f && movedX < expectedX * 0.3f)
        {
            _windStuckTimer += Time.deltaTime;
        }
        else
        {
            _windStuckTimer = 0f;
        }

        // 連續 0.06 秒推不動才判定卡住，避免正常減速的瞬間被誤判
        bool blocked = _windStuckTimer >= 0.06f;

        if (blocked && logWindBlockDebug && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[風推卡住] 風速 {windOffset:F2}，本幀實際位移 {movedX:F4} (預期 {expectedX:F4})，判定被擋住，暫停施加風推。");
        }

        return blocked;
    }

    private float GetPlayerRadius()
    {
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        if (playerCollider is CapsuleCollider cc) return cc.radius * transform.localScale.x;
        if (playerCollider is BoxCollider bc) return bc.size.x * 0.5f * transform.localScale.x;
        return 0.4f;
    }

    /// <summary>
    /// 接收來自落石/巨石的外部推力，順勢沿斜坡下滑化解碰撞抖動
    /// </summary>
    public void ApplyExternalSlopePush(Vector3 pushVelocity)
    {
        _externalPushVelocity = pushVelocity;
        _externalPushTimer = 0.12f; // 維持緩衝
    }

    public Transform GetCameraTarget()
    {
        GameObject customTarget = GameObject.Find("CameraFollowTarget");
        if (customTarget != null) return customTarget.transform;
        return this.transform;
    }

    /// <summary>
    /// 瞬間傳送玩家並重置攝影機跟隨點，防止 Y 軸追蹤延遲與碎震
    /// </summary>
    public void WarpTo(Vector3 position)
    {
        EndControlledFall("被傳送");   // ★1012 重生／傳送一律把物理還給剛體
        transform.position = position;
        if (rb != null)
        {
            rb.position = position;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (cameraTarget != null)
        {
            cameraTarget.position = position;
        }

        // 重置身上所有被狼咬住的計數，並恢復速度
        attachedWolvesCount = 0;
        BitesTaken = 0;   // ★1010 重生／傳送：累計口數歸零
        CalculateSpeed();
        _smoothYVelocity = 0f; // 重置 Y 軸平滑速度快取
        
        isStrictLockingX = false;
        freezeHorizontal = false;
        
        // 只有在非重生期間才解鎖劇情凍結 (防止黑屏期間可按 WASD 亂跑)
        if (!PlayerRespawnSystem.IsAnyRespawning)
        {
            isCutsceneFrozen = false;
        }
        
        // 瞬間將主相機對齊，防止 Cinemachine 出現大跨度拉扯
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            // 重置 Cinemachine 狀態
            CinemachineCamera[] vcams3 = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcams3)
            {
                vcam.PreviousStateIsValid = false;
            }
            CinemachineVirtualCamera[] vcamsLegacy = FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcamsLegacy)
            {
                vcam.PreviousStateIsValid = false;
            }
        }
    }


    // ==========================================
    // 觸發特定背景邏輯 (FallingBackground)
    // ==========================================
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallingBackground"))
        {
            // ★ 防止重複觸發：同一次墜落只允許進入一次
            if (_fallingBGEntered) return;
            _fallingBGEntered = true;
            _waitingForRuinsLanding = true;
            // 新的一次墜落：上一輪的「只做一次」旗標清掉，否則重生後再掉一次光球不會傳、鏡頭不會特寫
            _ruinsIntroLightSynced = false;
            _ruinsIntroCloseupStarted = false;

            Debug.Log("碰觸到 FallingBackground！鎖死橫向移動，開始順暢高速向下墜落！");
            freezeHorizontal = true;
            SyncGuidanceLightToRuinsIntroOnce();
            StartRuinsIntroLightGuard();

            // ★ 核心修復：絕不把向下速度歸零！保留既有動量並確保向下的重力加速度
            float currentDownSpeed = rb.linearVelocity.y;
            float targetDownSpeed = Mathf.Min(currentDownSpeed, -8f); // 至少維持 -8f 以上的向下加速衝力，絕不減速！
            rb.linearVelocity = new Vector3(0f, targetDownSpeed, 0f);
            BeginControlledFall();   // ★1012 之後的速度完全由固定曲線決定

            // 關閉重生系統，避免掉落出畫面後被傳送回去
            PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem != null)
            {
                respawnSystem.enabled = false;
            }
        }
        else if (other.CompareTag("RuinedBackground"))
        {
            if (_ruinsIntroCloseupStarted) return;

            Debug.Log("碰觸到 RuinedBackground！維持墜落鎖定，鏡頭暫時特寫廢墟第一光球，直到玩家落到廢墟地板。");
            freezeHorizontal = true;
            _waitingForRuinsLanding = true;
            SyncGuidanceLightToRuinsIntroOnce();
            _ruinsIntroCloseupStarted = true;
            StartRuinsIntroLightCloseup();
        }
    }

    private void SyncGuidanceLightToRuinsIntroOnce()
    {
        if (_ruinsIntroLightSynced) return;

        GuidanceLight light = Object.FindFirstObjectByType<GuidanceLight>();
        if (light == null)
        {
            Debug.LogWarning("【廢墟墜落光球同步】找不到 GuidanceLight，無法把光球傳送到廢墟第一點。");
            return;
        }

        if (!light.TeleportToWaypointName(ruinsIntroLightName))
        {
            Debug.LogWarning($"【廢墟墜落光球同步】GuidanceLight 的 waypoints 找不到 {ruinsIntroLightName}。");
            return;
        }

        _ruinsIntroLightSynced = true;
    }

    private bool IsRuinsLandingGround(RaycastHit groundHit)
    {
        if (transform.position.y > ruinsLandingUnlockY) return false;
        if (groundHit.collider == null) return true;

        Transform hitTransform = groundHit.collider.transform;
        return groundHit.collider.CompareTag("Floor")
               || groundHit.collider.CompareTag("Ground")
               || hitTransform.CompareTag("Floor")
               || hitTransform.CompareTag("Ground")
               || hitTransform.root.CompareTag("Floor")
               || hitTransform.root.CompareTag("Ground");
    }

    private void UnlockFallingControl(string reason)
    {
        EndControlledFall("玩家已著地");   // ★1012
        bool wasRuinsLanding = _waitingForRuinsLanding;   // ★1011 先記下來，下面會清掉
        freezeHorizontal = false;
        isStrictLockingX = false;
        _fallingBGEntered = false;
        _waitingForRuinsLanding = false;

        if (_ruinsIntroCameraCoroutine != null)
        {
            StopCoroutine(_ruinsIntroCameraCoroutine);
            _ruinsIntroCameraCoroutine = null;
        }
        if (_ruinsIntroLightGuardCoroutine != null)
        {
            StopCoroutine(_ruinsIntroLightGuardCoroutine);
            _ruinsIntroLightGuardCoroutine = null;
        }
        // ★1011 光球特寫還給玩家：慢慢過去，不是瞬間切
        CameraTargetXFollower.ClearCameraOverrideSmooth(ruinsIntroHandBackSeconds);

        PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
        if (respawnSystem != null && wasRuinsLanding)
        {
            // ★1011 只有「天上掉到廢墟」那次落地才當存檔點，而且不能把已經走到的廢墟存檔點倒退回去
            //   （原本任何解除鎖定都會把重生點改成當下位置，會蓋掉企劃放的重生點，之後死掉就被送到奇怪的地方）
            respawnSystem.TrySetLandingCheckpoint(this.transform.position);
        }

        SetCameraFollow((smoothCameraY && cameraTarget != null) ? cameraTarget : this.transform);
        Debug.Log($"【掉落解鎖】{reason}，玩家已落到廢墟地面，鏡頭還給玩家並恢復操作。");
        StartCoroutine(EnableRespawnWithDelay());
    }

    // 墜落期間每 0.1 秒確認一次光球還在 P12；被別的流程拉走（例如回到起點）就立刻補傳，直到玩家落地。
    private void StartRuinsIntroLightGuard()
    {
        if (_ruinsIntroLightGuardCoroutine != null) return;
        _ruinsIntroLightGuardCoroutine = StartCoroutine(RuinsIntroLightGuardRoutine());
    }

    private System.Collections.IEnumerator RuinsIntroLightGuardRoutine()
    {
        var wait = new WaitForSeconds(0.1f);
        while (_waitingForRuinsLanding)
        {
            GuidanceLight light = Object.FindFirstObjectByType<GuidanceLight>();
            if (light != null && !light.IsNearWaypointName(ruinsIntroLightName))
            {
                Debug.LogWarning($"【廢墟墜落光球守護】光球不在 {ruinsIntroLightName}（目前在 {light.transform.position}），補傳一次。");
                light.TeleportToWaypointName(ruinsIntroLightName);
            }
            yield return wait;
        }
        _ruinsIntroLightGuardCoroutine = null;
    }

    private void StartRuinsIntroLightCloseup()
    {
        if (_ruinsIntroCameraCoroutine != null) return;
        _ruinsIntroCameraCoroutine = StartCoroutine(RuinsIntroLightCloseupRoutine());
    }

    private System.Collections.IEnumerator RuinsIntroLightCloseupRoutine()
    {
        // 鏡頭跟的是「光球本體」；光球不在或找不到才退回 P12 這個點
        Transform focus = null;
        GuidanceLight guideLight = Object.FindFirstObjectByType<GuidanceLight>();
        if (guideLight != null && guideLight.isActiveAndEnabled) focus = guideLight.transform;
        if (focus == null)
        {
            GameObject focusObj = GameObject.Find(ruinsIntroLightName);
            if (focusObj != null) focus = focusObj.transform;
        }

        if (focus != null)
        {
            // ★1012 鏡頭從跟著玩家慢慢移到光球特寫（不是瞬間切過去）
            CameraTargetXFollower.SetCameraOverrideSmooth(focus, ruinsIntroCloseupLensSize, ruinsIntroCloseupBlendSeconds);
        }
        else
        {
            Debug.LogWarning($"【廢墟墜落特寫】找不到 {ruinsIntroLightName}，維持原本墜落鏡頭直到落地。");
        }

        while (_waitingForRuinsLanding && !isGrounded)
        {
            yield return null;
        }

        _ruinsIntroCameraCoroutine = null;
    }

    private void SetCameraFollow(Transform target)
    {
        GameObject customTarget = GameObject.Find("CameraFollowTarget");
        if (customTarget != null)
        {
            target = customTarget.transform;
        }
        else if (target == null)
        {
            target = this.transform;
        }

        // 尋找「所有」新版 CinemachineCamera 並修改追蹤目標
        var vcams3 = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcams3)
        {
            if (vcam != null)
            {
                var t = vcam.Target;
                t.TrackingTarget = target;
                t.LookAtTarget = target;
                t.CustomLookAtTarget = true;
                vcam.Target = t;
                vcam.Follow = target;
            }
        }

        var vcamsLegacy = Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcamsLegacy)
        {
            if (vcam != null)
            {
                vcam.Follow = target;
                vcam.Priority = 100;
            }
        }
    }

    private System.Collections.IEnumerator EnableRespawnWithDelay()
    {
        yield return new WaitForSeconds(0.3f);
        PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
        if (respawnSystem != null)
        {
            respawnSystem.enabled = true;
            Debug.Log("【重生系統】已延遲重啟，安全防護生效。");
        }
    }

    // ==========================================
    // 水下物理、浮力與真實深度波義耳負浮力運算 (FixedUpdate)
    // ==========================================
    private void FixedUpdate()
    {
        if (_controlledFall) { ControlledFallStep(); return; }   // ★1012 墜落期間速度由固定曲線決定

        if (isUnderwater && rb != null && !rb.isKinematic)
        {
            // 確保重力開啟
            rb.useGravity = true;

            // 1. 基礎浮力與微重力抵消 (Counter-Gravity / Buoyancy)
            float gravityMagnitude = Mathf.Abs(Physics.gravity.y);
            float baseCounterForce = gravityMagnitude * (1f - Mathf.Clamp01(underwaterGravityScale)) * rb.mass;

            // 2. 真實深度水壓氣體體積壓縮 (Boyle's Law & Negative Buoyancy)
            // 當深度 Y 越低 (低於 neutralBuoyancyY 中性浮力點)，水壓遞增壓縮氣體體積，正浮力衰減，產生向下加速沉降的負浮力
            float extraDownwardForce = 0f;
            if (enableDepthBuoyancyPhysics)
            {
                float depthBelowNeutral = neutralBuoyancyY - transform.position.y;
                if (depthBelowNeutral > 0f)
                {
                    extraDownwardForce = Mathf.Min(depthBelowNeutral * depthCompressionRate * rb.mass, maxNegativeBuoyancyExtraForce);
                }
            }

            float finalCounterForce = Mathf.Max(0f, baseCounterForce - extraDownwardForce);
            rb.AddForce(Vector3.up * finalCounterForce, ForceMode.Force);

            Vector3 vel = rb.linearVelocity;

            // 水中自然下沉速度上限 (越深越快：中性浮力點以下每深 1 米 +2%)
            float effectiveMaxFallSpeed = enableDepthBuoyancyPhysics && (transform.position.y < neutralBuoyancyY)
                ? underwaterMaxFallSpeed * (1f + (neutralBuoyancyY - transform.position.y) * 0.02f)
                : underwaterMaxFallSpeed;

            // ★1014 S 下潛的目標速度：至少比自然下沉上限快 (原本固定 -4.5，深處反而比不按還慢)
            float diveTargetSpeed = Mathf.Min(-underwaterDiveSpeed, effectiveMaxFallSpeed * underwaterDiveFallMultiplier);

            // 3. 水下游泳：W/Space/↑ 划一下往上升一段 (瑪利歐式)，S/↓ 主動下潛
            bool isPressingSwimDown = !IsControlLocked && (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow));

            bool stroke = _swimStrokeQueued;
            _swimStrokeQueued = false;
            if (stroke && !IsControlLocked && !isSwimExhausted && Time.time - lastSwimTime >= swimCooldown)
            {
                // ★1021 蛙式划水：不再瞬間給初速，而是「推水」swimStrokePushTime 秒內平滑加速到最高速，
                // 之後以 swimStrokeGlideDecel 慢慢滑行減速。最高速由「要升多高」反算：
                //   推水段 (smoothstep，平均速度 = peak/2)：peak·T/2
                //   滑行段 (等減速 Dg = 滑行水阻 + 淨重力 + 線性阻力近似 c·peak/2)：peak²/(2Dg)
                //   peak²/(2Dg) + peak·T/2 = h → peak = Dg·(√(T²/4 + 2h/Dg) − T/2)
                float netGravity = Mathf.Max(0f, gravityMagnitude - finalCounterForce / rb.mass);
                float h = Mathf.Max(0f, swimStrokeHeight);
                float c = rb.linearDamping;
                float T = Mathf.Max(0f, swimStrokePushTime);
                float peak = 2f;
                for (int i = 0; i < 3; i++)
                {
                    float Dg = Mathf.Max(0.1f, swimStrokeGlideDecel + netGravity + c * peak * 0.5f);
                    peak = Dg * (Mathf.Sqrt(T * T * 0.25f + 2f * h / Dg) - T * 0.5f);
                }
                _strokePeakVy = peak;
                _strokePushFromVy = vel.y;
                _strokePushStart = Time.fixedTime;
                if (T <= 0f) vel.y = peak;
                lastSwimTime = Time.time;
                isSwimming = true;

                currentSwimStamina -= swimStrokeStaminaCost;
                if (currentSwimStamina <= 0f)
                {
                    currentSwimStamina = 0f;
                    isSwimExhausted = true;
                    exhaustionTimer = swimExhaustionCooldown;
                    _swimHoldBlocked = true;   // 按住連划到此為止，要放開再按
                    Debug.Log("【水下體力機制】連續划水體力耗盡，暫時無法往上划！");
                }
            }
            float pushT = swimStrokePushTime > 0f ? (Time.fixedTime - _strokePushStart) / swimStrokePushTime : 1f;
            bool inStrokePush = pushT >= 0f && pushT < 1f;

            if (isPressingSwimDown)
            {
                // 按住 S / 下方向鍵 主動下潛 (會中斷正在推水的那一划)
                _strokePushStart = -99f;
                vel.y = Mathf.MoveTowards(vel.y, diveTargetSpeed, Time.fixedDeltaTime * underwaterDiveAccel);
            }
            else if (inStrokePush)
            {
                // ★1021 蛙式推水：從划水當下的速度平滑加速到這一划的最高速 (原本往下沉也會先慢下來再往上)
                float e = pushT * pushT * (3f - 2f * pushT);
                vel.y = Mathf.Lerp(_strokePushFromVy, _strokePeakVy, e);
            }
            else if (vel.y > 0f)
            {
                // 划水後的滑行：較柔和的水阻減速，到頂點後順應水中重力自然下沉
                vel.y = Mathf.MoveTowards(vel.y, 0f, swimStrokeGlideDecel * Time.fixedDeltaTime);
            }

            // ★ 水下穿模修正：
            //   maxDepenetrationVelocity 是「物理每秒最多能把玩家推出碰撞體多快」，
            //   陸地為了消除斜坡抖動把它壓到 2.0。但水下越深沉得越快
            //   (Y=-100 約 11.5/秒、Y=-134 約 14.9/秒；按 S 下潛還會再快)，是推出速度的 5~10 倍。
            //   只要有一幀身體稍微陷進石頭，物理就來不及把她推出來，
            //   於是越陷越深、直接穿過石頭掉出地圖 —— 而且越深越容易發生。
            //   這裡讓推出速度跟著最快可能的下沉速度走，永遠贏得過下沉。
            float fastestSink = isPressingSwimDown ? Mathf.Max(Mathf.Abs(diveTargetSpeed), Mathf.Abs(vel.y)) : Mathf.Abs(effectiveMaxFallSpeed);
            rb.maxDepenetrationVelocity = Mathf.Max(landDepenetrationVelocity, fastestSink * underwaterDepenetrationFactor);

            // 4. 水中最大自然沉降速度動態限制 (當沒有按 S 主動下潛時，限制自然下沉速度)
            if (!isPressingSwimDown && vel.y < effectiveMaxFallSpeed)
            {
                vel.y = effectiveMaxFallSpeed;
            }

            // ★ 水下穿模修正之二：碰撞偵測改成 ContinuousSpeculative (按 S 下潛時更需要)。
            //   水下石頭全部是 MeshCollider，非凸面的 MeshCollider 是「沒有厚度的面」。
            //   一般的 Continuous 是掃描式，玩家在深處一個物理幀就移動 0.3 單位，
            //   掃描漏掉一次就整個人穿到面的另一邊，而且穿過去之後就再也推不回來。
            //   Speculative 是「先預測會撞到什麼再限制這一步的位移」，
            //   對這種無厚度的面比掃描式可靠得多。陸地維持 Continuous（Speculative
            //   在平地接縫容易產生假碰撞、走路會頓）。
            if (rb.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            // ★1013 光球帶路：幾乎原地漂浮，下沉有速度與總深度上限
            if (_floatHold)
            {
                if (vel.y < -floatHoldSinkSpeed) vel.y = -floatHoldSinkSpeed;
                if (_floatHoldStartY - transform.position.y >= floatHoldMaxSink && vel.y < 0f) vel.y = 0f;
            }
            else if (_entryFloat)
            {
                // ★1016 剛入水／水下重生：同一套漂浮規則（下沉速度、總深度都有上限）
                if (vel.y < -floatHoldSinkSpeed) vel.y = -floatHoldSinkSpeed;
                if (_entryFloatStartY - transform.position.y >= floatHoldMaxSink && vel.y < 0f) vel.y = 0f;
            }

            rb.linearVelocity = vel;
        }
        else if (rb != null && !rb.isKinematic)
        {
            // 離開水面：還原陸地用的低值，維持原本的斜坡防抖行為
            if (!Mathf.Approximately(rb.maxDepenetrationVelocity, landDepenetrationVelocity))
            {
                rb.maxDepenetrationVelocity = landDepenetrationVelocity;
            }
            // 碰撞偵測也還原成 Continuous：Speculative 在平地接縫會產生假碰撞，走路會頓
            if (rb.collisionDetectionMode != CollisionDetectionMode.Continuous)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
        }
    }

    // ==========================================
    // 水下狀態判定處理 (三層防護)
    // ==========================================
    public void SetTriggerUnderwater(bool state)
    {
        isTriggerUnderwater = state;
    }

    private void UpdateUnderwaterState()
    {
        if (waterDetectionMode == WaterDetectionMode.ForceOn)
        {
            isUnderwater = true;
        }
        else if (waterDetectionMode == WaterDetectionMode.ForceOff)
        {
            isUnderwater = false;
        }
        else // Auto 模式：自動比對場景名稱包含 underwater，或踩入水體 Trigger
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToLower();
            bool isUnderwaterScene = sceneName.Contains("underwater");
            isUnderwater = isUnderwaterScene || isTriggerUnderwater;
        }
    }

    /// <summary>
    /// 精確地面與斜坡多點射線偵測系統 (多點掃描 BoxCollider 底部與斜坡法線)
    /// </summary>
    private bool CheckGrounded(out RaycastHit bestHit, out float slopeAngle)
    {
        bestHit = new RaycastHit();
        slopeAngle = 0f;

        if (playerCollider == null)
        {
            playerCollider = GetComponent<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInParent<Collider>();
        }

        if (playerCollider == null)
        {
            if (Physics.Raycast(transform.position, Vector3.down, out bestHit, 1.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                slopeAngle = Vector3.Angle(Vector3.up, bestHit.normal);
                return true;
            }
            return false;
        }

        Vector3 center = playerCollider.bounds.center;
        float extentsY = playerCollider.bounds.extents.y;
        float extentsX = playerCollider.bounds.extents.x * 0.75f;
        float rayLength = extentsY + 0.35f;

        // 3 點向下射線 (中央、左腳邊緣、右腳邊緣)
        Vector3[] checkPoints = new Vector3[]
        {
            center,
            center + Vector3.left * extentsX,
            center + Vector3.right * extentsX
        };

        float minDistance = float.MaxValue;
        bool found = false;
        int layerMask = ~LayerMask.GetMask("Ignore Raycast");

        foreach (var origin in checkPoints)
        {
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLength, layerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == playerCollider || hit.collider.transform.IsChildOf(transform)) continue;

                if (hit.distance < minDistance)
                {
                    minDistance = hit.distance;
                    bestHit = hit;
                    found = true;
                }
            }
        }

        if (!found)
        {
            Vector3 halfExtents = new Vector3(playerCollider.bounds.extents.x * 0.75f, 0.05f, playerCollider.bounds.extents.z * 0.75f);
            if (Physics.BoxCast(center, halfExtents, Vector3.down, out RaycastHit boxHit, Quaternion.identity, extentsY + 0.2f, layerMask, QueryTriggerInteraction.Ignore))
            {
                if (boxHit.collider != playerCollider && !boxHit.collider.transform.IsChildOf(transform))
                {
                    bestHit = boxHit;
                    found = true;
                }
            }
        }

        // 手拉的斜坡碰撞框或 MeshCollider 接縫上，射線偶爾會剛好落在縫外；
        // 只要物理接觸仍明確來自腳下，就給一小段 grounded 緩衝，避免站得住卻跳不起來。
        if (!found && Time.time - _lastGroundContactTime <= GroundContactGraceSeconds)
        {
            bestHit = new RaycastHit();
            bestHit.point = _lastGroundContactPoint;
            bestHit.normal = _lastGroundContactNormal.sqrMagnitude > 0.001f ? _lastGroundContactNormal.normalized : Vector3.up;
            slopeAngle = Vector3.Angle(Vector3.up, bestHit.normal);
            return true;
        }

        if (found)
        {
            slopeAngle = Vector3.Angle(Vector3.up, bestHit.normal);
        }

        return found;
    }

    // ==========================================
    // 推動物件 (Pushable) 判定機制
    // ==========================================
    private bool IsPushableObject(GameObject go, Collider col)
    {
        if (go == null && col == null) return false;
        if (go != null && (go == gameObject || go.transform.IsChildOf(transform))) return false;
        if (col != null && (col == playerCollider || col.transform.IsChildOf(transform))) return false;

        // 1. 直接 Tag 檢測
        if (go != null && go.CompareTag("Pushable")) return true;
        if (col != null && col.CompareTag("Pushable")) return true;

        // 2. 剛體 Tag 檢測
        if (col != null && col.attachedRigidbody != null && col.attachedRigidbody.CompareTag("Pushable")) return true;

        // 3. 向上遍歷父層階層直到 Root (確保石球或組合 Model 即使 Tag 掛在 Root 也能被辨識)
        Transform t = (go != null) ? go.transform : (col != null ? col.transform : null);
        while (t != null)
        {
            if (t.CompareTag("Pushable")) return true;
            t = t.parent;
        }

        return false;
    }

    /// <summary>
    /// 精確確認 Pushable 物件是否位於玩家「正前方」（排除站在物件頂部/腳底下方的情況）
    /// </summary>
    private bool IsPushableInFront(Collider col, Vector3 playerCenter, Vector3 playerExtents)
    {
        if (col == null) return false;
        if (!IsPushableObject(col.gameObject, col)) return false;

        float playerFeetY = playerCenter.y - playerExtents.y;
        float playerHeadY = playerCenter.y + playerExtents.y;

        // ★ 關鍵排除條件：Pushable 物件頂部必須明顯高於玩家腳底（至少 0.35m，達到小腿以上），才視為前方障礙
        // 若物件頂部在玩家腳底附近 (col.bounds.max.y <= playerFeetY + 0.35f)，代表玩家正站在該物件頂面，絕不判定為前方推動！
        if (col.bounds.max.y <= playerFeetY + 0.35f)
        {
            return false;
        }

        // 若物件底部高於玩家頭頂，代表在玩家上方懸空，也不算前方推動
        if (col.bounds.min.y >= playerHeadY)
        {
            return false;
        }

        return true;
    }

    private void UpdatePushingDetection(float input)
    {
        if (playerCollider == null)
        {
            playerCollider = GetComponent<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInParent<Collider>();
        }

        // 1. 玩家若沒有輸入推動方向鍵，立即離開 Pushing (鬆開方向鍵立即回歸 Idle/Run)
        if (Mathf.Abs(input) <= 0.05f)
        {
            isPushing = false;
            _collisionPushableTimer = 0f;
            return;
        }

        // 2. 空中跳躍/墜落、水下或鎖定狀態，不能判定為陸地 Pushing
        bool isAirborne = isJumping || (!isGrounded && rb.linearVelocity.y < -3.0f && currentAirTime > 0.25f);
        if (isUnderwater || isAirborne || IsControlLocked)
        {
            isPushing = false;
            _collisionPushableTimer = 0f;
            return;
        }

        Vector3 pushDir = (input > 0f) ? Vector3.right : Vector3.left;
        bool hasPushableInFront = false;

        // 3. 抓取物件中 (LeftShift)
        if (pulledObject != null)
        {
            hasPushableInFront = true;
        }

        Vector3 center = (playerCollider != null) ? playerCollider.bounds.center : transform.position;
        Vector3 extents = (playerCollider != null) ? playerCollider.bounds.extents : new Vector3(0.6f, 1.0f, 0.6f);

        // 4. 前方接觸面盒型範圍檢測 (精確覆蓋從腳踝到胸口的正面接觸區，且嚴格排除腳底頂部)
        if (!hasPushableInFront)
        {
            int pushMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI");
            Vector3 boxCenter = new Vector3(center.x + pushDir.x * (extents.x + pushDetectionDistance * 0.5f), center.y, center.z);
            Vector3 boxHalfExtents = new Vector3(pushDetectionDistance * 0.5f, extents.y * 0.85f, extents.z * 0.85f);

            Collider[] hits = Physics.OverlapBox(boxCenter, boxHalfExtents, Quaternion.identity, pushMask, QueryTriggerInteraction.Ignore);
            foreach (var col in hits)
            {
                if (IsPushableInFront(col, center, extents))
                {
                    hasPushableInFront = true;
                    break;
                }
            }
        }

        // 5. 正面多層射線陣列檢測 (膝蓋、腰部、胸口前向掃描，嚴格排除腳底)
        if (!hasPushableInFront)
        {
            int pushMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI");
            float rayStartX = center.x + pushDir.x * (extents.x * 0.8f);
            float rayLen = (extents.x * 0.2f) + pushDetectionDistance + 0.1f;

            Vector3[] rayOrigins = new Vector3[]
            {
                new Vector3(rayStartX, center.y - extents.y * 0.4f, center.z), // 小腿/膝蓋
                new Vector3(rayStartX, center.y + extents.y * 0.1f, center.z), // 腰部
                new Vector3(rayStartX, center.y + extents.y * 0.5f, center.z)  // 胸部
            };

            foreach (var origin in rayOrigins)
            {
                if (Physics.Raycast(origin, pushDir, out RaycastHit hit, rayLen, pushMask, QueryTriggerInteraction.Ignore))
                {
                    if (IsPushableInFront(hit.collider, center, extents))
                    {
                        hasPushableInFront = true;
                        break;
                    }
                }
            }
        }

        // 6. 實體碰撞接觸緩衝 (PhysX 剛體側面貼合判定)
        if (!hasPushableInFront && _collisionPushableTimer > 0f)
        {
            hasPushableInFront = true;
        }

        if (_collisionPushableTimer > 0f)
        {
            _collisionPushableTimer -= Time.deltaTime;
        }

        // 最終判定：只要玩家在地面/斜坡、正朝著前方貼近的 Pushable 物件持續輸入，就保持 Pushing！
        isPushing = hasPushableInFront;
    }

    private void CheckPushingCollision(Collision collision)
    {
        if (collision == null || collision.collider == null) return;
        if (IsPushableObject(collision.gameObject, collision.collider))
        {
            if (Mathf.Abs(CurrentMoveInput) > 0.05f)
            {
                Vector3 center = (playerCollider != null) ? playerCollider.bounds.center : transform.position;
                Vector3 extents = (playerCollider != null) ? playerCollider.bounds.extents : new Vector3(0.6f, 1.0f, 0.6f);
                float playerFeetY = center.y - extents.y;

                // ★ 排除站在頂部的碰撞：若 Pushable 物件頂面在玩家腳底附近，不判定為側面推動
                if (collision.collider.bounds.max.y <= playerFeetY + 0.35f) return;

                Vector3 pushDir = (CurrentMoveInput > 0f) ? Vector3.right : Vector3.left;

                // 檢查接觸點：必須是來自側面的水平推動接觸 (排除腳底垂直支撐接觸)
                for (int i = 0; i < collision.contactCount; i++)
                {
                    ContactPoint cp = collision.GetContact(i);

                    // ★ 接觸點不能在腳底（垂直支撐法線向上 cp.normal.y > 0.6f 排除）
                    if (cp.normal.y > 0.6f) continue;

                    float dotNormal = Vector3.Dot(cp.normal, -pushDir);
                    float relX = (cp.point.x - center.x) * pushDir.x;
                    if (dotNormal > 0.3f && relX > 0.05f)
                    {
                        _collisionPushableTimer = 0.2f;
                        break;
                    }
                }
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckPushingCollision(collision);
        CacheGroundContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CheckPushingCollision(collision);
        CacheGroundContact(collision);

        if (!isUnderwater || collision.contactCount == 0) return;

        // 當水下在凹凸不平的岩石夾角間游動時，輔助法線平滑滑動，杜絕在夾角處卡住
        bool hasInput = Mathf.Abs(CurrentMoveInput) > 0.05f || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D);
        if (hasInput && rb != null && !rb.isKinematic)
        {
            Vector3 avgNormal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++)
            {
                avgNormal += collision.GetContact(i).normal;
            }
            avgNormal = avgNormal.normalized;
            rb.AddForce(new Vector3(avgNormal.x, avgNormal.y, 0f) * 2.0f, ForceMode.Acceleration);
        }
    }

    private void CacheGroundContact(Collision collision)
    {
        if (collision == null || collision.contactCount == 0 || playerCollider == null) return;
        if (collision.collider != null && collision.collider.isTrigger) return;
        if (collision.collider != null && collision.collider.transform.IsChildOf(transform)) return;

        Bounds b = playerCollider.bounds;
        float footY = b.min.y + 0.35f;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint cp = collision.GetContact(i);
            Vector3 n = new Vector3(cp.normal.x, cp.normal.y, 0f);
            if (n.sqrMagnitude < 0.001f) continue;
            n.Normalize();

            if (cp.point.y <= footY && Mathf.Abs(n.y) > 0.2f)
            {
                _lastGroundContactTime = Time.time;
                _lastGroundContactNormal = n.y >= 0f ? n : -n;
                _lastGroundContactPoint = cp.point;
                return;
            }
        }
    }
}
