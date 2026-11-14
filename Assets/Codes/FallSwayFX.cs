using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1114 墜落時主角輕微晃動＋起身時身體不陷進地面（兩個都是純視覺，只動外觀子物件）。
///
///  ◆ 墜落晃動：失控、不安、不確定的感覺
///   ・以「左右搖晃」為主：繞畫面深度軸（Z）左右傾、繞垂直軸（Y）左右轉身，加上上下起伏（Y）；
///     前後（畫面橫向 X）的飄移與前後仰（繞 X 軸）刻意壓得很小。
///   ・幾個頻率互不成倍數的正弦疊起來，不是規律鐘擺；前 1.5 秒淡入，接近地面依 FallLandingK 收掉。
///  ◆ 起身防陷地：Standing Up 動畫的趴／跪階段，身體骨骼會低於地面（量過：最深約 0.5 公尺）。
///   每幀量「全身最低的骨骼」離碰撞盒底部多遠，不夠就把外觀整個往上抬一點，剛好不陷進去；站直後自然是 0。
///
///  為什麼不會跟別的機制打架：
///   ・只動外觀子物件（animator 那個 Transform），剛體、碰撞盒、鎖定的 X、墜落速度曲線一個都不碰，
///     落點、傳送接力、落地判定、墜落秒數都不變。
///   ・PlayerMovement 每幀用 Slerp 從「目前的旋轉」往目標轉、斜坡補償也會改 localPosition.y。
///     所以做成「疊加→下一幀開始前先撤掉→重新疊加」：Update 排最前面撤掉上一幀偏移，LateUpdate 排最後面再疊。
///     量骨骼高度發生在撤掉之後、疊加之前，量到的是乾淨的值，不會自己回授。
///   ・沒在墜落也沒在起身＝一律撤掉、什麼都不做。重生、傳送後不殘留。
///   ・不改鏡頭：鏡頭位置由 CameraTargetXFollower 每幀完整接管，疊東西會互相覆蓋。
///  所有數字都在 Inspector 可調；enableSway／enableGetUpGroundFix 取消勾選＝完全恢復原樣。
/// </summary>
[DefaultExecutionOrder(100)]
public class FallSwayFX : MonoBehaviour
{
    [Header("墜落晃動：開關與強度")]
    public bool enableSway = true;
    [Tooltip("整體強度倍率：1＝預設、0＝關掉、1.5＝更大")]
    [Range(0f, 3f)] public float intensity = 1f;

    [Header("左右搖晃（主角）")]
    [Tooltip("繞畫面深度軸左右傾斜（度）")]
    [Range(0f, 45f)] public float rollDegrees = 17f;
    [Tooltip("繞垂直軸左右轉身（度）：身體一下朝向鏡頭、一下背向鏡頭")]
    [Range(0f, 60f)] public float yawDegrees = 11f;
    [Tooltip("上下起伏（公尺）：像在氣流裡浮沉")]
    [Range(0f, 2f)] public float bobMeters = 0.2f;
    [Header("前後（刻意很小）")]
    [Tooltip("畫面橫向飄移（公尺）")]
    [Range(0f, 2f)] public float driftMeters = 0.25f;
    [Tooltip("前後仰（度，繞 X 軸）")]
    [Range(0f, 20f)] public float pitchDegrees = 4f;

    [Header("節奏")]
    [Tooltip("淡入秒數：墜落開始後幾秒內晃動從 0 慢慢長到全開")]
    public float fadeInSeconds = 1.5f;
    [Tooltip("越掉越不安：從開始到這個秒數，振幅由 70% 長到 100%")]
    public float buildUpSeconds = 6f;
    [Tooltip("離地面多近開始收掉（0~1，對應 PlayerMovement 的放慢距離比例）。0.6＝剩 60% 距離就開始收")]
    [Range(0.1f, 1f)] public float settleStartK = 0.6f;

    [Header("起身防陷地")]
    public bool enableGetUpGroundFix = true;
    [Tooltip("手腳骨骼離地面的最小距離，單位＝角色縮放倍數。0.065＝跟站立（Idle）時腳踝的高度一致，站起來接回 Idle 不會跳")]
    public float boneClearance = 0.065f;
    [Tooltip("軀幹（髖、脊椎、胸）骨骼離地面的最小距離（角色縮放倍數）。身體有厚度，骨骼在中心")]
    public float torsoClearance = 0.07f;
    [Tooltip("頭、頸骨骼離地面的最小距離（角色縮放倍數）。這個角色頭很大，趴下時頭會陷最深")]
    public float headClearance = 0.12f;
    [Tooltip("手臂、腿的骨骼離地面的最小距離（角色縮放倍數）")]
    public float limbClearance = 0.05f;
    [Tooltip("起身全程把身體壓在側面（脊椎與面向不朝鏡頭歪）。關掉＝回到動畫原本的扭轉")]
    public bool enableGetUpProfile = true;
    [Range(0f, 1f)] public float getUpProfileStrength = 1f;
    [Tooltip("抬起來的平滑速度（越大跟得越緊）")]
    public float liftFollowSpeed = 45f;
    [Tooltip("起身期間外觀抬升『往下放』的最快速度（公尺／秒），避免跟著動畫骨骼的小抖動上下跳")]
    public float liftFallMetersPerSecond = 1.2f;

    private PlayerMovement _pm;
    private Transform _visual;
    private Collider _col;
    private Transform[] _bones;
    private float[] _boneKind;   // 每根骨骼用哪一種離地距離：0 手腳、1 軀幹、2 頭頸、3 手臂腿
    private bool _applied;
    private Quaternion _appliedRot = Quaternion.identity;
    private Vector3 _appliedPos;
    private float _seed;
    private float _lift;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll();   // 第一個場景收不到 sceneLoaded，補跑一次
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { AttachAll(); }

    private static void AttachAll()
    {
        foreach (PlayerMovement pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (pm != null && pm.GetComponent<FallSwayFX>() == null) pm.gameObject.AddComponent<FallSwayFX>();
    }

    private void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
        _col = GetComponent<Collider>();
        _seed = Random.value * 100f;   // 每次墜落相位不同，但同一次內平滑連續
    }

    private Transform FindVisual()
    {
        if (_visual != null) return _visual;
        if (_pm == null || _pm.animator == null) return null;
        Transform t = _pm.animator.transform;
        if (t == transform) return null;   // 外觀就是根物件時不碰（會帶著剛體轉）
        _visual = t;
        return _visual;
    }

    private void CacheBones()
    {
        if (_bones != null) return;
        Animator a = _pm != null ? _pm.animator : null;
        if (a == null || a.avatar == null || !a.avatar.isHuman) { _bones = new Transform[0]; _boneKind = new float[0]; return; }
        var list = new System.Collections.Generic.List<Transform>(55);
        var kinds = new System.Collections.Generic.List<float>(55);
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            Transform b = a.GetBoneTransform((HumanBodyBones)i);
            if (b == null) continue;
            string n = ((HumanBodyBones)i).ToString();
            float kind = 3f;                                                             // 手臂、腿
            if (n == "Hips" || n == "Spine" || n == "Chest" || n == "UpperChest") kind = 1f;   // 軀幹
            else if (n == "Neck" || n == "Head" || n == "Jaw" || n == "LeftEye" || n == "RightEye") kind = 2f;   // 頭頸
            else if (n.Contains("Foot") || n.Contains("Toes") || n.Contains("Hand") || n.Contains("Thumb") || n.Contains("Index")
                  || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little")) kind = 0f;   // 手腳
            list.Add(b); kinds.Add(kind);
        }
        _bones = list.ToArray();
        _boneKind = kinds.ToArray();
    }

    private float ClearanceOf(float kind)
    {
        if (kind < 0.5f) return boneClearance;
        if (kind < 1.5f) return torsoClearance;
        if (kind < 2.5f) return headClearance;
        return limbClearance;
    }

    /// <summary>撤掉上一幀疊上去的偏移（排在 PlayerMovement 之前）。</summary>
    private void Undo()
    {
        if (!_applied) return;
        _applied = false;
        Transform v = FindVisual();
        if (v == null) return;
        v.position -= _appliedPos;
        v.rotation = Quaternion.Inverse(_appliedRot) * v.rotation;
    }

    private void OnDisable() { Undo(); }

    private void LateUpdate()
    {
        if (_pm == null) return;
        Transform v = FindVisual();
        if (v == null) return;

        Quaternion addRot = Quaternion.identity;
        Vector3 addPos = Vector3.zero;
        bool any = false;
        Quaternion preRot = Quaternion.identity;
        Vector3 prePos = Vector3.zero;

        // ── 起身全程保持側面 ──
        // Mixamo 的起身動畫會一邊撐起來一邊把身體轉向鏡頭（量過：爬起階段脊椎在鏡頭軸上偏約 35 度、肚子朝鏡頭約 25 度），
        // 橫向捲軸只看側面，這樣看起來就是「角度怪」。這裡以髖部為軸，把脊椎方向與面向壓回畫面平面（X-Y）。
        // 起身頭尾兩端本來就在平面內，所以進出沒有跳動；只動外觀，不碰剛體。
        if (enableGetUpProfile && _pm.IsGettingUp && _pm.animator != null)
        {
            Transform hips = _pm.animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                Vector3 up = hips.up, fwd = hips.forward;
                Vector3 upFlat = new Vector3(up.x, up.y, 0f);
                Quaternion r1 = Quaternion.identity;
                if (upFlat.sqrMagnitude > 0.04f) r1 = Quaternion.FromToRotation(up, upFlat.normalized);
                Vector3 up2 = r1 * up, fwd2 = r1 * fwd;
                Vector3 tgt = Vector3.Cross(up2, Vector3.forward);
                Quaternion r2 = Quaternion.identity;
                if (tgt.sqrMagnitude > 0.01f)
                {
                    tgt.Normalize();
                    if (Vector3.Dot(tgt, fwd2) < 0f) tgt = -tgt;
                    r2 = Quaternion.FromToRotation(fwd2, tgt);
                }
                Quaternion R = Quaternion.Slerp(Quaternion.identity, r2 * r1, Mathf.Clamp01(getUpProfileStrength));
                Vector3 pivotOffset = hips.position - v.position;
                preRot = R;
                prePos = pivotOffset - R * pivotOffset;   // 以髖部為軸轉，髖部位置不動
                v.rotation = preRot * v.rotation;
                v.position += prePos;
                any = true;
            }
        }

        // ── 起身防陷地 ──
        float liftTarget = 0f;
        // 起身結束後動畫還在淡入 Idle（約 0.35 秒），要繼續跟著對齊到抬升歸零，不然外觀會突然掉下去
        if (enableGetUpGroundFix && _col != null && (_pm.IsGettingUp || _lift > 0.001f))
        {
            CacheBones();
            // 每根骨骼各自有「離地最小距離」（頭比手腳大），算出每一根需要抬多少，取最大的
            float scale = Mathf.Max(0.01f, v.lossyScale.y);
            float groundY = _col.bounds.min.y;
            for (int i = 0; i < _bones.Length; i++)
            {
                Transform b = _bones[i];
                if (b == null) continue;
                float need = groundY + ClearanceOf(_boneKind[i]) * scale - b.position.y;
                if (need > liftTarget) liftTarget = need;
            }
        }
        // 起身期間：往上抬要快（不然會先陷進去），往下放限制速度——動畫骨骼高度在「跪→站」那一瞬間會快速抖動（±0.5 公尺），
        //   貼著它走外觀就會跟著上下跳；只升快、降慢，高度就是平滑的單向變化
        if (_pm.IsGettingUp)
        {
            if (liftTarget > _lift) _lift = Mathf.Lerp(_lift, liftTarget, 1f - Mathf.Exp(-liftFollowSpeed * 2f * Time.deltaTime));
            else _lift = Mathf.Max(liftTarget, _lift - liftFallMetersPerSecond * Time.deltaTime);
        }
        else
        {
            _lift = Mathf.Lerp(_lift, liftTarget, 1f - Mathf.Exp(-liftFollowSpeed * Time.deltaTime));
            if (_lift < 0.001f) _lift = 0f;
        }
        if (_lift > 0.0005f) { addPos.y += _lift; any = true; }

        // ── 墜落晃動 ──
        if (enableSway && intensity > 0f && _pm.IsInControlledFall)
        {
            float t = _pm.ControlledFallSeconds;
            float fade = fadeInSeconds <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeInSeconds));
            float build = 0.7f + 0.3f * Mathf.Clamp01(t / Mathf.Max(0.1f, buildUpSeconds));
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pm.FallLandingK / Mathf.Max(0.1f, settleStartK)));   // 接近地面→0
            float amp = intensity * fade * build * settle;
            if (amp > 0.001f)
            {
                float s = _seed;
                // 頻率互不成倍數 → 不會變成規律的鐘擺；比第一版快，才有「慌亂」的感覺
                float roll = Wave(t, s, 0.43f, 1.07f, 2.3f) * rollDegrees * amp;                  // 左右傾
                float yaw = Wave(t, s * 1.3f + 7f, 0.37f, 0.91f, 1.9f) * yawDegrees * amp;        // 左右轉身
                float bob = Wave(t, s * 0.8f + 3f, 0.29f, 0.71f, 1.6f) * bobMeters * amp;          // 上下浮沉
                float pitch = Wave(t, s * 1.9f + 5f, 0.33f, 0.83f, 1.7f) * pitchDegrees * amp;     // 前後仰（很小）
                float drift = Wave(t, s * 0.6f + 11f, 0.31f, 0.83f, 1.5f) * driftMeters * amp;    // 橫向飄（很小）

                // 世界軸左乘疊加（跟斜坡傾斜同一套做法，左右朝向翻面時方向仍然正確）
                addRot = Quaternion.Euler(pitch, yaw, roll) * addRot;
                addPos += new Vector3(drift, bob, 0f);
                any = true;
            }
        }

        if (!any) return;
        _appliedRot = addRot * preRot;   // 先轉（preRot）已經套在 v 上，這裡只補 addRot；撤銷時一起還原
        _appliedPos = addPos + prePos;
        v.rotation = addRot * v.rotation;
        v.position += addPos;
        _applied = true;
    }

    private static float Wave(float t, float seed, float f1, float f2, float f3)
    {
        return Mathf.Sin((t + seed) * 6.2832f * f1) * 0.6f
             + Mathf.Sin((t + seed * 1.7f) * 6.2832f * f2) * 0.3f
             + Mathf.Sin((t + seed * 2.3f) * 6.2832f * f3) * 0.1f;
    }

    /// <summary>排在最前面的小幫手：每幀 Update 之前先撤掉上一幀的偏移，PlayerMovement 才看到乾淨的值。</summary>
    [DefaultExecutionOrder(-10000)]
    private class FallSwayPre : MonoBehaviour
    {
        public FallSwayFX owner;
        private void Update() { if (owner != null) owner.Undo(); }
    }

    private void Start()
    {
        FallSwayPre pre = gameObject.AddComponent<FallSwayPre>();
        pre.owner = this;
    }
}
