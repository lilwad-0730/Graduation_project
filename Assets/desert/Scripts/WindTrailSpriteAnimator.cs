using UnityEngine;

/// <summary>
/// 輕盈高雅 S 曲線風痕氣流動畫控制器 (Elegant Slender Wind Trail Animator)。
/// 參考用戶提供之 Stylized Environment VFX 官方原圖精確打造：
/// - 純白亮麗雙端尖銳 S 曲線風痕 (Crisp White Curved Ribbon Swooshes)
/// - 清晰顯眼線寬 (Width 2.0~4.0) 與流暢波浪起伏
/// </summary>
public class WindTrailSpriteAnimator : MonoBehaviour
{
    [Header("平移動態與速度")]
    public float flySpeed = 18.0f;
    public float waveAmplitude = 1.8f;
    public float waveFrequency = 2.2f;

    [Header("顯眼優雅弧線尺寸設定")]
    public float lifetime = 3.5f;
    public Vector2 scaleXRange = new Vector2(20.0f, 38.0f); // 弧線長度
    public Vector2 scaleYRange = new Vector2(1.8f, 3.8f);   // 顯眼流線寬度 (完美貼合原圖)

    private SpriteRenderer sr;
    private float age = 0f;
    private float initialY = 0f;
    private float wavePhase = 0f;
    private Color baseColor;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
    }

    public void Init(Vector3 spawnWorldPos, Sprite spriteAsset, float speed, float sizeMult, float angle)
    {
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();

        sr.sprite = spriteAsset;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 500; // 最前端亮麗渲染

        transform.position = spawnWorldPos;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        initialY = spawnWorldPos.y;
        flySpeed = speed;
        wavePhase = Random.Range(0f, 10f);

        // 鮮明S弧線尺寸
        float sx = Random.Range(scaleXRange.x, scaleXRange.y) * sizeMult;
        float sy = Random.Range(scaleYRange.x, scaleYRange.y) * sizeMult;
        transform.localScale = new Vector3(sx, sy, 1f);

        // 原圖極致白亮發光色彩 (Crisp Bright White)
        baseColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);
        sr.color = baseColor;
        age = 0f;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // 1. 水平向左流暢平移
        float newX = transform.position.x - flySpeed * Time.deltaTime;

        // 2. 柔和 S 形波浪起伏
        float newY = initialY + Mathf.Sin(age * waveFrequency + wavePhase) * waveAmplitude;

        transform.position = new Vector3(newX, newY, transform.position.z);

        // 3. 高質感羽化淡入與淡出 (0.1s 快速顯現)
        float progress = age / lifetime;
        float alpha = baseColor.a;

        if (progress < 0.08f)
        {
            alpha = (progress / 0.08f) * baseColor.a;
        }
        else if (progress > 0.80f)
        {
            alpha = ((1.0f - progress) / 0.20f) * baseColor.a;
        }

        if (sr != null)
        {
            sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }
    }
}
