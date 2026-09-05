using System.Collections;
using UnityEngine;

/// <summary>
/// ★0905 拍三開場：巨鳥的影子掠過地面（GDD §6-3「預告」＝Q5-S5 鳥影／Q5-S6 她抬頭）。
/// 天上什麼都沒有，只有一道影子從左掃到右；那幾秒風停、所有鳥不動（世界屏息）；鏡頭第一次往上抬。
/// 不做「奪取」，不改 0904「鳥維持殺死」的定案——這只是一個演出觸發器。
/// 由 DesertBeatDirector 自動放在 x≈137（掩體帶剛結束、空地開始）。影子是程式生成的柔邊貼圖，不用素材。
/// </summary>
[DisallowMultipleComponent]
public class GiantShadowPass : MonoBehaviour
{
    [Header("觸發")]
    public bool oneShot = true;

    [Header("屏息")]
    [Tooltip("風保持平靜、鳥不攻擊的秒數")]
    public float holdCalmSeconds = 6f;

    [Header("影子")]
    [Tooltip("影子從畫面左外掃到右外要幾秒")]
    public float sweepSeconds = 3.2f;
    public float shadowLength = 18f;
    public float shadowHeight = 5f;
    public Color shadowColor = new Color(0.16f, 0.09f, 0.06f, 0.42f);
    [Tooltip("影子貼在地面上方多少")]
    public float groundOffset = 0.05f;
    public float zPosition = 0f;
    public int sortingOrder = 3;
    [Tooltip("掃過時影子的 y 漂移（正＝往上）；巨鳥是斜著飛過去的")]
    public float sweepYDrift = 1.2f;

    [Header("鏡頭抬頭（CameraTargetXFollower.desertFixedY 暫時加高）")]
    public bool liftCamera = true;
    public float cameraLift = 2.6f;
    public float cameraLiftDelay = 0.6f;
    public float cameraLiftSeconds = 1.2f;
    public float cameraHoldSeconds = 1.8f;
    public float cameraReturnSeconds = 1.8f;

    [Header("音（可留空）")]
    public AudioClip passClip;
    public float passVolume = 0.8f;

    private bool _fired = false;
    private Texture2D _tex;
    private Sprite _sprite;
    private GameObject _shadow;

    public static GiantShadowPass Install(float x)
    {
        GiantShadowPass existing = FindFirstObjectByType<GiantShadowPass>();
        if (existing != null) return existing;

        GameObject go = new GameObject("GiantShadowPass (自動生成)");
        go.transform.position = new Vector3(x, 0f, 0f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(3f, 80f, 30f);
        return go.AddComponent<GiantShadowPass>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_fired && oneShot) return;
        if (!DesertBeatDirector.IsPlayerObject(other.gameObject)) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;
        _fired = true;
        StartCoroutine(Play(other.transform));
    }

    private IEnumerator Play(Transform player)
    {
        // 1. 世界屏息：風停、鳥不動
        WindGustSystem wind = WindGustSystem.Instance;
        if (wind != null) wind.HoldCalm(holdCalmSeconds);
        IndividualBirdEnemy.SuppressAllUntil = Mathf.Max(IndividualBirdEnemy.SuppressAllUntil, Time.time + holdCalmSeconds);

        if (passClip != null)
        {
            AudioSource.PlayClipAtPoint(passClip, player != null ? player.position : transform.position, passVolume * AudioManager.SfxVolume);
        }

        // 2. 影子從畫面左外掃到右外
        Camera cam = Camera.main;
        float halfW = 30f;
        float camX = player != null ? player.position.x : transform.position.x;
        if (cam != null)
        {
            camX = cam.transform.position.x;
            halfW = cam.orthographic ? cam.orthographicSize * cam.aspect : 30f;
        }
        float px = player != null ? player.position.x : transform.position.x;
        float groundY = DesertBeatDirector.Instance != null ? DesertBeatDirector.Instance.GroundYAt(px) : -6.3f;

        BuildShadow();
        float startX = camX - halfW - shadowLength * 0.6f;
        float endX = camX + halfW + shadowLength * 0.6f;
        _shadow.SetActive(true);

        if (liftCamera) StartCoroutine(LiftCamera());

        float t = 0f;
        SpriteRenderer sr = _shadow.GetComponent<SpriteRenderer>();
        while (t < sweepSeconds)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / sweepSeconds);
            float eased = p * p * (3f - 2f * p);
            float x = Mathf.Lerp(startX, endX, eased);
            float y = groundY + groundOffset + shadowHeight * 0.5f + sweepYDrift * p;
            _shadow.transform.position = new Vector3(x, y, zPosition);

            // 進出畫面時淡入淡出，避免硬邊
            float edge = Mathf.Min(p / 0.15f, (1f - p) / 0.15f);
            Color c = shadowColor;
            c.a = shadowColor.a * Mathf.Clamp01(edge);
            if (sr != null) sr.color = c;
            yield return null;
        }
        _shadow.SetActive(false);
    }

    private IEnumerator LiftCamera()
    {
        CameraTargetXFollower follower = CameraTargetXFollower.Instance;
        if (follower == null) yield break;

        yield return new WaitForSeconds(cameraLiftDelay);
        float baseY = follower.desertFixedY;
        float t = 0f;
        while (t < cameraLiftSeconds)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / cameraLiftSeconds);
            follower.desertFixedY = baseY + cameraLift * (p * p * (3f - 2f * p));
            yield return null;
        }
        follower.desertFixedY = baseY + cameraLift;
        yield return new WaitForSeconds(cameraHoldSeconds);
        t = 0f;
        while (t < cameraReturnSeconds)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / cameraReturnSeconds);
            follower.desertFixedY = baseY + cameraLift * (1f - (p * p * (3f - 2f * p)));
            yield return null;
        }
        follower.desertFixedY = baseY;
    }

    private void BuildShadow()
    {
        if (_shadow != null) return;

        int w = 256, h = 96;
        _tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        _tex.name = "Procedural_GiantShadow";
        _tex.hideFlags = HideFlags.HideAndDontSave;
        _tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;            // 0～1
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;        // 0～1
                float a = BirdSilhouette(u, v);
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a)));
            }
        }
        _tex.SetPixels32(px);
        _tex.Apply();

        _sprite = Sprite.Create(_tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "Procedural_GiantShadow_Sprite";
        _sprite.hideFlags = HideFlags.HideAndDontSave;

        _shadow = new GameObject("GiantShadow (自動生成)");
        _shadow.transform.SetParent(transform, false);
        SpriteRenderer sr = _shadow.AddComponent<SpriteRenderer>();
        sr.sprite = _sprite;
        sr.color = shadowColor;
        sr.sortingOrder = sortingOrder;
        // 貼圖 256×96 @100ppu ＝ 2.56×0.96 世界單位，縮放到想要的長寬
        _shadow.transform.localScale = new Vector3(shadowLength / 2.56f, shadowHeight / 0.96f, 1f);
        _shadow.SetActive(false);
    }

    /// <summary>一隻展翅大鳥從正上方投下的軟影：身體一個橢圓＋兩片往外變薄的翅膀，邊緣全部糊掉。</summary>
    private static float BirdSilhouette(float u, float v)
    {
        // 身體（中央橢圓）
        float bx = (u - 0.5f) / 0.16f;
        float by = (v - 0.5f) / 0.30f;
        float body = 1f - Mathf.Sqrt(bx * bx + by * by);

        // 翅膀（左右各一片，越往外越薄、略往後掠）
        float wingL = Wing(u, v, -1f);
        float wingR = Wing(u, v, 1f);

        float s = Mathf.Max(body, Mathf.Max(wingL, wingR));
        // 柔邊：把 0～0.35 的邊緣攤平成漸層
        float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s + 0.15f) / 0.5f));
        // 少許顆粒，避免太像貼紙
        float grain = Mathf.PerlinNoise(u * 9.1f, v * 7.3f) * 0.12f;
        return Mathf.Clamp01(a - grain);
    }

    private static float Wing(float u, float v, float side)
    {
        float du = (u - 0.5f) * side;           // 0＝身體中心，往外變大
        if (du < 0f) return 0f;
        float span = 0.48f;                     // 翅膀半長
        float t = Mathf.Clamp01(du / span);     // 0＝根部，1＝翼尖
        float halfThick = Mathf.Lerp(0.26f, 0.05f, t);           // 根部厚、翼尖薄
        float sweep = 0.10f * t * t;                              // 後掠：翼尖往 v 小的方向彎
        float dv = Mathf.Abs(v - (0.52f - sweep));
        float across = 1f - dv / Mathf.Max(0.01f, halfThick);
        float along = 1f - t;                                     // 沿翼展越外越淡
        return Mathf.Min(across, along + 0.35f);
    }

    private void OnDestroy()
    {
        if (_sprite != null) Destroy(_sprite);
        if (_tex != null) Destroy(_tex);
    }
}
