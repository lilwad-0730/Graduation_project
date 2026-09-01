using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於燭火物件 (例如 'candle_01')。
/// 提供約 3 秒鐘由無到有 (Alpha 0->1, Scale 0->Target) 柔和淡入出現的光暈動畫，
/// 動畫結束後轉為優雅的呼吸脈動。
/// </summary>
public class CandleAuraGlow : MonoBehaviour, IResettable
{
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

    private void Awake()
    {
        SetupAuraObject();
    }

    private void OnEnable()
    {
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

    private void OnTriggerEnter(Collider other)
    {
        if (hasBeenTriggered || !IsPlayer(other.gameObject))
        {
            return;
        }

        hasBeenTriggered = true;
        PlayFadeInAnimation();
    }

    private bool IsPlayer(GameObject go)
    {
        if (go.CompareTag("Player")) return true;
        if (go.GetComponent<PlayerMovement>() != null) return true;
        if (go.name.ToLower().Contains("player")) return true;
        return false;
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
