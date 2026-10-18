using UnityEngine;

/// <summary>
/// ★1018 荒原危險說明圖（純線條，一次性）：風在吹→躲在掩體（石柱）後面沒事；站在外面會石化（全身灰黑），石化 3 次會重生（升天）；
/// 另一個危險是鳥，被鳥刺到也會重生（升天）。圖示固定在場景裡不跟著玩家，她走近時淡入、走過之後慢慢淡出。
///
/// 不用手動擺：進荒原時如果有掩體（WindShelter）會自動放在第一根掩體旁邊。
/// 想自己擺位置：在場景建一個空物件掛這支腳本，自動生成就會跳過；位置就是圖示位置。
/// 圖檔：Assets/Resources/Hints/DesertGuide.png（想換圖直接覆蓋同名檔）。
/// </summary>
public class DesertGuideHint : MonoBehaviour
{
    [Header("出現／消失（以玩家 X 對圖示 X 的差來算，單位公尺）")]
    [Tooltip("玩家在圖示左邊這麼遠開始淡入")]
    public float fadeInFarDistance = 32f;
    [Tooltip("玩家在圖示左邊這麼近時完全顯示")]
    public float fadeInFullDistance = 15f;
    [Tooltip("玩家走過圖示右邊這麼遠開始淡出")]
    public float fadeOutStartDistance = 12f;
    [Tooltip("玩家走過圖示右邊這麼遠時完全消失")]
    public float fadeOutEndDistance = 26f;
    public float fadeSeconds = 0.8f;

    [Header("外觀")]
    [Tooltip("圖示在世界裡的寬度（公尺）。這張圖很寬（三組畫面），所以預設比較大")]
    public float worldWidth = 26f;
    public float bobAmplitude = 0.15f;
    public int sortingOrder = 60;

    [Header("發光提示")]
    public bool glow = true;
    public Color glowColor = new Color(1f, 0.82f, 0.5f, 1f);
    [Range(0f, 1f)] public float glowStrength = 0.7f;
    public Vector2 glowScale = new Vector2(1.08f, 1.8f);
    public float glowPulseSpeed = 2.2f;

    [Header("自動生成位置（沒有手動擺時）")]
    [Tooltip("相對第一根掩體的偏移（X 正＝在掩體右邊、Y 正＝往上）。掩體底部大約在 y = -6.3")]
    public Vector2 offsetFromFirstShelter = new Vector2(-4f, 6f);

    private SpriteRenderer _sr, _glowSr;
    private Transform _player;
    private Vector3 _basePos;
    private float _alpha;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (FindAnyObjectByType<DesertGuideHint>() != null) return;
        WindShelter first = null;
        foreach (WindShelter s in FindObjectsByType<WindShelter>(FindObjectsInactive.Exclude))
            if (first == null || s.transform.position.x < first.transform.position.x) first = s;
        if (first == null) return;
        GameObject go = new GameObject("DesertGuideHint (自動生成)");
        DesertGuideHint h = go.AddComponent<DesertGuideHint>();
        Vector3 p = first.transform.position;
        go.transform.position = new Vector3(p.x + h.offsetFromFirstShelter.x, p.y + h.offsetFromFirstShelter.y, p.z);
    }

    private void Start()
    {
        Texture2D tex = Resources.Load<Texture2D>("Hints/DesertGuide");
        if (tex == null) { Debug.LogWarning("【荒原說明圖】找不到 Assets/Resources/Hints/DesertGuide.png，不顯示。"); enabled = false; return; }
        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / Mathf.Max(0.1f, worldWidth));
        _sr.sortingOrder = sortingOrder;
        _sr.color = new Color(1f, 1f, 1f, 0f);
        _basePos = transform.position;

        if (glow)
        {
            const int N = 128;
            Texture2D g = new Texture2D(N, N, TextureFormat.RGBA32, false);
            g.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    g.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                }
            g.Apply();
            GameObject go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            _glowSr = go.AddComponent<SpriteRenderer>();
            _glowSr.sprite = Sprite.Create(g, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            _glowSr.sortingOrder = sortingOrder - 1;
            float h = worldWidth * tex.height / Mathf.Max(1, tex.width);
            go.transform.localScale = new Vector3(worldWidth * glowScale.x, h * glowScale.y, 1f);
            _glowSr.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
        }
    }

    private void Update()
    {
        if (_sr == null) return;
        if (_player == null)
        {
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null) _player = pm.transform;
            if (_player == null) return;
        }
        float dx = _player.position.x - _basePos.x;
        float inA = Mathf.InverseLerp(-Mathf.Abs(fadeInFarDistance), -Mathf.Abs(fadeInFullDistance), dx);
        float outA = 1f - Mathf.InverseLerp(fadeOutStartDistance, fadeOutEndDistance, dx);
        float target = Mathf.Clamp01(Mathf.Min(inA, outA));
        _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));

        float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 3f);
        Color c = _sr.color; c.a = _alpha * pulse; _sr.color = c;
        if (_glowSr != null)
        {
            Color gc = glowColor; gc.a = _alpha * glowStrength * (0.65f + 0.35f * Mathf.Sin(Time.time * glowPulseSpeed)); _glowSr.color = gc;
        }
        transform.position = _basePos + new Vector3(0f, bobAmplitude * Mathf.Sin(Time.time * 1.6f), 0f);
        bool visible = _alpha > 0.001f;
        _sr.enabled = visible;
        if (_glowSr != null) _glowSr.enabled = visible;
    }
}
