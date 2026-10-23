using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1023 荒原的光球：碰到才拿到「按住 S 石化硬撐」（M 10/23：鳥先直衝、後追蹤、越來越密，最難的一段之前拿到石化，
/// 得到光球、代入漫畫敘事）。
///
/// 做了什麼
///   ・荒原載入時在 orbX（預設 117，最後一座掩體 134.7 之前）生一顆光球（程式畫的光暈，不用素材、不改場景）。
///   ・拿到之前：按住 S 沒有作用（PlayerPetrification 看 BraceAllowed）；風只能靠岩柱躲，被風石化三次照樣重生。
///   ・碰到光球：她定住 → 光飛進她胸口 → 翻繪本 039（荒-9：光把她罩住、鳥撲不進來）→ 放開，從此按住 S 有效。
///   ・「按住 S」與鳥的線條畫放在光球之後（LineHintDirector 讀 GuideLeftX）；修毅的說明圖（躲岩柱、石化三次、鳥）留在原處。
///   ・鳥的節奏由 DesertBeatDirector 分四段：開頭只直衝 → x 45 起混進追蹤（預判）→ 拿到光球後追蹤變多 → x 180 起最密。
///   ・主選單的繪本跟著開（StoryProgress）：進荒原開到 035 → 走到 x 45（鳥開始追她）開到 038 → 拿到光球開到 039。
///   ・光球的高度＝地面＋她身高的 0.62（胸口）；光暈是金色外圈＋白色核心＋十字星芒＋繞圈的光點（★1023c）。
///
/// 要調：場景裡放一個空物件掛這支，改 Inspector；沒放就用這裡的預設值。
/// 關掉：Inspector 的 enableOrbGate 取消勾選＝回到原本（一進荒原就能按 S，提示在原位）。
/// 死亡重生不會收回（拿到就是拿到）；回主選單再玩一輪會重來。
/// </summary>
public class DesertOrbDirector : MonoBehaviour
{
    [Header("開關")]
    [Tooltip("勾起來＝荒原要先拿到光球才能按住 S；取消＝回到原本")]
    public bool enableOrbGate = true;

    [Header("光球位置")]
    [Tooltip("光球的世界 x（最後一座掩體 134.7 之前；之後是沒掩體的鳥群空地）")]
    public float orbX = 117f;
    [Tooltip("光球中心在她身高的幾成（0.62＝胸口；她的碰撞框高約 2.9 公尺）")]
    public float orbHeightRatio = 0.62f;
    [Tooltip("量不到她身高時，光球中心離地面幾公尺")]
    public float orbHeight = 1.8f;
    [Tooltip("碰到的半徑（公尺）")]
    public float pickupRadius = 1.6f;
    [Tooltip("跟岩柱太近（公尺內）就往右挪開")]
    public float keepAwayFromShelter = 1.5f;

    [Header("拿到那一刻")]
    [Tooltip("翻哪一頁（Resources 路徑，不含副檔名）；留空＝不翻頁")]
    public string pageAfterPickup = "Pages/039_荒-9";
    [Tooltip("那一頁停幾秒（任意鍵可提早）")]
    public float pageHoldSeconds = 5f;
    [Tooltip("光飛進她身體要幾秒")]
    public float absorbSeconds = 0.8f;

    [Header("繪本同步")]
    [Tooltip("她走到這個 x（鳥開始追蹤的地方），主選單的繪本開到 038（鳥影、她抬頭看鳥群）")]
    public float birdPagesFromX = 45f;

    [Header("提示位置（LineHintDirector 讀）")]
    [Tooltip("「按住 S」線條畫的左緣放在光球右邊幾公尺")]
    public float guideOffsetAfterOrb = 4f;

    // ── 靜態（其他腳本讀）──
    /// <summary>這一關有光球門檻（荒原、而且開關打開）。</summary>
    public static bool GateActive { get; private set; }
    /// <summary>已經拿到光球。</summary>
    public static bool Collected { get; private set; }
    /// <summary>光球的 x（提示、鳥群分段用）。沒有門檻時＝負無限。</summary>
    public static float OrbX { get; private set; } = float.NegativeInfinity;
    /// <summary>「按住 S」線條畫左緣要放的 x。</summary>
    public static float GuideLeftX { get; private set; } = float.NegativeInfinity;
    /// <summary>現在可不可以按住 S 硬撐（沒有門檻的關卡永遠 true）。</summary>
    public static bool BraceAllowed { get { return !GateActive || Collected; } }

    private static DesertOrbDirector _instance;
    private GameObject _orb;
    private Transform _glow, _core;
    private float _bobT;
    private bool _busy;
    private static Sprite _glowSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        GateActive = false; Collected = false; OrbX = float.NegativeInfinity; GuideLeftX = float.NegativeInfinity; _instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m != LoadSceneMode.Single) return;
        Install(s);
    }

    private static void Install(Scene scene)
    {
        // 每個場景重算：不是荒原＝沒有門檻（Collected 跟著歸零，下一輪重來）
        GateActive = false; Collected = false; OrbX = float.NegativeInfinity; GuideLeftX = float.NegativeInfinity;
        if (!scene.IsValid() || !scene.name.ToLower().Contains("desert")) return;
        DesertOrbDirector d = null;
        foreach (DesertOrbDirector x in FindObjectsByType<DesertOrbDirector>(FindObjectsInactive.Include))
            if (x.gameObject.scene == scene) { d = x; break; }
        if (d == null)
        {
            GameObject host = new GameObject("[DesertOrbDirector]");
            SceneManager.MoveGameObjectToScene(host, scene);
            d = host.AddComponent<DesertOrbDirector>();
        }
        _instance = d;
        if (!d.enableOrbGate) { Debug.Log("【荒原光球】門檻關閉（enableOrbGate=false）：一進荒原就能按住 S"); return; }
        GateActive = true;
        OrbX = d.orbX;
        d.StartCoroutine(d.Setup());
    }

    private IEnumerator Setup()
    {
        yield return null;   // 等場景的 Awake／Start 都跑完（岩柱、地面）
        // 跟岩柱太近就往右挪
        float x = orbX;
        for (int guard = 0; guard < 4; guard++)
        {
            bool near = false;
            foreach (WindShelter ws in FindObjectsByType<WindShelter>(FindObjectsInactive.Exclude))
                if (ws != null && Mathf.Abs(ws.transform.position.x - x) < keepAwayFromShelter) { near = true; break; }
            if (!near) break;
            x += 2.5f;
        }
        OrbX = x;
        GuideLeftX = x + guideOffsetAfterOrb;
        if (DesertBeatDirector.Instance != null) DesertBeatDirector.Instance.ApplyRhythm(x);   // 鳥的第三段（光球後）從光球開始

        // 地面高度：從上往下打線（★1023c 打在 z=0 與她的 z 兩條：荒原地面、岩柱的碰撞框都很薄、在 z=0，
        //   只打她的 z（-0.3）會整條漏掉，第一版就是這樣才退回「她剛出生時的高度」，光球飄在半空）
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        float z = pm != null ? pm.transform.position.z : -0.3f;
        Collider body = BodyCollider(pm);
        float lift = (body != null && body.bounds.size.y > 0.5f) ? body.bounds.size.y * orbHeightRatio : orbHeight;
        bool found = false;
        float groundY = 0f;
        float bestDist = float.MaxValue;
        foreach (float zz in new[] { 0f, z })
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, 30f, zz), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                Collider c = h.collider;
                if (c == null) continue;
                // 跳過岩柱（WindShelter、會碎的岩石）、鳥、她自己，取最上面那一層＝地面
                if (c.GetComponentInParent<WindShelter>() != null || c.GetComponentInParent<Destructible>() != null) continue;
                if (c.GetComponentInParent<IndividualBirdEnemy>() != null || c.GetComponentInParent<PlayerMovement>() != null) continue;
                if (h.distance < bestDist) { bestDist = h.distance; groundY = h.point.y; found = true; }
            }
        if (!found) groundY = body != null ? body.bounds.min.y : (pm != null ? pm.transform.position.y : 0.6f);

        BuildOrb(new Vector3(x, groundY + lift, z));
        Debug.Log($"【荒原光球】★1023c 光球放在 x={x:F1}、離地 {lift:F2} 公尺（地面 y={groundY:F2}{(found ? "" : "，打不到地面：等她站穩用她的腳底")}）；拿到之前按住 S 沒有作用，提示都在光球之後");
        if (!found && pm != null) StartCoroutine(SettleHeightToHerFeet(pm, lift));
    }

    private static Collider BodyCollider(PlayerMovement pm)
    {
        if (pm == null) return null;
        foreach (Collider c in pm.GetComponentsInChildren<Collider>()) if (c != null && !c.isTrigger) return c;
        return null;
    }

    /// <summary>★1023b 打不到地面時的備案：她站穩（不在岩柱上、垂直速度≈0 持續 0.4 秒）就把光球放到她的腳底＋lift（荒原地面是平的）。</summary>
    private IEnumerator SettleHeightToHerFeet(PlayerMovement pm, float lift)
    {
        Rigidbody rb = pm.GetComponent<Rigidbody>();
        Collider body = BodyCollider(pm);
        if (rb == null || body == null) yield break;
        float still = 0f;
        float guard = 0f;
        while (_orb != null && guard < 120f)
        {
            guard += Time.unscaledDeltaTime;
            bool onPillar = false;
            foreach (WindShelter ws in FindObjectsByType<WindShelter>(FindObjectsInactive.Exclude))
                if (ws != null && Mathf.Abs(ws.transform.position.x - pm.transform.position.x) < 1.4f) { onPillar = true; break; }
            bool calm = pm.isGrounded && !onPillar && Mathf.Abs(rb.linearVelocity.y) < 0.05f && !PlayerRespawnSystem.IsAnyRespawning && Time.timeScale > 0f;
            still = calm ? still + Time.deltaTime : 0f;
            if (still >= 0.4f)
            {
                float feet = body.bounds.min.y;
                float y = feet + lift;
                if (Mathf.Abs(y - _baseY) > 0.05f)
                    Debug.Log($"【荒原光球】★1023b 高度改用她的腳底：腳底 y={feet:F2} → 光球 y={y:F2}（原本 {_baseY:F2}）");
                _baseY = y;
                yield break;
            }
            yield return null;
        }
    }

    private const float GlowSize = 4.2f;
    private const float CoreSize = 1.6f;
    private static Material _unlit;
    private static Material UnlitMat()
    {
        if (_unlit != null) return _unlit;
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh != null) _unlit = new Material(sh);
        return _unlit;
    }

    private static Sprite GlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;
        int n = 128;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[n * n];
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int xx = 0; xx < n; xx++)
            {
                float d = Mathf.Sqrt((xx - c) * (xx - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);           // 柔邊
                a = Mathf.Pow(a, 1.6f);
                px[y * n + xx] = new Color32(255, 240, 200, (byte)(a * 255f));
            }
        t.SetPixels32(px); t.Apply();
        _glowSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);   // 1 公尺寬
        return _glowSprite;
    }

    private void BuildOrb(Vector3 pos)
    {
        _orb = new GameObject("DesertOrb_光球");
        SceneManager.MoveGameObjectToScene(_orb, gameObject.scene);
        _orb.transform.position = pos;

        // ★1023b 第一版太淡、太小（在亮的天空前面幾乎看不到）：放大、加亮、用不受燈光影響的材質
        // ★1023c 外圈改成飽和一點的金色：荒原的天空、沙地都是淺粉橘，白黃色的光暈貼上去幾乎分不出來
        _glow = MakeLayer("Glow", GlowSize, new Color(1f, 0.72f, 0.3f, 0.8f), 48);
        MakeLayer("Halo", 2.8f, new Color(1f, 0.88f, 0.58f, 0.95f), 49);
        _core = MakeLayer("Core", CoreSize, new Color(1f, 0.99f, 0.93f, 1f), 50);
        MakeLayer("Spark", 0.55f, new Color(1f, 1f, 1f, 1f), 51);
        // ★1023c 十字星芒（細長的兩道光，慢慢轉）：遠遠看也知道那是一顆「光」
        GameObject fl = new GameObject("Flare");
        fl.transform.SetParent(_orb.transform, false);
        _flare = fl.transform;
        MakeLayer("FlareH", 1f, new Color(1f, 0.97f, 0.88f, 0.9f), 53, _flare).localScale = new Vector3(FlareLen, FlareThick, 1f);
        MakeLayer("FlareV", 1f, new Color(1f, 0.97f, 0.88f, 0.9f), 53, _flare).localScale = new Vector3(FlareThick, FlareLen, 1f);
        _sparks = new Transform[8];
        _sparkSr = new SpriteRenderer[8];
        for (int i = 0; i < _sparks.Length; i++)
        {
            _sparks[i] = MakeLayer("Mote" + i, 0.22f + 0.06f * (i % 3), new Color(1f, 0.82f, 0.42f, 1f), 52);
            _sparkSr[i] = _sparks[i].GetComponent<SpriteRenderer>();
        }

        SphereCollider col = _orb.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = pickupRadius;
        OrbTouch touch = _orb.AddComponent<OrbTouch>();
        touch.owner = this;
    }

    private Transform MakeLayer(string name, float size, Color color, int order, Transform parent = null)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent != null ? parent : _orb.transform, false);
        g.transform.localScale = Vector3.one * size;
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = GlowSprite();
        if (UnlitMat() != null) sr.sharedMaterial = UnlitMat();
        sr.color = color;
        sr.sortingOrder = order;
        return g.transform;
    }

    private void Update()
    {
        CheckBirdPages();
        if (_orb == null || _busy) return;
        _bobT += Time.deltaTime;
        float bob = Mathf.Sin(_bobT * 1.6f) * 0.22f;
        Vector3 p = _orb.transform.position;
        float baseY = _baseY < -9000f ? (_baseY = p.y) : _baseY;
        _orb.transform.position = new Vector3(p.x, baseY + bob, p.z);
        float pulse = 1f + Mathf.Sin(_bobT * 2.3f) * 0.08f;
        if (_glow != null) _glow.localScale = Vector3.one * GlowSize * pulse;
        if (_core != null) _core.localScale = Vector3.one * CoreSize * (2f - pulse);
        if (_flare != null)
        {
            _flare.localRotation = Quaternion.Euler(0f, 0f, _bobT * 18f);
            float fp = 0.85f + 0.25f * Mathf.Abs(Mathf.Sin(_bobT * 1.3f));
            _flare.localScale = new Vector3(fp, fp, 1f);
        }
        // ★1023b 繞著光球的金色光點（跟玻璃館、廢墟的光球同一種感覺；亮天空前面也看得出是「光」）
        if (_sparks != null)
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (_sparks[i] == null) continue;
                float a = _bobT * (0.6f + 0.15f * (i % 3)) + i * (Mathf.PI * 2f / _sparks.Length);
                float r = 0.75f + 0.35f * Mathf.Sin(_bobT * 0.9f + i);
                _sparks[i].localPosition = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.7f, 0f);
                SpriteRenderer sr = _sparkSr[i];
                if (sr != null) { Color c = sr.color; c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_bobT * 2.1f + i * 1.7f)); sr.color = c; }
            }
    }
    private PlayerMovement _pmRef;
    private bool _birdPagesDone;
    /// <summary>★1023c 她走到鳥開始追蹤的地方，繪本開到 038（不管有沒有光球門檻）。</summary>
    private void CheckBirdPages()
    {
        if (_birdPagesDone) return;
        if (_pmRef == null) { _pmRef = FindAnyObjectByType<PlayerMovement>(); if (_pmRef == null) return; }
        if (_pmRef.transform.position.x < birdPagesFromX) return;
        _birdPagesDone = true;
        StoryProgress.Unlock(37, "荒原鳥群開始追她（x≥" + birdPagesFromX.ToString("F0") + "）");
    }

    private Transform[] _sparks;
    private SpriteRenderer[] _sparkSr;
    private Transform _flare;
    private const float FlareLen = 4.2f;
    private const float FlareThick = 0.24f;
    private float _baseY = -9999f;

    /// <summary>她碰到光球（OrbTouch 呼叫）。</summary>
    public void OnTouched(PlayerMovement pm)
    {
        if (_busy || Collected || pm == null) return;
        if (PlayerRespawnSystem.IsAnyRespawning || pm.isCutsceneFrozen || PlayerMovement.IsHardCutsceneLocked) return;
        StartCoroutine(PickupRoutine(pm));
    }

    private IEnumerator PickupRoutine(PlayerMovement pm)
    {
        _busy = true;
        Collider col = _orb.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        Debug.Log("【荒原光球】★1023 她碰到光球：定住、光飛進身體、翻 " + pageAfterPickup);

        // 1 她定住；★1023b 世界一起停（鳥、風）——光飛進身體這 1.3 秒不能被撲到
        pm.isCutsceneFrozen = true;
        Rigidbody prb = pm.GetComponent<Rigidbody>();
        if (prb != null && !prb.isKinematic) prb.linearVelocity = Vector3.zero;
        float prevScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        // 2 光飛進她胸口、縮小、亮一下
        Vector3 from = _orb.transform.position;
        float t = 0f;
        float dur = Mathf.Max(0.2f, absorbSeconds);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            Vector3 to = pm.transform.position + new Vector3(0f, 0.9f, 0f);
            _orb.transform.position = Vector3.Lerp(from, to, k);
            float s = Mathf.Lerp(1f, 0.15f, k);
            if (_glow != null) _glow.localScale = Vector3.one * GlowSize * s;
            if (_core != null) _core.localScale = Vector3.one * CoreSize * s;
            if (_flare != null) _flare.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        // 身上亮一圈再散掉
        GameObject flash = new GameObject("OrbFlash");
        SceneManager.MoveGameObjectToScene(flash, gameObject.scene);
        flash.transform.position = pm.transform.position + new Vector3(0f, 0.9f, 0f);
        SpriteRenderer fsr = flash.AddComponent<SpriteRenderer>();
        fsr.sprite = GlowSprite(); fsr.sortingOrder = 60;
        if (UnlitMat() != null) fsr.sharedMaterial = UnlitMat();
        t = 0f;
        while (t < 0.55f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / 0.55f);
            flash.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 5.5f, k);
            fsr.color = new Color(1f, 0.95f, 0.8f, (1f - k) * 0.7f);
            yield return null;
        }
        Destroy(flash);
        Destroy(_orb); _orb = null;

        // 3 翻那一頁（世界暫停，鳥、風都停）
        if (!string.IsNullOrEmpty(pageAfterPickup))
            yield return StoryPageCard.PlayRoutine(new[] { pageAfterPickup }, pageHoldSeconds, false, true);

        // 4 從此按住 S 有效；時間放回來
        Time.timeScale = prevScale;
        Collected = true;
        StoryProgress.Unlock(38, "荒原拿到光球");   // 書開到 039（光把她罩住）
        pm.isCutsceneFrozen = false;
        _busy = false;
        Debug.Log("【荒原光球】★1023 拿到了：按住 S 石化硬撐從現在起有效；前面是「按住 S」與鳥的提示，再來是沒掩體的空地");
    }

    /// <summary>光球身上的碰觸偵測。</summary>
    public class OrbTouch : MonoBehaviour
    {
        public DesertOrbDirector owner;
        private void OnTriggerEnter(Collider other)
        {
            if (owner == null) return;
            PlayerMovement pm = other.GetComponentInParent<PlayerMovement>();
            if (pm != null) owner.OnTouched(pm);
        }
    }
}
