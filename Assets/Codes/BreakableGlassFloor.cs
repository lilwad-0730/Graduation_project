using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於可踩空/可碎裂的玻璃地磚 (例如 'glass floor_0 breakable')。
/// 當玩家接觸或踩上地磚時，無條件觸發鏡牆切片碎裂與重力崩塌特效。
/// </summary>
[RequireComponent(typeof(Destructible))]
public class BreakableGlassFloor : MonoBehaviour, IResettable
{
    [Header("踩踏碎裂設定")]
    [Tooltip("踩上地磚後，過幾秒開始向下下沉與碎裂 (秒)")]
    public float delayBeforeShatter = 2.0f;

    [Tooltip("踩上時是否有些微震動/下沉預兆")]
    public bool enableWarningShake = true;

    [Tooltip("震動幅度")]
    public float shakeIntensity = 0.05f;

    [Header("★1101 看得出會碎（52 章 B4：會碎的 5 片跟一般玻璃同圖同透明度）")]
    [Tooltip("踩上去之前，會碎的玻璃就跟一般玻璃不一樣：透明度慢慢忽明忽暗（Shimmer Min Alpha ～ 原本），每片相位不同。踩上、碎了、重置時回到原本。關掉＝跟原本一樣")]
    public bool shimmerBeforeBreak = true;
    [Tooltip("最暗時剩原本透明度的幾成")]
    [Range(0.3f, 1f)] public float shimmerMinAlpha = 0.6f;
    [Tooltip("一次明暗的秒數")]
    public float shimmerPeriod = 1.7f;

    private bool isTriggered = false;
    private Vector3 originalPosition;
    private Destructible destructible;

    private SpriteRenderer[] _shimmerSrs;
    private Color[] _shimmerBase;
    private float _shimmerPhase;
    private bool _shimmerApplied;

    private void Awake()
    {
        originalPosition = transform.position;
        destructible = GetComponent<Destructible>();
        _shimmerPhase = (Mathf.Abs(originalPosition.x * 7.31f + originalPosition.y * 3.17f)) % 6.2831853f;   // 每片不同相位
    }

    private void Update()
    {
        if (!shimmerBeforeBreak || isTriggered || shimmerPeriod <= 0.05f) { if (_shimmerApplied) RestoreShimmer(); return; }
        if (_shimmerSrs == null)
        {
            _shimmerSrs = GetComponentsInChildren<SpriteRenderer>(true);
            _shimmerBase = new Color[_shimmerSrs.Length];
            for (int i = 0; i < _shimmerSrs.Length; i++) if (_shimmerSrs[i] != null) _shimmerBase[i] = _shimmerSrs[i].color;
        }
        float w = 0.5f + 0.5f * Mathf.Sin(Time.time * 6.2831853f / shimmerPeriod + _shimmerPhase);
        float k = Mathf.Lerp(shimmerMinAlpha, 1f, w);
        for (int i = 0; i < _shimmerSrs.Length; i++)
        {
            if (_shimmerSrs[i] == null) continue;
            Color b = _shimmerBase[i];
            _shimmerSrs[i].color = new Color(b.r, b.g, b.b, b.a * k);
        }
        _shimmerApplied = true;
    }

    private void RestoreShimmer()
    {
        if (_shimmerSrs != null)
            for (int i = 0; i < _shimmerSrs.Length; i++) if (_shimmerSrs[i] != null) _shimmerSrs[i].color = _shimmerBase[i];
        _shimmerApplied = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsPlayer(collision.gameObject))
        {
            TriggerBreakSequence();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other.gameObject))
        {
            TriggerBreakSequence();
        }
    }

    private bool IsPlayer(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("Player")) return true;
        if (go.name.ToLower().Contains("player") || go.GetComponent<PlayerMovement>() != null) return true;
        return false;
    }

    public void TriggerBreakSequence()
    {
        if (isTriggered) return;
        if (CandlePlatformGuard.IsHeld(this)) return;   // ★1110 燭火還沒點燃完成：這片先不崩落（見 CandlePlatformGuard）
        isTriggered = true;
        RestoreShimmer();   // ★1101 碎片用原本的顏色

        GlassShatterFX fx = GetComponent<GlassShatterFX>();
        if (fx != null)
        {
            fx.TriggerBreakSequence();
            return;
        }

        StartCoroutine(ShatterRoutine());
    }

    private IEnumerator ShatterRoutine()
    {
        float timer = 0f;

        while (timer < delayBeforeShatter)
        {
            timer += Time.deltaTime;
            if (enableWarningShake)
            {
                Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity;
                shakeOffset.z = 0f;
                transform.position = originalPosition + Vector3.down * (timer * 0.4f) + shakeOffset;
            }
            yield return null;
        }

        if (destructible != null)
        {
            destructible.Shatter();
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public void ResetToInitialState()
    {
        // 存檔點進度檢查：若當前存檔點已推進到本地磚右側且地磚已碎裂，則保持碎裂消失
        Vector3 currentCheckpoint = PlayerRespawnSystem.ActiveRespawnPosition;
        if (currentCheckpoint != Vector3.zero && (originalPosition.x < currentCheckpoint.x - 1.0f))
        {
            if (isTriggered)
            {
                gameObject.SetActive(false);
                return;
            }
        }

        StopAllCoroutines();
        isTriggered = false;
        RestoreShimmer();   // ★1101
        transform.position = originalPosition;
        if (destructible != null)
        {
            destructible.ResetToInitialState();
        }
        gameObject.SetActive(true);
    }
}
