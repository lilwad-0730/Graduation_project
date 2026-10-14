using System.Collections;
using UnityEngine;

/// <summary>
/// ★1012 棉花堡上層：可吸收的氣球。
/// 飄著（上下輕晃、左右慢慢漂、微微搖擺）、發著柔光吸引玩家；主角靠近時會輕輕往主角那邊靠一點。
/// 主角的碰撞體碰到它就吸收：氣球脹開一下消失 → 嬰兒笑聲 → 在消失的位置浮出一張字卡（內容由 cardText 決定）。
/// 場景物件由 Tools ▸ 棉花堡上層 ▸ 建立氣球 建好，這支不用手動掛。
/// </summary>
public class UpperCastleBalloon : MonoBehaviour
{
    [Header("內容")]
    [TextArea(2, 4), Tooltip("吸收後浮出的字卡文字（換行請手動排）")]
    public string cardText = "";
    [Tooltip("火花與消失動畫的顏色")]
    public Color tint = new Color(1f, 0.8f, 0.75f, 1f);

    [Header("外觀（建立工具會自動接好）")]
    public SpriteRenderer body;
    public SpriteRenderer stringRenderer;
    public SpriteRenderer glow;
    [Tooltip("★1015 外圈大光暈（比 Glow 更大更淡，慢慢呼吸，讓玩家遠遠就看到這顆可以碰）")]
    public SpriteRenderer halo;

    [Header("飄動")]
    public float bobAmplitude = 0.35f;
    public float bobSpeed = 0.9f;
    [Tooltip("左右漂移的幅度（公尺）")]
    public float driftAmplitude = 1.1f;
    public float driftSpeed = 0.22f;
    [Tooltip("氣球身體左右搖擺的角度")]
    public float swayDegrees = 4f;
    [Tooltip("柔光呼吸的快慢")]
    public float glowPulseSpeed = 1.6f;

    [Header("吸引玩家")]
    [Tooltip("主角靠近到這個距離（公尺）內，氣球會輕輕往她靠")]
    public float attractRange = 7f;
    [Tooltip("最多往主角靠多遠（公尺）")]
    public float attractMaxOffset = 1.2f;

    [Header("觸碰")]
    [Tooltip("主角碰撞體邊緣離氣球中心多近（公尺）算碰到")]
    public float touchRadius = 1.1f;

    private Vector3 _home;
    private float _phase;
    private float _attractX;
    private bool _popped;
    private float _glowBaseAlpha = 0.4f;
    private float _haloBaseAlpha = 0.2f;

    private void Start()
    {
        _home = transform.position;
        _phase = Random.value * 100f;
        if (glow != null) _glowBaseAlpha = glow.color.a > 0.01f ? glow.color.a : 0.4f;
        if (halo != null) _haloBaseAlpha = halo.color.a > 0.01f ? halo.color.a : 0.2f;
    }

    private void Update()
    {
        if (_popped) return;

        float t = Time.time + _phase;
        float bob = Mathf.Sin(t * bobSpeed) * bobAmplitude;
        float drift = Mathf.Sin(t * driftSpeed) * driftAmplitude;

        UpperCastleBalloonField field = UpperCastleBalloonField.Instance;
        Bounds pb = default;
        bool hasPlayer = field != null && field.TryGetPlayerBounds(out pb);

        // 主角靠近就輕輕往她靠（平滑，不會突然閃）
        float wantAttract = 0f;
        if (hasPlayer)
        {
            float dx = pb.center.x - (_home.x + drift);
            if (Mathf.Abs(dx) < attractRange)
                wantAttract = Mathf.Sign(dx) * attractMaxOffset * (1f - Mathf.Abs(dx) / attractRange);
        }
        _attractX = Mathf.Lerp(_attractX, wantAttract, 1f - Mathf.Exp(-1.8f * Time.deltaTime));

        transform.position = new Vector3(_home.x + drift + _attractX, _home.y + bob, _home.z);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.8f) * swayDegrees);

        if (stringRenderer != null)
        {
            // 繩子比身體慢半拍地擺，才有被氣球拉著的感覺
            stringRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Sin(t * 0.8f - 0.7f) * swayDegrees * 1.8f);
        }
        if (glow != null)
        {
            Color c = glow.color;
            c.a = _glowBaseAlpha * (0.65f + 0.35f * Mathf.Sin(t * glowPulseSpeed));
            glow.color = c;
        }
        if (halo != null)
        {
            // 外圈比內圈慢半拍、一起一伏，像呼吸
            Color c = halo.color;
            c.a = _haloBaseAlpha * (0.55f + 0.45f * Mathf.Sin(t * glowPulseSpeed * 0.7f + 1.3f));
            halo.color = c;
        }

        // 觸碰：主角碰撞體的邊緣到氣球中心的距離（忽略 Z，這是 2.5D）
        if (hasPlayer)
        {
            Vector3 center = transform.position; center.z = pb.center.z;
            if (pb.SqrDistance(center) <= touchRadius * touchRadius) Pop();
        }
    }

    public void Pop()
    {
        if (_popped) return;
        _popped = true;

        UpperCastleBalloonField field = UpperCastleBalloonField.Instance;
        if (field != null) field.OnBalloonCollected(transform.position, tint, cardText);

        StartCoroutine(PopRoutine());
    }

    // 氣球輕輕脹開一下、柔柔地淡掉（不是爆破，是被吸收）
    private IEnumerator PopRoutine()
    {
        float dur = 0.28f;
        float t = 0f;
        Vector3 baseScale = transform.localScale;
        Color bodyC = body != null ? body.color : Color.white;
        Color strC = stringRenderer != null ? stringRenderer.color : Color.white;
        Color glowC = glow != null ? glow.color : Color.white;
        Color haloC = halo != null ? halo.color : Color.white;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = 1f - (1f - k) * (1f - k);
            transform.localScale = baseScale * (1f + 0.35f * e);
            float a = 1f - e;
            if (body != null) { Color c = bodyC; c.a *= a; body.color = c; }
            if (stringRenderer != null) { Color c = strC; c.a *= a; stringRenderer.color = c; }
            if (glow != null) { Color c = glowC; c.a = Mathf.Lerp(glowC.a, 0f, e); glow.color = c; }
            if (halo != null) { Color c = haloC; c.a = Mathf.Lerp(haloC.a, 0f, e); halo.color = c; }
            yield return null;
        }
        gameObject.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.4f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, touchRadius);
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, attractRange);
    }
}
