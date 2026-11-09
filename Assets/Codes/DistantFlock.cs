using UnityEngine;

/// <summary>
/// ★1109k 遠方裝飾鳥群：一小群很小、低對比的剪影鳥，在天空慢慢飄、翅膀兩格交替拍動。
/// 純裝飾：沒有碰撞、不跟任何遊戲邏輯互動，也不是 BirdEnemy。
///
/// 位置 = 鳥群中心 x（homeX）依遠景視差跟著攝影機走：
///   centerX = homeX + (攝影機 x − homeX) × parallaxFactor，再加上緩慢漂移（drift）。
/// parallaxFactor 越大代表越遠（越貼著攝影機走）。攝影機正好在 homeX 附近時，鳥群會在畫面中。
/// 子物件是執行時（以及編輯模式預覽）建出來的暫存物件（HideAndDontSave），不會存進場景。
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(10001)]
public class DistantFlock : MonoBehaviour
{
    [Header("圖")]
    public Sprite frameA;
    public Sprite frameB;

    [Header("鳥群")]
    [Range(3, 12)] public int birdCount = 7;
    [Tooltip("鳥群散開範圍（公尺，寬 × 高）")]
    public Vector2 spread = new Vector2(9f, 3.2f);
    [Tooltip("單隻鳥的大小（公尺高），隨機介於兩者之間")]
    public Vector2 sizeRange = new Vector2(0.55f, 0.95f);
    [Tooltip("拍翅膀頻率（Hz）隨機範圍")]
    public Vector2 flapHzRange = new Vector2(4f, 7f);
    public int seed = 1;

    [Header("位置與視差")]
    [Tooltip("鳥群中心的世界 x（攝影機在這附近時，鳥群正好在畫面中央）")]
    public float homeX = 100f;
    [Tooltip("鳥群中心的世界 y")]
    public float centerY = 4f;
    [Tooltip("遠景視差：0＝釘在世界，1＝釘在畫面。越大越遠")]
    [Range(0f, 1f)] public float parallaxFactor = 0.55f;
    [Tooltip("飄移速度（公尺/秒），正＝往右（鳥頭朝右），負＝往左")]
    public float driftSpeed = 1.1f;
    [Tooltip("飄移來回的循環範圍（公尺）；走到頭會淡出再從另一頭淡入")]
    public float driftRange = 40f;

    [Header("外觀")]
    public Color tint = new Color(0.42f, 0.33f, 0.28f, 0.5f);
    public int sortingOrder = -1;
    public float z = 0.3f;

    [Header("編輯模式預覽（不勾就完全不動）")]
    public bool previewInEditMode = false;
    public float previewCamX = 0f;
    public float previewTime = 0f;

    private class Bird
    {
        public Transform t; public SpriteRenderer sr;
        public Vector2 offset; public float scale, hz, phase, bobAmp, bobHz, bobPhase;
    }
    private Bird[] _birds;
    private Transform _holder;

    private void OnEnable() { Build(); }
    private void OnDisable() { Teardown(); }
    // OnValidate 裡不能建/刪物件（會噴 SendMessage 錯誤），只舉旗，下一個 LateUpdate 再重建
    private bool _rebuild;
    private void OnValidate() { _rebuild = true; }

    private void Teardown()
    {
        if (_holder != null)
        {
            if (Application.isPlaying) Destroy(_holder.gameObject); else DestroyImmediate(_holder.gameObject);
        }
        // 保險：清掉任何殘留的暫存容器
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c.name == "_FlockBirds") { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
        }
        _holder = null; _birds = null;
    }

    private void Build()
    {
        Teardown();
        if (frameA == null) return;
        GameObject h = new GameObject("_FlockBirds");
        h.hideFlags = HideFlags.HideAndDontSave;
        h.transform.SetParent(transform, false);
        _holder = h.transform;

        System.Random rnd = new System.Random(seed);
        System.Func<float> r01 = () => (float)rnd.NextDouble();
        _birds = new Bird[birdCount];
        for (int i = 0; i < birdCount; i++)
        {
            GameObject go = new GameObject("bird" + i);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(_holder, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frameA;
            sr.sortingOrder = sortingOrder;
            Bird b = new Bird();
            b.t = go.transform; b.sr = sr;
            // 鬆散的橢圓團，中間較密
            float ang = r01() * Mathf.PI * 2f, rad = Mathf.Sqrt(r01());
            b.offset = new Vector2(Mathf.Cos(ang) * rad * spread.x * 0.5f, Mathf.Sin(ang) * rad * spread.y * 0.5f);
            b.scale = Mathf.Lerp(sizeRange.x, sizeRange.y, r01());
            b.hz = Mathf.Lerp(flapHzRange.x, flapHzRange.y, r01());
            b.phase = r01();
            b.bobAmp = 0.12f + r01() * 0.18f; b.bobHz = 0.25f + r01() * 0.3f; b.bobPhase = r01() * 6.28f;
            _birds[i] = b;
        }
        UpdateNow(Application.isPlaying ? Time.time : previewTime, GetCamX());
    }

    private float GetCamX()
    {
        if (!Application.isPlaying && previewInEditMode) return previewCamX;
        Camera c = Camera.main;
        return c != null ? c.transform.position.x : homeX;
    }

    /// <summary>照指定時間與攝影機 x 立刻更新一次（執行時由 LateUpdate 呼叫，截圖腳本也會用）。</summary>
    public void UpdateNow(float time, float camX)
    {
        if (_birds == null) { Build(); if (_birds == null) return; }  // 圖是在 AddComponent 之後才指定的，這裡補建
        float range = Mathf.Max(1f, driftRange);
        float cycle = (time * Mathf.Abs(driftSpeed)) % range;           // 0..range
        float drift = (cycle - range * 0.5f) * Mathf.Sign(driftSpeed == 0f ? 1f : driftSpeed);
        float edge = 1f - Mathf.Clamp01(Mathf.Abs(cycle / range - 0.5f) * 2f);  // 中間 1，兩端 0
        float fade = Mathf.Clamp01(edge * 6f);                           // 兩端各 ~8% 淡入淡出
        float cx = homeX + (camX - homeX) * parallaxFactor + drift;
        bool faceRight = driftSpeed >= 0f;

        for (int i = 0; i < _birds.Length; i++)
        {
            Bird b = _birds[i];
            float flap = (time * b.hz + b.phase) % 1f;
            b.sr.sprite = (flap < 0.5f || frameB == null) ? frameA : frameB;
            b.sr.flipX = !faceRight;
            Color c = tint; c.a *= fade; b.sr.color = c;
            float bob = Mathf.Sin(time * b.bobHz * 6.28f + b.bobPhase) * b.bobAmp;
            b.t.position = new Vector3(cx + b.offset.x, centerY + b.offset.y + bob, z);
            b.t.localScale = new Vector3(b.scale, b.scale, 1f);
        }
    }

    private void LateUpdate()
    {
        if (_rebuild) { _rebuild = false; Build(); }
        if (Application.isPlaying) UpdateNow(Time.time, GetCamX());
        else if (previewInEditMode) UpdateNow(previewTime, previewCamX);
    }
}
