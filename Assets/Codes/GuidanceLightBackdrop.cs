using UnityEngine;

/// <summary>
/// 光球背後的暗色柔光暈：在光球後面墊一層半透明深色柔邊圓，把周圍的粉彩背景壓暗一圈，讓光球跳出來。
/// 掛在光球 (GuidanceLight) 物件上。光暈是獨立的隱藏物件 (不放在光球底下，
/// 否則 GuidanceLight 淡入淡出時會連它的顏色一起覆寫)，每幀跟著光球移動，
/// 並跟著光球主圖的透明度一起淡入淡出。編輯模式也會顯示，方便在 Inspector 調整。
/// </summary>
[ExecuteAlways]
public class GuidanceLightBackdrop : MonoBehaviour
{
    [Header("外觀")]
    [Tooltip("光暈顏色 (建議偏灰的藍紫，太深會像在發黑光)")]
    public Color color = new Color(0.42f, 0.45f, 0.68f, 1f);
    [Tooltip("光暈透明度")]
    [Range(0f, 1f)] public float opacity = 0.2f;
    [Tooltip("光暈直徑 = 光球主圖大小 × 這個倍率")]
    public float sizeMultiplier = 1.6f;
    [Tooltip("邊緣柔和度：0 = 硬邊圓，1 = 從中心就開始漸淡")]
    [Range(0f, 1f)] public float softness = 1f;

    [Header("脈動 (可選)")]
    public bool pulse = true;
    [Tooltip("脈動週期 (秒)")]
    public float pulsePeriod = 2.4f;
    [Tooltip("大小脈動幅度 (0.08 = ±8%)")]
    [Range(0f, 0.5f)] public float pulseScaleAmount = 0.08f;
    [Tooltip("透明度脈動幅度 (0.08 = ±8%)")]
    [Range(0f, 0.5f)] public float pulseOpacityAmount = 0.08f;

    [Header("排序與參考")]
    [Tooltip("光球主圖 (留空時自動選光球底下面積最大的 SpriteRenderer)。光暈的大小、排序與淡入淡出都以它為準")]
    public SpriteRenderer referenceSprite;
    [Tooltip("光暈的 sortingOrder 比光球主圖低多少 (1 = 緊貼在它後面)")]
    public int sortingOrderOffset = 1;
    [Tooltip("光暈比光球往後 (遠離鏡頭) 推多少，避免同一深度互相搶排序")]
    public float zOffset = 0.05f;
    [Tooltip("光暈材質 (留空用 URP Sprite-Unlit-Default；不能用加亮 (Additive) 材質，否則壓不暗)")]
    public Material material;

    private const string BackdropName = "__GuidanceLightBackdrop";
    private GameObject _backdrop;
    private SpriteRenderer _sr;
    private Texture2D _tex;
    private Sprite _sprite;
    private Material _autoMat;
    private float _builtSoftness = -1f;
    private float _refBaseAlpha = 1f;
    private Vector3 _refBaseLocalSize = Vector3.one;

    private void OnEnable()
    {
        ResolveReference();
        Build();
    }

    private void OnDisable()
    {
        Cleanup();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void ResolveReference()
    {
        if (referenceSprite == null)
        {
            float best = -1f;
            foreach (var s in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (s.sprite == null) continue;
                Vector3 b = s.sprite.bounds.size;
                Vector3 ls = s.transform.lossyScale;
                float area = Mathf.Abs(b.x * ls.x * b.y * ls.y);
                if (area > best) { best = area; referenceSprite = s; }
            }
        }
        if (referenceSprite != null)
        {
            _refBaseAlpha = Mathf.Max(0.001f, referenceSprite.color.a);
        }
    }

    private void Build()
    {
        Cleanup();

        _backdrop = new GameObject(BackdropName);
        _backdrop.hideFlags = HideFlags.HideAndDontSave;
        _sr = _backdrop.AddComponent<SpriteRenderer>();
        _sr.sharedMaterial = ResolveMaterial();
        RebuildSprite();
        LateUpdate();
    }

    private Material ResolveMaterial()
    {
        if (material != null) return material;
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) return null;
        _autoMat = new Material(sh) { name = "GuidanceLightBackdrop (auto)", hideFlags = HideFlags.HideAndDontSave };
        return _autoMat;
    }

    // 徑向漸層圓：中心不透明，往外依 softness 漸淡到 0
    private void RebuildSprite()
    {
        const int size = 128;
        if (_tex == null)
        {
            _tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GuidanceLightBackdropTex",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        float inner = 1f - Mathf.Clamp01(softness);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = r >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, 1f, r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        _tex.SetPixels32(px);
        _tex.Apply();

        if (_sprite != null) DestroySafe(_sprite);
        // pixelsPerUnit = size → 精靈直徑剛好 1 單位，之後用縮放決定實際大小
        _sprite = Sprite.Create(_tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        _sprite.name = "GuidanceLightBackdrop";
        _sprite.hideFlags = HideFlags.HideAndDontSave;
        _sr.sprite = _sprite;
        _builtSoftness = softness;
    }

    private void LateUpdate()
    {
        if (_backdrop == null || _sr == null) return;
        if (!Mathf.Approximately(_builtSoftness, softness)) RebuildSprite();
        if (material != null && _sr.sharedMaterial != material) _sr.sharedMaterial = material;

        // 位置：跟著光球 (主圖中心)，稍微往後推
        Vector3 center = referenceSprite != null ? referenceSprite.bounds.center : transform.position;
        _backdrop.transform.position = center + new Vector3(0f, 0f, zOffset);

        // 大小：主圖顯示大小 (取寬高較大者) × 倍率 × 脈動
        float baseSize = 1f;
        if (referenceSprite != null && referenceSprite.sprite != null)
        {
            Vector3 b = referenceSprite.sprite.bounds.size;
            Vector3 ls = referenceSprite.transform.lossyScale;
            baseSize = Mathf.Max(Mathf.Abs(b.x * ls.x), Mathf.Abs(b.y * ls.y));
        }
        float t = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
        float wave = pulse && pulsePeriod > 0.01f ? Mathf.Sin(t * Mathf.PI * 2f / pulsePeriod) : 0f;
        float s = baseSize * sizeMultiplier * (1f + wave * pulseScaleAmount);
        _backdrop.transform.localScale = new Vector3(s, s, 1f);

        // 透明度：設定值 × 脈動 × 光球目前的淡入淡出比例
        float fade = 1f;
        bool visible = true;
        if (referenceSprite != null)
        {
            fade = Mathf.Clamp01(referenceSprite.color.a / _refBaseAlpha);
            visible = referenceSprite.enabled && referenceSprite.gameObject.activeInHierarchy;
            _sr.sortingLayerID = referenceSprite.sortingLayerID;
            _sr.sortingOrder = referenceSprite.sortingOrder - sortingOrderOffset;
        }
        float a = opacity * (1f + wave * pulseOpacityAmount) * fade;
        _sr.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(a));
        _sr.enabled = visible;
    }

    private void Cleanup()
    {
        if (_backdrop != null) DestroySafe(_backdrop);
        if (_sprite != null) DestroySafe(_sprite);
        if (_tex != null) DestroySafe(_tex);
        if (_autoMat != null) DestroySafe(_autoMat);
        _backdrop = null;
        _autoMat = null;
        _sr = null;
        _sprite = null;
        _tex = null;
    }

    private static void DestroySafe(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
