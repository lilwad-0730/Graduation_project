using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// ★1012 棉花堡上層氣球：共用的「場」。
///
/// 主角上了階梯、進到棉花堡上層後，雲海上飄著一些粉嫩的氣球：
///   ・可吸收的氣球（UpperCastleBalloon）：飄著、發著柔光，主角碰到就吸收——
///     氣球輕輕脹開消失、嬰兒的笑聲、在氣球消失的地方浮出一張字卡（產婦對生產的美好幻想），停一下再淡掉。
///   ・後景氣球（UpperCastleBackdropBalloon）：只是鋪底，讓上層不單調，不能碰。
/// 這支掛在 UpperCastle_Balloons 根物件上，放字卡／笑聲／火花這些兩種氣球共用的東西。
/// 場景物件由 Tools ▸ 棉花堡上層 ▸ 建立氣球 一鍵建好。
/// </summary>
public class UpperCastleBalloonField : MonoBehaviour
{
    public static UpperCastleBalloonField Instance { get; private set; }

    [Header("字卡")]
    [Tooltip("字卡用的字型。留空會依序找：場景裡文字卡播放器用的字型 → Msjh_SDF → TMP 預設")]
    public TMP_FontAsset fontAsset;
    [Tooltip("字卡底圖（奶油色圓角卡片，需設好 9-slice 邊界）")]
    public Sprite cardSprite;
    public Color textColor = new Color(0.40f, 0.31f, 0.31f, 1f);
    [Tooltip("字卡文字大小（TMP 世界座標字級，10 約等於 1 個單位高）")]
    public float cardFontSize = 10f;
    [Tooltip("字卡在氣球消失位置的上方多少（公尺）")]
    public float cardOffsetY = 2.3f;
    [Tooltip("字卡停留的最短秒數")]
    public float cardHoldMin = 2.4f;
    [Tooltip("每多一個字，停留多加幾秒（字多的卡停久一點）")]
    public float cardHoldPerChar = 0.07f;
    public float cardFadeIn = 0.35f;
    public float cardFadeOut = 0.7f;
    [Tooltip("字卡出現到消失的過程中，慢慢往上飄多少（公尺）")]
    public float cardRise = 0.9f;
    [Tooltip("字卡跟畫面邊緣至少留多少（公尺），避免字卡被切掉")]
    public float cardScreenMargin = 0.6f;

    [Header("音效")]
    [Tooltip("吸收氣球時的嬰兒笑聲。目前是「嬰兒笑聲.mp3」裁出第 1.3~2.1 秒（第二段起伏），整理過頻率與音量、加了很淡的殘響")]
    public AudioClip laughClip;
    [Range(0f, 1f)] public float laughVolume = 0.9f;
    [Tooltip("每次笑聲音高隨機的幅度（±），避免連續吸收聽起來一模一樣")]
    [Range(0f, 0.3f)] public float laughPitchJitter = 0.1f;

    [Header("吸收特效")]
    public Sprite sparkSprite;
    public int sparkCount = 14;
    public float sparkLife = 0.8f;

    /// <summary>目前已吸收幾顆（除錯、之後想做收集數用）。</summary>
    public int CollectedCount { get; private set; }

    private Transform _player;
    private Collider _playerCol;

    private void Awake()
    {
        Instance = this;
        ResolveFont();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── 字型 ──────────────────────────────────────────────
    private static bool IsFontValid(TMP_FontAsset f)
    {
        return f != null && f.atlasTextures != null && f.atlasTextures.Length > 0 && f.atlasTextures[0] != null;
    }

    private void ResolveFont()
    {
        if (IsFontValid(fontAsset)) return;

        // 跟文字卡播放器借字型（各關都掛了能顯示中文的 Msjh_SDF）。用 Find 不用 Instance：找不到不會自己建一個
        StoryCardPlayer scp = FindFirstObjectByType<StoryCardPlayer>();
        if (scp != null && IsFontValid(scp.fontAsset)) { fontAsset = scp.fontAsset; return; }

        TMP_FontAsset f = Resources.Load<TMP_FontAsset>("Msjh_SDF");
        if (IsFontValid(f)) { fontAsset = f; return; }

        fontAsset = TMP_Settings.defaultFontAsset;
    }

    // ── 主角 ──────────────────────────────────────────────
    /// <summary>主角碰撞體的範圍；找不到主角就回 false。</summary>
    public bool TryGetPlayerBounds(out Bounds bounds)
    {
        if (_player == null)
        {
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null)
            {
                _player = pm.transform;
                _playerCol = null;
                foreach (Collider c in pm.GetComponentsInChildren<Collider>())
                {
                    if (c != null && c.enabled && !c.isTrigger) { _playerCol = c; break; }
                }
            }
        }

        if (_playerCol != null && _playerCol.enabled)
        {
            bounds = _playerCol.bounds;
            return true;
        }
        if (_player != null)
        {
            bounds = new Bounds(_player.position, new Vector3(1f, 2f, 1f));
            return true;
        }
        bounds = default;
        return false;
    }

    // ── 吸收 ──────────────────────────────────────────────
    /// <summary>某顆氣球被吸收了：笑聲、火花、字卡。氣球本身的消失動畫由它自己做。</summary>
    public void OnBalloonCollected(Vector3 worldPos, Color tint, string cardText)
    {
        CollectedCount++;
        PlayLaugh();
        SpawnSparks(worldPos, tint);
        if (!string.IsNullOrEmpty(cardText)) StartCoroutine(CardRoutine(worldPos, cardText));
    }

    private void PlayLaugh()
    {
        if (laughClip == null) return;

        GameObject go = new GameObject("BalloonLaugh");
        go.transform.SetParent(transform, false);
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = laughClip;
        src.playOnAwake = false;
        src.spatialBlend = 0f;   // 2D：不管氣球在畫面哪裡，都清楚聽到
        src.volume = AudioManager.ScaleSfx(laughVolume);
        src.pitch = 1f + Random.Range(-laughPitchJitter, laughPitchJitter);
        src.Play();
        Destroy(go, laughClip.length / Mathf.Max(0.5f, src.pitch) + 0.3f);
    }

    private void SpawnSparks(Vector3 pos, Color tint)
    {
        if (sparkSprite == null || sparkCount <= 0) return;
        StartCoroutine(SparkRoutine(pos, tint));
    }

    private IEnumerator SparkRoutine(Vector3 pos, Color tint)
    {
        int n = sparkCount;
        var rs = new SpriteRenderer[n];
        var vel = new Vector3[n];
        var spin = new float[n];
        var size = new float[n];
        Color warm = Color.Lerp(tint, Color.white, 0.55f);

        for (int i = 0; i < n; i++)
        {
            GameObject go = new GameObject("Spark");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sparkSprite;
            sr.sortingOrder = 30;
            sr.color = warm;
            rs[i] = sr;
            float ang = (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
            float spd = Random.Range(1.2f, 3.2f);
            vel[i] = new Vector3(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd + 0.6f, 0f);
            spin[i] = Random.Range(-90f, 90f);
            size[i] = Random.Range(0.25f, 0.6f);
        }

        float t = 0f;
        while (t < sparkLife)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / sparkLife);
            for (int i = 0; i < n; i++)
            {
                if (rs[i] == null) continue;
                vel[i] *= (1f - 2.2f * Time.deltaTime);
                vel[i].y += 0.9f * Time.deltaTime;   // 微微往上飄，像泡泡的碎光
                rs[i].transform.position += vel[i] * Time.deltaTime;
                rs[i].transform.Rotate(0f, 0f, spin[i] * Time.deltaTime);
                float s = size[i] * (1f - 0.5f * k);
                rs[i].transform.localScale = new Vector3(s, s, 1f);
                Color c = warm; c.a = (1f - k) * (1f - k);
                rs[i].color = c;
            }
            yield return null;
        }
        for (int i = 0; i < n; i++) if (rs[i] != null) Destroy(rs[i].gameObject);
    }

    // ── 字卡 ──────────────────────────────────────────────
    private IEnumerator CardRoutine(Vector3 balloonPos, string text)
    {
        ResolveFont();

        GameObject root = new GameObject("BalloonCard");
        root.transform.SetParent(transform, false);

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        TextMeshPro tmp = textGo.AddComponent<TextMeshPro>();
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.text = text;
        tmp.fontSize = cardFontSize;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;     // 換行是手排的
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.lineSpacing = 8f;
        tmp.characterSpacing = 2f;
        tmp.sortingOrder = 41;
        tmp.raycastTarget = false;
        tmp.ForceMeshUpdate();

        Vector2 textSize = new Vector2(tmp.preferredWidth, tmp.preferredHeight);
        Vector2 cardSize = textSize + new Vector2(2.2f, 1.7f);   // 卡片留白（含 9-slice 外圍的陰影）

        if (cardSprite != null)
        {
            SpriteRenderer bg = root.AddComponent<SpriteRenderer>();
            bg.sprite = cardSprite;
            bg.drawMode = SpriteDrawMode.Sliced;
            bg.size = cardSize;
            bg.sortingOrder = 40;
        }

        // 位置：氣球消失處的上方，夾在畫面範圍內
        Vector3 start = balloonPos + Vector3.up * cardOffsetY;
        start.z = balloonPos.z - 0.2f;
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
            float minX = cam.transform.position.x - halfW + cardSize.x * 0.5f + cardScreenMargin;
            float maxX = cam.transform.position.x + halfW - cardSize.x * 0.5f - cardScreenMargin;
            float minY = cam.transform.position.y - halfH + cardSize.y * 0.5f + cardScreenMargin;
            float maxY = cam.transform.position.y + halfH - cardSize.y * 0.5f - cardScreenMargin - cardRise;
            if (minX < maxX) start.x = Mathf.Clamp(start.x, minX, maxX);
            if (minY < maxY) start.y = Mathf.Clamp(start.y, minY, maxY);
        }
        root.transform.position = start;

        SpriteRenderer bgRenderer = root.GetComponent<SpriteRenderer>();
        float hold = Mathf.Max(cardHoldMin, text.Length * cardHoldPerChar);
        float total = cardFadeIn + hold + cardFadeOut;

        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;
            float a;
            if (t < cardFadeIn) a = Mathf.SmoothStep(0f, 1f, t / cardFadeIn);
            else if (t > cardFadeIn + hold) a = 1f - Mathf.SmoothStep(0f, 1f, (t - cardFadeIn - hold) / cardFadeOut);
            else a = 1f;

            if (bgRenderer != null) { Color c = bgRenderer.color; c.a = a; bgRenderer.color = c; }
            tmp.alpha = a;
            // 慢慢往上飄，像氣球帶走的一個念頭
            root.transform.position = start + Vector3.up * (cardRise * Mathf.Clamp01(t / total));
            yield return null;
        }
        Destroy(root);
    }
}
