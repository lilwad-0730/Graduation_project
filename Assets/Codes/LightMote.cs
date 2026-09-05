using UnityEngine;

/// <summary>
/// ★0905 廢墟光絮（0904 定案 #3：廢墟核心動詞＝收集光絮，推石頭是手段）。
/// GDD §5 光絮手感規格：
///   吸附　沒有磁吸，必須碰到才收
///   漂移　玩家靠近時被氣流推開一小段（0.3–0.6 個身寬）——這裡做成「跳開 0.5，最多離錨點 1.5，被逼到角落就不再跳」，所以一定拿得到，但要試兩三次
///   掉落　被狼咬住時掉約 1/3，散在附近繼續漂（由 LightMoteCollector 生成 dropped 版）
///   顯示　不用數字：她身上的暖光＝存量（LightMoteCollector 的光暈）
/// 視覺是程式生成的暖金柔光點，不用素材。由 RuinsMoteDirector 生成，不動場景檔。
/// </summary>
[DisallowMultipleComponent]
public class LightMote : MonoBehaviour
{
    [Header("外觀")]
    public Color color = new Color(0.98f, 0.84f, 0.55f, 0.95f);
    public float size = 0.55f;
    public int sortingOrder = 4;
    public float bobHeight = 0.12f;
    public float bobSpeed = 2.4f;
    public float flicker = 0.12f;

    [Header("漂開（流沙般的質感）")]
    [Tooltip("玩家進到這個距離內就跳開一次")]
    public float fleeRadius = 1.25f;
    [Tooltip("每次跳開多遠（0.3–0.6 個身寬）")]
    public float hopDistance = 0.55f;
    [Tooltip("離錨點最多多遠；到了就不再跳（被逼到角落）")]
    public float maxDriftFromAnchor = 1.5f;
    [Tooltip("兩次跳開之間的冷卻")]
    public float hopCooldown = 0.7f;
    public float hopSeconds = 0.22f;
    [Tooltip("玩家離開後慢慢飄回錨點的速度")]
    public float returnSpeed = 0.6f;

    [Header("收取")]
    public float collectRadius = 0.6f;
    public float collectSeconds = 0.35f;

    // 由 Collector／Director 設定
    [HideInInspector] public LightMoteCollector collector;
    [HideInInspector] public Vector3 anchor;
    [HideInInspector] public bool isDropped = false;   // 被狼咬掉的那一份

    private static Texture2D _tex;
    private static Sprite _sprite;

    private SpriteRenderer _sr;
    private Transform _player;
    private Collider _playerCol;
    private float _phase;
    private float _nextHopTime = 0f;
    private bool _hopping = false;
    private Vector3 _hopFrom, _hopTo;
    private float _hopT = 0f;
    private bool _collecting = false;
    private Vector3 _pos;

    // 掉落時的拋物線（不用物理，避免跟主角互推）
    private bool _ballistic = false;
    private Vector3 _vel;
    private float _ballisticTime = 0f;

    public static LightMote Spawn(Vector3 position, LightMoteCollector owner, bool dropped)
    {
        GameObject go = new GameObject(dropped ? "LightMote (掉落)" : "LightMote (自動生成)");
        go.transform.position = position;
        LightMote m = go.AddComponent<LightMote>();
        m.collector = owner;
        m.anchor = position;
        m.isDropped = dropped;
        return m;
    }

    private void Awake()
    {
        BuildSprite();
        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = _sprite;
        _sr.color = color;
        _sr.sortingOrder = sortingOrder;
        transform.localScale = new Vector3(size / 0.64f, size / 0.64f, 1f);   // 64px @100ppu ＝ 0.64 單位
        _phase = Random.Range(0f, 6.28f);
        _pos = transform.position;
        if (anchor == Vector3.zero) anchor = _pos;
    }

    /// <summary>被狼咬掉時：往外拋一下再落地成為普通光絮。</summary>
    public void LaunchDropped(Vector3 velocity)
    {
        _ballistic = true;
        _vel = velocity;
        _ballisticTime = 0f;
        _nextHopTime = Time.time + 1.2f;   // 剛落地先別跳，讓她有機會撿
    }

    private void Update()
    {
        if (_collecting) return;

        if (_player == null)
        {
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null)
            {
                _player = pm.transform;
                _playerCol = pm.GetComponent<Collider>();
                if (_playerCol == null) _playerCol = pm.GetComponentInChildren<Collider>();
            }
        }

        float dt = Time.deltaTime;

        if (_ballistic)
        {
            _ballisticTime += dt;
            _vel += Vector3.down * 9f * dt;
            _pos += _vel * dt;
            float groundY = GroundYBelow(_pos, 6f, _pos.y - 6f);
            if (_pos.y <= groundY + 0.35f || _ballisticTime > 1.2f)
            {
                _pos.y = Mathf.Max(_pos.y, groundY + 0.35f);
                _ballistic = false;
                anchor = _pos;
            }
        }
        else if (_hopping)
        {
            _hopT += dt / Mathf.Max(0.05f, hopSeconds);
            float e = 1f - (1f - Mathf.Clamp01(_hopT)) * (1f - Mathf.Clamp01(_hopT));
            _pos = Vector3.Lerp(_hopFrom, _hopTo, e);
            if (_hopT >= 1f) _hopping = false;
        }
        else
        {
            bool playerNear = false;
            if (_player != null)
            {
                // 用主角碰撞體最近點算距離：不管軸心在腳還是在胸口都對
                Vector3 nearest = _playerCol != null ? _playerCol.bounds.ClosestPoint(_pos) : _player.position;
                Vector3 toMote = _pos - nearest;
                toMote.z = 0f;
                float d = toMote.magnitude;
                playerNear = d < fleeRadius * 2.2f;

                // 碰到＝收（無磁吸）
                if (d <= collectRadius)
                {
                    BeginCollect();
                    return;
                }
                if (toMote.sqrMagnitude < 0.0001f) toMote = _pos - _player.position;

                // 靠近就跳開一次；離錨點太遠就不跳（被逼到角落）
                if (d < fleeRadius && Time.time >= _nextHopTime)
                {
                    Vector3 fromAnchor = _pos - anchor; fromAnchor.z = 0f;
                    if (fromAnchor.magnitude < maxDriftFromAnchor - 0.05f)
                    {
                        TryHop(toMote);
                    }
                    _nextHopTime = Time.time + hopCooldown;
                }
            }

            // 玩家不在附近：慢慢飄回錨點
            if (!playerNear)
            {
                Vector3 back = anchor - _pos; back.z = 0f;
                if (back.magnitude > 0.05f) _pos += back.normalized * Mathf.Min(back.magnitude, returnSpeed * dt);
            }
        }

        // 呼吸浮動＋微閃
        float bob = Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight;
        transform.position = new Vector3(_pos.x, _pos.y + bob, _pos.z);
        if (_sr != null)
        {
            Color c = color;
            c.a = color.a * (1f - flicker * 0.5f + flicker * 0.5f * Mathf.Sin(Time.time * 7.3f + _phase * 2f));
            _sr.color = c;
        }
    }

    private void TryHop(Vector3 awayFromPlayer)
    {
        Vector3 dir = awayFromPlayer;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
        dir.Normalize();
        // 往外＋一點點往上，像被氣流推開
        Vector3 hop = (dir * hopDistance) + Vector3.up * (hopDistance * 0.25f);

        // 前面有牆就改往另一邊；還是有牆就不跳
        RaycastHit hit;
        if (Physics.Raycast(_pos, hop.normalized, out hit, hop.magnitude + 0.3f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 alt = new Vector3(-dir.x, dir.y, 0f) * hopDistance + Vector3.up * (hopDistance * 0.25f);
            if (Physics.Raycast(_pos, alt.normalized, out hit, alt.magnitude + 0.3f, ~0, QueryTriggerInteraction.Ignore)) return;
            hop = alt;
        }

        _hopFrom = _pos;
        _hopTo = _pos + hop;
        _hopT = 0f;
        _hopping = true;
    }

    private void BeginCollect()
    {
        _collecting = true;
        if (collector != null) collector.OnMoteCollected(this);
        StartCoroutine(CollectRoutine());
    }

    private System.Collections.IEnumerator CollectRoutine()
    {
        float t = 0f;
        Vector3 start = transform.position;
        Vector3 startScale = transform.localScale;
        while (t < collectSeconds)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / collectSeconds);
            Vector3 target = _player != null ? _player.position + Vector3.up * 1.1f : start;
            transform.position = Vector3.Lerp(start, target, p * p);
            transform.localScale = startScale * (1f - 0.7f * p);
            if (_sr != null) { Color c = color; c.a = color.a * (1f - p); _sr.color = c; }
            yield return null;
        }
        Destroy(gameObject);
    }

    private static float GroundYBelow(Vector3 from, float maxDist, float fallback)
    {
        RaycastHit hit;
        if (Physics.Raycast(from + Vector3.up * 0.2f, Vector3.down, out hit, maxDist + 0.2f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return fallback;
    }

    private static void BuildSprite()
    {
        if (_sprite != null) return;
        int n = 64;
        _tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        _tex.name = "Procedural_LightMote";
        _tex.hideFlags = HideFlags.HideAndDontSave;
        _tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Clamp01(1f - r / 0.28f);          // 亮心
                float halo = Mathf.Clamp01(1f - r) ;                   // 柔暈
                float a = Mathf.Clamp01(core + halo * halo * 0.55f);
                byte A = (byte)Mathf.RoundToInt(255f * a);
                byte W = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.8f + 0.2f * core));
                px[y * n + x] = new Color32(255, W, (byte)Mathf.RoundToInt(W * 0.85f), A);
            }
        }
        _tex.SetPixels32(px);
        _tex.Apply();
        _sprite = Sprite.Create(_tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "Procedural_LightMote_Sprite";
        _sprite.hideFlags = HideFlags.HideAndDontSave;
    }
}
