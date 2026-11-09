using UnityEngine;

/// <summary>
/// ★1109c 鳥發現玩家後，頭部兩側亮起發光紅眼（核心亮點＋柔光暈），沒偵測到／撞地／被逼退時淡出。
/// 由 IndividualBirdEnemy.Start 自動加上；眼睛掛在 "Bip01 Head" 骨頭下，跟著頭動。
/// </summary>
[DisallowMultipleComponent]
public class BirdHostileEyes : MonoBehaviour
{
    [Header("外觀")]
    public Color coreColor = new Color(1f, 0.18f, 0.1f, 1f);
    public Color glowColor = new Color(1f, 0.1f, 0.05f, 0.6f);
    // 遊戲鏡頭（正交 17）約 32 像素／米：0.32 米≈10 像素、1.1 米≈35 像素，太小會認不出是哪隻鳥
    [Tooltip("眼睛亮點直徑（世界公尺）")]
    public float coreSize = 0.32f;
    [Tooltip("光暈直徑（世界公尺）")]
    public float glowSize = 1.1f;
    public float fadeIn = 0.25f;
    public float fadeOut = 0.4f;
    [Tooltip("光暈脈動幅度（±比例）與頻率（Hz）")]
    public float pulseAmount = 0.2f;
    public float pulseHz = 2f;

    [Header("位置（頭骨區域座標，+z 那顆眼；另一顆 z 鏡射）")]
    // 實測：頭骨 x 軸朝下、y 軸朝嘴、z 軸朝兩側；頭殼在這個位置的表面 |z|≈0.0045，所以 z 取 0.0045 再外推 0.003
    public Vector3 eyeLocalOffset = new Vector3(-0.0095f, 0.0075f, 0.0045f);
    [Tooltip("沿 ±z 往外推一點（頭骨區域座標），避免埋在頭裡")]
    public float outwardPush = 0.003f;

    private const string EyeName = "HostileEye_";
    private static Material s_mat;
    private static Sprite s_coreSprite, s_glowSprite;

    private IndividualBirdEnemy _bird;
    private Transform _head;
    private SpriteRenderer[] _core = new SpriteRenderer[2];
    private SpriteRenderer[] _glow = new SpriteRenderer[2];
    private Camera _cam;
    private float _a;          // 0..1 目前亮度
    private bool _shown;

    private void Awake()
    {
        _bird = GetComponent<IndividualBirdEnemy>();
        _head = FindDeep(transform, "Bip01 Head");
        if (_head == null) { enabled = false; return; }

        // 增援鳥是 Instantiate 複本：眼睛子物件會被一起複製，先清掉再重建，避免重複
        for (int i = _head.childCount - 1; i >= 0; i--)
        {
            Transform c = _head.GetChild(i);
            if (c.name.StartsWith(EyeName)) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        }

        EnsureAssets();
        for (int i = 0; i < 2; i++)
        {
            GameObject root = new GameObject(EyeName + (i == 0 ? "R" : "L"));
            root.transform.SetParent(_head, false);
            _glow[i] = MakeSprite(root.transform, "Glow", s_glowSprite, 0);
            _core[i] = MakeSprite(root.transform, "Core", s_coreSprite, 1);
        }
        ApplyLocalPositions();
        SetRenderersEnabled(false);
    }

    private void OnEnable() { _a = 0f; if (_head != null) SetRenderersEnabled(false); }

    public void HideImmediate() { _a = 0f; SetRenderersEnabled(false); }

    private void LateUpdate()
    {
        bool want = _bird != null && _bird.hostileEyes && _bird.IsHostile;
        float target = want ? 1f : 0f;
        float dur = want ? fadeIn : fadeOut;
        _a = Mathf.MoveTowards(_a, target, Time.deltaTime / Mathf.Max(0.01f, dur));

        if (_a <= 0.001f) { if (_shown) SetRenderersEnabled(false); return; }
        if (!_shown) SetRenderersEnabled(true);

        if (_cam == null) _cam = Camera.main;
        Quaternion rot = _cam != null ? _cam.transform.rotation : Quaternion.identity;
        float pulse = want ? 1f + pulseAmount * Mathf.Sin(Time.time * pulseHz * Mathf.PI * 2f) : 1f;

        Vector3 ls = _head.lossyScale;
        float s = Mathf.Max(0.0001f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
        float coreScale = coreSize / s;      // 貼圖 1 單位＝1 公尺，除掉骨頭縮放得到世界尺寸
        float glowScale = glowSize / s * (0.9f + 0.1f * pulse);

        for (int i = 0; i < 2; i++)
        {
            if (_core[i] == null) continue;
            Color c = coreColor; c.a *= _a;
            Color g = glowColor; g.a *= _a * pulse;
            _core[i].color = c;
            _glow[i].color = g;
            _core[i].transform.rotation = rot;
            _glow[i].transform.rotation = rot;
            _core[i].transform.localScale = Vector3.one * coreScale;
            _glow[i].transform.localScale = Vector3.one * glowScale;
        }
    }

    private void ApplyLocalPositions()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_core[i] == null) continue;
            float sign = i == 0 ? 1f : -1f;
            Vector3 p = new Vector3(eyeLocalOffset.x, eyeLocalOffset.y, sign * (Mathf.Abs(eyeLocalOffset.z) + outwardPush));
            _core[i].transform.parent.localPosition = p;
            _core[i].transform.localPosition = Vector3.zero;
            _glow[i].transform.localPosition = Vector3.zero;
        }
    }

    private void SetRenderersEnabled(bool on)
    {
        _shown = on;
        for (int i = 0; i < 2; i++)
        {
            if (_core[i] != null) _core[i].enabled = on;
            if (_glow[i] != null) _glow[i].enabled = on;
        }
    }

    private static SpriteRenderer MakeSprite(Transform parent, string name, Sprite sp, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sharedMaterial = s_mat;
        sr.sortingOrder = order;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    private static void EnsureAssets()
    {
        if (s_mat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            s_mat = new Material(sh) { name = "Mat_BirdHostileEye (runtime)" };
        }
        if (s_coreSprite == null) s_coreSprite = MakeRadial(64, 2.2f, 0.55f);   // 亮點：中間實、邊緣快速收掉
        if (s_glowSprite == null) s_glowSprite = MakeRadial(64, 2.2f, 0f);      // 光暈：全程柔和衰減
    }

    // 徑向衰減白色貼圖，1 單位＝1 公尺（ppu＝邊長）。hard＝中心保持全亮的半徑比例
    private static Sprite MakeRadial(int size, float power, float hard)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = (size - 1) * 0.5f;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;   // 0 中心，1 邊
                float a = d >= 1f ? 0f : Mathf.Pow(1f - Mathf.Clamp01((d - hard) / Mathf.Max(0.001f, 1f - hard)), power);
                if (d < hard) a = 1f;
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, b);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Transform FindDeep(Transform root, string n)
    {
        if (root.name == n) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = FindDeep(root.GetChild(i), n);
            if (r != null) return r;
        }
        return null;
    }
}
