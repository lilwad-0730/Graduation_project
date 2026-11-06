using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1105 荒原空中剪影（v4）：背景天空裡淡淡浮出三組會動的剪影，隨玩家走過慢慢淡入淡出，並且會「跟著真的風」反應。
///   ① 鳥群慢慢飛過（翅膀拍動）　② 被逆風壓彎的孤樹（風痕飄過）　③ 遠方一個披著圍巾的人影，沿著一條細線頂著風慢慢走
/// 真的起風時：樹搖得更用力、風痕變快變濃、人影前傾、圍巾被吹平、鳥拍翅變快；風停了慢慢回到平靜。
/// 全程式生成（不用美術檔、不碰場景）；進荒原場景自動出現。
/// 想拿掉：把 Enabled 改成 false，或直接刪掉這個檔案（沒有其他程式用到它，也沒有場景物件引用它）。
/// </summary>
public class SkySilhouetteFX : MonoBehaviour
{
    public static bool Enabled = false;   // 企劃想放其他關卡且要改成定格演出，先關；程式保留

    private const float Parallax = 0.3f;      // 越小越像貼在遠方
    private const float MaxAlpha = 0.27f;     // 最濃的透明度
    private const float FadeInner = 12f;      // 離錨點這麼近＝完全顯示
    private const float FadeOuter = 40f;      // 離錨點這麼遠＝完全消失
    private const float PPU = 24f;
    private const float MotionSpeed = 1f;     // 動作快慢倍率：0＝全部靜止，1＝預設
    private const float WindReaction = 1f;    // 跟著真的風反應的程度：0＝不理風，1＝預設，2＝更誇張

    private static readonly Color Tint = new Color(0.30f, 0.18f, 0.25f, 1f);   // 偏暖的暗紫灰，疊在粉色天空上不會髒

    private class Part { public Transform tr; public SpriteRenderer sr; public float a = 1f; }

    private class Group
    {
        public Transform root;
        public float anchorX, yOff, phase, alpha;
        public List<Part> parts = new List<Part>();
        public Action<float> anim;
    }

    private readonly List<Group> _groups = new List<Group>();
    private Transform _player;
    private Camera _cam;
    private float _gust;      // 0＝平靜，1＝真的在吹風（平滑過的）

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryInstall(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { TryInstall(s); }

    private static void TryInstall(Scene s)
    {
        if (!Enabled) return;
        if (!s.IsValid() || !s.name.ToLower().Contains("desert")) return;
        if (FindAnyObjectByType<SkySilhouetteFX>() != null) return;
        GameObject go = new GameObject("[空中剪影 SkySilhouetteFX]");
        SceneManager.MoveGameObjectToScene(go, s);
        go.AddComponent<SkySilhouetteFX>();
    }

    private void Start()
    {
        BuildFlock(NewGroup("鳥群", 30f, 7.5f));
        Group tree = NewGroup("風中的樹", 120f, -3.5f);
        tree.root.localScale = new Vector3(-1f, 1f, 1f);     // 風是逆風（往左吹），樹往左壓彎、風痕往左飄
        BuildTree(tree);
        BuildWalker(NewGroup("遠方的人影", 215f, 1.5f));
    }

    private Group NewGroup(string label, float anchorX, float yOff)
    {
        GameObject g = new GameObject("剪影_" + label);
        g.transform.SetParent(transform, false);
        Group grp = new Group { root = g.transform, anchorX = anchorX, yOff = yOff, phase = _groups.Count * 1.9f };
        _groups.Add(grp);
        return grp;
    }

    private Part AddPart(Group g, string name, Sprite sp, Transform parent, Vector3 localPos)
    {
        GameObject o = new GameObject(name);
        o.transform.SetParent(parent != null ? parent : g.root, false);
        o.transform.localPosition = localPos;
        SpriteRenderer sr = o.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = -1;                  // 跟背景圖同層，z 比背景近一點＝畫在背景前、遊戲物件後
        Color c = Tint; c.a = 0f; sr.color = c;
        Part p = new Part { tr = o.transform, sr = sr };
        g.parts.Add(p);
        return p;
    }

    private static float EdgeFade(float x, float half, float margin)
    {
        return Mathf.Clamp01((half - Mathf.Abs(x)) / Mathf.Max(0.01f, margin));
    }

    // ───────────────────────── ① 鳥群 ─────────────────────────
    private void BuildFlock(Group g)
    {
        Sprite bird = BirdSprite();
        Transform flock = new GameObject("鳥群").transform;
        flock.SetParent(g.root, false);
        const int n = 9;
        Part[] birds = new Part[n];
        Vector3[] off = new Vector3[n];
        float[] sc = { 1.3f, 1.1f, 1.1f, 0.95f, 0.95f, 0.8f, 0.8f, 0.65f, 0.65f };
        float[] dim = { 1f, 0.95f, 0.95f, 0.85f, 0.85f, 0.72f, 0.72f, 0.6f, 0.6f };      // 後面的鳥比較遠、比較淡
        for (int i = 0; i < n; i++)
        {
            int rank = (i + 1) / 2;                          // V 字隊形：領頭一隻，兩側往後展開
            float side = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f);
            off[i] = new Vector3(-rank * 2.7f, side * rank * 1.15f + (i % 3) * 0.2f, 0f);
            birds[i] = AddPart(g, "鳥" + i, bird, flock, off[i]);
        }
        const float span = 56f;
        g.anim = t =>
        {
            float s = ((t * 0.9f + g.phase * 6f) % span) - span * 0.5f;     // 慢慢從左飛到右，飛出去再從左邊回來
            flock.localPosition = new Vector3(s, Mathf.Sin(t * 0.25f + g.phase) * 0.6f, 0f);
            flock.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.3f) * 1.5f);
            float fade = EdgeFade(s, span * 0.5f, 9f);
            float rate = 4.6f * (1f + 0.4f * _gust);
            for (int i = 0; i < n; i++)
            {
                float ph = t * rate + i * 0.75f;
                float down = Mathf.Sin(ph);
                float flap = down * 0.85f + 0.15f;                         // 翅膀拍動：舉起→攤平→垂下
                birds[i].tr.localScale = new Vector3(sc[i], sc[i] * flap, 1f);
                birds[i].tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(ph * 0.5f) * 3f);   // 身體隨拍翅微微起伏
                birds[i].tr.localPosition = off[i] + new Vector3(0f, Mathf.Sin(t * 1.1f + i * 1.3f) * 0.22f - down * 0.06f, 0f);
                birds[i].a = fade * dim[i];
            }
        };
    }

    /// <summary>一隻鳥（正面看、翅膀往上舉）：翅膀有羽尖的鋸齒邊、身體、尾巴。縮放 Y 就等於拍翅。</summary>
    private static Sprite BirdSprite()
    {
        Cv c = new Cv(96, 48);
        // 海鷗式的 M 形翅膀：前緣一條優雅的弧線，越靠身體越粗；翅尖再補兩根細羽
        c.Curve(new Vector2(3, 24), new Vector2(18, 46), new Vector2(47, 21), 1.6f, 6f);
        c.Curve(new Vector2(49, 21), new Vector2(78, 46), new Vector2(93, 24), 6f, 1.6f);
        c.Seg(3, 24, 10, 17, 1.6f); c.Seg(8, 30, 14, 22, 1.4f);
        c.Seg(93, 24, 86, 17, 1.6f); c.Seg(88, 30, 82, 22, 1.4f);
        c.Ellipse(48, 19, 4.4f, 6.5f);                                          // 身體
        c.Disc(48, 25.5f, 3.2f);                                                // 頭
        c.Poly(new[] { new Vector2(45, 14), new Vector2(51, 14), new Vector2(48, 6) });   // 尾
        return c.ToSprite(PPU, new Vector2(0.5f, 0.4f), 0, 1f);
    }

    // ───────────────────────── ② 風中的樹 ─────────────────────────
    private void BuildTree(Group g)
    {
        Cv t = new Cv(192, 300);
        t.Curve(new Vector2(88, 0), new Vector2(72, 110), new Vector2(138, 222), 19f, 6f);                  // 主幹，往右壓彎
        t.Poly(new[] { new Vector2(68, 0), new Vector2(124, 0), new Vector2(104, 26), new Vector2(86, 28) });   // 根部張開
        t.Curve(new Vector2(98, 100), new Vector2(142, 124), new Vector2(176, 136), 8f, 2.5f);
        t.Curve(new Vector2(116, 160), new Vector2(152, 186), new Vector2(180, 226), 7f, 2.5f);
        t.Curve(new Vector2(92, 72), new Vector2(60, 90), new Vector2(30, 90), 6f, 2f);
        t.Seg(150, 130, 161, 152, 2.2f); t.Seg(165, 134, 179, 154, 2f);
        t.Seg(160, 200, 150, 217, 2.2f); t.Seg(46, 88, 40, 105, 2f);
        Part trunk = AddPart(g, "樹幹", t.ToSprite(PPU, new Vector2(0.5f, 0f), 40, 0.75f), null, Vector3.zero);

        Vector2[] tips = { new Vector2(176, 136), new Vector2(180, 226), new Vector2(128, 238), new Vector2(30, 90), new Vector2(150, 188) };
        float[] size = { 1.05f, 0.95f, 1.2f, 0.65f, 0.55f };
        Part[] leaf = new Part[tips.Length];
        for (int i = 0; i < tips.Length; i++)
        {
            Vector3 lp = new Vector3((tips[i].x - 96f) / PPU, tips[i].y / PPU, 0f);
            leaf[i] = AddPart(g, "葉叢" + i, FoliageSprite(100 + i), trunk.tr, lp);
        }

        // 風痕：幾條細細的線，隨風飄過（真的起風時變快、變濃）
        Sprite streak = StreakSprite();
        Part[] st = new Part[4];
        float[] sy = { 3.2f, 6.6f, 9.4f, 11.6f };
        for (int i = 0; i < st.Length; i++) st[i] = AddPart(g, "風痕" + i, streak, g.root, new Vector3(0f, sy[i], 0f));
        float[] stPos = { 0.1f, 0.45f, 0.7f, 0.9f };
        float lastT = -1f;

        g.anim = tt =>
        {
            float dt = lastT < 0f ? 0f : Mathf.Clamp(tt - lastT, 0f, 0.1f);
            lastT = tt;
            float amp = 1f + 2.0f * _gust;
            float sway = Mathf.Sin(tt * 0.6f + g.phase) * 1.3f * amp + Mathf.Sin(tt * 1.7f + g.phase * 2f) * 0.5f * amp + 2.5f * _gust;
            trunk.tr.localRotation = Quaternion.Euler(0f, 0f, -sway);                // 整棵隨風搖，風來時再多壓彎一點
            for (int i = 0; i < leaf.Length; i++)
            {
                float r = (Mathf.Sin(tt * 1.15f + i * 1.4f) * 4f + Mathf.Sin(tt * 2.3f + i) * 1.5f) * (1f + 1.4f * _gust);
                leaf[i].tr.localRotation = Quaternion.Euler(0f, 0f, r);              // 葉叢自己再多搖一點
                leaf[i].tr.localScale = Vector3.one * size[i] * (1f + Mathf.Sin(tt * 0.9f + i * 2f) * 0.03f);
            }
            for (int i = 0; i < st.Length; i++)
            {
                stPos[i] = (stPos[i] + dt * (0.06f + i * 0.012f) * (1f + 3.5f * _gust)) % 1f;
                float x = Mathf.Lerp(-17f, 17f, stPos[i]);
                st[i].tr.localPosition = new Vector3(x, sy[i] + Mathf.Sin(tt * 0.7f + i) * 0.25f, 0f);
                st[i].a = EdgeFade(x, 17f, 6f) * (0.55f + 0.45f * _gust);
            }
        };
    }

    private static Sprite FoliageSprite(int seed)
    {
        Cv c = new Cv(120, 72);
        System.Random rng = new System.Random(seed);
        for (int i = 0; i < 11; i++)
        {
            float ox = 60f + ((float)rng.NextDouble() * 2f - 0.75f) * 30f;       // 偏右＝下風處
            float oy = 36f + ((float)rng.NextDouble() * 2f - 1f) * 12f;
            float r = 7f + (float)rng.NextDouble() * 6.5f;
            c.Ellipse(ox, oy, r * 1.75f, r);
        }
        return c.ToSprite(PPU, new Vector2(0.28f, 0.5f), 0, 1f);   // 樞軸偏左：接在枝尖上
    }

    private static Sprite StreakSprite()
    {
        Cv c = new Cv(320, 16);
        c.Curve(new Vector2(6, 8), new Vector2(150, 14), new Vector2(314, 7), 0.8f, 3f);
        c.Curve(new Vector2(150, 12), new Vector2(230, 8), new Vector2(314, 7), 3f, 0.6f);
        return c.ToSprite(PPU, new Vector2(0.5f, 0.5f), 0, 1f);
    }

    // ───────────────────────── ③ 遠方的人影 ─────────────────────────
    private void BuildWalker(Group g)
    {
        Cv line = new Cv(800, 6);
        for (int x = 0; x < 800; x++)
        {
            float f = Mathf.Clamp01(Mathf.Min(x, 799 - x) / 140f);       // 兩端淡出
            line.Put(x, 2, f); line.Put(x, 3, f * 0.7f);
        }
        AddPart(g, "細線", line.ToSprite(PPU, new Vector2(0.5f, 0.5f), 0, 1f), null, Vector3.zero).a = 0.5f;

        const float legLen = 2.2f;
        Transform walker = new GameObject("人影").transform;
        walker.SetParent(g.root, false);
        walker.localScale = Vector3.one * 0.8f;

        Cv torso = new Cv(40, 84);
        torso.Poly(new[] { new Vector2(13, 58), new Vector2(27, 58), new Vector2(37, 6), new Vector2(3, 6) });   // 長外套
        torso.Ellipse(20, 58, 9.5f, 4.2f);                                                                        // 肩
        torso.Seg(20, 58, 20, 64, 5f);
        torso.Disc(20, 71, 8.2f);
        torso.Ellipse(15.5f, 71, 8.5f, 9.5f);                                                                     // 兜帽（頭後面鼓起來）
        Part body = AddPart(g, "身體", torso.ToSprite(PPU, new Vector2(0.5f, 0f), 0, 1f), walker, new Vector3(0f, legLen - 0.15f, 0f));

        Cv leg = new Cv(16, 56);
        leg.Seg(8, 52, 8, 5, 10f);
        Sprite legSp = leg.ToSprite(PPU, new Vector2(0.5f, 52f / 56f), 0, 1f);
        Part legA = AddPart(g, "腿A", legSp, walker, new Vector3(0.05f, legLen, 0f));
        Part legB = AddPart(g, "腿B", legSp, walker, new Vector3(-0.05f, legLen, 0f));

        Cv arm = new Cv(14, 44);
        arm.Seg(7, 40, 7, 6, 7f);
        Sprite armSp = arm.ToSprite(PPU, new Vector2(0.5f, 40f / 44f), 0, 1f);
        Part armA = AddPart(g, "手A", armSp, walker, new Vector3(0.1f, legLen + 2.2f, 0f));
        Part armB = AddPart(g, "手B", armSp, walker, new Vector3(-0.1f, legLen + 2.2f, 0f));

        // 圍巾：四節接起來，每節有自己的擺動延遲，像布料被風吹
        Cv sc = new Cv(26, 10);
        sc.Curve(new Vector2(1, 5), new Vector2(13, 5), new Vector2(25, 5), 6.5f, 3.5f);
        Sprite scSp = sc.ToSprite(PPU, new Vector2(0f, 0.5f), 0, 1f);
        const int segs = 4;
        Part[] scarf = new Part[segs];
        for (int i = 0; i < segs; i++)
            scarf[i] = AddPart(g, "圍巾" + i, scSp, i == 0 ? walker : scarf[i - 1].tr, i == 0 ? new Vector3(-0.05f, legLen + 2.45f, 0f) : new Vector3(0.95f, 0f, 0f));

        const float span = 40f;
        g.anim = t =>
        {
            float s = ((t * 0.9f + g.phase * 7f) % span) - span * 0.5f;   // 很慢地一路往右走，走出去再從左邊回來
            float step = Mathf.Sin(t * (3.2f + 0.8f * _gust));
            walker.localPosition = new Vector3(s, 0.15f + Mathf.Abs(step) * 0.07f, 0f);
            walker.localRotation = Quaternion.Euler(0f, 0f, -(2f + 7f * _gust));       // 頂風時身體前傾
            float stride = 24f + 6f * _gust;
            legA.tr.localRotation = Quaternion.Euler(0f, 0f, step * stride);
            legB.tr.localRotation = Quaternion.Euler(0f, 0f, -step * stride);
            armA.tr.localRotation = Quaternion.Euler(0f, 0f, -step * 16f);
            armB.tr.localRotation = Quaternion.Euler(0f, 0f, step * 16f);
            float droop = Mathf.Lerp(28f, 4f, _gust) + Mathf.Sin(t * 2f) * 4f;      // 風大時圍巾被吹平
            for (int i = 0; i < segs; i++)
            {
                float ang = i == 0 ? 180f + droop : Mathf.Sin(t * 3.4f - i * 0.9f) * (8f + 22f * _gust);
                scarf[i].tr.localRotation = Quaternion.Euler(0f, 0f, ang);
            }
            float fade = EdgeFade(s, span * 0.5f, 8f);
            body.a = legA.a = legB.a = armA.a = armB.a = fade;
            for (int i = 0; i < segs; i++) scarf[i].a = fade;
        };
    }

    // ───────────────────────── 每幀 ─────────────────────────
    private float WindTarget()
    {
        WindGustSystem w = WindGustSystem.Instance;
        if (w == null || w.IsStoppedForever) return 0f;
        if (w.CurrentState == WindState.Blowing) return 1f;
        return w.IsTelegraphing ? 0.3f : 0f;      // 起風前沙塵線出現時，先有一點預兆
    }

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        if (_player == null)
        {
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null) _player = pm.transform;
        }
        float px = _player != null ? _player.position.x : _cam.transform.position.x;
        float camX = _cam.transform.position.x, camY = _cam.transform.position.y;
        float t = Time.unscaledTime * MotionSpeed;
        _gust = Mathf.MoveTowards(_gust, Mathf.Clamp01(WindTarget() * WindReaction), Time.unscaledDeltaTime * 1.2f);

        for (int i = 0; i < _groups.Count; i++)
        {
            Group g = _groups[i];
            float x = g.anchorX + (camX - g.anchorX) * Parallax;
            g.root.position = new Vector3(x, camY + g.yOff, 0.5f);
            if (g.anim != null) g.anim(t);

            float d = Mathf.Abs(px - g.anchorX);
            float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeInner, FadeOuter, d));
            g.alpha = Mathf.MoveTowards(g.alpha, k * MaxAlpha, Time.unscaledDeltaTime * 0.25f);   // 淡入淡出慢一點，不突兀
            for (int p = 0; p < g.parts.Count; p++)
            {
                Part part = g.parts[p];
                Color c = part.sr.color;
                c.a = g.alpha * part.a;
                part.sr.color = c;
            }
        }
    }

    // ───────────────────────── 畫小圖 ─────────────────────────
    private class Cv
    {
        public float[] a;
        public int w, h;
        public Cv(int w, int h) { this.w = w; this.h = h; a = new float[w * h]; }

        public void Put(int x, int y, float cov)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || cov <= 0f) return;
            int i = y * w + x;
            if (cov > a[i]) a[i] = cov;
        }

        public void Disc(float cx, float cy, float r) { Ellipse(cx, cy, r, r); }

        public void Ellipse(float cx, float cy, float rx, float ry)
        {
            int x0 = Mathf.FloorToInt(cx - rx - 2), x1 = Mathf.CeilToInt(cx + rx + 2);
            int y0 = Mathf.FloorToInt(cy - ry - 2), y1 = Mathf.CeilToInt(cy + ry + 2);
            float rm = Mathf.Min(rx, ry);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x - cx) / rx, ny = (y - cy) / ry;
                    float d = (Mathf.Sqrt(nx * nx + ny * ny) - 1f) * rm;
                    Put(x, y, Mathf.Clamp01(0.5f - d));
                }
        }

        public void Seg(float x0, float y0, float x1, float y1, float wd)
        {
            float r = wd * 0.5f;
            int bx0 = Mathf.FloorToInt(Mathf.Min(x0, x1) - r - 2), bx1 = Mathf.CeilToInt(Mathf.Max(x0, x1) + r + 2);
            int by0 = Mathf.FloorToInt(Mathf.Min(y0, y1) - r - 2), by1 = Mathf.CeilToInt(Mathf.Max(y0, y1) + r + 2);
            float dx = x1 - x0, dy = y1 - y0;
            float len2 = Mathf.Max(0.0001f, dx * dx + dy * dy);
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    float t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / len2);
                    float qx = x0 + dx * t, qy = y0 + dy * t;
                    float d = Mathf.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy)) - r;
                    Put(x, y, Mathf.Clamp01(0.5f - d));
                }
        }

        public void Curve(Vector2 p0, Vector2 c, Vector2 p1, float w0, float w1)
        {
            Vector2 prev = p0;
            const int N = 28;
            for (int i = 1; i <= N; i++)
            {
                float t = i / (float)N;
                Vector2 p = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * c + t * t * p1;
                Seg(prev.x, prev.y, p.x, p.y, Mathf.Lerp(w0, w1, t));
                prev = p;
            }
        }

        /// <summary>填滿一個多邊形（邊緣有抗鋸齒）。</summary>
        public void Poly(Vector2[] p)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < p.Length; i++)
            {
                minX = Mathf.Min(minX, p[i].x); maxX = Mathf.Max(maxX, p[i].x);
                minY = Mathf.Min(minY, p[i].y); maxY = Mathf.Max(maxY, p[i].y);
            }
            for (int y = Mathf.FloorToInt(minY) - 1; y <= Mathf.CeilToInt(maxY) + 1; y++)
                for (int x = Mathf.FloorToInt(minX) - 1; x <= Mathf.CeilToInt(maxX) + 1; x++)
                {
                    bool inside = false;
                    float best = float.MaxValue;
                    for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                    {
                        Vector2 a0 = p[j], b0 = p[i];
                        if (((b0.y > y) != (a0.y > y)) && (x < (a0.x - b0.x) * (y - b0.y) / (a0.y - b0.y) + b0.x)) inside = !inside;
                        float ex = b0.x - a0.x, ey = b0.y - a0.y;
                        float l2 = Mathf.Max(0.0001f, ex * ex + ey * ey);
                        float t = Mathf.Clamp01(((x - a0.x) * ex + (y - a0.y) * ey) / l2);
                        float dx = x - (a0.x + ex * t), dy = y - (a0.y + ey * t);
                        best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
                    }
                    Put(x, y, Mathf.Clamp01(0.5f + (inside ? best : -best)));
                }
        }

        /// <summary>fadeBottomPx＝底邊淡出幾像素；topFade＝最上面的濃度（1＝不漸層，0.75＝往上漸淡，有遠近的空氣感）。</summary>
        public Sprite ToSprite(float ppu, Vector2 pivot, int fadeBottomPx, float topFade)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float fade = fadeBottomPx > 0 ? Mathf.Clamp01(y / (float)fadeBottomPx) : 1f;
                fade *= Mathf.Lerp(1f, topFade, y / (float)h);
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(a[y * w + x] * fade * 255f), 0, 255));
            }
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, ppu);
        }
    }
}
