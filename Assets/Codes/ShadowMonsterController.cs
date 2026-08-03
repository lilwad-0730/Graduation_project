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
///
/// 【場景建置步驟】
///   1. 複製玩家 Prefab，刪除所有邏輯腳本，放大 Scale，掛載此腳本。
///   2. 調整 SpriteRenderer 顏色為暗紅黑 (0.3, 0.1, 0.1, 1)。
///   3. 建立空物件 "ShadowMonsterTrigger"，加 BoxCollider(IsTrigger=true)，掛載 ShadowMonsterTriggerZone.cs。
///   4. 在 candles[] 欄位手動拖曳所有 CandleCollectible 物件。
///   5. 確認場景底部有 DeathZone，各安全平台有 RespawnPoint（可沿用 dark glasses 既有設定）。
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
    [Tooltip("玩家 Transform（留空時自動透過 Tag 尋找）")]
    public Transform player;

    [Header("燭火清單 (⚠ 請手動拖曳所有燭火物件到此陣列)")]
    public CandleCollectible[] candles;

    [Header("追逐速度設定")]
    [Tooltip("一般追逐速度（玩家正常速度約 5，低於 5 讓玩家可以跑掉）")]
    public float chaseSpeed = 3.2f;
    [Tooltip("懲罰階段追逐速度（應快於被恐懼壓制的玩家速度 2.5）")]
    public float punishChaseSpeed = 4.8f;
    [Tooltip("距離玩家多近算「追上吞噬」(世界單位)")]
    public float catchDistance = 1.5f;

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
    [Tooltip("光暈粒子環的基礎半徑（隨 Scale 動態調整）")]
    public float haloBaseRadius = 2.5f;
    [Tooltip("光暈粒子顏色（灰帶紅，建議 A 值 0.5 左右）")]
    public Color haloColor = new Color(0.6f, 0.3f, 0.3f, 0.5f);
    [Tooltip("點光源顏色")]
    public Color lightColor = new Color(0.55f, 0.15f, 0.15f, 1f);
    [Tooltip("點光源基礎強度")]
    public float lightBaseIntensity = 3.0f;
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
    private CandleCollectible _lastCandleByX; // X 值最大（最右邊）的燭火

    private Vector3 _initialPosition;
    private Vector3 _baseScale;
    private float _currentScaleMultiplier = 1f;

    // 光暈組件（程式動態生成）
    private ParticleSystem _haloPs;
    private Light _haloLight;

    // 視覺組件快取
    private SpriteRenderer[] _spriteRenderers;
    private Color[] _originalColors;

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
        // Singleton
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _baseScale = transform.localScale;
        _initialPosition = transform.position;
    }

    void Start()
    {
        _mainCam = Camera.main;

        // 快取視覺組件
        _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        _originalColors = new Color[_spriteRenderers.Length];
        for (int i = 0; i < _spriteRenderers.Length; i++)
            _originalColors[i] = _spriteRenderers[i].color;

        // 自動尋找玩家
        if (player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
        }

        // 快取 PlayerMovement
        if (player != null)
        {
            _pm = player.GetComponent<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInChildren<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInParent<PlayerMovement>();
        }

        // 自動尋找重生系統
        if (respawnSystem == null)
            respawnSystem = FindFirstObjectByType<PlayerRespawnSystem>();

        // 初始化燭火資訊
        SetupCandles();

        // 建立光暈特效（程式自動生成，不需手動設定）
        CreateHaloEffect();

        // 初始狀態：完全不可見
        SetVisualAlpha(0f);
    }

    void Update()
    {
        switch (currentState)
        {
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

    // ──────────────────────────────────────────────────────────────────────────
    // 公開介面（供外部腳本呼叫）
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 由 ShadowMonsterTriggerZone 呼叫：啟動怪物出現序列。
    /// </summary>
    public void ActivateChase()
    {
        if (currentState != MonsterState.Dormant) return;
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(AppearSequence());
    }

    /// <summary>
    /// 由 CandleCollectible 呼叫：玩家收集一根燭火。
    /// </summary>
    public void OnCandleCollected(CandleCollectible candle)
    {
        // 只有追逐中，燭火才有效
        if (currentState != MonsterState.Chasing)
        {
            Debug.Log($"【影子怪物】非追逐狀態（{currentState}），燭火收集無效。");
            return;
        }

        _candlesCollected++;
        Debug.Log($"【影子怪物】燭火收集 {_candlesCollected}/{_totalCandles}（{candle.gameObject.name}）");

        // 縮小怪物（主體 + 光暈同步）
        _currentScaleMultiplier = Mathf.Max(minScaleMultiplier, 1f - shrinkPerCandle * _candlesCollected);
        ApplyCurrentScale();

        // ── 判斷：吃到最右邊那根，但尚未全部收集 → 觸發懲罰 ──
        if (candle == _lastCandleByX && _candlesCollected < _totalCandles)
        {
            Debug.Log("【影子怪物】吃到最後一根燭火但未全部收集！觸發懲罰！");
            TriggerPunishment();
            return;
        }

        // ── 判斷：全部燭火收集完畢 → 怪物消失（玩家勝利）──
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

    /// <summary>怪物從透明漸漸出現，完成後進入追逐狀態。</summary>
    private IEnumerator AppearSequence()
    {
        currentState = MonsterState.Appearing;
        Debug.Log("【影子怪物】開始出現...");

        float t = 0f;
        while (t < appearDuration)
        {
            t += Time.deltaTime;
            SetVisualAlpha(Mathf.SmoothStep(0f, 1f, t / appearDuration));
            yield return null;
        }
        SetVisualAlpha(1f);
        currentState = MonsterState.Chasing;
        Debug.Log("【影子怪物】出現完成，開始追逐！");
    }

    /// <summary>全部燭火收集後，怪物漸漸消失。</summary>
    private IEnumerator VanishSequence()
    {
        currentState = MonsterState.Vanishing;
        RemoveFear();
        UnlockCamera();

        float t = 0f;
        while (t < vanishDuration)
        {
            t += Time.deltaTime;
            SetVisualAlpha(Mathf.SmoothStep(1f, 0f, t / vanishDuration));
            yield return null;
        }
        SetVisualAlpha(0f);
        currentState = MonsterState.Dormant;
        Debug.Log("【影子怪物】玩家收集所有燭火，怪物消失！玩家勝利！");
    }

    /// <summary>吞噬玩家：黑幕覆蓋 → 觸發重生 → 重置怪物。</summary>
    private IEnumerator DevourSequence()
    {
        Debug.Log("【影子怪物】玩家被吞噬！");

        // 立刻還原玩家速度，避免重生後速度仍被壓制
        RemoveFear();
        UnlockCamera();

        // 觸發重生系統（帶黑幕轉場效果）
        if (respawnSystem != null)
            respawnSystem.TriggerRespawn();
        else
            Debug.LogWarning("【影子怪物】找不到 PlayerRespawnSystem！請手動指定。");

        // 等待黑幕完全覆蓋後再重置（避免重置瞬間被玩家看見）
        yield return new WaitForSecondsRealtime(2.0f);

        // 重置整個系統（怪物 + 燭火）
        ResetToInitialState();
    }

    /// <summary>懲罰觸發：鏡頭鎖定、玩家減速、怪物恢復原始大小、光暈增大 20%。</summary>
    private void TriggerPunishment()
    {
        if (currentState == MonsterState.Punishing) return;
        currentState = MonsterState.Punishing;

        // 怪物恢復原始大小
        _currentScaleMultiplier = 1f;
        transform.localScale = _baseScale;

        // 套用恐懼效果（玩家減速）
        ApplyFear();

        // 鏡頭鎖定
        LockCamera();

        // 光暈從當前大小漸增到 +20%
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(GrowHaloRoutine(
            _currentScaleMultiplier,
            punishHaloMultiplier,
            punishHaloGrowDuration
        ));

        Debug.Log("【影子怪物】懲罰狀態：鏡頭鎖定、玩家減速、怪物恢復大小！");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：移動與判定
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>怪物只追 X 軸（橫向捲軸設計），保持 Y/Z 不變。</summary>
    private void MoveTowardPlayer(float speed)
    {
        if (player == null) return;
        float targetX = player.position.x;
        float newX = Mathf.MoveTowards(transform.position.x, targetX, speed * Time.deltaTime);
        transform.position = new Vector3(newX, transform.position.y, transform.position.z);
    }

    /// <summary>每幀檢查是否追上玩家（XY 平面距離，忽略 Z）。</summary>
    private void CheckCatch()
    {
        if (player == null || currentState == MonsterState.Devouring) return;
        float dist = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.y),
            new Vector2(player.position.x, player.position.y)
        );
        if (dist <= catchDistance)
        {
            currentState = MonsterState.Devouring;
            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(DevourSequence());
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：相機鎖定
    // ──────────────────────────────────────────────────────────────────────────

    private void LockCamera()
    {
        if (_camLocked) return;
        _camLocked = true;

        // 建立虛假跟隨目標（固定在玩家當前位置，相機停止移動）
        if (_camLockDummy == null)
            _camLockDummy = new GameObject("ShadowMonster_CamLockDummy").transform;
        _camLockDummy.position = player != null ? player.position : Vector3.zero;

        // 快取並替換所有 Cinemachine 相機的 Follow 目標
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

    /// <summary>
    /// 懲罰期間每幀夾住玩家的 X 座標，使其無法超出相機可視範圍。
    /// 在 Update 中呼叫（Cinemachine 相機已固定，邊界穩定）。
    /// </summary>
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

        float margin = 0.5f; // 距螢幕邊緣留半格安全距離
        float minX = _mainCam.transform.position.x - halfW + margin;
        float maxX = _mainCam.transform.position.x + halfW - margin;

        Vector3 pos = player.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        player.position = pos;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：恐懼效果
    // ──────────────────────────────────────────────────────────────────────────

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

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：燭火初始化
    // ──────────────────────────────────────────────────────────────────────────

    private void SetupCandles()
    {
        if (candles == null || candles.Length == 0)
        {
            Debug.LogWarning("【影子怪物】⚠ 燭火清單為空！請在 Inspector 手動拖曳所有 CandleCollectible 物件至 candles[]。");
            return;
        }

        _totalCandles = candles.Length;

        // 找出 X 座標最大（最右邊）的燭火
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

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：縮放應用
    // ──────────────────────────────────────────────────────────────────────────

    private void ApplyCurrentScale()
    {
        transform.localScale = _baseScale * _currentScaleMultiplier;
        UpdateHaloRadius(_currentScaleMultiplier);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：光暈協程
    // ──────────────────────────────────────────────────────────────────────────

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
    // 私有：光暈建立（程式自動生成，無需手動設定）
    // ──────────────────────────────────────────────────────────────────────────

    private void CreateHaloEffect()
    {
        // ── 粒子光暈（環繞型粒子環） ──
        GameObject psObj = new GameObject("Shadow_HaloParticles");
        psObj.transform.SetParent(transform);
        psObj.transform.localPosition = Vector3.zero;
        // 旋轉 -90 度讓 Circle 發射方向從 XZ 平面轉到 XY 平面（2.5D 橫向視角正確）
        psObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _haloPs = psObj.AddComponent<ParticleSystem>();

        // 主模組
        var main = _haloPs.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.maxParticles = 350;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(haloColor.r, haloColor.g, haloColor.b, 0.25f),
            new Color(haloColor.r * 0.6f, haloColor.g * 0.25f, haloColor.b * 0.25f, 0.75f)
        );

        // 發射率
        var emission = _haloPs.emission;
        emission.rateOverTime = 90f;

        // 環形發射器
        var shape = _haloPs.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = haloBaseRadius;
        shape.radiusThickness = 0.12f;
        shape.arc = 360f;

        // 顏色隨生命週期淡出
        var col = _haloPs.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(haloColor.r, haloColor.g, haloColor.b), 0f),
                new GradientColorKey(new Color(haloColor.r * 0.4f, 0.05f, 0.05f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // 大小隨生命週期縮小
        var sol = _haloPs.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));

        // 渲染器設定
        var psRend = _haloPs.GetComponent<ParticleSystemRenderer>();
        psRend.renderMode = ParticleSystemRenderMode.Billboard;
        psRend.sortingOrder = 5; // 讓粒子在大多數背景物件之上

        // 選取最相容的 Shader（依序嘗試）
        Shader sh = Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Transparent")
                 ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply")
                 ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (sh != null)
        {
            Material mat = new Material(sh);
            mat.color = new Color(haloColor.r, haloColor.g, haloColor.b, haloColor.a);
            psRend.material = mat;
        }

        _haloPs.Play();

        // ── 點光源（加深氛圍感） ──
        GameObject lightObj = new GameObject("Shadow_HaloLight");
        lightObj.transform.SetParent(transform);
        lightObj.transform.localPosition = new Vector3(0f, 0f, -0.5f); // 比 Sprite 稍前方

        _haloLight = lightObj.AddComponent<Light>();
        _haloLight.type = LightType.Point;
        _haloLight.color = lightColor;
        _haloLight.intensity = lightBaseIntensity;
        _haloLight.range = haloBaseRadius * 3f;
        _haloLight.shadows = LightShadows.None;

        Debug.Log("【影子怪物】光暈特效已建立（粒子環 + 點光源）。");
    }

    private void UpdateHaloRadius(float multiplier)
    {
        if (_haloPs != null)
        {
            var shape = _haloPs.shape;
            shape.radius = haloBaseRadius * Mathf.Max(0.1f, multiplier);
        }
        if (_haloLight != null)
        {
            _haloLight.range = haloBaseRadius * 3f * Mathf.Max(0.1f, multiplier);
            _haloLight.intensity = lightBaseIntensity * Mathf.Max(0.1f, multiplier);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：視覺透明度控制
    // ──────────────────────────────────────────────────────────────────────────

    private void SetVisualAlpha(float alpha)
    {
        // SpriteRenderer
        for (int i = 0; i < _spriteRenderers.Length; i++)
        {
            if (_spriteRenderers[i] == null) continue;
            Color c = _originalColors[i];
            c.a = c.a * alpha;
            _spriteRenderers[i].color = c;
        }

        // 粒子光暈透明度
        if (_haloPs != null)
        {
            var m = _haloPs.main;
            m.startColor = new ParticleSystem.MinMaxGradient(
                new Color(haloColor.r, haloColor.g, haloColor.b, haloColor.a * alpha * 0.3f),
                new Color(haloColor.r * 0.6f, haloColor.g * 0.2f, haloColor.b * 0.2f, haloColor.a * alpha * 0.9f)
            );
        }

        // 光源亮度
        if (_haloLight != null)
            _haloLight.intensity = lightBaseIntensity * alpha;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResettable：重生後由 DevourSequence 呼叫，重置一切至初始狀態
    // ──────────────────────────────────────────────────────────────────────────

    public void ResetToInitialState()
    {
        StopAllCoroutines();
        _stateCoroutine = null;

        // 重置所有狀態變數
        currentState = MonsterState.Dormant;
        _candlesCollected = 0;
        _currentScaleMultiplier = 1f;

        // 還原玩家效果
        RemoveFear();
        UnlockCamera();

        // 還原怪物大小與位置（回到觸發點前的初始位置）
        transform.localScale = _baseScale;
        transform.position = _initialPosition;

        // 還原光暈
        UpdateHaloRadius(1f);

        // 怪物完全不可見
        SetVisualAlpha(0f);

        // 重置所有燭火（讓它們重新出現，可以再次收集）
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
