using UnityEngine;

// ★1109e 綠洲氣氛總控：海市蜃樓依玩家 x 淡入淡出、水面光暈呼吸、光點只在附近時才發射
// 掛在 Oasis_Ambience 根物件上；整個關掉根物件就全部消失
public class OasisAmbience : MonoBehaviour
{
    [Header("海市蜃樓")]
    public Renderer mirage;
    [Tooltip("玩家 x 到這之前完全看不到")] public float mirageStartX = 215f;
    [Tooltip("玩家 x 到這之後完全顯示")] public float mirageFullX = 230f;
    [Tooltip("玩家 x 超過這開始淡出")] public float mirageFadeOutX = 255f;
    [Tooltip("玩家 x 超過這完全消失")] public float mirageGoneX = 266f;

    [Header("水面光暈")]
    public SpriteRenderer glow;
    public Color glowTint = new Color(0.25f, 0.95f, 0.9f, 1f);
    public float glowAlpha = 0.15f;
    [Tooltip("呼吸幅度 (正負比例)")] public float glowBreath = 0.25f;
    [Tooltip("呼吸週期 (秒)")] public float glowPeriod = 4f;

    [Header("光點")]
    public ParticleSystem motes;
    public float pondCenterX = 281f;
    [Tooltip("玩家離池塘中心超過這距離就停止發射")] public float moteActiveRange = 45f;

    Transform player;
    MaterialPropertyBlock mpb;
    MaterialPropertyBlock glowMpb;
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    float glowBaseScaleX, glowBaseScaleY;
    bool motesOn = true;

    void Start()
    {
        mpb = new MaterialPropertyBlock();
        glowMpb = new MaterialPropertyBlock();
        if (glow != null) { glowBaseScaleX = glow.transform.localScale.x; glowBaseScaleY = glow.transform.localScale.y; }
    }

    void Update()
    {
        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
        }

        if (player != null)
        {
            float px = player.position.x;

            if (mirage != null)
            {
                float fin = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mirageStartX, mirageFullX, px));
                float fout = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mirageFadeOutX, mirageGoneX, px));
                float f = fin * fout;
                mirage.enabled = f > 0.001f;
                mirage.GetPropertyBlock(mpb);
                mpb.SetFloat(FadeId, f);
                mirage.SetPropertyBlock(mpb);
            }

            if (motes != null)
            {
                bool near = Mathf.Abs(px - pondCenterX) < moteActiveRange;
                if (near != motesOn)
                {
                    motesOn = near;
                    var em = motes.emission;
                    em.enabled = near;
                }
            }
        }

        if (glow != null)
        {
            float k = 1f + glowBreath * Mathf.Sin(Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, glowPeriod));
            // 這個 shader 吃材質的 _Color (SpriteRenderer.color 不會傳進去)，所以用 MPB 改
            glow.GetPropertyBlock(glowMpb);
            Color c = glowTint; c.a = glowAlpha * k;
            glowMpb.SetColor(ColorId, c);
            glow.SetPropertyBlock(glowMpb);
            // 大小也跟著呼吸一點點 (幅度為 alpha 的 1/5)
            float s = 1f + (k - 1f) * 0.2f;
            glow.transform.localScale = new Vector3(glowBaseScaleX * s, glowBaseScaleY * s, 1f);
        }
    }
}
