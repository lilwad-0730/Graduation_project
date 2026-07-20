using UnityEngine;
using System.Collections;

/// <summary>
/// 控制掩體（如真/假掩體）週期性「漸顯、存在、漸暗、消失」的規律循環，並自動在消失時停用碰撞偵測。
/// </summary>
public class DynamicFadeShelter : MonoBehaviour, IResettable
{
    [Header("時效與循環設定")]
    [Tooltip("完全亮起且可躲避的持續時間 (秒)")]
    public float activeDuration = 5.0f;
    [Tooltip("漸暗（消失）的持續時間 (秒)")]
    public float fadeOutDuration = 1.5f;
    [Tooltip("完全消失不可躲避的持續時間 (秒)")]
    public float inactiveDuration = 4.0f;
    [Tooltip("漸顯（出現）的持續時間 (秒)")]
    public float fadeInDuration = 1.5f;

    private Collider shelterCollider;
    private SpriteRenderer[] spriteRenderers;
    private Renderer[] meshRenderers;
    
    private float cycleTimer = 0f;
    private enum ShelterFadeState { Active, FadingOut, Inactive, FadingIn }
    private ShelterFadeState currentState = ShelterFadeState.Active;

    private void Start()
    {
        shelterCollider = GetComponent<Collider>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        meshRenderers = GetComponentsInChildren<Renderer>(true);

        ResetToStart();
    }

    private void Update()
    {
        cycleTimer += Time.deltaTime;

        switch (currentState)
        {
            case ShelterFadeState.Active:
                SetAlpha(1.0f);
                if (cycleTimer >= activeDuration)
                {
                    currentState = ShelterFadeState.FadingOut;
                    cycleTimer = 0f;
                }
                break;

            case ShelterFadeState.FadingOut:
                float fadeOutPct = 1.0f - (cycleTimer / fadeOutDuration);
                SetAlpha(Mathf.Clamp01(fadeOutPct));

                // 當透明度低於 0.2 時，關閉物理 Collider，玩家失去防風保護
                if (fadeOutPct < 0.2f && shelterCollider != null && shelterCollider.enabled)
                {
                    shelterCollider.enabled = false;
                }

                if (cycleTimer >= fadeOutDuration)
                {
                    SetAlpha(0.0f);
                    if (shelterCollider != null) shelterCollider.enabled = false;
                    currentState = ShelterFadeState.Inactive;
                    cycleTimer = 0f;
                }
                break;

            case ShelterFadeState.Inactive:
                SetAlpha(0.0f);
                if (cycleTimer >= inactiveDuration)
                {
                    currentState = ShelterFadeState.FadingIn;
                    cycleTimer = 0f;
                }
                break;

            case ShelterFadeState.FadingIn:
                float fadeInPct = cycleTimer / fadeInDuration;
                SetAlpha(Mathf.Clamp01(fadeInPct));

                // 當透明度高於 0.2 時，開啟 Collider 碰撞重新提供保護
                if (fadeInPct > 0.2f && shelterCollider != null && !shelterCollider.enabled)
                {
                    shelterCollider.enabled = true;
                }

                if (cycleTimer >= fadeInDuration)
                {
                    SetAlpha(1.0f);
                    if (shelterCollider != null) shelterCollider.enabled = true;
                    currentState = ShelterFadeState.Active;
                    cycleTimer = 0f;
                }
                break;
        }
    }

    private void SetAlpha(float alpha)
    {
        foreach (var sr in spriteRenderers)
        {
            if (sr != null)
            {
                Color c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
        }

        foreach (var mr in meshRenderers)
        {
            if (mr != null && !(mr is SpriteRenderer))
            {
                if (mr.material != null && mr.material.HasProperty("_Color"))
                {
                    Color c = mr.material.color;
                    c.a = alpha;
                    mr.material.color = c;
                }
            }
        }
    }

    private void ResetToStart()
    {
        currentState = ShelterFadeState.Active;
        cycleTimer = 0f;
        if (shelterCollider != null) shelterCollider.enabled = true;
        SetAlpha(1.0f);
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        ResetToStart();
    }
}
