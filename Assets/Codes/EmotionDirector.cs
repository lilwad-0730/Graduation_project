using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1109 情緒大腦（只讀）：把「狼逼近」「快沒氣」兩個既有訊號整理成共用的三個數，
/// 後面的情緒效果（存檔點暖光、顏色就是心情、世界呼吸…）都讀這裡，不各自重複偵測，也不會各自演各自的。
///   Fear         0～1  被追的恐懼（來源：WolfHeartbeatFX.Level）
///   Suffocation  0～1  缺氧（來源：EmotionAtmosphereFX.Suffocation01）
///   Tension      0～1  兩者取大
///   Calm         0～1  安心感：緊張全消、安全待了一陣子才慢慢長出來（驚魂甫定那幾秒不算安心）
///   Quiet        0～1  裝飾性效果可乘的音量：越緊張越小（緊張時畫面要留給暗角與心跳，不要再疊花俏的東西）
/// 只讀、不改任何東西。關掉：EmotionDirector.Enabled = false（所有數值回到 0，Quiet 回到 1）。
/// </summary>
public class EmotionDirector : MonoBehaviour
{
    public static bool Enabled = true;

    public static float Fear;
    public static float Suffocation;
    public static float Tension;
    public static float Calm;
    public static float Quiet = 1f;
    /// <summary>已經安全（Tension 幾乎為 0）多久，秒。</summary>
    public static float SafeSeconds;

    /// <summary>緊張全消後，要待多久才開始有安心感、再多久到滿（秒）。</summary>
    public static float CalmDelay = 4f, CalmRamp = 10f;

    private static EmotionDirector s_inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Ensure();
    }

    private static void OnLoaded(Scene s, LoadSceneMode m)
    {
        ResetValues();
        Ensure();
    }

    private static void Ensure()
    {
        if (!Enabled || s_inst != null) return;
        GameObject go = new GameObject("[情緒大腦]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<EmotionDirector>();
    }

    private static void ResetValues()
    {
        Fear = Suffocation = Tension = Calm = 0f;
        Quiet = 1f;
        SafeSeconds = 0f;
    }

    private void Update()
    {
        if (!Enabled) { ResetValues(); return; }
        float dt = Time.unscaledDeltaTime;

        Fear = Mathf.Clamp01(WolfHeartbeatFX.Level);
        Suffocation = Mathf.Clamp01(EmotionAtmosphereFX.Suffocation01);
        Tension = Mathf.Max(Fear, Suffocation);

        if (Tension < 0.03f) SafeSeconds += dt; else SafeSeconds = 0f;
        float calmTarget = HeartbeatPulse.Smooth(CalmDelay, CalmDelay + CalmRamp, SafeSeconds);
        // 上升慢（約 10 秒才滿）、一緊張立刻掉：安心要慢慢累積，驚嚇一下就沒了
        Calm = calmTarget < Calm ? calmTarget : Mathf.MoveTowards(Calm, calmTarget, dt * 0.12f);

        Quiet = 1f - 0.85f * Mathf.SmoothStep(0f, 1f, Tension);
    }

    private void OnDestroy()
    {
        if (s_inst == this) s_inst = null;
        ResetValues();
    }
}
