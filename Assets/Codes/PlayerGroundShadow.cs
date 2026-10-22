using UnityEngine;

/// <summary>
/// 主角腳下的柔和橢圓影子（2D 背景上的 3D 主角容易看起來浮著，加一塊影子把她「貼」在地上）。
/// 每幀往下找她腳下的地面（地板、石柱頂都算），影子貼在那個表面上；
/// 跳得越高影子越淡、越小；主角被藏起來（重生升天等）或腳下沒有地面時影子就消失。
/// 放在場景任一物件上即可（荒原場景裡的「PlayerGroundShadow」），執行時才生成，不改其他物件。
/// </summary>
public class PlayerGroundShadow : MonoBehaviour
{
    [Header("參考")]
    [Tooltip("留空自動找 Tag = Player")]
    public Transform player;

    [Header("外觀")]
    [Tooltip("影子顏色（偏暖的深褐色比純黑更融入沙地）")]
    public Color shadowColor = new Color(0.32f, 0.16f, 0.08f, 1f);
    [Tooltip("站在地上時的濃度")]
    [Range(0f, 1f)] public float maxAlpha = 0.45f;
    [Tooltip("影子寬度 = 主角碰撞框寬度 × 這個倍率")]
    public float widthScale = 0.75f;
    [Tooltip("影子高度 = 影子寬度 × 這個比例（越小越扁）")]
    public float heightRatio = 0.22f;
    [Tooltip("離地多高時影子完全消失（公尺）")]
    public float fadeHeight = 4f;
    [Tooltip("跳到最高時影子縮到幾倍")]
    [Range(0.1f, 1f)] public float minScaleWhenHigh = 0.55f;
    [Tooltip("影子往鏡頭方向提前多少（避免跟地面重疊閃爍；要比主角離鏡頭遠，才會被主角擋住）")]
    public float zTowardCamera = 0.12f;

    [Header("偵測")]
    [Tooltip("往下找地面的最遠距離")]
    public float maxRayDistance = 8f;

    [Header("除錯（唯讀）")]
    [Tooltip("影子目前的狀態：顯示中，或沒顯示的原因")]
    [SerializeField] private string debugStatus = "";

    private SpriteRenderer _sr;
    private Collider _playerCol;
    private Renderer[] _playerRenderers;
    private static Sprite _sprite;

    private void Start()
    {
        FindPlayer();
        GameObject go = new GameObject("PlayerGroundShadow_Sprite");
        go.transform.SetParent(transform, false);
        _sr = go.AddComponent<SpriteRenderer>();
        _sr.sprite = GetSprite();
        _sr.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, 0f);
        _sr.enabled = false;
    }

    private void FindPlayer()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null)
        {
            _playerCol = null;
            foreach (Collider c in player.GetComponents<Collider>())
                if (c.enabled && !c.isTrigger) { _playerCol = c; break; }
            _playerRenderers = player.GetComponentsInChildren<Renderer>(true);
        }
    }

    private void LateUpdate()
    {
        if (_sr == null) return;
        if (player == null || _playerCol == null) { FindPlayer(); if (player == null || _playerCol == null) { Hide("找不到主角或主角的碰撞框"); return; } }

        if (!PlayerVisible()) { Hide("主角被藏起來"); return; }

        Bounds b = _playerCol.bounds;
        // 地面的碰撞很薄（Z 方向），單一條射線可能從前面穿過去；改用跟主角碰撞框一樣深的盒子往下掃
        Vector3 origin = b.center;
        Vector3 half = new Vector3(Mathf.Max(0.05f, b.extents.x * 0.3f), 0.05f, Mathf.Max(0.05f, b.extents.z));
        float best = float.MaxValue;
        RaycastHit hit = default;
        foreach (RaycastHit h in Physics.BoxCastAll(origin, half, Vector3.down, Quaternion.identity, b.extents.y + maxRayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == null || h.collider.transform.IsChildOf(player)) continue;
            if (h.distance <= 0f && h.point == Vector3.zero) continue;   // 一開始就重疊的碰撞，沒有接觸點可用
            if (h.distance < best) { best = h.distance; hit = h; }
        }
        if (hit.collider == null) { Hide("腳下找不到地面"); return; }

        float height = Mathf.Max(0f, b.min.y - hit.point.y);
        float t = fadeHeight > 0f ? Mathf.Clamp01(height / fadeHeight) : 1f;
        float alpha = maxAlpha * (1f - t);
        if (alpha <= 0.005f) { Hide("離地太高"); return; }

        // 影子貼在腳下表面上；排序跟著那個表面（地板、石柱），前面的東西照樣能蓋住影子
        SpriteRenderer surface = hit.collider.GetComponentInParent<SpriteRenderer>();
        _sr.sortingLayerID = surface != null ? surface.sortingLayerID : 0;
        _sr.sortingOrder = surface != null ? surface.sortingOrder : 1;
        float surfaceZ = surface != null ? surface.transform.position.z : hit.point.z;
        float z = Mathf.Max(surfaceZ - zTowardCamera, player.position.z + 0.05f);   // 一定在主角後面，主角會擋住影子

        float w = b.size.x * widthScale * Mathf.Lerp(1f, minScaleWhenHigh, t);
        _sr.transform.position = new Vector3(b.center.x, hit.point.y, z);
        _sr.transform.rotation = Quaternion.identity;
        _sr.transform.localScale = new Vector3(w, w * heightRatio, 1f);
        _sr.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, alpha);
        _sr.enabled = true;
        debugStatus = $"顯示中：貼在 {hit.collider.name}（離地 {height:F2} 米，濃度 {alpha:F2}）";
    }

    private void Hide(string why)
    {
        _sr.enabled = false;
        debugStatus = "沒顯示：" + why;
    }

    private bool PlayerVisible()
    {
        if (!player.gameObject.activeInHierarchy) return false;
        if (_playerRenderers == null) return true;
        foreach (Renderer r in _playerRenderers)
            if (r != null && r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer))
                return true;
        return false;
    }

    /// <summary>程式產生一張 1 單位寬的柔邊圓點（不需要額外圖檔）。</summary>
    private static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PlayerGroundShadowTex" };
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 1f - Mathf.Clamp01((d - 0.4f) / 0.6f);   // 中心 40% 實心，外圈柔邊淡出
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        _sprite.name = "PlayerGroundShadow";
        return _sprite;
    }
}
