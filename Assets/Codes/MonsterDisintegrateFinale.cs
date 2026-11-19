using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1119c 玻璃館結局：書頭怪倒下後，肢體與身體逐漸碎裂成微小粒子 → 粒子聚成一顆沒有染色的白色光球
///        → 光球飛向鏡頭、越來越大，蓋住整個畫面變全白 → EndingFade（白色）載入結局漫畫、從白淡入。
///
/// 做法：
///   ・碎裂：把倒下姿勢的每個 SkinnedMeshRenderer 烘焙一次，頂點直接轉成世界座標，做成一個靜態網格（同材質、同 PropertyBlock），
///     原本的 SkinnedMeshRenderer 關掉。每個三角形一個「碎裂順序」：離身體中心（Spine1）越遠越早（＝四肢先碎、軀幹最後），
///     再加 Perlin 雜訊讓邊緣不規則。時間推進時，從排序好的三角形清單前面一段一段拿掉，拿掉的位置噴出粒子。
///   ・聚光：粒子先往外、往上飄散，再被吸向身體上方的一點，顏色從暗紅餘燼漸漸變白；吸進去的粒子越多，白光球越大越亮。
///   ・白光球：複製場景裡的陪伴光球（GuidanceLight 的外觀：動畫圖、點光源、背後光暈），拿掉行為腳本；光暈與光源改成白色，光球圖本身不改色。
///   ・飛向鏡頭：正交鏡頭沒有「近大遠小」，用「往畫面中心移動＋指數放大」模擬靠近，放大到超過畫面對角線；
///     途中呼叫 EndingFade（白色）把畫面淡成全白，再載入結局漫畫。
/// 由 ShadowMonsterController.DeathFinaleSequence 呼叫 Play()。
/// </summary>
[DisallowMultipleComponent]
public class MonsterDisintegrateFinale : MonoBehaviour
{
    [Header("碎裂")]
    [Tooltip("從第一片碎掉到整隻碎完的秒數")]
    public float crumbleSeconds = 3.2f;
    [Tooltip("整隻怪物最多噴出幾顆粒子")]
    public int maxParticles = 2400;
    [Range(0f, 0.6f), Tooltip("碎裂邊緣的不規則程度（0＝照距離整齊地一圈一圈碎）")]
    public float crumbleNoise = 0.28f;
    [Tooltip("粒子大小（畫面高度的比例，最小～最大）")]
    public Vector2 particleScreenSize = new Vector2(0.0035f, 0.009f);
    [Tooltip("粒子剛碎出來的顏色（暗紅餘燼～亮一點的餘燼），之後漸漸變白")]
    public Color emberDark = new Color(0.38f, 0.08f, 0.1f, 1f);
    public Color emberBright = new Color(0.9f, 0.42f, 0.32f, 1f);

    [Header("聚成白色光球")]
    [Tooltip("粒子碎出來後先自由飄多久，才開始被吸向光球")]
    public float driftSeconds = 0.55f;
    [Tooltip("光球在身體中心上方多高（怪物身體大小的比例）")]
    public float orbLift = 0.25f;
    [Tooltip("光球聚好時的直徑（畫面高度的比例）")]
    public float orbScreenFraction = 0.12f;
    [Tooltip("聚好之後在怪物上方上下漂浮多久，才飛去繞她")]
    public float orbHoverSeconds = 2f;
    [Tooltip("漂浮時上下擺動的幅度（畫面高度的比例）")]
    public float orbBobHeight = 0.02f;
    [Tooltip("漂浮時上下擺動一次的秒數")]
    public float orbBobPeriod = 1f;
    [Tooltip("光球的暈光（白色柔光）貼圖直徑＝光球圖框的幾倍。亮度是高斯衰減、尾巴很長，看起來只比光球突出一點，邊緣沒有線條")]
    public float glowSizeMultiplier = 1.8f;
    [Range(0.15f, 0.6f), Tooltip("柔光的擴散程度（貼圖半徑的比例）：越小越集中在光球周圍")]
    public float glowSpread = 0.3f;

    [Header("飛到她身邊繞圈")]
    [Tooltip("從空中飛到她身邊、繞完所有圈數的總秒數（0＝不繞，直接飛向鏡頭）")]
    public float circlePlayerSeconds = 6.5f;
    [Tooltip("繞幾圈")]
    public float circleTurns = 2f;
    [Tooltip("繞圈時光球邊緣離她多遠（公尺）")]
    public float circleGap = 0.8f;
    [Range(0f, 0.6f), Tooltip("水平圓從鏡頭看的扁平程度：0＝正側面看成一條橫線，越大越像從斜上方看（後半圈較高、前半圈較低）")]
    public float circleTilt = 0.1f;
    [Range(0f, 1f), Tooltip("繞圈的高度：0＝她的腳、1＝她的頭頂")]
    public float circleHeight = 0.55f;
    [Range(0f, 0.4f), Tooltip("近大遠小：繞到她前面時放大、繞到她後面時縮小的比例")]
    public float circleDepthScale = 0.15f;

    [Header("繞完 → 畫一個大圓繞到畫面左邊")]
    [Tooltip("繞完她之後，沿著一個大圓弧繞到畫面左邊的秒數（0＝不繞過去，直接從她身邊飛向鏡頭）")]
    public float toLeftSeconds = 2.6f;
    [Tooltip("繞到畫面左邊後停一下的秒數（輕輕上下漂）")]
    public float leftHoldSeconds = 0.4f;
    [Tooltip("畫面左邊的落點（畫面座標 0～1：x 0＝最左、y 0＝最下）")]
    public Vector2 leftScreenPoint = new Vector2(0.2f, 0.5f);
    [Tooltip("大圓弧往上拱多高：1＝正圓（上下半徑＝左右半徑）；畫面放不下時會自動壓扁到不超出畫面頂端")]
    public float bigCircleRoundness = 1f;
    [Tooltip("大圓弧從上方繞過去（關掉＝從下方繞）")]
    public bool bigCircleOverTop = true;
    [Range(0f, 0.9f), Tooltip("大圓弧最高（或最低）點離畫面邊緣至少多遠（畫面比例）")]
    public float bigCircleScreenLimit = 0.9f;
    [Range(0f, 0.5f), Tooltip("近大遠小：繞到大圓最遠處時縮小的比例（像在遠處繞一大圈再回來）")]
    public float bigCircleDepthScale = 0.25f;
    [Range(0f, 1f), Tooltip("從左邊逼近鏡頭時往畫面中央靠多少：0＝停在左邊直接變大、1＝移到正中央")]
    public float approachCenterPull = 0.35f;

    [Header("飛向鏡頭 → 全白 → 結局漫畫")]
    [Tooltip("光球從空中飛到蓋滿鏡頭的秒數")]
    public float flySeconds = 2.6f;
    [Tooltip("飛到鏡頭前時光球直徑＝畫面對角線的幾倍。光球只有正中心是白的、外圈帶橘黃，要夠大才會只剩白色中心蓋滿畫面")]
    public float flyEndScreenCover = 14f;
    [Tooltip("開始飛向鏡頭後第幾秒開始把畫面淡成白色；比飛行秒數長的話，光球會先停在鏡頭前蓋住畫面，等到這個時間才淡白")]
    public float whiteStartSeconds = 3.3f;
    [Tooltip("淡成全白的秒數（白光淡入的長度）")]
    public float whiteFadeSeconds = 0.45f;
    [Tooltip("全白停留秒數（結局漫畫在這段時間載入）")]
    public float whiteHoldSeconds = 0.8f;
    [Tooltip("從全白淡入結局漫畫的秒數")]
    public float whiteFadeInSeconds = 2.0f;

    /// <summary>測試／驗證用：0 碎裂 1 聚光 2 停留 3 繞她一圈 4 飛向鏡頭 5 結束</summary>
    public int Phase { get; private set; } = -1;
    public float CrumbleProgress { get; private set; }
    public int ParticlesEmitted { get; private set; }
    public int ParticlesAbsorbed { get; private set; }
    public Transform Orb => _orb;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<Mesh> _meshes = new List<Mesh>();
    private Transform _orb;
    private readonly List<SpriteRenderer> _orbSprites = new List<SpriteRenderer>();
    private readonly List<float> _orbSpriteAlpha = new List<float>();
    private SpriteRenderer _glow;
    private Light _orbLight;
    private float _orbLightBase = 1f;
    private float _orbBaseScale = 1f;
    private Vector3 _orbOffset, _orbCenter;
    private float _orbDrawZ;   // 縮放 1 時主圖中心相對物件原點的位移；光球邏輯中心
    private AudioClip _orbSfx;
    private float _orbSfxVol = 0.8f;

    private class Piece
    {
        public Mesh mesh;
        public List<int> sortedTris;   // 依碎裂順序排好的三角形索引（每 3 個一組）
        public float[] keys;           // 每個三角形的碎裂順序（0～1，小的先碎）
        public Vector3[] centroids;
        public int removed;            // 前面已經碎掉幾個三角形
    }

    /// <param name="setMonsterAlpha">怪物身上的紅光、眼睛紅光等跟著淡掉（ShadowMonsterController.SetVisualAlpha）</param>
    public IEnumerator Play(Action<float> setMonsterAlpha, string endingScene, Camera cam)
    {
        if (cam == null) cam = Camera.main;
        float camH = CamHalfHeight(cam) * 2f;

        // ── 1. 烘焙成靜態網格 ──
        var smrs = new List<SkinnedMeshRenderer>();
        foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>())
            if (s.enabled && s.gameObject.activeInHierarchy && s.sharedMesh != null) smrs.Add(s);

        Vector3 center = BodyCenter(smrs);
        var pieces = new List<Piece>();
        float maxD = 0.01f;
        foreach (var smr in smrs)
        {
            Piece p = BakePiece(smr);
            if (p == null) continue;
            pieces.Add(p);
            foreach (var c in p.centroids) maxD = Mathf.Max(maxD, Vector2.Distance(c, center));
        }
        foreach (var smr in smrs) smr.enabled = false;
        int totalTris = 0;
        float nf = 1f / Mathf.Max(0.01f, maxD * 0.22f);   // 雜訊頻率：大約一隻手臂的寬度一個起伏
        foreach (var p in pieces)
        {
            int n = p.centroids.Length;
            p.keys = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 c = p.centroids[i];
                float d = Vector2.Distance(c, center) / maxD;
                float noise = (Mathf.PerlinNoise(c.x * nf + 17.3f, c.y * nf + 4.1f) - 0.5f) * 2f * crumbleNoise;
                p.keys[i] = (1f - d) + noise + UnityEngine.Random.Range(-0.03f, 0.03f);
            }
            totalTris += n;
        }
        float kMin = float.MaxValue, kMax = float.MinValue;
        foreach (var p in pieces) foreach (var k in p.keys) { kMin = Mathf.Min(kMin, k); kMax = Mathf.Max(kMax, k); }
        float kRange = Mathf.Max(0.0001f, kMax - kMin);
        foreach (var p in pieces)
        {
            for (int i = 0; i < p.keys.Length; i++) p.keys[i] = (p.keys[i] - kMin) / kRange;
            SortPiece(p);
        }
        Debug.Log($"【書頭怪碎裂】★1119c 開始碎裂：{pieces.Count} 個網格、{totalTris} 個三角形、身體半徑 {maxD:F1}m、鏡頭高 {camH:F1}m");

        // ── 2. 粒子系統（手動控制：只用 Emit，每幀改速度與顏色）──
        float orbZ = transform.position.z;
        var ctrl = GetComponent<ShadowMonsterController>();
        if (ctrl != null && ctrl.player != null) orbZ = ctrl.player.position.z;
        Vector3 orbPos = new Vector3(center.x, center.y + maxD * orbLift, orbZ);
        // 光球別飛出畫面外（上緣留一點空間）
        if (cam != null)
        {
            float top = cam.transform.position.y + camH * 0.5f - camH * orbScreenFraction;
            orbPos.y = Mathf.Min(orbPos.y, top);
        }

        ParticleSystem ps = BuildParticles();
        var buf = new ParticleSystem.Particle[Mathf.Max(64, maxParticles + 64)];
        float emitChance = totalTris > 0 ? Mathf.Clamp01(maxParticles / (float)totalTris) : 0f;
        float sizeMin = particleScreenSize.x * camH, sizeMax = particleScreenSize.y * camH;

        // 光球放在所有東西的前面（她是 3D 模型、會寫深度，光球跟她同一個 z 會被她擋住）
        //   粒子仍在原本的深度被吸過去（orbPos），只有光球的畫面位置用這個 z
        _orbDrawZ = cam != null ? cam.transform.position.z + Mathf.Max(cam.nearClipPlane + 0.5f, 3f) * (cam.transform.forward.z >= 0f ? 1f : -1f) : orbPos.z;
        BuildOrb(orbPos, camH, cam);
        float orbVisual = 0f;

        // ── 3. 碎裂＋聚光 ──
        Phase = 0;
        float t = 0f;
        float absorbR = Mathf.Max(0.2f, orbScreenFraction * camH * 0.5f);
        float gatherEnd = crumbleSeconds + driftSeconds + 4f;   // 保底：最多再等 4 秒就強制收完
        bool crumbleDone = false;
        while (true)
        {
            float dt = Time.deltaTime;
            t += dt;

            // 3a. 碎裂進度：前段慢、中段快（緩入），最後一點點軀幹
            float prog = Mathf.Clamp01(t / Mathf.Max(0.1f, crumbleSeconds));
            CrumbleProgress = prog;
            float threshold = prog < 1f ? Mathf.SmoothStep(0f, 1f, prog) * 1.001f : 1.01f;
            foreach (var p in pieces)
            {
                int n = p.keys.Length, k = p.removed;
                while (k < n && p.keys[k] <= threshold)
                {
                    if (UnityEngine.Random.value < emitChance && ParticlesEmitted < maxParticles)
                    {
                        Vector3 c = p.centroids[k];
                        Vector3 outDir = (c - center); outDir.z = 0f;
                        outDir = outDir.sqrMagnitude > 0.0001f ? outDir.normalized : Vector3.up;
                        var ep = new ParticleSystem.EmitParams
                        {
                            position = new Vector3(c.x, c.y, Mathf.Lerp(c.z, orbZ, 0.5f)),
                            velocity = (outDir * UnityEngine.Random.Range(0.05f, 0.18f) + Vector3.up * UnityEngine.Random.Range(0.04f, 0.14f)) * maxD
                                       + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.05f * maxD),
                            startSize = UnityEngine.Random.Range(sizeMin, sizeMax),
                            startLifetime = 100f,
                            randomSeed = (uint)UnityEngine.Random.Range(1, int.MaxValue),
                        };
                        ps.Emit(ep, 1);
                        ParticlesEmitted++;
                    }
                    k++;
                }
                if (k != p.removed)
                {
                    p.removed = k;
                    ApplyRemaining(p);
                }
            }
            if (setMonsterAlpha != null) setMonsterAlpha(Mathf.Clamp01(1f - prog * 1.4f));
            foreach (var smr in smrs) if (smr != null && smr.enabled) smr.enabled = false;   // SetVisualAlpha 會把它們打開
            if (prog >= 1f && !crumbleDone)
            {
                crumbleDone = true;
                Phase = 1;
                foreach (var p in pieces) if (p.mesh != null) p.mesh.Clear();
                Debug.Log($"【書頭怪碎裂】★1119c 碎完（{t:F2}s），粒子 {ParticlesEmitted} 顆，已聚 {ParticlesAbsorbed}");
            }

            // 3b. 粒子：先飄散 → 被吸向光球，顏色漸白
            int count = ps.GetParticles(buf);
            bool force = t > gatherEnd;
            for (int i = 0; i < count; i++)
            {
                var q = buf[i];
                if (q.remainingLifetime <= 0.001f) continue;
                float age = q.startLifetime - q.remainingLifetime;
                Vector3 to = orbPos - q.position;
                float dist = to.magnitude;
                if (dist < absorbR || force)
                {
                    q.remainingLifetime = 0f;
                    ParticlesAbsorbed++;
                    buf[i] = q;
                    continue;
                }
                float w = Mathf.SmoothStep(0f, 1f, (age - driftSeconds) / 1.3f);
                Vector3 dir = to / dist;
                Vector3 tangent = new Vector3(-dir.y, dir.x, 0f);
                float speed = Mathf.Lerp(0.25f, 1.6f, w) * maxD + dist * 0.6f * w;
                Vector3 desired = dir * speed + tangent * (0.35f * maxD * (1f - w));   // 一開始繞著轉，越靠近越直直被吸進去
                Vector3 v = q.velocity;
                v *= Mathf.Exp(-dt * 0.8f * (1f - w));                               // 飄散時慢慢減速
                v = Vector3.Lerp(v, desired, 1f - Mathf.Exp(-dt * 5f * w));          // 被吸過去
                q.velocity = v;
                float white = Mathf.Clamp01(w * 1.1f);
                Color ember = Color.Lerp(emberDark, emberBright, (q.randomSeed % 10007u) / 10007f);
                q.startColor = Color.Lerp(ember, Color.white, white);
                if (white > 0.6f) q.startSize = Mathf.MoveTowards(q.startSize, sizeMin * 0.8f, dt * sizeMax);
                buf[i] = q;
            }
            ps.SetParticles(buf, count);

            // 3c. 光球跟著吸進去的粒子長大
            float frac = ParticlesEmitted > 0 ? ParticlesAbsorbed / (float)Mathf.Max(1, ParticlesEmitted) : (crumbleDone ? 1f : 0f);
            float target = crumbleDone ? Mathf.SmoothStep(0f, 1f, frac) : Mathf.SmoothStep(0f, 1f, frac) * Mathf.Clamp01(prog * 1.2f);
            if (ParticlesAbsorbed > 0 || crumbleDone) orbVisual = Mathf.MoveTowards(orbVisual, Mathf.Max(orbVisual, target), dt * 1.2f);
            SetOrbLook(orbVisual, orbPos, 1f, t);

            if (crumbleDone && ps.particleCount == 0 && count == 0) break;
            yield return null;
        }

        // 收尾：光球長到滿
        while (orbVisual < 1f)
        {
            t += Time.deltaTime;
            orbVisual = Mathf.MoveTowards(orbVisual, 1f, Time.deltaTime * 1.5f);
            SetOrbLook(orbVisual, orbPos, 1f, t);
            yield return null;
        }
        foreach (var p in pieces) if (p.mesh != null) p.mesh.Clear();
        if (setMonsterAlpha != null) setMonsterAlpha(0f);
        Debug.Log($"【書頭怪碎裂】★1119c 白色光球聚好（{t:F2}s），粒子 {ParticlesEmitted} 顆全部吸進去");
        if (_orbSfx != null)
            AudioSource.PlayClipAtPoint(_orbSfx, cam != null ? cam.transform.position : orbPos, AudioManager.ScaleSfx(_orbSfxVol));

        // ── 4. 停在空中一下（同時把鏡頭拉開，留出她身邊繞圈的空間）──
        Phase = 2;
        Transform pl = ctrl != null ? ctrl.player : null;
        float orbD = orbScreenFraction * camH;
        Bounds pb = PlayerBounds(pl);
        float circleR = pb.extents.x + orbD * 0.6f + circleGap;   // 水平圓：半徑看她的寬度
        if (ctrl != null && pl != null && circlePlayerSeconds > 0f)
            ctrl.SetFinaleFrameExtra(pb.center.x - pl.position.x + circleR + orbD * 0.7f + 1.5f - 4f,
                                     pb.center.y - pl.position.y + circleR + orbD * 0.7f + 1.5f - 8f);
        for (float h = 0f; h < orbHoverSeconds; h += Time.deltaTime)
        {
            t += Time.deltaTime;
            // 上下漂浮：幅度在開頭 0.4 秒淡入、最後 0.4 秒淡出，起飛時剛好回到原位
            float amp = orbBobHeight * camH * Mathf.SmoothStep(0f, 1f, h / 0.4f) * Mathf.SmoothStep(0f, 1f, (orbHoverSeconds - h) / 0.4f);
            float bob = Mathf.Sin(h * Mathf.PI * 2f / Mathf.Max(0.2f, orbBobPeriod)) * amp;
            SetOrbLook(1f, orbPos + Vector3.up * bob, 1f + 0.1f * Mathf.Sin(h * Mathf.PI / Mathf.Max(0.1f, orbHoverSeconds)), t);
            yield return null;
        }

        // ── 5. 飛到她身邊繞一圈：圓在水平面上（像行星環），繞到她後面時被她擋住、繞到前面時蓋在她身上 ──
        //   從離光球較近的那一側切進圓，先往她身後繞，再從她前面繞回同一側
        Phase = 3;
        if (pl != null && circlePlayerSeconds > 0f)
        {
            Vector3 p0 = _orbCenter;
            float side = p0.x >= pb.center.x ? 1f : -1f;           // 從右邊切進：a0＝0；左邊：a0＝π
            float a0 = side > 0f ? 0f : Mathf.PI;
            float dirSign = side;                                   // 讓 sin(a) 先變正＝先繞到她身後
            float fwd = cam != null && cam.transform.forward.z < 0f ? -1f : 1f;
            float total = Mathf.Max(0.5f, circlePlayerSeconds);
            Debug.Log($"【書頭怪碎裂】★1119e 光球繞她一圈（水平圓，半徑 {circleR:F1}m，{total:F1}s，鏡頭{(cam != null && cam.orthographic ? "正交" : "透視")}）");
            for (float f = 0f; f < total; f += Time.deltaTime)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(f / total);
                // ★1119g 後面接著繞大圓到畫面左邊時，結尾不減速（不然會在她身邊停一下才出發，看起來像兩段）
                float e = toLeftSeconds > 0f && cam != null ? u * u * (2f - u) : u * u * (3f - 2f * u);
                float a = a0 + dirSign * Mathf.PI * 2f * circleTurns * e;
                Bounds b = PlayerBounds(pl);
                float depth = Mathf.Sin(a);                          // >0 在她後面、<0 在她前面
                Vector3 ring = new Vector3(b.center.x + Mathf.Cos(a) * circleR,
                                           b.min.y + b.size.y * circleHeight + depth * circleR * circleTilt,
                                           p0.z);
                float k = Mathf.SmoothStep(0f, 1f, u / 0.3f);        // 前 3 成從空中滑進圓上
                Vector3 pos = Vector3.Lerp(p0, ring, k);
                float near = 1f - circleDepthScale * depth * k;
                // 在她後面：畫在她身體後方一點點（她是 3D 模型會寫深度，自然把光球擋住）；在前面：畫在鏡頭前
                float? drawZ = depth > 0f ? b.center.z + (b.extents.z + 0.15f) * fwd : (float?)null;
                SetOrbLook(1f, pos, near, t, drawZ);
                yield return null;
            }
        }

        float aspect = cam != null ? Mathf.Max(0.5f, cam.aspect) : 1.78f;

        // ── 5b. ★1119g 繞完她之後，沿著一個大圓弧繞到畫面左邊（畫在鏡頭前），停一下，再從那裡逼近鏡頭 ──
        //   她在畫面右邊：光球從她身邊出發，往上（或往下）拱成半個大圓，落到畫面左邊。
        //   大圓的左右半徑＝起點到落點距離的一半，上下半徑照 bigCircleRoundness，畫面放不下就壓扁到不超出畫面。
        if (toLeftSeconds > 0f && cam != null)
        {
            Vector3 from = _orbCenter;
            float total = Mathf.Max(0.2f, toLeftSeconds);
            float ySign = bigCircleOverTop ? 1f : -1f;
            float dist = from.z - cam.transform.position.z;
            for (float f = 0f; f < total + Mathf.Max(0f, leftHoldSeconds); f += Time.deltaTime)
            {
                t += Time.deltaTime;
                // 鏡頭可能還在動，每幀重算畫面左邊、畫面頂端在哪
                Vector3 to = cam.ViewportToWorldPoint(new Vector3(leftScreenPoint.x, leftScreenPoint.y, dist)); to.z = from.z;
                float edgeY = cam.ViewportToWorldPoint(new Vector3(0.5f, bigCircleOverTop ? bigCircleScreenLimit : 1f - bigCircleScreenLimit, dist)).y;
                float rx = (from.x - to.x) * 0.5f;                     // 從右往左＝正
                float midY = (from.y + to.y) * 0.5f;
                float ry = Mathf.Abs(rx) * Mathf.Max(0f, bigCircleRoundness);
                ry = Mathf.Max(0f, Mathf.Min(ry, (edgeY - midY) * ySign));   // 最高（低）點在弧的正中間，那時高度基準剛好是 midY
                Vector3 pos; float near = 1f;
                if (f < total)
                {
                    float u = f / total;
                    float e = 0.5f * u * (2f - u) + 0.5f * u * u * (3f - 2f * u);   // 起步就有速度（接續繞圈），到左邊時慢慢停
                    float th = e * Mathf.PI;                            // 0＝起點（右）→ π＝落點（左）
                    float cx = (from.x + to.x) * 0.5f + Mathf.Cos(th) * rx;
                    float baseY = Mathf.Lerp(from.y, to.y, e);
                    pos = new Vector3(cx, baseY + ySign * Mathf.Sin(th) * ry, from.z);
                    near = 1f - bigCircleDepthScale * Mathf.Sin(th);     // 繞到大圓最遠處時小一點
                }
                else
                {
                    float h = f - total;
                    float amp = orbBobHeight * camH * Mathf.SmoothStep(0f, 1f, h / 0.25f);
                    pos = to + Vector3.up * (Mathf.Sin(h * Mathf.PI * 2f / Mathf.Max(0.2f, orbBobPeriod)) * amp);
                }
                SetOrbLook(1f, pos, near, t);
                yield return null;
            }
        }

        // ── 6. 飛向鏡頭：從目前位置往畫面中央靠一點、指數放大到超過畫面對角線；途中畫面淡成白色 ──
        Phase = 4;
        float diag = camH * Mathf.Sqrt(1f + aspect * aspect);
        float growTo = Mathf.Max(2f, diag * Mathf.Max(1f, flyEndScreenCover) / Mathf.Max(0.01f, orbScreenFraction * camH));
        bool whiteStarted = false;
        Vector3 start = _orbCenter;
        float fly = Mathf.Max(0.3f, flySeconds);
        bool wantWhite = !string.IsNullOrEmpty(endingScene);
        // 飛完之後若還沒到淡白的時間，光球就停在鏡頭前（蓋滿畫面、繼續脈動）等
        for (float f = 0f; f < fly || (wantWhite && !whiteStarted); f += Time.deltaTime)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(f / fly);
            Vector3 camC = cam != null ? cam.transform.position : start;
            float mu = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.35f));
            // ★1119g 原本一路移到畫面正中央；現在從左邊逼近，只往中央靠 approachCenterPull（光球會大到蓋滿整個畫面，不用置中）
            Vector3 pos = Vector3.Lerp(start, new Vector3(camC.x, camC.y, start.z), mu * (toLeftSeconds > 0f ? approachCenterPull : 1f));
            float grow = Mathf.Pow(growTo, Mathf.Pow(u, 2.2f));   // 一開始慢、越靠近鏡頭放大越快
            if (f > fly) grow *= 1f + (f - fly) * 0.6f;           // 飛完還沒淡白：繼續往鏡頭貼近
            SetOrbLook(1f, pos, grow, t);
            if (wantWhite && !whiteStarted && f >= whiteStartSeconds)
            {
                whiteStarted = true;
                float fadeOut = Mathf.Max(whiteFadeSeconds, fly - f + 0.15f);
                EndingFade.Begin(endingScene, fadeOut, whiteHoldSeconds, whiteFadeInSeconds, Color.white);
                Debug.Log($"【書頭怪碎裂】★1119f 飛向鏡頭第 {f:F2}s，畫面 {fadeOut:F2}s 淡成全白 → 載入 {endingScene}");
            }
            yield return null;
        }
        Phase = 5;
        Debug.Log("【書頭怪碎裂】★1119c 光球蓋滿鏡頭");
    }

    // ───────── 碎裂網格 ─────────

    private Piece BakePiece(SkinnedMeshRenderer smr)
    {
        // 用骨架逐頂點手動蒙皮（bone.localToWorld × bindpose），直接得到世界座標。
        // BakeMesh 在這隻 Mixamo 骨架上的縮放座標對不上（實測 useScale 兩種都偏），網格不能讀時才退回 BakeMesh。
        Mesh src = smr.sharedMesh;
        Mesh baked = null;
        Vector3[] v, nrm; Vector4[] tan;
        Transform[] bones = smr.bones;
        if (src.isReadable && bones != null && bones.Length > 0 && src.bindposes.Length == bones.Length && src.boneWeights.Length == src.vertexCount)
        {
            Matrix4x4[] bind = src.bindposes;
            var skin = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++)
                skin[b] = bones[b] != null ? bones[b].localToWorldMatrix * bind[b] : smr.transform.localToWorldMatrix;
            BoneWeight[] bw = src.boneWeights;
            v = src.vertices; nrm = src.normals; tan = src.tangents;
            bool hasN = nrm.Length == v.Length, hasT = tan.Length == v.Length;
            for (int i = 0; i < v.Length; i++)
            {
                BoneWeight w = bw[i];
                Vector3 p0 = v[i];
                Vector3 pw = skin[w.boneIndex0].MultiplyPoint3x4(p0) * w.weight0 + skin[w.boneIndex1].MultiplyPoint3x4(p0) * w.weight1
                           + skin[w.boneIndex2].MultiplyPoint3x4(p0) * w.weight2 + skin[w.boneIndex3].MultiplyPoint3x4(p0) * w.weight3;
                float ws = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                v[i] = ws > 0.0001f ? pw / ws : smr.transform.TransformPoint(p0);
                if (hasN)
                {
                    Vector3 n0 = nrm[i];
                    nrm[i] = (skin[w.boneIndex0].MultiplyVector(n0) * w.weight0 + skin[w.boneIndex1].MultiplyVector(n0) * w.weight1
                            + skin[w.boneIndex2].MultiplyVector(n0) * w.weight2 + skin[w.boneIndex3].MultiplyVector(n0) * w.weight3).normalized;
                }
                if (hasT)
                {
                    Vector3 t0 = new Vector3(tan[i].x, tan[i].y, tan[i].z);
                    Vector3 tw = (skin[w.boneIndex0].MultiplyVector(t0) * w.weight0 + skin[w.boneIndex1].MultiplyVector(t0) * w.weight1).normalized;
                    tan[i] = new Vector4(tw.x, tw.y, tw.z, tan[i].w);
                }
            }
        }
        else
        {
            baked = new Mesh();
            smr.BakeMesh(baked, true);
            src = baked;
            Transform tr = smr.transform;
            Matrix4x4 m = Matrix4x4.TRS(tr.position, tr.rotation, Vector3.one);
            v = baked.vertices; nrm = baked.normals; tan = baked.tangents;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < nrm.Length; i++) nrm[i] = m.MultiplyVector(nrm[i]).normalized;
            Debug.LogWarning($"【書頭怪碎裂】{smr.name}：網格不能讀，改用 BakeMesh（位置可能不準）");
        }
        if (v.Length == 0) { if (baked != null) Destroy(baked); return null; }

        var mesh = new Mesh { name = smr.name + "（碎裂中）", indexFormat = src.indexFormat };
        mesh.vertices = v;
        if (nrm.Length == v.Length) mesh.normals = nrm;
        if (tan.Length == v.Length) mesh.tangents = tan;
        var uv = new List<Vector2>(); src.GetUVs(0, uv); if (uv.Count == v.Length) mesh.SetUVs(0, uv);
        var uv1 = new List<Vector2>(); src.GetUVs(1, uv1); if (uv1.Count == v.Length) mesh.SetUVs(1, uv1);
        var col = src.colors32; if (col.Length == v.Length) mesh.colors32 = col;
        mesh.subMeshCount = src.subMeshCount;

        // 每個子網格的三角形各自排序；為了一起推進，攤平成「(子網格, 三角形)」一條清單
        var flat = new List<int>();    // 每組 4 個：子網格、i0、i1、i2
        for (int s = 0; s < src.subMeshCount; s++)
        {
            int[] tri = src.GetTriangles(s);
            for (int i = 0; i + 2 < tri.Length; i += 3) { flat.Add(s); flat.Add(tri[i]); flat.Add(tri[i + 1]); flat.Add(tri[i + 2]); }
            mesh.SetTriangles(tri, s, false);
        }
        mesh.RecalculateBounds();
        if (baked != null) Destroy(baked);
        _meshes.Add(mesh);

        int n = flat.Count / 4;
        var piece = new Piece { mesh = mesh, sortedTris = flat, centroids = new Vector3[n] };
        for (int i = 0; i < n; i++)
            piece.centroids[i] = (v[flat[i * 4 + 1]] + v[flat[i * 4 + 2]] + v[flat[i * 4 + 3]]) / 3f;

        var go = new GameObject("書頭怪_碎裂中（自動生成）");
        go.layer = smr.gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = smr.sharedMaterials;
        mr.shadowCastingMode = smr.shadowCastingMode;
        mr.receiveShadows = smr.receiveShadows;
        mr.lightProbeUsage = smr.lightProbeUsage;
        mr.sortingLayerID = smr.sortingLayerID;
        mr.sortingOrder = smr.sortingOrder;
        var mpb = new MaterialPropertyBlock();
        smr.GetPropertyBlock(mpb);
        mr.SetPropertyBlock(mpb);
        _spawned.Add(go);
        return piece;
    }

    // sortedTris 每 4 個一組；排序後 keys／centroids 也跟著重排，索引 k 對應第 k 組
    private static void SortPiece(Piece p)
    {
        int n = p.keys.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        float[] keys = p.keys;
        Array.Sort(order, (a, b) => keys[a].CompareTo(keys[b]));
        var tris = new List<int>(p.sortedTris.Count);
        var nk = new float[n];
        var nc = new Vector3[n];
        for (int j = 0; j < n; j++)
        {
            int i = order[j];
            tris.Add(p.sortedTris[i * 4]); tris.Add(p.sortedTris[i * 4 + 1]); tris.Add(p.sortedTris[i * 4 + 2]); tris.Add(p.sortedTris[i * 4 + 3]);
            nk[j] = keys[i]; nc[j] = p.centroids[i];
        }
        p.sortedTris = tris; p.keys = nk; p.centroids = nc;
    }

    private void ApplyRemaining(Piece p)
    {
        int subs = p.mesh.subMeshCount;
        var lists = new List<int>[subs];
        for (int s = 0; s < subs; s++) lists[s] = new List<int>();
        int n = p.keys.Length;
        for (int k = p.removed; k < n; k++)
        {
            int s = p.sortedTris[k * 4];
            var l = lists[s];
            l.Add(p.sortedTris[k * 4 + 1]); l.Add(p.sortedTris[k * 4 + 2]); l.Add(p.sortedTris[k * 4 + 3]);
        }
        for (int s = 0; s < subs; s++) p.mesh.SetTriangles(lists[s], s, false);
    }

    private Vector3 BodyCenter(List<SkinnedMeshRenderer> smrs)
    {
        foreach (var tr in GetComponentsInChildren<Transform>())
            if (tr.name == "mixamorig:Spine1") return tr.position;
        if (smrs.Count == 0) return transform.position;
        Bounds b = smrs[0].bounds;
        foreach (var s in smrs) b.Encapsulate(s.bounds);
        return b.center;
    }

    // ───────── 粒子 ─────────

    private ParticleSystem BuildParticles()
    {
        var go = new GameObject("書頭怪_碎裂粒子（自動生成）");
        _spawned.Add(go);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = 100f;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(64, maxParticles + 64);
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var psr = go.GetComponent<ParticleSystemRenderer>();
        Material mat = MonsterBloodLook.FxMaterial();
        if (mat != null) psr.sharedMaterial = mat;
        psr.sortingOrder = 30000;
        ps.Play();
        return ps;
    }

    // ───────── 白色光球 ─────────

    private void BuildOrb(Vector3 pos, float camH, Camera cam)
    {
        GuidanceLight src = null;
        foreach (var g in FindObjectsByType<GuidanceLight>(FindObjectsInactive.Include))
        {
            if (g == null) continue;
            if (src == null || (!src.gameObject.activeInHierarchy && g.gameObject.activeInHierarchy)) src = g;
        }

        GameObject orb;
        if (src != null)
        {
            _orbSfx = src.absorbSFX; _orbSfxVol = src.sfxVolume;
            // 在關著的父物件底下複製：GuidanceLight 等行為腳本不會跑 Awake，拿掉後才打開
            var holder = new GameObject("holder");
            holder.SetActive(false);
            orb = Instantiate(src.gameObject, holder.transform);
            // 行為腳本全拿掉；背後光暈（GuidanceLightBackdrop）也不要：它在短距離內淡到 0，深色背景上看得出一圈邊線，改由下面的高斯柔光負責
            foreach (var mb in orb.GetComponentsInChildren<MonoBehaviour>(true))
                DestroyImmediate(mb);
            orb.transform.SetParent(null, true);
            Destroy(holder);
            orb.name = "白色光球（自動生成）";
            orb.SetActive(true);
            // 陪伴光球的拖尾粒子發射點不在光球圖中心（會在旁邊拖出一串、放大時變成大團），白光球不用
            foreach (var ps in orb.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null) r.enabled = false;
            }
            // 陪伴光球的點光源照在背景上，照射範圍的邊界會變成一圈看得出邊線的淡圓盤：白光球不用
            foreach (var l in orb.GetComponentsInChildren<Light>(true)) l.enabled = false;
        }
        else
        {
            orb = new GameObject("白色光球（自動生成）");
            var core = new GameObject("核心");
            core.transform.SetParent(orb.transform, false);
            var sr = core.AddComponent<SpriteRenderer>();
            sr.sprite = SoftDisc(0.55f);
            sr.sortingOrder = 30010;
            sr.sortingLayerID = TopLayer;
        }
        _spawned.Add(orb);
        _orb = orb.transform;
        _orb.position = pos;

        foreach (var sr in orb.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr.name.StartsWith("__")) continue;   // GuidanceLightBackdrop 自己生的光暈
            _orbSprites.Add(sr);
            _orbSpriteAlpha.Add(1f);
            sr.color = Color.white;
            sr.sortingLayerID = TopLayer;
            sr.sortingOrder = 30010;
        }

        // 外層白色柔光：讓光球是「白光」不是只有一顆圖，也負責飛近鏡頭時把畫面洗白
        var glowGo = new GameObject("白色光球_柔光");
        glowGo.transform.SetParent(_orb, false);
        glowGo.transform.localRotation = Quaternion.Inverse(_orb.rotation);
        _glow = glowGo.AddComponent<SpriteRenderer>();
        _glow.sprite = GaussianDisc(glowSpread);
        _glow.sortingLayerID = TopLayer;
        _glow.sortingOrder = 30005;
        _glow.color = new Color(1f, 1f, 1f, 0f);

        // 光球大小：量主圖的世界大小，換算成「直徑＝畫面高度 × orbScreenFraction」的縮放
        _orb.localScale = Vector3.one;
        float size = 0f;
        foreach (var sr in _orbSprites) if (sr.sprite != null) size = Mathf.Max(size, Mathf.Max(sr.bounds.size.x, sr.bounds.size.y));
        if (size <= 0.0001f) size = 1f;
        _orbBaseScale = orbScreenFraction * camH / size;
        SpriteRenderer main0 = null;
        foreach (var sr in _orbSprites) if (sr.sprite != null && (main0 == null || sr.bounds.size.sqrMagnitude > main0.bounds.size.sqrMagnitude)) main0 = sr;
        Vector3 sc = main0 != null ? main0.bounds.center : _orb.position;
        _orbOffset = sc - _orb.position; _orbOffset.z = 0f;
        _glow.transform.position = new Vector3(sc.x, sc.y, _orb.position.z);
        // 柔光是光球的子物件、貼圖直徑 1 單位：本地縮放固定＝倍率 × 主圖在縮放 1 時的大小，就會一直是光球的 glowSizeMultiplier 倍
        _glow.transform.localScale = Vector3.one * glowSizeMultiplier * size;
        _orb.localScale = Vector3.zero;
    }

    private void SetOrbLook(float formed, Vector3 pos, float grow, float t, float? drawZ = null)
    {
        if (_orb == null) return;
        _orbCenter = pos;
        float pulse = 1f + 0.05f * Mathf.Sin(t * 3.1f) * formed;
        float s = _orbBaseScale * Mathf.Lerp(0.08f, 1f, formed) * pulse * grow;
        _orb.localScale = Vector3.one * s;
        Vector3 rp = pos - _orbOffset * s; rp.z = drawZ ?? _orbDrawZ;
        _orb.position = rp;   // 光球主圖不在物件原點上：讓「主圖中心」落在 pos
        float a = Mathf.Clamp01(formed * 1.6f);
        foreach (var sr in _orbSprites) if (sr != null) { var c = Color.white; c.a = a; sr.color = c; }
        if (_glow != null)
        {
            var gc = Color.white; gc.a = Mathf.Clamp01(formed) * 0.6f + Mathf.Clamp01((grow - 1f) * 0.15f) * 0.4f;
            _glow.color = gc;
        }
        if (_orbLight != null) _orbLight.intensity = _orbLightBase * Mathf.Clamp01(formed) * Mathf.Min(4f, grow);
    }

    /// <summary>最上面的排序圖層：光球飛近鏡頭時要蓋過她和所有場景物件。</summary>
    private static int TopLayer
    {
        get { var ls = SortingLayer.layers; return ls.Length > 0 ? ls[ls.Length - 1].id : 0; }
    }

    private static Sprite _gauss; private static float _gaussSpread = -1f;
    /// <summary>
    /// 高斯衰減的白色柔光：alpha ＝ exp(−(r/spread)²)，再乘一點 (1−r) 讓貼圖邊緣剛好歸零。
    /// 用半精度浮點貼圖（RGBAHalf）：8 位元 alpha 最後一階 1/255 在線性色彩空間、深色背景上會被放大成一圈明顯的邊線。
    /// </summary>
    private static Sprite GaussianDisc(float spread)
    {
        if (_gauss != null && Mathf.Approximately(_gaussSpread, spread)) return _gauss;
        const int N = 256;
        bool half = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf);
        var tex = new Texture2D(N, N, half ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, true)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "白色光球柔光（高斯）" };
        var px = new Color[N * N];
        float sp = Mathf.Max(0.05f, spread);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = r >= 1f ? 0f : Mathf.Exp(-(r / sp) * (r / sp)) * Mathf.SmoothStep(0f, 1f, 1f - r);
                // 抖動：畫面緩衝是 8 位元，很淡的漸層到最後一階會在深色背景上連成一圈線；加一點三角分佈雜訊，讓那一階散成顆粒
                if (a > 0f)
                {
                    float n = Hash(x, y) + Hash(x + 7919, y + 104729) - 1f;   // −1～1 三角分佈
                    a = Mathf.Max(0f, a + n * DitherAmp * Mathf.Clamp01(a * 400f));
                }
                px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px);
        tex.Apply();
        _gauss = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);   // Tight 網格會把很淡的外緣切掉
        _gaussSpread = spread;
        if (!half) Debug.LogWarning("【書頭怪碎裂】這台機器不支援 RGBAHalf，柔光改用 8 位元貼圖（外緣可能看得出一圈）");
        return _gauss;
    }

    // 柔光乘上 0.6 的透明度後疊在背景上；1.5 階（/255）的抖動足夠打散 8 位元的最後一階
    private const float DitherAmp = 1.5f / 255f / 0.6f;
    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777215f;
    }

    private static Sprite _discHard, _discSoft;
    /// <summary>白色圓形柔光；core＝0 時從中心就開始漸淡，大於 0 時中心一圈實心。</summary>
    private static Sprite SoftDisc(float core)
    {
        bool hard = core > 0f;
        if (hard && _discHard != null) return _discHard;
        if (!hard && _discSoft != null) return _discSoft;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = r >= 1f ? 0f : (r <= core ? 1f : Mathf.Pow(1f - (r - core) / (1f - core), 2f));
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);   // 1 單位 ＝ 直徑
        if (hard) _discHard = sp; else _discSoft = sp;
        return sp;
    }

    /// <summary>她的外框（所有開著的 Renderer），量不到就用位置往上 1.5 公尺。</summary>
    private static Bounds PlayerBounds(Transform pl)
    {
        if (pl == null) return new Bounds(Vector3.zero, Vector3.one * 3f);
        bool any = false; Bounds b = new Bounds(pl.position + Vector3.up * 1.5f, Vector3.one * 3f);
        foreach (var r in pl.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static float CamHalfHeight(Camera cam)
    {
        if (cam == null) return 10f;
        if (cam.orthographic) return cam.orthographicSize;
        return Mathf.Abs(cam.transform.position.z) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    private void OnDestroy()
    {
        foreach (var g in _spawned) if (g != null) Destroy(g);
        foreach (var m in _meshes) if (m != null) Destroy(m);
    }
}
