using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1117 玻璃館最後一段「碎橋衝刺」（提案 B；M：「將最後一個燭台到倒數第二個燭台間的區塊改為碎橋衝刺」）。
///
/// 倒數第二根到最後一根燭火之間那五片「踩上去 2 秒就整片碎掉」的玻璃，改成：
///   ・她踏上第一片，身後傳來一聲碎裂——崩塌從橋頭開始，一小塊一小塊（約 1.4 公尺）往她追過來，
///     每塊碎掉都往下掉成碎片、配一聲玻璃碎裂。
///   ・崩塌速度（★1120 6.2 m/s，比她跑的 6 稍快；原本 4.6 比她慢），一直跑會被慢慢逼近，要不停地跑；它最多只落後她 maxLag 公尺，
///     停下來、回頭，大約 1.5 秒腳下就碎了（掉下去＝回到橋頭的存檔點 229.5 重來）。
///   ・最後一根燭火的光擋住崩塌：她點燃之前，崩塌進不了燭台那片玻璃，點燃時（不能動的那 1.5～1.9 秒）不會被追上。
///   ・點燃的那一刻：整座橋往她腳邊崩下去（一聲破鏡巨響），停在她身後、燭台旁邊——她被光接住，接結局。
///   ・漏掉最後一根（跑過頭沒點）：光不擋了，崩塌繼續往前吞。
///
/// 做法：
///   ・原本的 BreakablePlatform／BreakableGlassFloor／GlassShatterFX 開始崩落前會問 Owns()，這幾片由這支接管就不自己碎；
///     怪物（MonsterGlassBreaker）也不整片打碎它們。
///   ・碎掉的部分：碰撞框從左邊縮短；畫面用一張 SpriteMask 蓋住「已經碎掉的範圍」（只影響這幾片玻璃）；
///     碎片：★1120 照 mirror wall_001 的碎法，把那一小塊玻璃原圖切成細小不規則四邊形，往下掉、3D 翻轉、2 秒後縮小消失。
///   ・★1118 崩塌前緣不再有任何光提示（冷光、熄燈的透光洞都拿掉），黑暗裡只靠聲音和腳下的碎裂感。
///   ・重生（IResettable）：全部復原。
/// 由 ShadowMonsterController.EnsureInitialized 自動掛上（場景裡也掛了一份，Inspector 數值會保留）。
/// 關掉：GlassBridgeCollapse.Enabled = false（五片玻璃回到原本踩上去 2 秒就碎）。
/// </summary>
[DisallowMultipleComponent]
public class GlassBridgeCollapse : MonoBehaviour, IResettable
{
    public static bool Enabled = true;

    private static readonly HashSet<GameObject> s_owned = new HashSet<GameObject>();
    /// <summary>這片玻璃歸碎橋管嗎（原本的崩落腳本、怪物打碎玻璃前會問）。</summary>
    public static bool Owns(Component c) { return Enabled && c != null && s_owned.Contains(c.gameObject); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_owned.Clear(); }

    [Header("崩塌的速度（她跑 6 m/s）")]
    [Tooltip("崩塌往前追的速度（公尺／秒）。要比她慢，一直跑就一定來得及")]
    public float collapseSpeed = 6.2f;   // ★1120 4.6 → 6.2（碎得太慢）；注意：比她跑的 6 m/s 快
    [Tooltip("崩塌最多落後她幾公尺：她跑太前面，崩塌會加速追到這個距離。停下來大約 maxLag ÷ collapseSpeed 秒腳下就碎")]
    public float maxLag = 7f;
    [Tooltip("落後太多時追上來的速度")]
    public float catchUpSpeed = 12f;
    [Tooltip("她踏上橋之後，第一塊碎掉前的空檔（秒）")]
    public float startDelay = 0.6f;
    [Tooltip("一小塊玻璃多長（公尺）。每片 11.4 公尺會切成差不多這個長度的幾塊")]
    public float tileLength = 1.4f;

    [Header("最後一根燭火")]
    [Tooltip("點燃之前，崩塌進不了燭台那片玻璃（燭光擋住崩塌）")]
    public bool candleHoldsCollapse = true;
    [Tooltip("點燃後整座橋往她腳邊崩下去的速度")]
    public float finaleCrumbleSpeed = 16f;
    [Tooltip("點燃後崩到她身後幾公尺停（也不會崩進燭台左右這個距離）")]
    public float finaleStopBehind = 2f;

    [Header("碎片（★1120 照 mirror wall_001 的碎法：Destructible 切細小不規則四邊形 + ShatteredObject 的力道與縮小消失）")]
    [Tooltip("玻璃短邊（高度）切成幾列；長邊照比例算。mirror wall_001 是 6")]
    [Range(3, 10)] public int gridRows = 6;
    [Tooltip("四邊形頂點的不規則抖動。mirror wall_001 是 0.35")]
    [Range(0.1f, 0.45f)] public float jitterAmount = 0.35f;
    [Tooltip("碎裂爆裂力道。mirror wall_001 是 2.5")]
    public float explosionForce = 2.5f;
    [Tooltip("碎片維持原大的時間（秒），之後開始縮小。mirror wall_001 是 2")]
    public float disappearDelay = 2f;
    [Tooltip("縮小到消失的時間（秒）。mirror wall_001 是 1")]
    public float shrinkDuration = 1f;
    [Tooltip("碎片翻轉的最快角速度（弧度／秒）。mirror wall_001 的碎片是剛體，Unity 預設上限 7")]
    public float maxSpin = 7f;
    [Tooltip("同時存在的碎片上限；超過就把新碎的那一小塊切粗一點（列數減半），避免整座橋崩下去時太多")]
    public int maxLiveShards = 1400;

    [Header("聲音")]
    [Tooltip("每碎一塊的聲音（留空＝Resources/Sfx/Glass_BridgeCrackFiltered：原本的 Glass_BridgeCrack 濾掉 6 kHz 以上的高頻、壓低音量（約低 10 dB），原檔太大聲太尖）")]
    public AudioClip crackSfx;
    [Range(0f, 1f)] public float crackVolume = 0.5f;
    [Tooltip("點燃最後一根、整座橋崩下去的聲音（留空＝Resources/Sfx/Glass_BridgeCollapse，原檔 玻璃館_破鏡牆.wav）")]
    public AudioClip collapseSfx;
    [Range(0f, 1f)] public float collapseVolume = 0.8f;

    private enum State { Idle, Arming, Running, Finale }
    private State _state = State.Idle;

    private class Piece
    {
        public GameObject go;
        public Transform tr;
        public BoxCollider col;
        public SpriteRenderer sr;
        public SpriteRenderer[] srs;
        public SpriteMaskInteraction[] origMask;
        public Vector3 c0, s0;
        public float left, right, tileW;
        public int tiles, broken;
        public bool isCandle;
        public Material shardMat;   // 碎片用的材質（玻璃原本的材質＋原圖），每片玻璃一個
    }
    private readonly List<Piece> _pieces = new List<Piece>();
    private Piece _candlePiece;

    private class Frag
    {
        public Transform t;
        public Mesh mesh;
        public Vector3 v, axis, scale0;
        public float spin, age;
    }
    private readonly List<Frag> _frags = new List<Frag>();

    private ShadowMonsterController _ctrl;
    private Transform _player;
    private CandleCollectible _last;
    private bool _built;
    private float _left, _right;       // 整座橋的左右
    private float _floorTop, _floorMidY;
    private float _front;              // 崩塌前緣（世界 X）
    private float _t;

    /// <summary>★1118 測試鍵用：碎橋已經建好、可以用了。</summary>
    public bool IsReady => _pieces.Count > 0;
    /// <summary>★1118 測試鍵用：整座橋左端的世界 X。</summary>
    public float BridgeLeftX => _left;
    /// <summary>★1118 測試鍵用：橋面頂端的世界 Y。</summary>
    public float BridgeFloorTop => _floorTop;

    private SpriteMask _mask;
    private Sprite _square;
    private AudioSource[] _src;
    private int _srcNext;
    private float _lastCrackAt = -1f;

    private void Start()
    {
        _ctrl = GetComponent<ShadowMonsterController>();
        if (crackSfx == null) crackSfx = Resources.Load<AudioClip>("Sfx/Glass_BridgeCrackFiltered");
        if (collapseSfx == null) collapseSfx = Resources.Load<AudioClip>("Sfx/Glass_BridgeCollapse");
    }

    // ───────── 找出橋 ─────────
    private void TryBuild()
    {
        if (_ctrl == null) return;
        _last = _ctrl.LastCandle;
        if (_last == null || _ctrl.candles == null) return;
        _built = true;

        // 倒數第二根燭火
        float lastX = _last.transform.position.x, prevX = float.NegativeInfinity;
        foreach (var c in _ctrl.candles)
            if (c != null && c != _last && c.transform.position.x < lastX) prevX = Mathf.Max(prevX, c.transform.position.x);
        if (float.IsNegativeInfinity(prevX)) { Debug.LogWarning("【碎橋】找不到倒數第二根燭火，碎橋不啟用"); return; }

        foreach (var gf in FindObjectsByType<BreakableGlassFloor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            BoxCollider col = gf.GetComponent<BoxCollider>();
            SpriteRenderer sr = gf.GetComponent<SpriteRenderer>();
            if (col == null || sr == null) continue;
            Bounds b = col.bounds;
            if (b.center.x <= prevX || b.center.x > lastX + 10f) continue;
            var p = new Piece
            {
                go = gf.gameObject, tr = gf.transform, col = col, sr = sr,
                c0 = col.center, s0 = col.size, left = b.min.x, right = b.max.x,
            };
            p.tiles = Mathf.Max(1, Mathf.RoundToInt((p.right - p.left) / Mathf.Max(0.3f, tileLength)));
            p.tileW = (p.right - p.left) / p.tiles;
            p.srs = gf.GetComponentsInChildren<SpriteRenderer>(true);
            p.origMask = new SpriteMaskInteraction[p.srs.Length];
            for (int i = 0; i < p.srs.Length; i++) p.origMask[i] = p.srs[i].maskInteraction;
            p.isCandle = lastX >= p.left && lastX <= p.right;
            _pieces.Add(p);
        }
        if (_pieces.Count == 0) { Debug.LogWarning("【碎橋】兩根燭火之間沒有會碎的玻璃，碎橋不啟用"); return; }
        _pieces.Sort((a, b) => a.left.CompareTo(b.left));
        foreach (var p in _pieces) { s_owned.Add(p.go); if (p.isCandle) _candlePiece = p; }

        _left = _pieces[0].left;
        _right = _pieces[_pieces.Count - 1].right;
        _floorTop = _pieces[0].col.bounds.max.y;
        _floorMidY = _pieces[0].sr.bounds.center.y;
        _front = _left;

        _square =Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

        // 蓋住「已經碎掉的範圍」的遮罩（只影響這幾片玻璃）
        _mask = new GameObject("碎橋_已碎範圍遮罩（自動生成）").AddComponent<SpriteMask>();
        _mask.sprite = _square;
        _mask.gameObject.SetActive(false);
        foreach (var p in _pieces)
            foreach (var r in p.srs) if (r != null) r.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;

        // 聲音放在自己的子物件上（怪物身上的 GetComponent<AudioSource>() 不會拿到這幾個）
        GameObject sg = new GameObject("碎橋_聲音（自動生成）");
        sg.transform.SetParent(transform, false);
        _src = new AudioSource[4];
        for (int i = 0; i < _src.Length; i++)
        {
            _src[i] = sg.AddComponent<AudioSource>();
            _src[i].playOnAwake = false;
            _src[i].spatialBlend = 0f;
        }

        Debug.Log($"【碎橋】★1117 碎橋衝刺就緒：{_pieces.Count} 片玻璃（x {_left:F1}～{_right:F1}），燭台那片 {(_candlePiece != null ? $"x {_candlePiece.left:F1}～{_candlePiece.right:F1}" : "沒有")}；崩塌 {collapseSpeed} m/s、最多落後 {maxLag} 公尺");
    }

    // ───────── 每幀 ─────────
    private void Update()
    {
        if (!_built) TryBuild();
        UpdateFragments(Time.deltaTime);
        if (_pieces.Count == 0 || !Enabled) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;
        if (_player == null && _ctrl != null) _player = _ctrl.player;
        if (_player == null) return;

        float px = _player.position.x;
        bool lit = _last != null && (_last.IsLitByPlayer || _last.isCollected);
        bool missed = _last != null && _last.missed;
        float dt = Time.deltaTime;

        switch (_state)
        {
            case State.Idle:
                if (px > _left + 0.5f && px < _right && IsChasing() && !lit && !missed)
                {
                    _state = State.Arming;
                    _t = 0f;
                    PlayCrack(1.05f, 0.75f);
                    Debug.Log($"【碎橋】她踏上橋（x={px:F1}），身後開始崩塌");
                }
                break;

            case State.Arming:
                _t += dt;
                if (_t >= startDelay) { _state = State.Running; _front = _left; }
                break;

            case State.Running:
                if (lit)
                {
                    _state = State.Finale;
                    PlayCollapse();
                    Debug.Log($"【碎橋】最後一根燭火點燃：整座橋往她腳邊崩下去（前緣 x={_front:F1}，她 x={px:F1}）");
                    break;
                }
                float speed = _front < px - maxLag ? catchUpSpeed : collapseSpeed;
                _front += speed * dt;
                if (candleHoldsCollapse && !missed && _candlePiece != null)
                    _front = Mathf.Min(_front, _candlePiece.left - 0.001f);   // 燭光擋住崩塌
                _front = Mathf.Min(_front, _right + 0.01f);
                BreakUpTo(_front);
                break;

            case State.Finale:
                float stop = px - finaleStopBehind;
                if (_last != null) stop = Mathf.Min(stop, _last.transform.position.x - finaleStopBehind);
                if (stop > _front) _front = Mathf.MoveTowards(_front, stop, finaleCrumbleSpeed * dt);
                BreakUpTo(_front);
                break;
        }

        UpdateFrontVisuals();
    }

    private bool IsChasing()
    {
        if (_ctrl == null) return false;
        var s = _ctrl.currentState;
        return s == ShadowMonsterController.MonsterState.Chasing
            || s == ShadowMonsterController.MonsterState.Appearing
            || s == ShadowMonsterController.MonsterState.Punishing;
    }

    /// <summary>前緣過了一小塊的中間，那一小塊就碎。</summary>
    private void BreakUpTo(float x)
    {
        foreach (var p in _pieces)
        {
            bool changed = false;
            while (p.broken < p.tiles && p.left + (p.broken + 0.5f) * p.tileW <= x)
            {
                BreakTile(p, p.broken);
                p.broken++;
                changed = true;
            }
            if (changed) ApplyCollider(p);
        }
    }

    private void ApplyCollider(Piece p)
    {
        if (p.broken >= p.tiles) { p.col.enabled = false; return; }
        float worldLeft = p.left + p.broken * p.tileW;
        float l0 = p.c0.x - p.s0.x * 0.5f, r0 = p.c0.x + p.s0.x * 0.5f;
        float lx = p.tr.InverseTransformPoint(new Vector3(worldLeft, p.tr.position.y, p.tr.position.z)).x;
        lx = Mathf.Clamp(lx, Mathf.Min(l0, r0), Mathf.Max(l0, r0));
        float far = p.tr.lossyScale.x >= 0f ? r0 : l0;   // 右緣在本地座標的哪一邊
        Vector3 c = p.c0, s = p.s0;
        s.x = Mathf.Abs(far - lx);
        c.x = (far + lx) * 0.5f;
        p.col.size = s;
        p.col.center = c;
    }

    private void RestoreCollider(Piece p)
    {
        p.col.size = p.s0;
        p.col.center = p.c0;
        p.col.enabled = true;
    }

    // ───────── 碎一小塊 ─────────
    // ★1120 照 mirror wall_001 的碎法（Destructible.ShatterSpriteToMicroGlassQuads ＋ ShatteredObject）：
    //   這一小塊玻璃的原圖切成「短邊 gridRows 列、長邊照比例」的網格，內部頂點不規則抖動，每格是一片小四邊形碎片；
    //   碎片往下掉、3D 翻轉（翻到側面會一閃一閃，像真的玻璃片），disappearDelay 秒後 shrinkDuration 秒內縮小消失。
    private void BreakTile(Piece p, int i)
    {
        SpriteRenderer sr = p.sr;
        Sprite sp = sr.sprite;
        if (sp != null && sp.texture != null)
        {
            Bounds sb = sr.bounds;
            float x0 = Mathf.Max(p.left + i * p.tileW, sb.min.x), x1 = Mathf.Min(p.left + (i + 1) * p.tileW, sb.max.x);
            if (x1 - x0 > 0.02f) ShatterRegion(p, sb, x0, x1);
        }
        PlayCrack(Random.Range(0.85f, 1.05f), 1f);   // 音調只往下飄：往上會更尖
    }

    private void ShatterRegion(Piece p, Bounds sb, float x0, float x1)
    {
        SpriteRenderer sr = p.sr;
        Sprite sp = sr.sprite;
        Texture2D tex = sp.texture;
        Rect rect = sp.textureRect;
        if (p.shardMat == null)
        {
            p.shardMat = new Material(sr.sharedMaterial) { name = "碎橋碎片（自動生成）" };
            p.shardMat.mainTexture = tex;
        }

        float w = x1 - x0, h = Mathf.Max(0.01f, sb.size.y);
        int rows = Mathf.Max(3, gridRows);
        if (_frags.Count > maxLiveShards) rows = Mathf.Max(2, rows / 2);
        int cols = Mathf.Clamp(Mathf.RoundToInt(rows * w / h), 2, 60);

        // 網格（0～1，x 是這一小塊的範圍、y 是整片玻璃的高度）；外框不抖
        Vector2[,] g = new Vector2[cols + 1, rows + 1];
        for (int x = 0; x <= cols; x++)
            for (int y = 0; y <= rows; y++)
            {
                float ox = (x > 0 && x < cols) ? Random.Range(-jitterAmount, jitterAmount) / cols : 0f;
                float oy = (y > 0 && y < rows) ? Random.Range(-jitterAmount, jitterAmount) / rows : 0f;
                g[x, y] = new Vector2(Mathf.Clamp01((float)x / cols + ox), Mathf.Clamp01((float)y / rows + oy));
            }

        Color32 col = sr.color;
        float z = sb.center.z;
        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
            {
                Vector2 q0 = g[x, y], q1 = g[x + 1, y], q2 = g[x + 1, y + 1], q3 = g[x, y + 1];
                Vector2 c = (q0 + q1 + q2 + q3) * 0.25f;
                Vector3 world = new Vector3(x0 + c.x * w, sb.min.y + c.y * h, z + Random.Range(-0.02f, 0.02f));

                var verts = new Vector3[4];
                var uvs = new Vector2[4];
                Vector2[] q = { q0, q1, q2, q3 };
                for (int k = 0; k < 4; k++)
                {
                    float wx = x0 + q[k].x * w, wy = sb.min.y + q[k].y * h;
                    verts[k] = new Vector3(wx - world.x, wy - world.y, 0f);
                    float u = (wx - sb.min.x) / sb.size.x;            // 在整張玻璃圖上的位置
                    if (sr.flipX) u = 1f - u;                          // 原圖左右翻面：碎片貼圖跟著翻
                    uvs[k] = new Vector2((rect.x + u * rect.width) / tex.width, (rect.y + q[k].y * rect.height) / tex.height);
                }
                var mesh = new Mesh { name = "碎橋碎片" };
                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.colors32 = new[] { col, col, col, col };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();

                var go = new GameObject("碎橋碎片");
                go.transform.position = world;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = p.shardMat;
                mr.sortingLayerID = sr.sortingLayerID;
                mr.sortingOrder = sr.sortingOrder + 1;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // 力道照 mirror wall_001：Destructible 的自由落體推力 ＋ ShatteredObject 的爆裂推力（碎片質量 1）
                float f = explosionForce;
                Vector3 v = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.8f, -0.2f), 0f) * (f * Random.Range(0.5f, 1f))
                          + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(-3.5f, 0.5f), 0f) * f;
                _frags.Add(new Frag
                {
                    t = go.transform, mesh = mesh, v = v, scale0 = Vector3.one,
                    axis = Random.onUnitSphere, spin = Random.Range(0.45f, 1f) * maxSpin * Mathf.Rad2Deg,
                });
            }
    }

    private void UpdateFragments(float dt)
    {
        float gy = Physics.gravity.y;
        for (int i = _frags.Count - 1; i >= 0; i--)
        {
            Frag f = _frags[i];
            f.age += dt;
            if (f.t == null || f.age >= disappearDelay + shrinkDuration) { KillFrag(f); _frags.RemoveAt(i); continue; }
            f.v.y += gy * dt;
            f.t.position += f.v * dt;
            f.t.rotation = Quaternion.AngleAxis(f.spin * dt, f.axis) * f.t.rotation;
            if (f.age > disappearDelay)
            {
                float t = (f.age - disappearDelay) / Mathf.Max(0.01f, shrinkDuration);
                f.t.localScale = f.scale0 * Mathf.Clamp01(1f - t * t * t);   // 跟 ShatteredObject 一樣：三次方縮小
            }
        }
    }

    private static void KillFrag(Frag f)
    {
        if (f.t != null) Destroy(f.t.gameObject);
        if (f.mesh != null) Destroy(f.mesh);
    }

    // ───────── 前緣：遮罩 ─────────
    private void UpdateFrontVisuals()
    {
        bool running = _state == State.Running || _state == State.Finale;
        float edge = BrokenEdge();
        bool any = running && edge > _left + 0.01f;

        if (_mask != null)
        {
            if (_mask.gameObject.activeSelf != any) _mask.gameObject.SetActive(any);
            if (any)
            {
                float l = _left - 2f;
                _mask.transform.position = new Vector3((l + edge) * 0.5f, _floorMidY, 0f);
                _mask.transform.localScale = new Vector3(edge - l, 8f, 1f);
            }
        }

    }

    /// <summary>已經碎到哪裡（世界 X）：最右邊那個「碎了至少一塊」的玻璃的碎邊。</summary>
    private float BrokenEdge()
    {
        float e = _left;
        foreach (var p in _pieces)
            if (p.broken > 0) e = Mathf.Max(e, p.left + p.broken * p.tileW);
        return e;
    }

    // ───────── 聲音 ─────────
    private void PlayCrack(float pitch, float volMul)
    {
        if (crackSfx == null || _src == null) return;
        if (Time.time - _lastCrackAt < 0.09f) return;
        _lastCrackAt = Time.time;
        AudioSource s = _src[_srcNext];
        _srcNext = (_srcNext + 1) % _src.Length;
        s.Stop();
        s.clip = crackSfx;
        s.pitch = pitch;
        s.volume = AudioManager.ScaleSfx(crackVolume * volMul);
        s.Play();
    }

    private void PlayCollapse()
    {
        if (collapseSfx == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(collapseSfx, collapseVolume);
        else AudioSource.PlayClipAtPoint(collapseSfx, _player != null ? _player.position : transform.position, AudioManager.ScaleSfx(collapseVolume));
    }

    // ───────── 重生 ─────────
    public void ResetToInitialState()
    {
        _state = State.Idle;
        _t = 0f;
        _front = _left;
        foreach (var p in _pieces) { p.broken = 0; RestoreCollider(p); }
        for (int i = _frags.Count - 1; i >= 0; i--) KillFrag(_frags[i]);
        _frags.Clear();
        if (_mask != null) _mask.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        foreach (var p in _pieces)
        {
            if (p.go != null) s_owned.Remove(p.go);
            if (p.srs != null)
                for (int i = 0; i < p.srs.Length; i++) if (p.srs[i] != null) p.srs[i].maskInteraction = p.origMask[i];
        }
        for (int i = _frags.Count - 1; i >= 0; i--) KillFrag(_frags[i]);
        if (_mask != null) Destroy(_mask.gameObject);
        foreach (var p in _pieces) if (p.shardMat != null) Destroy(p.shardMat);
    }
}
