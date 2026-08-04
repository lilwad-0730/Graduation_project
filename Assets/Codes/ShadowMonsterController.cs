using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

/// <summary>
/// 影子怪物（巨大玩家形象）主控制器。
/// 
/// 【狀態流程】
///   Dormant → (觸發點) → Appearing → Chasing → (全部燭火) → Vanishing (勝利)
///                                            → (最後燭火未全集) → Punishing → Devouring → 重生→Dormant
/// </summary>
public class ShadowMonsterController : MonoBehaviour, IResettable
{
    // ──────────── Singleton ────────────
    public static ShadowMonsterController Instance { get; private set; }

    // ──────────── 狀態機 ────────────
    public enum MonsterState { Dormant, Appearing, Chasing, Punishing, Devouring, Vanishing }

    [Header("【狀態監控 (勿手動修改)】")]
    public MonsterState currentState = MonsterState.Dormant;

    // ──────────── Inspector 設定 ────────────
    [Header("目標設定")]
    [Tooltip("玩家 Transform（留空時自動透過 Tag 或 PlayerMovement 腳本尋找）")]
    public Transform player;

    [Header("燭火清單 (⚠ 請手動拖曳所有燭火物件到此陣列)")]
    public CandleCollectible[] candles;

    [Header("追逐行為設定")]
    [Tooltip("怪物登場出現時，是否同步朝玩家追擊？（勾選 = 邊現身邊追擊；取消 = 原地登場完再追）")]
    public bool chaseWhileAppearing = true;
    [Tooltip("一般追逐速度（玩家正常速度約 5，低於 5 讓玩家可以跑掉）")]
    public float chaseSpeed = 3.2f;
    [Tooltip("懲罰階段追逐速度（應快於被恐懼壓制的玩家速度 2.5）")]
    public float punishChaseSpeed = 4.8f;
    [Tooltip("距離玩家多近算「追上吞噬」(巨大怪物建議 3.5 ~ 4.5，因為體型大)")]
    public float catchDistance = 3.5f;

    [Header("動畫設定")]
    [Tooltip("怪物追擊時自動強行播放的動畫 Clip 名稱 (預設 Walk)")]
    public string walkAnimationName = "Walk";

    [Header("轉場時間設定")]
    [Tooltip("怪物漸漸出現的時間（秒）")]
    public float appearDuration = 2.5f;
    [Tooltip("怪物勝利後漸漸消失的時間（秒）")]
    public float vanishDuration = 2.0f;
    [Tooltip("懲罰時光暈增大的漸變時間（秒）")]
    public float punishHaloGrowDuration = 1.5f;

    [Header("燭火縮放設定")]
    [Range(0.05f, 0.5f)]
    [Tooltip("每收集一根燭火，怪物縮小的比例（0.15 = 15%）")]
    public float shrinkPerCandle = 0.15f;
    [Range(0.1f, 0.6f)]
    [Tooltip("怪物縮小的下限，避免縮到消失（0.2 = 最小為原本的 20%）")]
    public float minScaleMultiplier = 0.2f;

    [Header("光暈設定")]
    [Tooltip("光暈基礎半徑/倍率")]
    public float haloBaseRadius = 3.5f;
    [Tooltip("光暈視覺顏色（暗灰紅光環）")]
    public Color haloColor = new Color(0.95f, 0.25f, 0.25f, 0.75f);
    [Tooltip("點光源顏色")]
    public Color lightColor = new Color(1.0f, 0.2f, 0.2f, 1f);
    [Tooltip("點光源強度")]
    public float lightBaseIntensity = 8.0f;
    [Tooltip("點光源影響範圍")]
    public float lightRange = 15.0f;
    [Tooltip("懲罰時光暈增大倍率（1.2 = 比基礎大 20%）")]
    public float punishHaloMultiplier = 1.2f;

    [Header("恐懼懲罰設定")]
    [Range(0.1f, 0.9f)]
    [Tooltip("懲罰期間玩家速度倍率（0.5 = 速度降到 50%）")]
    public float fearSpeedMultiplier = 0.5f;
    [Range(0.1f, 0.9f)]
    [Tooltip("懲罰期間玩家跳躍力倍率（0.65 = 跳躍力降到 65%）")]
    public float fearJumpMultiplier = 0.65f;

    [Header("重生系統參考")]
    [Tooltip("留空時自動尋找場景中的 PlayerRespawnSystem")]
    public PlayerRespawnSystem respawnSystem;

    // ──────────── 內部狀態 ────────────
    private int _candlesCollected = 0;
    private int _totalCandles = 0;
    private CandleCollectible _lastCandleByX;

    private Vector3 _initialPosition;
    private Vector3 _baseScale;
    private float _currentScaleMultiplier = 1f;

    // 光暈組件（程式動態生成）
    private SpriteRenderer _haloSpriteRenderer;
    private ParticleSystem _haloPs;
    private Light _haloLight;

    // 視覺與動畫組件快取
    private Renderer[] _renderers;
    private Color[] _originalColors;
    private MaterialPropertyBlock _mpb;
    private Animator _animator;

    // 玩家組件快取
    private PlayerMovement _pm;
    private float _origSpeed;
    private float _origJumpForce;
    private bool _fearActive = false;

    // 相機鎖定
    private Camera _mainCam;
    private Transform _camLockDummy;
    private bool _camLocked = false;
    private List<CinemachineCamera> _vcams3 = new List<CinemachineCamera>();
    private List<CinemachineVirtualCamera> _vcamsLegacy = new List<CinemachineVirtualCamera>();
    private List<Transform> _origFollow = new List<Transform>();

    // Coroutine 追蹤
    private Coroutine _stateCoroutine;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity 生命週期
    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _baseScale = transform.localScale;
        _initialPosition = transform.position;
    }

    void Start()
    {
        _mainCam = Camera.main;

        // 快取動畫組件
        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        // 快取視覺組件（包括 SpriteRenderer、SkinnedMeshRenderer、MeshRenderer）
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb = new MaterialPropertyBlock();
        _originalColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            Material mat = _renderers[i].sharedMaterial;
            if (mat != null && mat.HasProperty("_BaseColor"))
                _originalColors[i] = mat.GetColor("_BaseColor");
            else if (mat != null && mat.HasProperty("_Color"))
                _originalColors[i] = mat.GetColor("_Color");
            else
                _originalColors[i] = Color.white;
        }

        // 自動強效搜尋玩家
        EnsurePlayerReference();

        // 自動尋找重生系統
        if (respawnSystem == null)
            respawnSystem = FindFirstObjectByType<PlayerRespawnSystem>();

        // 初始化燭火資訊
        SetupCandles();

        // 建立光暈特效（包含 procedural 2D 光暈貼圖 + 粒子 + 光源）
        CreateHaloEffect();

        // 初始狀態：完全隱藏（Scale = 0 & Visuals Disabled）
        SetVisualAlpha(0f);
        transform.localScale = Vector3.zero;
    }

    void Update()
    {
        EnsurePlayerReference();

        switch (currentState)
        {
            case MonsterState.Appearing:
                if (chaseWhileAppearing)
                {
                    MoveTowardPlayer(chaseSpeed);
                    CheckCatch();
                }
                break;

            case MonsterState.Chasing:
                MoveTowardPlayer(chaseSpeed);
                CheckCatch();
                break;

            case MonsterState.Punishing:
                MoveTowardPlayer(punishChaseSpeed);
                CheckCatch();
                if (_camLocked) ClampPlayerToCameraView();
                break;
        }
    }

    // 碰觸雙重保險判定（除了距離判定，物理碰撞也算抓到）
    private void OnTriggerEnter(Collider other)
    {
        CheckCollisionCatch(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckCollisionCatch(collision.gameObject);
    }

    private void CheckCollisionCatch(GameObject go)
    {
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Punishing && currentState != MonsterState.Appearing) return;
        if (go.CompareTag("Player") || go.GetComponent<PlayerMovement>() != null || go.name.ToLower().Contains("player"))
        {
            TriggerDevour();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 公開介面
    // ──────────────────────────────────────────────────────────────────────────

    public void ActivateChase()
    {
        if (currentState != MonsterState.Dormant) return;
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(AppearSequence());
    }

    public void OnCandleCollected(CandleCollectible candle)
    {
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Appearing)
        {
            Debug.Log($"【影子怪物】非追逐狀態（{currentState}），燭火收集無效。");
            return;
        }

        _candlesCollected++;
        Debug.Log($"【影子怪物】燭火收集 {_candlesCollected}/{_totalCandles}（{candle.gameObject.name}）");

        _currentScaleMultiplier = Mathf.Max(minScaleMultiplier, 1f - shrinkPerCandle * _candlesCollected);
        ApplyCurrentScale();

        if (candle == _lastCandleByX && _candlesCollected < _totalCandles)
        {
            Debug.Log("【影子怪物】吃到最後一根燭火但未全部收集！觸發懲罰！");
            TriggerPunishment();
            return;
        }

        if (_candlesCollected >= _totalCandles)
        {
            Debug.Log("【影子怪物】全部燭火收集完畢！怪物開始消失...");
            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(VanishSequence());
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：狀態機核心邏輯
    // ──────────────────────────────────────────────────────────────────────────

    private IEnumerator AppearSequence()
    {
        currentState = MonsterState.Appearing;
        Debug.Log("【影子怪物】開始現身...");

        PlayWalkAnimation();

        float t = 0f;
        while (t < appearDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / appearDuration);

            SetVisualAlpha(progress);
            transform.localScale = _baseScale * _currentScaleMultiplier * progress;

            yield return null;
        }

        SetVisualAlpha(1f);
        transform.localScale = _baseScale * _currentScaleMultiplier;

        currentState = MonsterState.Chasing;
        Debug.Log("【影子怪物】登場完成，全力追逐！");
    }

    private IEnumerator VanishSequence()
    {
        currentState = MonsterState.Vanishing;
        RemoveFear();
        UnlockCamera();

        float t = 0f;
        Vector3 startScale = transform.localScale;

        while (t < vanishDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(1f, 0f, t / vanishDuration);

            SetVisualAlpha(progress);
            transform.localScale = startScale * progress;

            yield return null;
        }

        SetVisualAlpha(0f);
        transform.localScale = Vector3.zero;
        currentState = MonsterState.Dormant;
        Debug.Log("【影子怪物】玩家收集所有燭火，怪物消散！玩家勝利！");
    }

    private void TriggerDevour()
    {
        if (currentState == MonsterState.Devouring) return;
        currentState = MonsterState.Devouring;
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(DevourSequence());
    }

    private IEnumerator DevourSequence()
    {
        Debug.Log("【影子怪物】玩家被吞噬！啟動重生機制與世界刷新！");

        RemoveFear();
        UnlockCamera();

        // 觸發 Respawn 畫面黑化轉場
        if (respawnSystem != null)
        {
            respawnSystem.TriggerRespawn();
        }
        else
        {
            Debug.LogWarning("【影子怪物】找不到 PlayerRespawnSystem！");
        }

        // 等待畫面黑幕覆蓋（1.5秒後）
        yield return new WaitForSecondsRealtime(1.5f);

        // 重置影子怪物與所有燭火回初始狀態
        ResetToInitialState();
    }

    private void TriggerPunishment()
    {
        if (currentState == MonsterState.Punishing) return;
        currentState = MonsterState.Punishing;

        _currentScaleMultiplier = 1f;
        transform.localScale = _baseScale;

        ApplyFear();
        LockCamera();

        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(GrowHaloRoutine(
            _currentScaleMultiplier,
            punishHaloMultiplier,
            punishHaloGrowDuration
        ));

        Debug.Log("【影子怪物】懲罰狀態：鏡頭鎖定、玩家減速、怪物恢復大小！");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：移動、自動搜尋與判定
    // ──────────────────────────────────────────────────────────────────────────

    private void EnsurePlayerReference()
    {
        if (player != null) return;

        GameObject p = GameObject.FindWithTag("Player");
        if (p == null)
        {
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) p = pm.gameObject;
        }
        if (p == null) p = GameObject.Find("Player");

        if (p != null)
        {
            player = p.transform;
            _pm = player.GetComponent<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInChildren<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInParent<PlayerMovement>();
            Debug.Log($"【影子怪物】成功連結玩家物件：{player.name}");
        }
    }

    private void MoveTowardPlayer(float speed)
    {
        if (player == null) return;

        PlayWalkAnimation();

        float targetX = player.position.x;
        float newX = Mathf.MoveTowards(transform.position.x, targetX, speed * Time.deltaTime);
        transform.position = new Vector3(newX, transform.position.y, transform.position.z);
    }

    private void PlayWalkAnimation()
    {
        if (_animator != null && !string.IsNullOrEmpty(walkAnimationName))
        {
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            if (!stateInfo.IsName(walkAnimationName))
            {
                _animator.Play(walkAnimationName);
            }
        }
    }

    private void CheckCatch()
    {
        if (player == null || currentState == MonsterState.Devouring) return;

        // 依據當前體型動態計算抓取距離（體型越大抓取距離越大）
        float effectiveCatchDist = catchDistance * _currentScaleMultiplier;
        float dist = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.y),
            new Vector2(player.position.x, player.position.y)
        );

        if (dist <= effectiveCatchDist)
        {
            TriggerDevour();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：相機鎖定與恐懼
    // ──────────────────────────────────────────────────────────────────────────

    private void LockCamera()
    {
        if (_camLocked) return;
        _camLocked = true;

        if (_camLockDummy == null)
            _camLockDummy = new GameObject("ShadowMonster_CamLockDummy").transform;
        _camLockDummy.position = player != null ? player.position : Vector3.zero;

        _vcams3.Clear();
        _vcamsLegacy.Clear();
        _origFollow.Clear();

        foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            _vcams3.Add(v);
            _origFollow.Add(v.Follow);
            v.Follow = _camLockDummy;
        }
        foreach (var v in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None))
        {
            _vcamsLegacy.Add(v);
            _origFollow.Add(v.Follow);
            v.Follow = _camLockDummy;
        }

        Debug.Log("【影子怪物】相機已鎖定。");
    }

    private void UnlockCamera()
    {
        if (!_camLocked) return;
        _camLocked = false;

        int idx = 0;
        foreach (var v in _vcams3)
        {
            if (v != null && idx < _origFollow.Count)
                v.Follow = _origFollow[idx];
            idx++;
        }
        foreach (var v in _vcamsLegacy)
        {
            if (v != null && idx < _origFollow.Count)
                v.Follow = _origFollow[idx];
            idx++;
        }

        Debug.Log("【影子怪物】相機已解鎖。");
    }

    private void ClampPlayerToCameraView()
    {
        if (_mainCam == null || player == null) return;

        float halfW;
        if (_mainCam.orthographic)
        {
            halfW = _mainCam.orthographicSize * _mainCam.aspect;
        }
        else
        {
            float dist = Mathf.Abs(_mainCam.transform.position.z - player.position.z);
            halfW = dist * Mathf.Tan(_mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _mainCam.aspect;
        }

        float margin = 0.5f;
        float minX = _mainCam.transform.position.x - halfW + margin;
        float maxX = _mainCam.transform.position.x + halfW - margin;

        Vector3 pos = player.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        player.position = pos;
    }

    private void ApplyFear()
    {
        if (_fearActive || _pm == null) return;
        _fearActive = true;
        _origSpeed = _pm.baseSpeed;
        _origJumpForce = _pm.jumpForce;
        _pm.baseSpeed *= fearSpeedMultiplier;
        _pm.jumpForce *= fearJumpMultiplier;
        Debug.Log($"【影子怪物】恐懼效果：速度 {_origSpeed} → {_pm.baseSpeed}，跳躍力 {_origJumpForce} → {_pm.jumpForce}");
    }

    private void RemoveFear()
    {
        if (!_fearActive || _pm == null) return;
        _fearActive = false;
        _pm.baseSpeed = _origSpeed;
        _pm.jumpForce = _origJumpForce;
        Debug.Log("【影子怪物】恐懼效果已解除。");
    }

    private void SetupCandles()
    {
        if (candles == null || candles.Length == 0)
        {
            Debug.LogWarning("【影子怪物】⚠ 燭火清單為空！請在 Inspector 手動拖曳所有 CandleCollectible 物件至 candles[]。");
            return;
        }

        _totalCandles = candles.Length;
        _lastCandleByX = candles[0];
        foreach (var c in candles)
        {
            if (c != null && c.transform.position.x > _lastCandleByX.transform.position.x)
                _lastCandleByX = c;
        }

        Debug.Log($"【影子怪物】共 {_totalCandles} 根燭火。" +
                  $"X 最大（懲罰判定）：{_lastCandleByX?.gameObject.name} " +
                  $"(X = {_lastCandleByX?.transform.position.x:F1})");
    }

    private void ApplyCurrentScale()
    {
        transform.localScale = _baseScale * _currentScaleMultiplier;
        UpdateHaloRadius(_currentScaleMultiplier);
    }

    private IEnumerator GrowHaloRoutine(float fromMult, float toMult, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float m = Mathf.SmoothStep(fromMult, toMult, t / duration);
            UpdateHaloRadius(m);
            yield return null;
        }
        UpdateHaloRadius(toMult);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：強效光暈（Sprite 貼圖 + 粒子 + 光源 三重保障）
    // ──────────────────────────────────────────────────────────────────────────

    private void CreateHaloEffect()
    {
        // ── 1. 程式生成 2D 柔光 Sprite（100% 保障在任何渲染管線中都能顯現） ──
        GameObject glowObj = new GameObject("Shadow_HaloGlowSprite");
        glowObj.transform.SetParent(transform);
        glowObj.transform.localPosition = new Vector3(0f, 1.2f, -0.2f);
        glowObj.transform.localScale = Vector3.one * haloBaseRadius * 1.8f;

        _haloSpriteRenderer = glowObj.AddComponent<SpriteRenderer>();
        _haloSpriteRenderer.sprite = GenerateSoftGlowSprite();
        _haloSpriteRenderer.color = haloColor;
        _haloSpriteRenderer.sortingOrder = 999;

        // ── 2. 點光源（強大紅光，Z=-1.2） ──
        GameObject lightObj = new GameObject("Shadow_HaloLight");
        lightObj.transform.SetParent(transform);
        lightObj.transform.localPosition = new Vector3(0f, 1.2f, -1.2f);

        _haloLight = lightObj.AddComponent<Light>();
        _haloLight.type = LightType.Point;
        _haloLight.color = lightColor;
        _haloLight.intensity = lightBaseIntensity;
        _haloLight.range = lightRange;
        _haloLight.shadows = LightShadows.None;

        // ── 3. 粒子氣場 ──
        GameObject psObj = new GameObject("Shadow_HaloParticles");
        psObj.transform.SetParent(transform);
        psObj.transform.localPosition = new Vector3(0f, 1.0f, -0.5f);
        psObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _haloPs = psObj.AddComponent<ParticleSystem>();

        var main = _haloPs.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(haloColor.r, haloColor.g, haloColor.b, 0.8f),
            new Color(haloColor.r * 0.7f, 0.05f, 0.05f, 0.9f)
        );

        var emission = _haloPs.emission;
        emission.rateOverTime = 120f;

        var shape = _haloPs.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = haloBaseRadius;

        var psRend = _haloPs.GetComponent<ParticleSystemRenderer>();
        psRend.renderMode = ParticleSystemRenderMode.Billboard;
        psRend.sortingOrder = 1000;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Transparent");
        if (sh != null)
        {
            Material mat = new Material(sh);
            mat.color = haloColor;
            psRend.material = mat;
        }

        _haloPs.Play();
        Debug.Log("【影子怪物】強效光暈（2D貼圖 + 粒子 + 光源）建立完成。");
    }

    /// <summary>自動程式生成漸層柔光 Sprite</summary>
    private Sprite GenerateSoftGlowSprite()
    {
        int res = 128;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
        float radius = res * 0.48f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float normDist = Mathf.Clamp01(dist / radius);
                // 高斯/平滑放射漸層
                float alpha = Mathf.Pow(1f - normDist, 2.0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
    }

    private void UpdateHaloRadius(float multiplier)
    {
        float m = Mathf.Max(0.1f, multiplier);
        if (_haloSpriteRenderer != null)
        {
            _haloSpriteRenderer.transform.localScale = Vector3.one * haloBaseRadius * 1.8f * m;
        }
        if (_haloPs != null)
        {
            var shape = _haloPs.shape;
            shape.radius = haloBaseRadius * m;
        }
        if (_haloLight != null)
        {
            _haloLight.range = lightRange * m;
            _haloLight.intensity = lightBaseIntensity * m;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：視覺與光暈開關控制
    // ──────────────────────────────────────────────────────────────────────────

    private void SetVisualAlpha(float alpha)
    {
        bool showVisuals = alpha > 0.001f;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            _renderers[i].enabled = showVisuals;

            if (showVisuals)
            {
                Color c = _originalColors[i];
                c.a = c.a * alpha;

                _renderers[i].GetPropertyBlock(_mpb);
                if (_renderers[i].sharedMaterial != null && _renderers[i].sharedMaterial.HasProperty("_BaseColor"))
                    _mpb.SetColor("_BaseColor", c);
                else
                    _mpb.SetColor("_Color", c);

                _renderers[i].SetPropertyBlock(_mpb);

                if (_renderers[i] is SpriteRenderer sr)
                    sr.color = c;
            }
        }

        if (_haloSpriteRenderer != null)
        {
            _haloSpriteRenderer.enabled = showVisuals;
            Color sc = haloColor;
            sc.a = haloColor.a * alpha;
            _haloSpriteRenderer.color = sc;
        }

        if (_haloPs != null)
        {
            if (!showVisuals)
            {
                _haloPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            else
            {
                if (!_haloPs.isPlaying) _haloPs.Play();
            }
        }

        if (_haloLight != null)
        {
            _haloLight.enabled = showVisuals;
            _haloLight.intensity = lightBaseIntensity * alpha;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResettable：重生後由 DevourSequence 呼叫，重置一切至初始狀態
    // ──────────────────────────────────────────────────────────────────────────

    public void ResetToInitialState()
    {
        StopAllCoroutines();
        _stateCoroutine = null;

        currentState = MonsterState.Dormant;
        _candlesCollected = 0;
        _currentScaleMultiplier = 1f;

        RemoveFear();
        UnlockCamera();

        transform.position = _initialPosition;
        transform.localScale = Vector3.zero;

        UpdateHaloRadius(1f);
        SetVisualAlpha(0f);

        // 重置場景中所有燭火（讓它們重新出現）
        if (candles != null)
        {
            foreach (var c in candles)
            {
                if (c != null) c.ResetToInitialState();
            }
        }

        Debug.Log("【影子怪物】已完整重置至初始狀態（含全部燭火）。");
    }
}
