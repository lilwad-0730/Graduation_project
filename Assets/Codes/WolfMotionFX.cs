using System.Reflection;
using UnityEngine;

/// <summary>
/// ★1114 狼的程式動態（疊在 sprite sheet 動畫上，不需要新美術）：
///   ‧ 步伐起伏：奔跑時隨步伐上下起伏＋前後搖擺（像搖椅），慢走時小幅起伏。相位取自動畫進度，跟腿對得上。
///   ‧ 加速前傾、煞車後仰；跑越快身體越往前拉長。
///   ‧ 待機呼吸：胸口微微起伏、身體輕晃。
///   ‧ 撲咬：靠近要咬時先壓低蓄力 → 咬上那一刻沿弧線撲起、身體伸長 → 咬住時甩頭拉扯，每隔一段時間猛扯一下。
///   ‧ 被打暈：往後仰、彈起，之後搖搖晃晃；鬆口退後時往後輕跳。
///   ‧ 個體差異：體型 ±8%、剪影深淺 ±7%（每隻狼出生時取一次）。
///   ‧ 腳下揚塵：奔跑時依速度揚起灰塵，撲咬落地與被打暈時各炸一團。灰塵貼圖由程式產生。
///
/// 跟 WolfEnemy 的斜坡貼合怎麼共存：WolfEnemy 在 LateUpdate 依外觀物件「目前的」角度與高度慢慢靠近斜坡目標。
/// 這支在所有 LateUpdate 之後把動態「疊上去」，下一幀 Update 再「拿掉」，WolfEnemy 讀到的永遠是乾淨的值。
/// 如果別的程式在這中間改了外觀物件（例如暈眩時歸零），就保留它的值、不還原。
/// </summary>
[DefaultExecutionOrder(500)]
public class WolfMotionFX : MonoBehaviour
{
    [Header("步伐起伏")]
    [Tooltip("奔跑時身體上下起伏的高度（米）")]
    public float runBob = 0.14f;
    [Tooltip("奔跑時身體前後搖擺的角度（度）")]
    public float runRock = 3.5f;
    [Tooltip("慢走／倒退時的起伏高度（米）")]
    public float walkBob = 0.05f;

    [Header("加速前傾、速度伸展")]
    [Tooltip("每 1 米／秒² 的加速度前傾幾度")]
    public float leanPerAccel = 0.35f;
    [Tooltip("前傾／後仰最大角度")]
    public float maxLean = 8f;
    [Tooltip("全速時身體往前拉長的比例")]
    public float speedStretch = 0.06f;
    [Tooltip("算伸展用的「全速」（米／秒）")]
    public float stretchFullSpeed = 12f;

    [Header("待機呼吸")]
    public float breathScale = 0.015f;
    public float breathPeriod = 2.6f;
    public float idleSway = 0.8f;

    [Header("撲咬")]
    [Tooltip("離玩家多近開始壓低蓄力（在 WolfEnemy.biteApproachDistance 之外再加這段）")]
    public float crouchExtraDistance = 1.5f;
    public float crouchDepth = 0.12f;
    public float pounceDuration = 0.32f;
    public float pounceHeight = 0.55f;
    public float tugAngle = 5f;
    public float tugFrequency = 6f;
    [Tooltip("咬住時每隔幾秒猛扯一下")]
    public float yankInterval = 0.7f;

    [Header("被打暈／鬆口")]
    public float stunRecoilAngle = 20f;
    public float stunRecoilSeconds = 0.5f;
    public float stunHop = 0.35f;
    public float releaseHop = 0.25f;

    [Header("個體差異")]
    [Range(0f, 0.2f)] public float sizeVariation = 0.08f;
    [Range(0f, 0.2f)] public float shadeVariation = 0.07f;

    [Header("腳下揚塵")]
    public bool dust = true;
    [Tooltip("奔跑時每前進 1 米揚起幾顆灰塵")]
    public float dustPerMeter = 1.6f;
    [Tooltip("超過這個速度才揚塵（米／秒）")]
    public float dustMinSpeed = 4.5f;
    public Color dustColor = new Color(0.78f, 0.75f, 0.70f, 0.35f);
    public int pounceDustBurst = 12;

    // ── 參考 ──
    private WolfEnemy _wolf;
    private WolfSpriteAnimator _anim;
    private Rigidbody _rb;
    private Transform _visual;
    private SpriteRenderer _sr;
    private Animator _animator;
    private Transform _player;
    private static FieldInfo _fAttached, _fStunned;

    // ── 狀態 ──
    private float _seed, _size, _shade;

    /// <summary>這隻狼的深淺倍率（顏色由 WolfSpriteAnimator 每幀套用，跟各張圖的亮度校正一起算）。</summary>
    public float Shade => _shade <= 0f ? 1f : _shade;
    private float _prevVx, _accel, _lean, _crouch, _stretch;
    private bool _wasAttached, _wasStunned;
    private float _pounceT = -1f, _stunT = -1f, _releaseT = -1f, _attachTime;
    private bool _pounceLanded;

    // ── 疊加／還原 ──
    private bool _applied;
    private Vector3 _basePos, _appliedPos, _baseScale, _appliedScale;
    private Quaternion _baseRot, _appliedRot;

    // ── 灰塵 ──
    private ParticleSystem _dust;
    private float _dustCarry;
    private float _footLocalY;
    private static Texture2D _dustTex;
    private static Material _dustMat;

    private void Awake()
    {
        _seed = Random.Range(0f, 100f);
        _size = 1f + Random.Range(-sizeVariation, sizeVariation);
        _shade = 1f + Random.Range(-shadeVariation, shadeVariation);
    }

    private void Start()
    {
        _wolf = GetComponent<WolfEnemy>();
        _anim = GetComponent<WolfSpriteAnimator>();
        _rb = GetComponent<Rigidbody>();
        _sr = GetComponentInChildren<SpriteRenderer>(true);
        _visual = _sr != null && _sr.transform != transform ? _sr.transform : (transform.childCount > 0 ? transform.GetChild(0) : null);
        _animator = _visual != null ? _visual.GetComponent<Animator>() : null;
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _player = p.transform;
        if (_fAttached == null)
        {
            _fAttached = typeof(WolfEnemy).GetField("isAttached", BindingFlags.NonPublic | BindingFlags.Instance);
            _fStunned = typeof(WolfEnemy).GetField("isStunned", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        // 狼在 sprite 格子裡畫得偏上，格子下方約 18% 是空白：腳掌高度＝格子底再往上 18%
        if (_sr != null) _footLocalY = _sr.bounds.min.y + _sr.bounds.size.y * 0.18f - transform.position.y;
        if (dust) CreateDust();
    }

    private bool Attached => _wolf != null && _fAttached != null && (bool)_fAttached.GetValue(_wolf);
    private bool Stunned => _wolf != null && _fStunned != null && (bool)_fStunned.GetValue(_wolf);

    /// <summary>上一幀疊上去的動態拿掉（別的程式這中間改過就保留它的值）。</summary>
    private void Update()
    {
        if (!_applied || _visual == null) return;
        if (_visual.localPosition == _appliedPos) _visual.localPosition = _basePos;
        if (_visual.localRotation == _appliedRot) _visual.localRotation = _baseRot;
        if (_visual.localScale == _appliedScale) _visual.localScale = _baseScale;
        _applied = false;
    }

    private void LateUpdate()
    {
        if (_visual == null || _rb == null) return;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        float t = Time.time + _seed;

        bool attached = Attached, stunned = Stunned;
        float vx = _rb.isKinematic ? 0f : _rb.linearVelocity.x;
        float speed = Mathf.Abs(vx);
        float facing = Mathf.Sign(_visual.localScale.x == 0f ? 1f : _visual.localScale.x);

        // 狀態變化觸發
        if (attached && !_wasAttached) { _pounceT = 0f; _pounceLanded = false; _attachTime = Time.time; }
        if (stunned && !_wasStunned) { _stunT = 0f; EmitBurst(pounceDustBurst / 2); }
        if (!attached && _wasAttached && !stunned) _releaseT = 0f;
        _wasAttached = attached; _wasStunned = stunned;

        float offY = 0f, offX = 0f, ang = 0f, sx = _size, sy = _size;

        // ⓪ 三張 sprite sheet 的比例與腳底高度校正（依這一格來自哪張圖，換圖同一幀就對齊）
        if (_anim != null && _sr != null)
        {
            _anim.GetSheetCorrection(_sr.sprite, out _, out float sheetScale, out float sheetY);
            sx *= sheetScale; sy *= sheetScale; offY += sheetY;
        }

        // ① 步伐起伏（依動畫相位）
        bool idle = _anim != null && _anim.IsIdlePose;
        if (!idle && _animator != null && !attached && !stunned && speed > 0.1f)
        {
            float nt = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float stride = nt * 2f - Mathf.Floor(nt * 2f);   // 每段動畫約兩個步伐
            bool run = _anim != null && _anim.CurrentState == _anim.runStateName;
            float w = Mathf.Clamp01(speed / 3f);
            if (run)
            {
                offY += runBob * w * (0.5f - 0.5f * Mathf.Cos(stride * Mathf.PI * 2f));
                ang += runRock * w * Mathf.Sin(stride * Mathf.PI * 2f);
            }
            else
            {
                float step = stride * 2f - Mathf.Floor(stride * 2f);   // 慢走每個步伐兩次起伏
                offY += walkBob * w * (0.5f - 0.5f * Mathf.Cos(step * Mathf.PI * 2f));
            }
        }

        // ② 加速前傾、速度伸展
        float rawAccel = (vx - _prevVx) / dt;
        _prevVx = vx;
        _accel = Mathf.Lerp(_accel, rawAccel, 1f - Mathf.Exp(-6f * dt));
        float leanTarget = (attached || stunned) ? 0f : Mathf.Clamp(-_accel * facing * leanPerAccel, -maxLean, maxLean);
        _lean = Mathf.Lerp(_lean, leanTarget, 1f - Mathf.Exp(-8f * dt));
        ang += _lean;
        _stretch = Mathf.Lerp(_stretch, (attached || stunned) ? 0f : Mathf.Clamp01(speed / Mathf.Max(1f, stretchFullSpeed)), 1f - Mathf.Exp(-5f * dt));
        sx *= 1f + speedStretch * _stretch;
        sy *= 1f - speedStretch * 0.6f * _stretch;

        // ③ 待機呼吸
        if (idle && !attached && !stunned)
        {
            float b = Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.5f, breathPeriod));
            sy *= 1f + breathScale * b;
            ang += idleSway * Mathf.Sin(t * 0.9f);
        }

        // ④ 撲咬前蓄力：快咬到時壓低
        float crouchTarget = 0f;
        if (!attached && !stunned && _player != null && _wolf != null)
        {
            float dx = _player.position.x - transform.position.x;
            float range = _wolf.biteApproachDistance + crouchExtraDistance;
            bool closing = vx * dx > 0f;
            if (Mathf.Abs(dx) < range && closing) crouchTarget = 1f - Mathf.Clamp01((Mathf.Abs(dx) - _wolf.biteApproachDistance) / Mathf.Max(0.1f, crouchExtraDistance));
        }
        _crouch = Mathf.Lerp(_crouch, crouchTarget, 1f - Mathf.Exp(-10f * dt));
        offY -= crouchDepth * _crouch;
        sy *= 1f - 0.06f * _crouch;
        sx *= 1f + 0.04f * _crouch;
        ang -= 4f * _crouch * facing;

        // ⑤ 撲咬弧線 → 咬住拉扯
        if (_pounceT >= 0f)
        {
            _pounceT += dt;
            float u = Mathf.Clamp01(_pounceT / Mathf.Max(0.05f, pounceDuration));
            float s = Mathf.Sin(u * Mathf.PI);
            offY += pounceHeight * s;
            sx *= 1f + 0.14f * s;
            sy *= 1f - 0.08f * s;
            ang += 12f * (1f - 2f * u) * s * facing;   // 先抬頭、再往下咬
            if (u >= 1f)
            {
                _pounceT = -1f;
                if (!_pounceLanded) { EmitBurst(pounceDustBurst); _pounceLanded = true; }
            }
        }
        else if (attached)
        {
            float tt = Time.time - _attachTime;
            ang += tugAngle * Mathf.Sin(tt * tugFrequency * Mathf.PI * 2f) + 1.5f * Mathf.Sin(tt * 17.3f + _seed);
            offX += 0.05f * Mathf.Sin(tt * tugFrequency * Mathf.PI);
            float yank = Mathf.Repeat(tt, Mathf.Max(0.2f, yankInterval)) / 0.15f;   // 每隔一段時間猛扯 0.15 秒
            if (yank < 1f)
            {
                float k = Mathf.Sin(yank * Mathf.PI);
                offX -= 0.15f * k * facing;
                ang += 6f * k * facing;
            }
        }

        // ⑥ 被打暈：後仰彈起 → 搖晃
        if (stunned)
        {
            if (_stunT >= 0f)
            {
                _stunT += dt;
                float u = Mathf.Clamp01(_stunT / Mathf.Max(0.05f, stunRecoilSeconds));
                ang += stunRecoilAngle * (1f - u) * (1f - u) * facing;
                offY += stunHop * Mathf.Sin(Mathf.Min(1f, u * 1.6f) * Mathf.PI);
                if (u >= 1f) _stunT = -1f;
            }
            else ang += 2.5f * Mathf.Sin(t * 1.5f * Mathf.PI * 2f);
        }
        else _stunT = -1f;

        // ⑦ 鬆口退後：往後輕跳
        if (_releaseT >= 0f)
        {
            _releaseT += dt;
            float u = Mathf.Clamp01(_releaseT / 0.25f);
            offY += releaseHop * Mathf.Sin(u * Mathf.PI);
            if (u >= 1f) _releaseT = -1f;
        }

        // 疊上去（以外觀物件目前的值為基準）
        _basePos = _visual.localPosition;
        _baseRot = _visual.localRotation;
        _baseScale = _visual.localScale;
        _appliedPos = _basePos + new Vector3(offX, offY, 0f);
        _appliedRot = _baseRot * Quaternion.Euler(0f, 0f, ang);
        _appliedScale = new Vector3(_baseScale.x * sx, _baseScale.y * sy, _baseScale.z);
        _visual.localPosition = _appliedPos;
        _visual.localRotation = _appliedRot;
        _visual.localScale = _appliedScale;
        _applied = true;

        UpdateDust(speed, attached || stunned, dt);
    }

    // ── 灰塵 ──
    private void CreateDust()
    {
        // Spawner 複製模板狼時，模板身上已經生好的灰塵也會被複製過來：直接沿用，不要再生一個
        Transform existing = transform.Find("WolfDust");
        if (existing != null && existing.TryGetComponent(out _dust))
        {
            existing.localPosition = new Vector3(0f, _footLocalY, 0f);
            _dust.Clear();
            _dust.Play();
            return;
        }

        if (_dustMat == null)
        {
            _dustTex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f;
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                    _dustTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            _dustTex.Apply();
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) { dust = false; return; }
            _dustMat = new Material(sh) { mainTexture = _dustTex };
        }

        var go = new GameObject("WolfDust");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, _footLocalY, 0f);
        _dust = go.AddComponent<ParticleSystem>();
        _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _dust.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startColor = dustColor;
        main.gravityModifier = -0.03f;
        main.maxParticles = 80;

        var em = _dust.emission; em.rateOverTime = 0f;
        var shape = _dust.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = 0.4f;
        shape.rotation = new Vector3(-90f, 0f, 0f);   // 往上噴

        var sol = _dust.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
        var col = _dust.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = _dustMat;
        if (_sr != null) { pr.sortingLayerID = _sr.sortingLayerID; pr.sortingOrder = _sr.sortingOrder - 1; }
        _dust.Play();
    }

    private void UpdateDust(float speed, bool busy, float dt)
    {
        if (!dust || _dust == null) return;
        if (_sr != null)
        {
            var pr = _dust.GetComponent<ParticleSystemRenderer>();
            if (pr.sortingOrder != _sr.sortingOrder - 1) pr.sortingOrder = _sr.sortingOrder - 1;
            // 狼淡入時灰塵也跟著（Spawner 只改 alpha）
            var main = _dust.main;
            Color c = dustColor; c.a *= _sr.color.a; main.startColor = c;
        }
        if (busy || speed < dustMinSpeed || _rb.isKinematic) { _dustCarry = 0f; return; }
        _dustCarry += speed * dt * dustPerMeter;
        int n = Mathf.FloorToInt(_dustCarry);
        if (n > 0) { _dust.Emit(n); _dustCarry -= n; }
    }

    private void EmitBurst(int n)
    {
        if (dust && _dust != null && n > 0) _dust.Emit(n);
    }

    private void OnDisable()
    {
        // 關掉時把疊上去的動態拿掉，外觀物件回到乾淨的值
        if (_applied && _visual != null)
        {
            if (_visual.localPosition == _appliedPos) _visual.localPosition = _basePos;
            if (_visual.localRotation == _appliedRot) _visual.localRotation = _baseRot;
            if (_visual.localScale == _appliedScale) _visual.localScale = _baseScale;
            _applied = false;
        }
    }
}
