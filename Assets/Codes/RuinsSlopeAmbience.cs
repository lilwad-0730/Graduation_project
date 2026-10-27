using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 廢墟山坡的氛圍與回饋（純視覺／聲音，不影響玩法：沒有任何碰撞體、不碰物理）。
///   1. 風：越往上爬風聲越大，沙塵順著坡往下吹（越高越密、越快）
///   2. 鏡頭：在坡上時鏡頭往坡上方偏一點，看得到前面還有多高
/// 只有她在山坡區（zone 的範圍）裡、而且沒被過場鎖住（光球飛行、風暴）時才會出現，離開或過場時慢慢淡掉。
/// </summary>
public class RuinsSlopeAmbience : MonoBehaviour
{
    [Header("山坡範圍")]
    [Tooltip("山坡區（用 MountainCameraZone 的觸發框）。她在這個範圍裡才有效果")]
    public Collider zone;
    [Tooltip("坡面（她踩的那條線）上的兩個點：坡腳附近、坡上方。沙塵順著這條線的方向往下吹")]
    public Vector2 surfaceA = new Vector2(191.35f, -128.27f);
    public Vector2 surfaceB = new Vector2(250.54f, -87.37f);
    [Tooltip("從坡腳（0）到這個 x（1）算爬坡進度；風、沙塵會跟著進度變強")]
    public float progressStartX = 195f;
    public float progressEndX = 243f;
    [Tooltip("進出山坡區時，效果淡入淡出的速度（每秒）")]
    public float fadeSpeed = 0.8f;

    [Header("1. 風聲與沙塵")]
    public AudioClip windLoop;
    [Tooltip("坡腳／坡頂的風聲音量（會再乘音效音量設定）")]
    public float windVolumeBottom = 0.08f;
    public float windVolumeTop = 0.45f;
    [Tooltip("沙塵粒子數上限")]
    public int dustMax = 70;
    [Tooltip("坡腳／坡頂每秒產生幾顆沙塵")]
    public float dustRateBottom = 4f;
    public float dustRateTop = 32f;
    [Tooltip("坡腳／坡頂沙塵往下吹的速度")]
    public float dustSpeedBottom = 3f;
    public float dustSpeedTop = 10f;
    [Tooltip("坡腳／坡頂沙塵最濃的透明度")]
    public float dustAlphaBottom = 0.12f;
    public float dustAlphaTop = 0.42f;
    [Tooltip("沙塵裡有幾成是拉長的「風絲」")]
    [Range(0f, 1f)] public float streakRatio = 0.3f;
    public Color dustColor = new Color(0.9f, 0.87f, 0.82f, 1f);
    public int dustSortingOrder = 4;

    [Header("2. 鏡頭往坡上看")]
    public bool cameraLookUphill = true;
    [Tooltip("在坡上時鏡頭偏移多少（往右、往上）。仍受背景範圍限制，不會拍到背景外面")]
    public Vector2 cameraLookOffset = new Vector2(3.2f, 2.2f);
    [Tooltip("鏡頭偏移跟上的速度（世界單位／秒）")]
    public float cameraLookSpeed = 2.5f;

    private Transform _player;
    private PlayerMovement _pm;
    private float _fade;          // 0＝不在山坡（或過場中），1＝在坡上
    private float _progress;      // 0＝坡腳，1＝坡上方
    private Vector2 _uphill;      // 沿坡面往上的方向
    private AudioSource _wind;
    private Vector2 _lookOffset;

    private Sprite _dot;
    private class Dust { public SpriteRenderer sr; public Vector2 vel; public float life, age, maxAlpha; public bool streak; }
    private readonly List<Dust> _dust = new List<Dust>();
    private float _dustAccum;

    private void Awake()
    {
        _uphill = (surfaceB - surfaceA).normalized;
        _dot = BuildSoftDot();

        if (windLoop != null)
        {
            _wind = gameObject.AddComponent<AudioSource>();
            _wind.clip = windLoop;
            _wind.loop = true;
            _wind.playOnAwake = false;
            _wind.spatialBlend = 0f;
            _wind.volume = 0f;
        }
    }

    private void OnDisable()
    {
        CameraTargetXFollower.AmbientLookOffset = Vector2.zero;
        if (_wind != null) _wind.Stop();
    }

    private void Update()
    {
        if (_player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _player = p.transform;
            _pm = p.GetComponent<PlayerMovement>();
        }

        Vector3 pp = _player.position;
        bool inZone = zone != null && zone.enabled
            ? zone.bounds.Contains(new Vector3(pp.x, pp.y, zone.bounds.center.z))
            : pp.x >= progressStartX && pp.x <= progressEndX + 15f;
        bool frozen = _pm != null && _pm.isCutsceneFrozen;
        float targetFade = inZone && !frozen ? 1f : 0f;
        _fade = Mathf.MoveTowards(_fade, targetFade, fadeSpeed * Time.deltaTime);
        _progress = Mathf.Clamp01(Mathf.InverseLerp(progressStartX, progressEndX, pp.x));

        UpdateWind();
        UpdateDust();
        UpdateCamera();
    }

    // ── 1. 風 ─────────────────────────────────────────
    private void UpdateWind()
    {
        if (_wind == null) return;
        float v = Mathf.Lerp(windVolumeBottom, windVolumeTop, _progress) * _fade;
        if (v > 0.001f && !_wind.isPlaying) _wind.Play();
        _wind.volume = AudioManager.ScaleSfx(v);
        if (v <= 0.001f && _wind.isPlaying) _wind.Stop();
    }

    private void UpdateDust()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float dt = Time.deltaTime;
        Vector2 down = -_uphill;

        if (_fade > 0.01f)
        {
            _dustAccum += Mathf.Lerp(dustRateBottom, dustRateTop, _progress) * _fade * dt;
            float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
            Vector2 c = cam.transform.position;
            while (_dustAccum >= 1f)
            {
                _dustAccum -= 1f;
                Dust d = GetDust();
                if (d == null) break;
                // 風從坡上（右上）往下吹：大多從畫面右邊、上面進來；兩成直接在畫面裡出現，畫面才不會一半空著
                float r = Random.value;
                Vector2 pos = r < 0.55f ? c + new Vector2(halfW + 1f, Random.Range(-halfH, halfH))
                            : r < 0.8f ? c + new Vector2(Random.Range(-halfW, halfW + 4f), halfH + 1f)
                            : c + new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
                float speed = Mathf.Lerp(dustSpeedBottom, dustSpeedTop, _progress) * Random.Range(0.7f, 1.3f);
                Vector2 dir = Rotate(down, Random.Range(-12f, 12f));
                d.vel = dir * speed;
                d.age = 0f;
                d.life = Random.Range(1.6f, 3.2f);
                d.streak = Random.value < streakRatio;
                d.maxAlpha = Mathf.Lerp(dustAlphaBottom, dustAlphaTop, _progress) * (d.streak ? 0.55f : 1f) * Random.Range(0.6f, 1f);
                float size = Random.Range(0.14f, 0.42f);
                d.sr.transform.position = new Vector3(pos.x, pos.y, -0.5f);
                d.sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                d.sr.transform.localScale = d.streak ? new Vector3(size * Random.Range(8f, 14f), size * 0.35f, 1f) : new Vector3(size, size, 1f);
                d.sr.gameObject.SetActive(true);
            }
        }

        foreach (Dust d in _dust)
        {
            if (!d.sr.gameObject.activeSelf) continue;
            d.age += dt;
            if (d.age >= d.life) { d.sr.gameObject.SetActive(false); continue; }
            float u = d.age / d.life;
            Vector3 p = d.sr.transform.position;
            p += (Vector3)(d.vel * dt);
            p.y += Mathf.Sin((d.age + p.x) * 3f) * 0.15f * dt;   // 一點點飄
            d.sr.transform.position = p;
            float a = d.maxAlpha * Mathf.Min(1f, u * 5f) * Mathf.Min(1f, (1f - u) * 3f) * Mathf.Max(_fade, 0f);
            Color col = dustColor; col.a = a;
            d.sr.color = col;
        }
    }

    private Dust GetDust()
    {
        foreach (Dust d in _dust) if (!d.sr.gameObject.activeSelf) return d;
        if (_dust.Count >= dustMax) return null;
        GameObject go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _dot;
        sr.sortingOrder = dustSortingOrder;
        go.SetActive(false);
        Dust nd = new Dust { sr = sr };
        _dust.Add(nd);
        return nd;
    }

    // ── 2. 鏡頭 ────────────────────────────────────────
    private void UpdateCamera()
    {
        Vector2 target = cameraLookUphill ? cameraLookOffset * _fade : Vector2.zero;
        _lookOffset = Vector2.MoveTowards(_lookOffset, target, cameraLookSpeed * Time.deltaTime);
        CameraTargetXFollower.AmbientLookOffset = _lookOffset;
    }

    private static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private static Sprite BuildSoftDot()
    {
        const int n = 32;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);   // 1 單位大小，用 scale 控制
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawLine(surfaceA, surfaceB);
        Gizmos.DrawWireSphere(surfaceA, 0.5f);
        Gizmos.DrawWireSphere(surfaceB, 0.5f);
    }
}
