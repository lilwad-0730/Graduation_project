using UnityEngine;

/// <summary>
/// ★1013 水下關卡的提示光：
///  B. 日誌四周一圈暖黃奶油色微光（冰冷水底裡唯一的暖色＝安心的東西）
///  C. 快窒息時，光球跟著心跳一樣慢慢脈動，把視線引過去
/// 全部用程式生成，不需要額外圖片。
/// </summary>
public static class UnderwaterHintGlow
{
    private static Sprite _soft;

    /// <summary>程式畫一張柔邊圓形光暈（中心亮、往外淡出）。</summary>
    public static Sprite SoftCircle()
    {
        if (_soft != null) return _soft;
        const int n = 64;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f;
                float dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                float a = Mathf.Pow(1f - d, 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        _soft = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return _soft;
    }

    /// <summary>只在水下關卡（場景裡有窒息黑圈）幫日誌加暖光。</summary>
    public static void AddNoteGlow(GameObject note)
    {
        SpriteRenderer sr = note.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        if (note.GetComponentInChildren<NoteWarmGlow>(true) != null) return;
        if (Object.FindFirstObjectByType<UnderwaterSuffocationEffect>() == null) return;

        GameObject g = new GameObject("WarmGlow");
        g.transform.SetParent(note.transform, false);
        g.AddComponent<NoteWarmGlow>().Init(sr);
    }
}

/// <summary>日誌的暖黃光暈，會慢慢明暗呼吸。</summary>
public class NoteWarmGlow : MonoBehaviour
{
    private SpriteRenderer _glow;
    private SpriteRenderer _paper;
    private float _phase;
    private static readonly Color Cream = new Color(1f, 0.82f, 0.45f, 1f);

    public void Init(SpriteRenderer paper)
    {
        _paper = paper;
        _phase = Random.value * 6.28f;
        _glow = gameObject.AddComponent<SpriteRenderer>();
        _glow.sprite = UnderwaterHintGlow.SoftCircle();
        _glow.color = new Color(Cream.r, Cream.g, Cream.b, 0.4f);
        _glow.sortingLayerID = paper.sortingLayerID;
        _glow.sortingOrder = paper.sortingOrder - 1;   // 墊在紙的後面

        // 光暈大小＝紙實際大小的 2.6 倍
        Bounds b = paper.bounds;
        float size = Mathf.Max(b.size.x, b.size.y) * 2.6f;
        Vector3 ls = paper.transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(ls.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(ls.y));
        transform.position = b.center;
        transform.localScale = new Vector3(size / sx, size / sy, 1f);
    }

    private void Update()
    {
        if (_glow == null) return;
        float t = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.6f + _phase);
        float a = Mathf.Lerp(0.28f, 0.5f, t);
        if (_paper != null) a *= _paper.color.a;   // 紙淡出時光暈一起淡出
        Color c = _glow.color;
        c.a = a;
        _glow.color = c;
    }
}

/// <summary>快窒息時，光球像心跳一樣脈動（只改子物件的縮放，不碰透明度）。</summary>
public class OrbDangerPulse : MonoBehaviour
{
    [Tooltip("危險程度超過這個值才開始脈動")]
    public float startDanger = 0.5f;
    [Tooltip("最危險時放大多少（0.25＝±25%）")]
    public float maxScaleAmount = 0.25f;

    private Transform[] _visuals;
    private Vector3[] _baseScales;
    private float _lastApplied = 1f;

    private void Start()
    {
        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>(true);
        _visuals = new Transform[srs.Length];
        _baseScales = new Vector3[srs.Length];
        for (int i = 0; i < srs.Length; i++)
        {
            _visuals[i] = srs[i].transform;
            _baseScales[i] = srs[i].transform.localScale;
        }
    }

    private void LateUpdate()
    {
        UnderwaterSuffocationEffect fx = UnderwaterSuffocationEffect.Instance;
        float k = 1f;
        if (fx != null && fx.Danger01 > startDanger)
        {
            float d = Mathf.InverseLerp(startDanger, 1f, fx.Danger01);
            float beat = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(3f, 7f, d)), 2f);
            k = 1f + beat * maxScaleAmount * d;
        }
        if (Mathf.Approximately(k, 1f) && Mathf.Approximately(_lastApplied, 1f)) return;
        _lastApplied = k;
        for (int i = 0; i < _visuals.Length; i++)
        {
            if (_visuals[i] == null) continue;
            if (_visuals[i] == transform) continue;   // 根物件可能被別的流程縮放，不碰
            _visuals[i].localScale = _baseScales[i] * k;
        }
    }
}
