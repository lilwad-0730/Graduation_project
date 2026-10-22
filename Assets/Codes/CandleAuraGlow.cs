using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於燭火物件 (例如 'candle_01')。
/// 玩家走到燭火旁按住 S 鍵點燃，點完光暈才淡入亮起，之後轉為呼吸脈動。
/// 點燃時間依燭火在 Candles 底下的順序：第 1 根 1.5 秒，之後每根多 0.1 秒。
/// </summary>
public class CandleAuraGlow : MonoBehaviour, IResettable
{
    [Header("🕯️ 點燃操作")]
    [Tooltip("點燃用的按鍵")]
    public KeyCode lightKey = KeyCode.S;

    [Tooltip("勾選＝要一直按住直到點完，放開就重來；不勾＝按一下就開始，玩家離開範圍才會中斷")]
    public bool holdToLight = true;

    [Tooltip("第一根燭火的點燃時間 (秒)")]
    public float baseLightDuration = 1.5f;

    [Tooltip("依 Candles 底下的順序，每往後一根多花的秒數")]
    public float extraPerCandleOrder = 0.1f;

    [Tooltip("玩家離燭火多近才能點燃 (左右距離)")]
    public float lightRangeX = 2.0f;

    [Tooltip("玩家離燭火多近才能點燃 (上下距離)")]
    public float lightRangeY = 5.0f;

    /// <summary>開始點燃時呼叫（之後接點燃動作模型用）。</summary>
    public event System.Action<CandleAuraGlow> LightingStarted;
    /// <summary>點到一半中斷時呼叫。</summary>
    public event System.Action<CandleAuraGlow> LightingCanceled;
    /// <summary>點燃完成時呼叫。</summary>
    public event System.Action<CandleAuraGlow> LightingCompleted;

    /// <summary>正在點燃中嗎。</summary>
    public bool IsLighting { get { return _isLighting; } }
    /// <summary>點燃進度 0~1。</summary>
    public float LightProgress { get { return _lightDuration > 0f ? Mathf.Clamp01(_lightTimer / _lightDuration) : 0f; } }
    /// <summary>這根燭火需要的點燃秒數。</summary>
    public float LightDuration { get { return GetLightDuration(); } }

    private bool _isLighting = false;
    private float _lightTimer = 0f;
    private float _lightDuration = 0f;
    private PlayerMovement _player;
    private CandleCollectible _collectible;

    [Header("淡入出現動畫設定")]
    [Tooltip("光暈從無到有的出現過程時間 (秒)")]
    public float fadeInDuration = 3.0f;

    [Header("光暈位置與層級設定")]
    [Tooltip("光暈圓心位置偏移 (X, Y 軸向量位移)")]
    public Vector2 auraCenterOffset = new Vector2(0f, 0.5f);

    [Tooltip("Z 軸深淺偏移")]
    public float zOffset = -0.05f;

    [Tooltip("圖層順序相對偏移")]
    public int sortingOrderOffset = 1;

    [Header("光暈視覺顏色與目標尺寸")]
    [Tooltip("光暈目標顏色與亮度")]
    public Color auraColor = new Color(0.3f, 0.75f, 1.0f, 0.75f);

    [Tooltip("光暈目標尺寸 (直徑)")]
    public Vector3 targetAuraScale = new Vector3(3.5f, 3.5f, 1.0f);

    [Header("動態呼吸與脈動")]
    [Tooltip("淡入完成後是否開啟柔和呼吸脈動")]
    public bool enablePulse = true;

    [Tooltip("脈動頻率 (速度)")]
    public float pulseSpeed = 2.2f;

    [Tooltip("脈動振幅 (幅度)")]
    public float pulseAmount = 0.08f;

    private SpriteRenderer auraSr;
    private Transform auraTransform;
    private static Sprite cachedAuraSprite;
    private bool isFadingIn = false;
    private float fadeTimer = 0f;
    private bool hasBeenTriggered = false;

    /// <summary>★1021 玩家碰過這根燭火、光暈亮起來了＝已點燃。影子怪物靠它判斷這根燭火算不算數。</summary>
    public bool IsLit { get { return hasBeenTriggered; } }

    private void Awake()
    {
        SetupAuraObject();
        if (Application.isPlaying && GetComponent<CandleLightProgressBar>() == null)
            gameObject.AddComponent<CandleLightProgressBar>();
    }

    private void OnEnable()
    {
        _isLighting = false;
        _lightTimer = 0f;
        StopAllCoroutines();
        isFadingIn = false;
        fadeTimer = 0f;
        hasBeenTriggered = false;
        SetupAuraObject();
    }

    public void SetupAuraObject()
    {
        Transform child = transform.Find("CandleAuraGlow");
        GameObject auraGo;

        if (child == null)
        {
            auraGo = new GameObject("CandleAuraGlow");
            auraGo.transform.SetParent(transform);
            auraGo.transform.localPosition = new Vector3(auraCenterOffset.x, auraCenterOffset.y, zOffset);
            auraGo.transform.localScale = Vector3.zero;
        }
        else
        {
            auraGo = child.gameObject;
        }

        auraTransform = auraGo.transform;
        auraSr = auraGo.GetComponent<SpriteRenderer>();
        if (auraSr == null) auraSr = auraGo.AddComponent<SpriteRenderer>();

        if (cachedAuraSprite == null)
        {
            cachedAuraSprite = GenerateSoftAuraSprite();
        }
        auraSr.sprite = cachedAuraSprite;
        SetAuraHidden();
    }

    // ──────────────── 點燃操作 ────────────────

    /// <summary>依 Candles 底下的順序算點燃時間：第 1 根 base，之後每根 +extra。</summary>
    private float GetLightDuration()
    {
        int order = 0;
        Transform parent = transform.parent;
        if (parent != null)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == transform) break;
                if (sibling.GetComponent<CandleAuraGlow>() != null) order++;
            }
        }
        return Mathf.Max(0f, baseLightDuration + extraPerCandleOrder * order);
    }

    private PlayerMovement FindPlayer()
    {
        if (_player != null) return _player;
        GameObject tagged = GameObject.FindWithTag("Player");
        if (tagged != null) _player = tagged.GetComponent<PlayerMovement>();
        if (_player == null) _player = FindAnyObjectByType<PlayerMovement>();
        return _player;
    }

    private bool IsPlayerInRange()
    {
        PlayerMovement player = FindPlayer();
        if (player == null || !player.isActiveAndEnabled) return false;
        Vector3 d = player.transform.position - transform.position;
        return Mathf.Abs(d.x) <= lightRangeX && Mathf.Abs(d.y) <= lightRangeY;
    }

    /// <summary>被怪物吃掉或漏掉的燭火不能再點。</summary>
    private bool CanBeLit()
    {
        if (hasBeenTriggered) return false;
        if (_collectible == null) _collectible = GetComponent<CandleCollectible>();
        if (_collectible != null && (_collectible.isCollected || _collectible.missed)) return false;
        return true;
    }

    private void UpdateLighting()
    {
        if (!Application.isPlaying) return;

        bool canLight = CanBeLit()
                        && IsPlayerInRange()
                        && !_player.IsControlLocked
                        && !PlayerMovement.IsHardCutsceneLocked;

        if (!_isLighting)
        {
            if (canLight && Input.GetKeyDown(lightKey)) BeginLighting();
            return;
        }

        bool stillHolding = !holdToLight || Input.GetKey(lightKey);
        if (!canLight || !stillHolding || IsPressingMoveKey())
        {
            CancelLighting();
            return;
        }

        _lightTimer += Time.deltaTime;
        if (_lightTimer >= _lightDuration)
        {
            _isLighting = false;
            _lightTimer = _lightDuration;
            if (_player != null) _player.EndCandleLightAnim(true);
            PlayFadeInAnimation();
            if (LightingCompleted != null) LightingCompleted(this);
        }
    }

    private void BeginLighting()
    {
        _isLighting = true;
        _lightTimer = 0f;
        _lightDuration = GetLightDuration();
        Debug.Log($"🕯️【點燃】開始點燃 {gameObject.name}，需要 {_lightDuration:F1} 秒");
        // 燭火位置＝光暈圓心（燭台上方的火焰處）
        if (_player != null) _player.BeginCandleLightAnim(_lightDuration, transform.position + new Vector3(auraCenterOffset.x, auraCenterOffset.y, 0f));
        if (LightingStarted != null) LightingStarted(this);
    }

    /// <summary>中斷點燃，進度歸零。</summary>
    public void CancelLighting()
    {
        if (!_isLighting) return;
        _isLighting = false;
        _lightTimer = 0f;
        if (_player != null) _player.EndCandleLightAnim(false);
        if (LightingCanceled != null) LightingCanceled(this);
    }

    /// <summary>點燃中按移動或跳躍鍵＝放棄點燃。</summary>
    private static bool IsPressingMoveKey()
    {
        return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.W)
            || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftArrow)
            || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.UpArrow);
    }

    public void PlayFadeInAnimation()
    {
        StopAllCoroutines();
        if (auraTransform == null || auraSr == null)
        {
            SetupAuraObject();
        }

        hasBeenTriggered = true;
        isFadingIn = true;
        StartCoroutine(BeginFadeNextFrame());
    }

    private IEnumerator BeginFadeNextFrame()
    {
        yield return null;

        if (!hasBeenTriggered)
        {
            isFadingIn = false;
            yield break;
        }

        yield return FadeInRoutine();
    }

    private IEnumerator FadeInRoutine()
    {
        isFadingIn = true;
        fadeTimer = 0f;

        if (auraTransform == null || auraSr == null)
        {
            SetupAuraObject();
        }

        if (fadeInDuration <= 0f)
        {
            auraTransform.localScale = targetAuraScale;
            auraTransform.localPosition = new Vector3(auraCenterOffset.x, auraCenterOffset.y, zOffset);

            Color immediateColor = auraColor;
            immediateColor.a = auraColor.a;
            auraSr.color = immediateColor;
            auraSr.enabled = true;
            UpdateSortingOrder();

            isFadingIn = false;
            yield break;
        }

        while (fadeTimer < fadeInDuration)
        {
            fadeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(fadeTimer / fadeInDuration);
            float smoothProgress = Mathf.SmoothStep(0f, 1f, t);

            auraTransform.localScale = targetAuraScale * smoothProgress;
            auraTransform.localPosition = new Vector3(auraCenterOffset.x, auraCenterOffset.y, zOffset);

            Color c = auraColor;
            c.a = auraColor.a * smoothProgress;
            auraSr.color = c;
            // CandleCollectible may disable all cached SpriteRenderers in the same
            // trigger callback; only restore this aura renderer, never the candle root.
            auraSr.enabled = true;

            UpdateSortingOrder();

            yield return null;
        }

        auraSr.enabled = true;
        isFadingIn = false;
    }

    private void Update()
    {
        if (auraTransform == null || auraSr == null)
        {
            SetupAuraObject();
        }

        UpdateLighting();

        if (!hasBeenTriggered)
        {
            SetAuraHidden();
            return;
        }

        if (isFadingIn)
        {
            // Keep the glow renderer visible after CandleCollectible's hide pass;
            // its scale/alpha still control whether any pixels are shown.
            auraSr.enabled = true;
            return;
        }

        float wave = enablePulse && Application.isPlaying ? Mathf.Sin(Time.time * pulseSpeed) * pulseAmount : 0f;
        auraTransform.localScale = targetAuraScale * (1f + wave);
        auraTransform.localPosition = new Vector3(auraCenterOffset.x, auraCenterOffset.y, zOffset);

        UpdateSortingOrder();

        float alphaWave = enablePulse && Application.isPlaying ? Mathf.Sin(Time.time * pulseSpeed) * pulseAmount * 0.5f : 0f;
        Color c = auraColor;
        c.a = auraColor.a * (1f + alphaWave);
        auraSr.color = c;
        auraSr.enabled = true;
    }

    private void SetAuraHidden()
    {
        if (auraTransform != null)
        {
            auraTransform.localScale = Vector3.zero;
        }

        if (auraSr != null)
        {
            Color hiddenColor = auraColor;
            hiddenColor.a = 0f;
            auraSr.color = hiddenColor;
            auraSr.enabled = false;
        }
    }

    private void UpdateSortingOrder()
    {
        if (auraSr == null) return;
        int maxParentOrder = -500;
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r != auraSr && r.sortingOrder > maxParentOrder)
            {
                maxParentOrder = r.sortingOrder;
            }
        }
        auraSr.sortingOrder = maxParentOrder + sortingOrderOffset;
    }

    private Sprite GenerateSoftAuraSprite()
    {
        int size = 512;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxRadius = size * 0.49f;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float normalizedDist = dist / maxRadius;

                if (normalizedDist >= 1.0f)
                {
                    pixels[y * size + x] = Color.clear;
                }
                else
                {
                    float gaussian = Mathf.Exp(-6.0f * normalizedDist * normalizedDist);
                    float edgeFeather = Mathf.SmoothStep(1.0f, 0.0f, normalizedDist);
                    float alpha = gaussian * edgeFeather * edgeFeather;

                    Color coreColor = new Color(0.9f, 0.96f, 1.0f, 1.0f);
                    Color outerColor = new Color(0.25f, 0.75f, 1.0f, 1.0f);
                    Color pixelColor = Color.Lerp(coreColor, outerColor, Mathf.Pow(normalizedDist, 0.7f));
                    pixelColor.a = alpha;

                    pixels[y * size + x] = pixelColor;
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    public void HideAura()
    {
        CancelLighting();
        StopAllCoroutines();
        isFadingIn = false;
        fadeTimer = 0f;
        hasBeenTriggered = false;

        if (auraTransform == null || auraSr == null)
            SetupAuraObject();

        SetAuraHidden();
    }

    public void ResetToInitialState()
    {
        CancelLighting();
        StopAllCoroutines();
        isFadingIn = false;
        fadeTimer = 0f;
        hasBeenTriggered = false;

        if (auraTransform == null || auraSr == null)
        {
            SetupAuraObject();
        }

        SetAuraHidden();
    }
}
