using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於氣氛踩踏龜裂地磚 (例如 'glass floor_0 (1)')。
/// 當玩家踏上時，在物件表面產生不規則鏡面裂紋。
/// 自動適應並百分之百吻合地磚 Sprite 的長寬比例與邊界，絕不上出界或拉伸。
/// </summary>
public class AtmosphericCrackFloor : MonoBehaviour, IResettable
{
    [Header("龜裂視覺與數量設定")]
    [Tooltip("龜裂撞擊中心數量 (數量越多，裂痕涵蓋範圍與密度越密)")]
    [Range(1, 35)]
    public int crackCenterCount = 8;

    [Tooltip("每個撞擊中心放射出的主裂痕分支數量 (數值越大裂痕越密)")]
    [Range(1, 35)]
    public int branchesPerCenter = 14;

    [Tooltip("裂痕分支伸展長度與細節段數")]
    [Range(5, 50)]
    public int crackStepCount = 30;

    [Tooltip("龜裂線條的顏色與折射光澤")]
    public Color crackLineColor = new Color(0.95f, 0.98f, 1.0f, 0.95f);

    [Tooltip("龜裂時顯示的不規則裂紋 Sprite (若留空，腳本會依地磚尺寸自動生成完美吻合的蛛網鏡裂貼圖)")]
    public Sprite crackOverlaySprite;

    [Header("踩踏與震動設定")]
    [Tooltip("觸發龜裂時的微幅震動時間 (秒)")]
    public float warningShakeDuration = 0.15f;

    [Tooltip("微幅震動幅度")]
    public float shakeIntensity = 0.03f;

    [Header("🎵 龜裂音效 (Crack SFX)")]
    [Tooltip("踩踏產生龜裂時播放的音效 (例如 玻璃館_玻璃輕脆.wav / 玻璃碎裂.mp3 / 玻璃館_踩玻璃.mp3)")]
    public AudioClip crackSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    private bool hasCracked = false;
    private Vector3 originalPosition;
    private GameObject crackOverlayObject;
    private SpriteRenderer overlaySr;

    private void Awake()
    {
        originalPosition = transform.position;
        CreateOverlayObject();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!hasCracked && IsPlayer(collision.gameObject))
        {
            TriggerCrackEffect();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!hasCracked && IsPlayer(other.gameObject))
        {
            TriggerCrackEffect();
        }
    }

    private bool IsPlayer(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("Player")) return true;
        if (go.name.ToLower().Contains("player") || go.GetComponent<PlayerMovement>() != null) return true;
        return false;
    }

    public void TriggerCrackEffect()
    {
        if (hasCracked) return;
        hasCracked = true;

        if (crackSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(crackSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(crackSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        StartCoroutine(CrackRoutine());
    }

    private IEnumerator CrackRoutine()
    {
        float timer = 0f;
        while (timer < warningShakeDuration)
        {
            timer += Time.deltaTime;
            Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity;
            shakeOffset.z = 0f;
            transform.position = originalPosition + shakeOffset;
            yield return null;
        }
        transform.position = originalPosition;

        if (crackOverlayObject != null)
        {
            crackOverlayObject.SetActive(true);
        }
    }

    private void CreateOverlayObject()
    {
        if (crackOverlayObject != null) return;

        crackOverlayObject = new GameObject("GlassCrackOverlay");
        crackOverlayObject.transform.SetParent(transform);
        crackOverlayObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        crackOverlayObject.transform.localRotation = Quaternion.identity;
        crackOverlayObject.transform.localScale = Vector3.one;

        overlaySr = crackOverlayObject.AddComponent<SpriteRenderer>();
        
        SpriteRenderer parentSr = GetComponent<SpriteRenderer>();
        if (parentSr == null) parentSr = GetComponentInChildren<SpriteRenderer>();

        if (parentSr != null)
        {
            overlaySr.sortingOrder = parentSr.sortingOrder + 2;
            overlaySr.drawMode = parentSr.drawMode;
            overlaySr.size = parentSr.size;
            overlaySr.tileMode = parentSr.tileMode;
        }

        if (crackOverlaySprite == null)
        {
            crackOverlaySprite = GenerateProceduralCrackSprite(parentSr);
        }
        overlaySr.sprite = crackOverlaySprite;
        crackOverlayObject.SetActive(false);
    }

    /// <summary>
    /// 自動讀取地磚的邊界尺寸 (bounds size)，動態生成 100% 比例吻合且不超出邊界的不規則蛛網裂紋貼圖
    /// </summary>
    public Sprite GenerateProceduralCrackSprite(SpriteRenderer parentSr = null)
    {
        float aspect = 4.0f;
        if (parentSr != null && parentSr.bounds.size.y > 0)
        {
            aspect = parentSr.bounds.size.x / parentSr.bounds.size.y;
        }
        else if (parentSr != null && parentSr.sprite != null)
        {
            aspect = parentSr.sprite.rect.width / parentSr.sprite.rect.height;
        }

        int height = 160;
        int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 128, 2048);
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        
        Color[] clear = new Color[width * height];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        tex.SetPixels(clear);

        Random.InitState(GetInstanceID());

        for (int c = 0; c < crackCenterCount; c++)
        {
            Vector2 center = new Vector2(Random.Range(width * 0.08f, width * 0.92f), Random.Range(height * 0.15f, height * 0.85f));
            for (int b = 0; b < branchesPerCenter; b++)
            {
                float angle = (360f / branchesPerCenter * b + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 current = center;

                float maxBranchLen = Mathf.Min(width, height) * 0.45f;
                float stepDist = maxBranchLen / crackStepCount;

                for (int s = 0; s < crackStepCount; s++)
                {
                    Vector2 next = current + dir * Random.Range(stepDist * 0.6f, stepDist * 1.4f) + new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f));
                    
                    next.x = Mathf.Clamp(next.x, 3f, width - 4f);
                    next.y = Mathf.Clamp(next.y, 3f, height - 4f);

                    DrawLine(tex, (int)current.x, (int)current.y, (int)next.x, (int)next.y, crackLineColor);
                    current = next;
                }
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
    }

    private void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = (dx > dy ? dx : -dy) / 2, e2;

        while (true)
        {
            if (x0 >= 2 && x0 < tex.width - 2 && y0 >= 2 && y0 < tex.height - 2)
            {
                tex.SetPixel(x0, y0, col);
                if (x0 + 1 < tex.width - 2) tex.SetPixel(x0 + 1, y0, col * 0.75f);
                if (y0 + 1 < tex.height - 2) tex.SetPixel(x0, y0 + 1, col * 0.75f);
            }
            if (x0 == x1 && y0 == y1) break;
            e2 = err;
            if (e2 > -dx) { err -= dy; x0 += sx; }
            if (e2 < dy) { err += dx; y0 += sy; }
        }
    }

    [ContextMenu("Randomize Crack Parameters (5-30)")]
    public void RandomizeCrackParameters()
    {
        crackCenterCount = Random.Range(5, 31);
        branchesPerCenter = Random.Range(5, 31);
        crackStepCount = Random.Range(5, 31);
    }

    public void ResetToInitialState()
    {
        StopAllCoroutines();
        hasCracked = false;
        transform.position = originalPosition;
        if (crackOverlayObject != null)
        {
            crackOverlayObject.SetActive(false);
        }
    }
}
