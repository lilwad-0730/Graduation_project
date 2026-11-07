using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1106 綠洲下沉演出（全程式生成，不用任何美術檔、不改場景、不碰音效）。
/// 由 SceneTransitionZone 的「沉水模式」呼叫：
///   ① 入水瞬間：三圈水波從她腳下散開、時間輕微變慢約 0.3 秒再回來
///   ② 下沉期間：身後拖一串氣泡；畫面隨深度由暖色慢慢變青綠、再變深藍，四邊漸暗（暗角）
///   ③ 光從上面來：幾道斜光柱（越深越淡）、緩緩往上飄的細小微粒
///   ④ 載入水下場景後：從深藍裡慢慢「浮現」，不是突然亮起來（同一份演出跨場景接續）
/// 想關掉：把 SceneTransitionZone 的 Cinematic Sink 取消勾選，就回到原本的黑屏淡出下沉。
/// 想整個拿掉：刪掉這個檔案，並把 SceneTransitionZone 裡標 ★1106 的幾行拿掉。
/// </summary>
public class OasisSinkFX : MonoBehaviour
{
    private static OasisSinkFX _inst;

    private Transform _player;
    private float _p;                 // 0＝剛入水，1＝沉到底
    private bool _armed;              // 已經沉得夠深，下一次載入場景就進入「浮現」階段
    private bool _arrival;
    private float _arrivalT;
    private float _master = 1f;       // 全部的總濃度（浮現結束時淡出）
    private float _navy;              // 純深藍蓋板（剛載入時完全蓋住，慢慢退）
    private float _prevScale = 1f;
    private bool _slowed;
    private ParticleSystem _bubbles;
    private string _target = "";      // 目的地場景名（載入到這一關才進入「浮現」，中間的文字卡／漫畫場景不算）
    private bool _hold;               // 沉完了：深藍蓋滿後把自己藏起來，讓文字卡與漫畫接手
    private float _holdT;
    private bool _hidden;
    private float _age;

    // ★1106 與文字卡同時進行（卡模式）：世界已被文字卡暫停，所有時間都走 unscaled；畫面畫在玩家後面，不蓋到字
    private bool _cardMode;
    private float _cardDur = 8f, _cardT;
    private bool _cardDone;
    private Vector3 _p0;
    private float _depth = 3f;
    private Canvas _canvas;
    private bool _behindNow;
    private float _beat;
    private Image _surface;

    // ★1106 荒原去色：文字卡一觸發，黃土色慢慢退成灰。荒原本來沒有後處理，這裡臨時開、用完立刻還原
    private GameObject _greyGo;
    private Volume _greyVol;
    private VolumeProfile _greyProfile;
    private ColorAdjustments _greyCa;
    private UniversalAdditionalCameraData _greyCamData;
    private bool _greyPrevPost;
    private float _greyT;

    private Image _tint, _vignette, _navyImg;
    private Image _flash;                 // ★入水瞬間的一閃（配合停格）
    private float _cardAge;
    private readonly HeartbeatClock _hb = new HeartbeatClock();
    private readonly List<Image> _shafts = new List<Image>();
    private readonly List<float> _shaftBaseX = new List<float>();
    private readonly List<Image> _specks = new List<Image>();
    private readonly List<Vector3> _speckData = new List<Vector3>();   // x=速度, y=相位, z=基礎透明度

    // ───────────────────────── 對外 ─────────────────────────
    /// <summary>
    /// 播完整段下沉（給 SceneTransitionZone 與 StoryCardZoneHook 共用）：
    /// 入水水波＋慢動作、氣泡、隨深度變色、光柱，最後深藍蓋滿然後把自己藏起來，等目的地場景載入後再「浮現」。
    /// onProgress：每幀回報 0～1 的進度（SceneTransitionZone 用來同步黑屏）。
    /// </summary>
    public static IEnumerator PlaySink(Transform t, string targetScene, float seconds, float depth, System.Action<float> onProgress)
    {
        if (t == null) yield break;
        OasisSinkFX fx = Begin(t, targetScene);
        Rigidbody rb = t.GetComponent<Rigidbody>();
        if (rb != null) { rb.useGravity = false; rb.linearVelocity = Vector3.zero; }
        Collider col = t.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;               // 讓她順利沒入水窪，不被下面的地面卡住

        float dur = Mathf.Max(1f, seconds);
        float d = Mathf.Max(0.5f, depth);
        Vector3 p0 = t.position;
        float ct = 0f;
        while (t != null && ct < dur)
        {
            ct += Time.deltaTime;
            float cp = Mathf.Clamp01(ct / dur);
            t.position = new Vector3(p0.x, p0.y - d * Mathf.SmoothStep(0f, 1f, cp), p0.z);   // 先輕輕地沉、中間順、最後放慢
            if (fx != null) fx.SetProgress(cp);
            if (onProgress != null) onProgress(cp);
            yield return null;
        }
        if (fx != null) { fx.SetProgress(1f); fx.Hold(); }
        if (onProgress != null) onProgress(1f);
    }

    /// <summary>
    /// ★1106 跟文字卡「同時」開始：入水水波、氣泡、下沉、變色全部走不受暫停影響的時間，
    /// 節奏排成 seconds 秒（＝文字卡長度）剛好沉到底；卡播完後呼叫 FinishCard()。
    /// </summary>
    public static OasisSinkFX BeginCardSink(Transform t, string targetScene, float seconds, float depth)
    {
        if (t == null) return null;
        Rigidbody rb = t.GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.useGravity = false; rb.isKinematic = true; }
        Collider col = t.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        OasisSinkFX fx = Begin(t, targetScene, true);
        fx._cardDur = Mathf.Max(2f, seconds);
        fx._depth = Mathf.Max(0.5f, depth);
        fx._p0 = t.position;
        fx.StartGrey();
        return fx;
    }

    private void StartGrey()
    {
        try
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            _greyCamData = cam.GetUniversalAdditionalCameraData();
            if (_greyCamData == null) return;
            _greyPrevPost = _greyCamData.renderPostProcessing;
            _greyCamData.renderPostProcessing = true;

            _greyProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _greyCa = _greyProfile.Add<ColorAdjustments>(true);
            _greyCa.saturation.Override(0f);

            _greyGo = new GameObject("[荒原去色 Volume]");
            _greyGo.transform.SetParent(transform, false);
            _greyVol = _greyGo.AddComponent<Volume>();
            _greyVol.isGlobal = true;
            _greyVol.priority = 100f;
            _greyVol.weight = 1f;
            _greyVol.sharedProfile = _greyProfile;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[OasisSinkFX] 去色啟動失敗，略過（不影響轉場）：" + e.Message);
            StopGrey();
        }
    }

    private void StopGrey()
    {
        if (_greyCamData != null) { _greyCamData.renderPostProcessing = _greyPrevPost; _greyCamData = null; }
        if (_greyGo != null) { Destroy(_greyGo); _greyGo = null; }
        if (_greyProfile != null) { Destroy(_greyProfile); _greyProfile = null; }
        _greyVol = null; _greyCa = null;
    }

    private void UpdateGrey()
    {
        if (_greyCa == null) return;
        _greyT += Time.unscaledDeltaTime;
        // 前面先慢慢退成灰（最多 4.5 秒，卡短就按比例），灰到底之後，沉進深藍時才回補一些色，不然藍會被一起抽掉
        float ramp = Mathf.Min(4.5f, _cardDur * 0.4f);
        float down = Mathf.SmoothStep(0f, 1f, _greyT / Mathf.Max(0.5f, ramp));          // 0→1：越來越灰
        float back = Mathf.SmoothStep(0.5f, 1f, _p);                                      // 後半段回補
        _greyCa.saturation.value = Mathf.Lerp(0f, -100f, down) * Mathf.Lerp(1f, 0.55f, back);
    }

    /// <summary>文字卡播完了：直接沉到底並接手後續（等目的地場景載入再浮現）。</summary>
    public void FinishCard()
    {
        _cardDone = true;
        SetProgress(1f);
        if (_player != null) _player.position = new Vector3(_p0.x, _p0.y - _depth, _p0.z);
        Hold();
        _hidden = true;   // 這時文字卡的黑幕已經全黑蓋在最上層，演出可以先收起來
        StopGrey();       // 同上：全黑之下還原攝影機，下個場景不會留灰
    }

    public static OasisSinkFX Begin(Transform player, string targetScene = "", bool cardMode = false)
    {
        CancelAll();
        GameObject go = new GameObject("[綠洲沉水演出 OasisSinkFX]");
        DontDestroyOnLoad(go);
        OasisSinkFX fx = go.AddComponent<OasisSinkFX>();
        fx._cardMode = cardMode;
        fx._player = player;
        fx._target = targetScene != null ? targetScene.Trim() : "";
        _inst = fx;
        fx.BuildUI();
        fx.StartCoroutine(fx.Intro());
        SceneManager.sceneLoaded += fx.OnLoaded;
        return fx;
    }

    public static void CancelAll()
    {
        if (_inst == null) return;
        _inst.RestoreTime();
        Destroy(_inst.gameObject);
        _inst = null;
    }

    /// <summary>沉到底了：深藍慢慢蓋滿，之後把自己藏起來（文字卡的黑幕會接手），等目的地場景載入再浮現。</summary>
    public void Hold() { _hold = true; _holdT = 0f; }

    public void SetProgress(float p)
    {
        if (_arrival) return;
        _p = Mathf.Clamp01(p);
        if (_p > 0.55f) _armed = true;
    }

    // ───────────────────────── 生命週期 ─────────────────────────
    private void OnLoaded(Scene s, LoadSceneMode m)
    {
        if (!_armed || _arrival) return;
        if (_target.Length > 0 && !string.Equals(s.name, _target, System.StringComparison.OrdinalIgnoreCase)) return;   // 文字卡、漫畫場景不算
        _arrival = true;
        _hidden = false;
        _hold = false;
        _arrivalT = 0f;
        _p = 1f;
        _navy = 1f;
        RestoreTime();
        if (_bubbles != null) { Destroy(_bubbles.gameObject); _bubbles = null; }
    }

    private void OnDestroy()
    {
        StopGrey();
        SceneManager.sceneLoaded -= OnLoaded;
        RestoreTime();
        if (_inst == this) _inst = null;
    }

    private void RestoreTime()
    {
        if (_slowed && Time.timeScale > 0f) Time.timeScale = _prevScale;
        _slowed = false;
    }

    private void Update()
    {
        if (_arrival)
        {
            _arrivalT += Time.unscaledDeltaTime;
            _p = Mathf.Lerp(1f, 0.15f, Mathf.SmoothStep(0f, 1f, _arrivalT / 4.2f));        // 慢慢從深處浮向水面
            _navy = 1f - Mathf.SmoothStep(0f, 1f, _arrivalT / 1.8f);                       // 深藍蓋板先退
            _master = 1f - Mathf.SmoothStep(0f, 1f, (_arrivalT - 3.4f) / 1.3f);            // 最後整個淡掉
            if (_arrivalT > 4.8f) { Destroy(gameObject); return; }
        }
        else if (_hold)
        {
            _holdT += Time.unscaledDeltaTime;
            _navy = 1f;               // 深藍先蓋滿
            if (_holdT > 1.3f) _hidden = true;                             // 這時文字卡的黑幕已經蓋住，可以放心藏起來
        }
        else if (_player == null && !_armed)
        {
            // 玩家不見了（例如死亡重生）：演出自己收掉
            Destroy(gameObject);
            return;
        }
        if (_cardMode && !_arrival && !_hold && !_cardDone)
        {
            // 跟著文字卡走：一開始很慢，越沉越快（被拖進去的感覺）
            _cardT += Time.unscaledDeltaTime;
            float cp = Mathf.Clamp01(_cardT / _cardDur);
            float ease = Mathf.Pow(cp, 1.7f);
            if (_player != null) _player.position = new Vector3(_p0.x, _p0.y - _depth * ease, _p0.z);
            SetProgress(cp);
            UpdateGrey();
        }
        if (!_arrival && !_hold) _navy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 1f, _p));   // ★1106 沉的最後 26% 就開始蓋深藍，不再多等一拍
        if (_bubbles != null && _p > 0.92f)
        {
            var em = _bubbles.emission; em.enabled = false;
        }
        _age += Time.unscaledDeltaTime;
        if (_age > 180f) { Destroy(gameObject); return; }                 // 保險：卡太久（例如玩家中途離開）自己收掉
        Apply(Time.unscaledTime);
    }

    private void LateUpdate()
    {
        if (_canvas == null) return;
        bool behind = false;
        if (_cardMode && !_arrival && !_hidden && _player != null)
        {
            StoryCardPlayer sc = StoryCardPlayer.Instance;
            Camera cam = Camera.main;
            if (sc != null && sc.IsBehindPlayer && cam != null)
            {
                // 跟文字卡同一個空間，放在比字更後面一點：只蓋背景，不蓋字也不蓋她
                float d = Vector3.Dot(_player.position - cam.transform.position, cam.transform.forward) + sc.behindDistance + 0.3f;
                if (d > cam.nearClipPlane + 0.05f && d < cam.farClipPlane - 0.05f)
                {
                    if (_canvas.renderMode != RenderMode.ScreenSpaceCamera) _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    _canvas.worldCamera = cam;
                    _canvas.planeDistance = d;
                    _canvas.sortingOrder = 9000;
                    behind = true;
                }
            }
        }
        if (!behind && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = null;
            _canvas.sortingOrder = 9000;
        }
        _behindNow = behind;
    }

    // ───────────────────────── 入水瞬間 ─────────────────────────
    private IEnumerator Intro()
    {
        _prevScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        if (!_cardMode)
        {
            Time.timeScale = _prevScale * 0.35f;     // 入水那一下，時間輕輕慢下來
            _slowed = true;
        }

        Vector3 pos = _player != null ? _player.position : Vector3.zero;
        Collider col = _player != null ? _player.GetComponent<Collider>() : null;
        if (col != null) pos.y = col.bounds.min.y + 0.15f;
        Texture2D ring = RingTexture();
        for (int i = 0; i < 3; i++) StartCoroutine(Ripple(pos, ring, i * 0.22f, 1f - i * 0.22f));
        StartBubbles();

        if (_cardMode) yield break;
        float t = 0f;
        while (t < 0.3f) { t += Time.unscaledDeltaTime; yield return null; }
        t = 0f;
        float from = _prevScale * 0.35f;
        while (t < 0.25f)
        {
            t += Time.unscaledDeltaTime;
            if (_slowed) Time.timeScale = Mathf.Lerp(from, _prevScale, t / 0.25f);
            yield return null;
        }
        RestoreTime();
    }

    private IEnumerator Ripple(Vector3 pos, Texture2D ringTex, float delay, float strength)
    {
        float d = 0f;
        while (d < delay) { d += Time.unscaledDeltaTime; yield return null; }
        GameObject o = new GameObject("水波");
        o.transform.SetParent(transform, false);
        o.transform.position = new Vector3(pos.x, pos.y, pos.z);
        SpriteRenderer sr = o.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(ringTex, new Rect(0, 0, ringTex.width, ringTex.height), new Vector2(0.5f, 0.5f), 64f);
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null) sr.sharedMaterial = new Material(sh);
        sr.sortingOrder = _cardMode ? 10050 : 30;   // 卡模式：文字卡的畫布是 10000，水波要畫在它上面才看得到
        float t = 0f, dur = _cardMode ? 2.6f : 1.7f;   // 卡模式水波散得更慢，一路淡到接上變色
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = 1f - (1f - k) * (1f - k);                       // 先快後慢地擴散
            o.transform.localScale = new Vector3(Mathf.Lerp(0.5f, 6.5f, e), Mathf.Lerp(0.14f, 1.2f, e), 1f);   // 壓扁的橢圓＝水面上的圈
            sr.color = new Color(0.85f, 1f, 1f, (1f - k) * (1f - k) * 0.8f * strength);
            yield return null;
        }
        Destroy(o);
    }

    private void StartBubbles()
    {
        if (_player == null) return;
        GameObject o = new GameObject("沉水氣泡");
        o.transform.SetParent(_player, false);
        o.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        ParticleSystem ps = o.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.useUnscaledTime = true;                      // 文字卡期間世界暫停，氣泡照樣要動
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.2f);
        main.startColor = new Color(0.9f, 1f, 1f, 0.85f);
        main.gravityModifier = -0.1f;                     // 往上飄
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 140;
        var em = ps.emission; em.rateOverTime = 20f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.35f;
        var nz = ps.noise; nz.enabled = true; nz.strength = 0.4f; nz.frequency = 0.5f;
        var col = ps.colorOverLifetime; col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystemRenderer pr = o.GetComponent<ParticleSystemRenderer>();
        Shader s = Shader.Find("Sprites/Default");
        if (s != null)
        {
            Material m = new Material(s);
            m.mainTexture = BubbleTexture();
            pr.sharedMaterial = m;
        }
        pr.sortingOrder = _cardMode ? 10055 : 35;
        ps.Play();
        _bubbles = ps;
    }

    // ───────────────────────── 畫面（螢幕疊色） ─────────────────────────
    private void BuildUI()
    {
        GameObject cv = new GameObject("畫面");
        cv.transform.SetParent(transform, false);
        Canvas canvas = cv.AddComponent<Canvas>();
        _canvas = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;                      // 在遊戲畫面之上、黑屏淡出（9999）之下
        CanvasScaler cs = cv.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight = 0.5f;

        _tint = MakeFull(cv.transform, "色調", null);
        _tint.color = new Color(0f, 0f, 0f, 0f);

        Sprite bar = SoftBarSprite();
        float[] xs = { -760f, -380f, -40f, 330f, 690f };
        float[] ws = { 240f, 330f, 200f, 360f, 260f };
        for (int i = 0; i < xs.Length; i++)
        {
            Image im = MakeImage(cv.transform, "光柱" + i, bar);
            RectTransform rt = im.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(ws[i], 1700f);
            rt.anchoredPosition = new Vector2(xs[i], 160f);
            rt.localRotation = Quaternion.Euler(0f, 0f, -16f);
            im.color = new Color(0.75f, 1f, 0.98f, 0f);
            _shafts.Add(im); _shaftBaseX.Add(xs[i]);
        }

        Sprite dot = DotSprite();
        System.Random rng = new System.Random(5);
        for (int i = 0; i < 40; i++)
        {
            Image im = MakeImage(cv.transform, "微粒" + i, dot);
            RectTransform rt = im.rectTransform;
            float sz = 5f + (float)rng.NextDouble() * 10f;
            rt.sizeDelta = new Vector2(sz, sz);
            rt.anchoredPosition = new Vector2((float)(rng.NextDouble() * 2 - 1) * 980f, (float)(rng.NextDouble() * 2 - 1) * 560f);
            im.color = new Color(0.85f, 1f, 1f, 0f);
            _specks.Add(im);
            _speckData.Add(new Vector3(10f + (float)rng.NextDouble() * 26f, (float)rng.NextDouble() * 6.28f, 0.15f + (float)rng.NextDouble() * 0.3f));
        }

        _surface = MakeImage(cv.transform, "水面光圈", DotSprite());
        _surface.rectTransform.anchorMin = _surface.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _surface.color = new Color(0.85f, 1f, 0.98f, 0f);

        _vignette = MakeFull(cv.transform, "暗角", VignetteSprite());
        _vignette.color = new Color(0f, 0.02f, 0.06f, 0f);
        _navyImg = MakeFull(cv.transform, "深藍蓋板", null);
        _navyImg.color = new Color(0.02f, 0.07f, 0.15f, 0f);
        _flash = MakeFull(cv.transform, "入水一閃", null);
        _flash.color = new Color(0.85f, 0.97f, 1f, 0f);
    }

    private static Image MakeImage(Transform parent, string name, Sprite sp)
    {
        GameObject o = new GameObject(name);
        o.transform.SetParent(parent, false);
        Image im = o.AddComponent<Image>();
        im.sprite = sp;
        im.raycastTarget = false;
        return im;
    }

    private static Image MakeFull(Transform parent, string name, Sprite sp)
    {
        Image im = MakeImage(parent, name, sp);
        RectTransform rt = im.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return im;
    }

    private void Apply(float t)
    {
        float p = Mathf.Clamp01(_p);
        float m = _hidden ? 0f : _master;

        // ★入水停格的視覺：接觸水面那一下，畫面閃一下冷白光，很快退掉（世界同時被 StoryCardZoneHook 瞬間停住約 0.12 秒）
        if (_cardMode && _flash != null)
        {
            _cardAge += Time.unscaledDeltaTime;
            float fl = Mathf.Exp(-_cardAge * 9f) * Mathf.SmoothStep(0f, 1f, _cardAge / 0.03f) * 0.5f;
            _flash.color = new Color(0.85f, 0.97f, 1f, _hidden ? 0f : fl);
        }

        // 色調：暖色 → 青綠 → 深藍（越深越濃）
        Color a = new Color(0.35f, 0.85f, 0.8f, 0f);
        Color b = new Color(0.15f, 0.6f, 0.65f, 0.17f);
        Color c = new Color(0.02f, 0.10f, 0.26f, 0.62f);
        Color g = new Color(0.55f, 0.6f, 0.62f, 0.16f);      // 先把暖色抽掉（去色），再沉進青、再沉進深藍
        Color col = p < 0.15f ? Color.Lerp(a, g, p / 0.15f)
                  : p < 0.4f ? Color.Lerp(g, b, (p - 0.15f) / 0.25f)
                  : Color.Lerp(b, c, (p - 0.4f) / 0.6f);
        float boost = _behindNow ? 1.7f : 1f;      // 卡模式畫在文字卡的暗幕下面，濃度要補一點才看得出來
        col.a = Mathf.Clamp01(col.a * m * boost);
        _tint.color = col;

        // 心跳：越沉越快，暗角跟著一縮一縮
        float pulse = _hb.Tick(Time.unscaledDeltaTime, Mathf.Lerp(0.9f, 1.8f, p)) * 1.2f;   // ★共用心跳語言（HeartbeatPulse）
        float pulseAmt = Mathf.SmoothStep(0.12f, 0.5f, p) * 0.4f;
        _vignette.color = new Color(0f, 0.02f, 0.06f, Mathf.Clamp01(p * 0.78f * (1f + pulseAmt * pulse)) * m);          // 越深邊角越暗

        // 頭上的水面光圈：越沉越小、越遠，最後熄成一個點
        if (_surface != null)
        {
            float sp = Mathf.SmoothStep(0f, 1f, p);
            float sz = Mathf.Lerp(560f, 36f, sp);
            _surface.rectTransform.sizeDelta = new Vector2(sz, sz);
            _surface.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(250f, 400f, sp));
            float sa = Mathf.SmoothStep(0.02f, 0.16f, p) * (1f - Mathf.SmoothStep(0.6f, 0.96f, p)) * 0.6f * m;
            if (_arrival) sa = 0f;
            _surface.color = new Color(0.85f, 1f, 0.98f, sa);
        }
        _navyImg.color = new Color(0.02f, 0.07f, 0.15f, _navy * m);

        // 光柱：入水後出現、最深處淡掉；會緩緩搖
        float shaftA = Mathf.SmoothStep(0.04f, 0.3f, p) * (1f - Mathf.SmoothStep(0.62f, 1f, p)) * 0.24f * m;
        if (_arrival) shaftA = Mathf.SmoothStep(0.15f, 0.5f, 1f - p) * 0.12f * m + (1f - _navy) * 0.0f;
        for (int i = 0; i < _shafts.Count; i++)
        {
            Image im = _shafts[i];
            float breathe = 0.75f + 0.25f * Mathf.Sin(t * 0.45f + i * 1.7f);
            im.color = new Color(0.75f, 1f, 0.98f, Mathf.Clamp01(shaftA * breathe * (_behindNow ? 1.5f : 1f)));
            im.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -16f + Mathf.Sin(t * 0.22f + i) * 2.4f);
            im.rectTransform.anchoredPosition = new Vector2(_shaftBaseX[i] + Mathf.Sin(t * 0.17f + i * 2.1f) * 40f, 160f);
        }

        // 微粒：慢慢往上飄，左右輕輕晃
        float speckA = Mathf.SmoothStep(0.08f, 0.5f, p) * 0.9f * m;
        for (int i = 0; i < _specks.Count; i++)
        {
            Image im = _specks[i];
            Vector3 d = _speckData[i];
            RectTransform rt = im.rectTransform;
            Vector2 pos = rt.anchoredPosition;
            pos.y += d.x * Time.unscaledDeltaTime;
            pos.x += Mathf.Sin(t * 0.6f + d.y) * 6f * Time.unscaledDeltaTime;
            if (pos.y > 580f) pos.y = -580f;
            rt.anchoredPosition = pos;
            im.color = new Color(0.85f, 1f, 1f, d.z * speckA);
        }
    }

    // ───────────────────────── 程式生成的小貼圖 ─────────────────────────
    private static Texture2D NewTex(int w, int h)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    private static Texture2D RingTexture()
    {
        const int N = 128;
        Texture2D t = NewTex(N, N);
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.86f) / 0.07f) * (r < 1f ? 1f : 0f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    private static Texture2D BubbleTexture()
    {
        const int N = 64;
        Texture2D t = NewTex(N, N);
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.88f) / 0.1f) * (r < 1f ? 1f : 0f);
                float fill = r < 0.88f ? 0.12f : 0f;
                float hi = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(N * 0.34f, N * 0.68f)) / 9f) * 0.9f;   // 左上高光
                float a = Mathf.Clamp01(rim * 0.85f + fill + hi);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    private static Sprite SoftBarSprite()
    {
        Texture2D t = NewTex(64, 256);
        Color32[] px = new Color32[64 * 256];
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f;
                float across = Mathf.Exp(-dx * dx * 3.2f);                       // 橫向柔和的高斯
                float along = Mathf.Pow(Mathf.Clamp01(y / 255f), 1.4f);          // y 大＝上端較亮、下端慢慢消失（貼圖上端對應畫面上端）
                px[y * 64 + x] = new Color32(255, 255, 255, (byte)(across * along * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, 64, 256), new Vector2(0.5f, 1f), 100f);
    }

    private static Sprite DotSprite()
    {
        const int N = 32;
        Texture2D t = NewTex(N, N);
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                float a = Mathf.Clamp01(1f - r); a *= a;
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite VignetteSprite()
    {
        const int N = 256;
        Texture2D t = NewTex(N, N);
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x - N * 0.5f) / (N * 0.5f), dy = (y - N * 0.5f) / (N * 0.5f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0.45f, 1.35f, r);                      // 中間透明、越靠邊越暗
                px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }
}
