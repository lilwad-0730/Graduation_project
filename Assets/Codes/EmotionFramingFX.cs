using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1115 B9 鏡頭取景隨情緒：害怕、缺氧的時候視野悄悄縮緊一點（最多約 3%），身邊的東西顯得更近、更逼人；
/// 緊張散掉就回到原本的取景。玩家幾乎不會意識到，但會覺得「空間在收」。
///
///  做法（為什麼不會跟別的鏡頭程式打架）：
///   ・不改任何鏡頭程式、不改 Cinemachine 的設定。只在「這台鏡頭要畫畫面的前一刻」把它的 orthographicSize 乘上一個略小於 1 的係數，
///     畫完立刻還原。所以 CameraTargetXFollower、各種鏡頭邊界、特寫、放大縮小，看到與計算用的都是原本的數字，完全不知道有這回事。
///   ・只會縮小、不會放大：放大視野可能露出背景圖邊緣；縮小只會少看到一點，永遠安全。
///  讀取：EmotionDirector.Fear（狼逼近）、Suffocation（缺氧）。乘上 MotionComfort.Scale（動態強度）。
///  目前只開廢墟與水下（跟顏色就是心情同一批）。關掉：EmotionFramingFX.Enabled = false。
/// </summary>
public class EmotionFramingFX : MonoBehaviour
{
    public static bool Enabled = true;
    public static bool RuinedEnabled = true;
    public static bool UnderwaterEnabled = true;

    /// <summary>最緊張時視野縮的比例（0.03＝縮 3%）。</summary>
    public static float FearShrink = 0.03f;
    public static float SuffocationShrink = 0.02f;
    /// <summary>縮緊／放開的速度（越大越快）。</summary>
    public static float FollowRate = 1.1f;

    /// <summary>目前套用的係數（1＝沒縮；驗收／除錯用）。</summary>
    public static float CurrentFactor = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Ensure(SceneManager.GetActiveScene());
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { Ensure(s); }

    private static void Ensure(Scene s)
    {
        if (!Enabled || !s.IsValid()) return;
        string n = s.name.ToLowerInvariant();
        bool ok = (n.Contains("ruin") && RuinedEnabled) || (n.Contains("underwater") && UnderwaterEnabled);
        if (!ok) return;
        if (FindFirstObjectByType<EmotionFramingFX>() != null) return;
        new GameObject("[取景隨情緒]").AddComponent<EmotionFramingFX>();
    }

    private float _factor = 1f;
    private Camera _cam;
    private float _savedSize;
    private bool _applied;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        Restore();
        CurrentFactor = 1f;
    }

    private void Update()
    {
        float target = 1f;
        if (Enabled && EmotionDirector.Enabled)
        {
            float fear = Mathf.SmoothStep(0f, 1f, EmotionDirector.Fear);
            float suff = Mathf.SmoothStep(0f, 1f, EmotionDirector.Suffocation);
            float shrink = FearShrink * fear + SuffocationShrink * suff;
            target = 1f - Mathf.Min(0.06f, shrink) * MotionComfort.Scale;
        }
        _factor = Mathf.Lerp(_factor, target, 1f - Mathf.Exp(-FollowRate * Time.unscaledDeltaTime));
        if (Mathf.Abs(_factor - 1f) < 0.0003f && target >= 1f) _factor = 1f;
        CurrentFactor = _factor;
    }

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_factor >= 1f) return;
        if (cam == null || cam != Camera.main || !cam.orthographic) return;
        _cam = cam;
        _savedSize = cam.orthographicSize;
        cam.orthographicSize = _savedSize * _factor;
        _applied = true;
    }

    private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_applied && cam == _cam) Restore();
    }

    private void Restore()
    {
        if (_applied && _cam != null) _cam.orthographicSize = _savedSize;
        _applied = false;
    }
}
