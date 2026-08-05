using UnityEngine;

/// <summary>
/// 為玩家 (Player) 角色背後添加一圈優雅淡淡藍色光暈 (Soft Blue Aura Glow)。
/// 採用 512x512 超高精細高斯雙重羽化漸層 (Gaussian Smoothstep Feathering)，
/// 徹底消除邊界圓環切痕，呈現純淨極致的無限漸變大氣氛圍光暈。
/// </summary>
[ExecuteAlways]
public class PlayerAuraGlow : MonoBehaviour
{
    [Header("光暈圖層與層級設定")]
    [Tooltip("圖層順序相對偏移 (預設 +1，保證渲染在背景前方、主角模型後方)")]
    public int sortingOrderOffset = 1;

    [Header("光暈視覺顏色")]
    [Tooltip("光暈顏色與亮度 (天空藍/電光藍)")]
    public Color auraColor = new Color(0.3f, 0.75f, 1.0f, 0.75f);

    [Header("動態呼吸與脈動")]
    [Tooltip("是否開啟柔和呼吸脈動")]
    public bool enablePulse = true;

    [Tooltip("脈動頻率 (速度)")]
    public float pulseSpeed = 2.2f;

    [Tooltip("脈動振幅 (幅度)")]
    public float pulseAmount = 0.08f;

    private SpriteRenderer auraSr;
    private Transform auraTransform;
    private Vector3 baseScale = Vector3.one;
    private static Sprite cachedAuraSprite;

    private void Awake()
    {
        SetupAuraObject();
    }

    private void OnEnable()
    {
        SetupAuraObject();
    }

    public void SetupAuraObject()
    {
        Transform child = transform.Find("PlayerAuraGlow");
        GameObject auraGo;

        if (child == null)
        {
            auraGo = new GameObject("PlayerAuraGlow");
            auraGo.transform.SetParent(transform);
            auraGo.transform.localPosition = new Vector3(0f, 1.1f, -0.05f);
            auraGo.transform.localScale = new Vector3(4.2f, 4.2f, 1.0f);
        }
        else
        {
            auraGo = child.gameObject;
        }

        auraTransform = auraGo.transform;

        if (auraTransform.localScale != Vector3.zero)
        {
            baseScale = auraTransform.localScale;
        }

        auraSr = auraGo.GetComponent<SpriteRenderer>();
        if (auraSr == null) auraSr = auraGo.AddComponent<SpriteRenderer>();

        // 強制重新生成並刷新羽化漸層 Sprite
        cachedAuraSprite = GenerateSoftAuraSprite();
        auraSr.sprite = cachedAuraSprite;

        UpdateAuraProperties();
    }

    private void Update()
    {
        if (auraTransform == null || auraSr == null)
        {
            SetupAuraObject();
        }

        UpdateAuraProperties();
    }

    private void UpdateAuraProperties()
    {
        if (auraTransform == null || auraSr == null) return;

        if (!Application.isPlaying)
        {
            if (auraTransform.localScale != Vector3.zero)
            {
                baseScale = auraTransform.localScale;
            }
        }

        if (enablePulse && Application.isPlaying)
        {
            float wave = Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            auraTransform.localScale = baseScale * (1f + wave);
        }
        else
        {
            auraTransform.localScale = baseScale;
        }

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

        float alphaWave = enablePulse && Application.isPlaying ? Mathf.Sin(Time.time * pulseSpeed) * pulseAmount * 0.5f : 0f;
        Color c = auraColor;
        c.a = auraColor.a * (1f + alphaWave);
        auraSr.color = c;
    }

    /// <summary>
    /// 動態生成 512x512 超高質感高斯雙重羽化漸層貼圖 (無邊界硬痕)
    /// </summary>
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
                    Color outerColor = new Color(0.2f, 0.65f, 1.0f, 1.0f);
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
}
