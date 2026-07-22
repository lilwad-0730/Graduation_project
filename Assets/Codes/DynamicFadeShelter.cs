using UnityEngine;
using System.Collections;

/// <summary>
/// 控制掩體（如真/假掩體）週期性「漸顯、存在、漸暗、消失」的規律循環，並自動在消失時停用碰撞偵測。
/// 支援：Built-in Shader (_Color)、URP Shader (_BaseColor)、以及純 MeshRenderer 開關（無透明材質備用方案）。
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

    // 自動偵測材質類型
    private enum MaterialMode { None, BuiltinColor, URPBaseColor, RendererToggle }
    private MaterialMode materialMode = MaterialMode.None;

    // 抓取 Collider（含子物件，相容 WindShelter 掛在子物件的結構）
    private Collider shelterCollider;
    private SpriteRenderer[] spriteRenderers;
    private Renderer[] meshRenderers;

    private float cycleTimer = 0f;
    private enum ShelterFadeState { Active, FadingOut, Inactive, FadingIn }
    private ShelterFadeState currentState = ShelterFadeState.Active;

    private void Start()
    {
        // 【修復】改用 GetComponentInChildren，相容 WindShelter Trigger 在子物件的場景結構
        shelterCollider = GetComponentInChildren<Collider>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        meshRenderers = GetComponentsInChildren<Renderer>(true);

        // 自動偵測此物件的材質類型，決定要用哪種方式改透明度
        DetectMaterialMode();

        ResetToStart();
    }

    /// <summary>
    /// 自動偵測材質支援哪種透明度屬性，決定漸隱漸顯的實作方式。
    /// </summary>
    private void DetectMaterialMode()
    {
        foreach (var mr in meshRenderers)
        {
            if (mr == null || mr is SpriteRenderer) continue;
            if (mr.sharedMaterial == null) continue;

            if (mr.sharedMaterial.HasProperty("_BaseColor"))
            {
                // URP Shader (Lit, Unlit 等)
                materialMode = MaterialMode.URPBaseColor;
                return;
            }
            if (mr.sharedMaterial.HasProperty("_Color"))
            {
                // Built-in Shader (Standard, Legacy 等)
                materialMode = MaterialMode.BuiltinColor;
                return;
            }
        }

        if (spriteRenderers.Length > 0)
        {
            // 有 SpriteRenderer，走 SpriteRenderer 透明度
            materialMode = MaterialMode.BuiltinColor;
            return;
        }

        // 以上都不支援時，fallback：直接開關 MeshRenderer（無漸變，但至少能出現消失）
        materialMode = MaterialMode.RendererToggle;
        Debug.LogWarning($"[DynamicFadeShelter] '{gameObject.name}' 的材質不支援透明度，" +
                         "將改用 MeshRenderer 開關模式（無漸變效果）。\n" +
                         "若要有漸變效果，請將材質的 Surface Type 改為 Transparent（URP）或 Fade（Built-in）。");
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
                    // 【修復 Unity 已知問題】：Collider 被程式停用時 OnTriggerExit 不會觸發，
                    // 導致 IsPlayerSheltered 永遠卡在 true，玩家從此不會被石化。
                    // 強制在此重置保護狀態，確保掩體消失後石化機制恢復正常。
                    WindGustSystem.IsPlayerSheltered = false;
                }

                if (cycleTimer >= fadeOutDuration)
                {
                    SetAlpha(0.0f);
                    if (shelterCollider != null) shelterCollider.enabled = false;
                    WindGustSystem.IsPlayerSheltered = false; // 保險：確保完全消失時也重置
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
        // --- SpriteRenderer 透明度 ---
        foreach (var sr in spriteRenderers)
        {
            if (sr == null) continue;
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }

        // --- MeshRenderer 透明度（根據偵測到的模式處理）---
        foreach (var mr in meshRenderers)
        {
            if (mr == null || mr is SpriteRenderer) continue;

            if (materialMode == MaterialMode.RendererToggle)
            {
                // 無透明材質時：0 = 關閉 Renderer，1 = 開啟 Renderer
                mr.enabled = alpha > 0.01f;
            }
            else if (materialMode == MaterialMode.URPBaseColor)
            {
                // URP Shader：屬性名稱為 _BaseColor
                if (mr.material != null && mr.material.HasProperty("_BaseColor"))
                {
                    Color c = mr.material.GetColor("_BaseColor");
                    c.a = alpha;
                    mr.material.SetColor("_BaseColor", c);
                }
            }
            else if (materialMode == MaterialMode.BuiltinColor)
            {
                // Built-in Shader：屬性名稱為 _Color
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
