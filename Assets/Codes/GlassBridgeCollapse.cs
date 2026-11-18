using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1117 玻璃館最後一段「碎橋衝刺」（提案 B；M：「將最後一個燭台到倒數第二個燭台間的區塊改為碎橋衝刺」）。
///
/// 倒數第二根到最後一根燭火之間那五片「踩上去 2 秒就整片碎掉」的玻璃，改成：
///   ・她踏上第一片，身後傳來一聲碎裂——崩塌從橋頭開始，一小塊一小塊（約 1.4 公尺）往她追過來，
///     每塊碎掉都往下掉成碎片、配一聲玻璃碎裂。
///   ・崩塌比她慢（4.6 m/s，她跑 6），一直往前跑一定來得及；但它最多只落後她 maxLag 公尺，
///     停下來、回頭，大約 1.5 秒腳下就碎了（掉下去＝回到橋頭的存檔點 229.5 重來）。
///   ・最後一根燭火的光擋住崩塌：她點燃之前，崩塌進不了燭台那片玻璃，點燃時（不能動的那 1.5～1.9 秒）不會被追上。
///   ・點燃的那一刻：整座橋往她腳邊崩下去（一聲破鏡巨響），停在她身後、燭台旁邊——她被光接住，接結局。
///   ・漏掉最後一根（跑過頭沒點）：光不擋了，崩塌繼續往前吞。
///
/// 做法：
///   ・原本的 BreakablePlatform／BreakableGlassFloor／GlassShatterFX 開始崩落前會問 Owns()，這幾片由這支接管就不自己碎；
///     怪物（MonsterGlassBreaker）也不整片打碎它們。
///   ・碎掉的部分：碰撞框從左邊縮短；畫面用一張 SpriteMask 蓋住「已經碎掉的範圍」（只影響這幾片玻璃）；
///     碎片是用玻璃原圖切出來的小塊，自己做重力、旋轉、淡出。
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
    public float collapseSpeed = 4.6f;
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

    [Header("碎片")]
    [Tooltip("每一小塊碎成幾大片")]
    public int chunksPerTile = 3;
    [Tooltip("每一小塊另外噴幾顆細碎屑")]
    public int shardsPerTile = 7;
    public float fragmentGravity = 18f;
    [Tooltip("碎片多久消失（秒）")]
    public float fragmentLife = 1.6f;

    [Header("聲音")]
    [Tooltip("每碎一塊的聲音（留空＝Resources/Sfx/Glass_BridgeCrack，原檔 玻璃碎裂.mp3）")]
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
    }
    private readonly List<Piece> _pieces = new List<Piece>();
    private Piece _candlePiece;

    private class Frag
    {
        public Transform t;
        public SpriteRenderer sr;
        public Sprite ownSprite;
        public Vector3 v;
        public float w, age, life;
        public Color c;
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
    private Sprite _dot, _square;
    private AudioSource[] _src;
    private int _srcNext;
    private float _lastCrackAt = -1f;

    private void Start()
    {
        _ctrl = GetComponent<ShadowMonsterController>();
        if (crackSfx == null) crackSfx = Resources.Load<AudioClip>("Sfx/Glass_BridgeCrack");
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

        _dot = MakeDot(32);
        _square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

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
                    PlayCrack(1.3f, 0.75f);
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
    private void BreakTile(Piece p, int i)
    {
        float x0 = p.left + i * p.tileW, x1 = x0 + p.tileW;
        SpriteRenderer sr = p.sr;
        Sprite sp = sr.sprite;
        Color baseC = sr.color;
        baseC.a = Mathf.Clamp01(Mathf.Max(baseC.a * 1.8f, 0.45f));

        if (sp != null && sp.texture != null)
        {
            Bounds sb = sr.bounds;
            Rect tr = sp.textureRect;
            // 隨機切成幾大片
            int n = Mathf.Max(1, chunksPerTile);
            float[] cuts = new float[n + 1];
            cuts[0] = x0; cuts[n] = x1;
            for (int k = 1; k < n; k++) cuts[k] = Mathf.Lerp(x0, x1, (k + Random.Range(-0.3f, 0.3f)) / n);
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.Max(cuts[k], sb.min.x), b = Mathf.Min(cuts[k + 1], sb.max.x);
                if (b - a < 0.05f) continue;
                float u0 = tr.x + (a - sb.min.x) / sb.size.x * tr.width;
                float u1 = tr.x + (b - sb.min.x) / sb.size.x * tr.width;
                if (sr.flipX) { float f0 = tr.x + tr.xMax - u1, f1 = tr.x + tr.xMax - u0; u0 = f0; u1 = f1; }
                Sprite piece = Sprite.Create(sp.texture, new Rect(u0, tr.y, Mathf.Max(1f, u1 - u0), tr.height), new Vector2(0.5f, 0.5f), sp.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                var f = SpawnFrag(piece, true, new Vector3((a + b) * 0.5f, sb.center.y, sr.transform.position.z - 0.02f), p.tr.lossyScale, baseC, p);
                f.v = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(-0.5f, 1.4f), 0f);
                f.w = Random.Range(-220f, 220f);
                f.sr.flipX = sr.flipX;
            }
        }

        // 細碎屑
        for (int k = 0; k < shardsPerTile; k++)
        {
            float s = Random.Range(0.06f, 0.2f);
            Color c = Color.Lerp(new Color(0.8f, 0.92f, 1f, 0.9f), baseC, Random.value * 0.5f);
            var f = SpawnFrag(_dot, false, new Vector3(Random.Range(x0, x1), _floorTop + Random.Range(-0.3f, 0.2f), p.tr.position.z - 0.03f), new Vector3(s, s, 1f), c, p);
            f.v = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(0.5f, 3f), 0f);
            f.w = Random.Range(-400f, 400f);
            f.life = fragmentLife * Random.Range(0.4f, 0.8f);
        }

        PlayCrack(Random.Range(0.92f, 1.22f), 1f);
    }

    private Frag SpawnFrag(Sprite s, bool own, Vector3 pos, Vector3 scale, Color c, Piece p)
    {
        var go = new GameObject("碎橋碎片");
        go.transform.position = pos;
        go.transform.localScale = scale;
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = s;
        r.sharedMaterial = p.sr.sharedMaterial;
        r.sortingLayerID = p.sr.sortingLayerID;
        r.sortingOrder = p.sr.sortingOrder + 1;
        r.color = c;
        var f = new Frag { t = go.transform, sr = r, ownSprite = own ? s : null, c = c, life = fragmentLife * Random.Range(0.8f, 1.1f) };
        _frags.Add(f);
        return f;
    }

    private void UpdateFragments(float dt)
    {
        for (int i = _frags.Count - 1; i >= 0; i--)
        {
            Frag f = _frags[i];
            f.age += dt;
            if (f.t == null || f.age >= f.life) { KillFrag(f); _frags.RemoveAt(i); continue; }
            f.v.y -= fragmentGravity * dt;
            f.t.position += f.v * dt;
            f.t.Rotate(0f, 0f, f.w * dt);
            float fade = 1f - Mathf.Clamp01((f.age - f.life * 0.45f) / (f.life * 0.55f));
            Color c = f.c; c.a *= fade;
            f.sr.color = c;
        }
    }

    private static void KillFrag(Frag f)
    {
        if (f.t != null) Destroy(f.t.gameObject);
        if (f.ownSprite != null) Destroy(f.ownSprite);
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
    }

    private static Sprite MakeDot(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
