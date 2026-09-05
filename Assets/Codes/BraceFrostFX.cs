using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★0905 硬撐的「表現」（不是代價）：按住 ⬇/S 石化時，畫面邊緣慢慢結一層淡淡的霜；放開就退掉。
/// 被動石化（風暴盒裡僵住）也結霜，而且更重一點——她不是選擇不動，是動不了。
/// Celeste 第四章的呼吸：撐＝閉氣、放開＝吐氣。純表現，不影響任何判定；沒有閉氣上限（被動石化三次死亡已經是代價）。
/// 顏色是骨白，不用冷藍——冷藍在本作是「他」的顏色（GDD §11）。
/// 由 DesertBeatDirector 自動生成；Canvas 排序 500，壓在提示條（9000）與文字卡（10000）底下。
/// </summary>
[DisallowMultipleComponent]
public class BraceFrostFX : MonoBehaviour
{
    [Range(0f, 1f)] public float maxAlpha = 0.5f;
    [Tooltip("按住幾秒結到最深")]
    public float rampSeconds = 2.2f;
    [Tooltip("放開幾秒退乾淨")]
    public float releaseSeconds = 0.45f;
    public Color frostColor = new Color(0.93f, 0.90f, 0.86f, 1f);
    [Tooltip("被動石化（僵住）時的加重倍率")]
    public float passivePetrifyBoost = 1.25f;
    public int canvasSortOrder = 500;

    private PlayerPetrification _pet;
    private RawImage _img;
    private Texture2D _tex;
    private float _level = 0f;
    private float _nextFind = 0f;

    public static BraceFrostFX Install()
    {
        BraceFrostFX existing = FindFirstObjectByType<BraceFrostFX>();
        if (existing != null) return existing;
        GameObject go = new GameObject("BraceFrostFX (自動生成)");
        return go.AddComponent<BraceFrostFX>();
    }

    private void Awake()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortOrder;
        gameObject.AddComponent<CanvasScaler>();

        GameObject imgGo = new GameObject("Frost");
        imgGo.transform.SetParent(transform, false);
        _img = imgGo.AddComponent<RawImage>();
        _img.raycastTarget = false;
        _tex = BuildVignette(256);
        _img.texture = _tex;
        _img.color = new Color(frostColor.r, frostColor.g, frostColor.b, 0f);

        RectTransform rt = _img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void Update()
    {
        if (_pet == null && Time.time >= _nextFind)
        {
            _pet = FindFirstObjectByType<PlayerPetrification>();
            _nextFind = Time.time + 1f;
        }

        bool active = false, passive = false;
        if (_pet != null)
        {
            active = _pet.IsBracing;
            passive = _pet.isPetrified && !_pet.IsBracing;
        }
        bool on = active || passive;

        float target = on ? 1f : 0f;
        float speed = on ? 1f / Mathf.Max(0.05f, rampSeconds) : 1f / Mathf.Max(0.05f, releaseSeconds);
        _level = Mathf.MoveTowards(_level, target, Time.deltaTime * speed);

        if (_img == null) return;
        float a = maxAlpha * _level * _level * (passive ? passivePetrifyBoost : 1f);
        _img.color = new Color(frostColor.r, frostColor.g, frostColor.b, Mathf.Clamp01(a));
    }

    /// <summary>邊緣結霜：中央透明，往四角越來越白，加一點不規則的霜紋。</summary>
    private static Texture2D BuildVignette(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Procedural_FrostVignette";
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v) / 1.4142f;          // 0＝中心，1＝角落
                float edge = Mathf.Clamp01((r - 0.38f) / 0.62f);
                float a = Mathf.Pow(edge, 1.7f);
                float frost = Mathf.PerlinNoise(u * 6.3f + 3.1f, v * 6.3f + 7.7f);   // 霜紋
                a *= 0.75f + 0.5f * frost;
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a)));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
    }
}
