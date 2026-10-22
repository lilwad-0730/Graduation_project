using UnityEngine;

/// <summary>
/// 燭火點燃進度條：玩家按住 S 點燃時，燭台上方出現一條進度條，由左往右填滿。
/// 點完、放開或走開就隱藏。由 CandleAuraGlow 自動加上，不用手動掛。
/// </summary>
[RequireComponent(typeof(CandleAuraGlow))]
public class CandleLightProgressBar : MonoBehaviour
{
    [Header("位置與大小")]
    [Tooltip("進度條相對燭台中心的位置")]
    public Vector3 barOffset = new Vector3(0f, 3.2f, -0.1f);
    public float barWidth = 2.0f;
    public float barHeight = 0.18f;
    [Tooltip("外框粗細")]
    public float borderSize = 0.04f;

    [Header("顏色")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
    // 顏色取自燭台玻璃柱本體（done-spritesheet-table-320-480.png，排除頂端暖色蠟燭）
    public Color borderColor = new Color32(211, 230, 234, 204);     // 玻璃亮部
    public Color fillStartColor = new Color32(162, 188, 197, 255);  // 玻璃平均色（主體色）
    public Color fillEndColor = new Color32(211, 230, 234, 255);    // 玻璃亮部：越接近點燃越亮

    [Tooltip("圖層順序（要蓋在燭台和光暈上面）")]
    public int sortingOrder = 50;

    private CandleAuraGlow _aura;
    private Transform _root;
    private Transform _fill;
    private SpriteRenderer _fillSr;
    private SpriteRenderer[] _all;
    private static Sprite _whiteSprite;

    private void Awake()
    {
        _aura = GetComponent<CandleAuraGlow>();
        Build();
        SetVisible(false);
    }

    private void Build()
    {
        if (_whiteSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        _root = new GameObject("LightProgressBar").transform;
        _root.SetParent(transform, false);
        _root.localPosition = barOffset;

        SpriteRenderer border = MakeQuad("Border", borderColor, sortingOrder,
            new Vector3(barWidth + borderSize * 2f, barHeight + borderSize * 2f, 1f), Vector3.zero);
        SpriteRenderer bg = MakeQuad("Background", backgroundColor, sortingOrder + 1,
            new Vector3(barWidth, barHeight, 1f), new Vector3(0f, 0f, -0.001f));
        _fillSr = MakeQuad("Fill", fillStartColor, sortingOrder + 2,
            new Vector3(0f, barHeight, 1f), new Vector3(-barWidth * 0.5f, 0f, -0.002f));
        _fill = _fillSr.transform;

        _all = new[] { border, bg, _fillSr };
    }

    private SpriteRenderer MakeQuad(string name, Color color, int order, Vector3 scale, Vector3 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(_root, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _whiteSprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private void LateUpdate()
    {
        bool show = _aura != null && _aura.IsLighting;
        SetVisible(show);
        if (!show) return;

        // 燭台可能被翻轉或縮放，進度條維持正向、固定大小
        Vector3 s = transform.lossyScale;
        _root.localScale = new Vector3(
            Mathf.Abs(s.x) > 0.0001f ? 1f / s.x : 1f,
            Mathf.Abs(s.y) > 0.0001f ? 1f / s.y : 1f,
            1f);
        _root.rotation = Quaternion.identity;

        float p = _aura.LightProgress;
        float w = barWidth * p;
        _fill.localScale = new Vector3(w, barHeight, 1f);
        _fill.localPosition = new Vector3(-barWidth * 0.5f + w * 0.5f, 0f, -0.002f);
        _fillSr.color = Color.Lerp(fillStartColor, fillEndColor, p);
    }

    private void SetVisible(bool visible)
    {
        if (_root != null && _root.gameObject.activeSelf != visible)
            _root.gameObject.SetActive(visible);
    }
}
