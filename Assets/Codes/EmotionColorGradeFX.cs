using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1109 顏色就是心情：畫面的顏色跟著情緒走，不用任何新美術。
///   緊張（狼逼近、缺氧）→ 顏色慢慢褪掉、對比拉緊；缺氧再多一點冷藍。
///   安心（EmotionDirector.Calm，躲過一陣子之後）→ 顏色回暖一點、一點點暖調。
/// 只調飽和度、對比、色濾鏡三樣，Volume 優先權 50（隊友場景裡用的是 100，他們的永遠贏）。
/// 目前只開廢墟與水下；荒原的去色是隊友的敘事，棉花堡有自己的色彩回來，這裡都不碰。
///
/// 後處理規則：這個專案多數場景鏡頭的後處理是關的。只有「我真的有東西要調」才打開，
/// 放開時先看場上有沒有別人的 Volume：有就不關（人家可能需要），沒有才還原成原本的樣子。
/// 關掉：EmotionColorGradeFX.Enabled = false。
/// </summary>
public class EmotionColorGradeFX : MonoBehaviour
{
    public static bool Enabled = true;
    public static bool RuinedEnabled = true;
    public static bool UnderwaterEnabled = true;

    // 企劃可調（最緊張時的最大幅度）
    public static float FearDesaturate = -34f;
    public static float SuffocationDesaturate = -26f;
    public static float TensionContrast = 10f;
    public static float CalmSaturate = 9f;
    public static Color CalmTint = new Color(1f, 0.965f, 0.9f, 1f);
    public static Color SuffocationTint = new Color(0.86f, 0.93f, 1f, 1f);
    public static float Priority = 50f;

    // ★1115 B4 墜落色調連續漸變：從粉彩雲一路褪色、變暗、偏冷，落地前已經是廢墟的灰；落地後慢慢退回（約 5 秒），不是兩段硬切。
    //   只在有「控制式墜落」的場景會動（目前只有廢墟）；關掉：FallGradeEnabled = false。
    public static bool FallGradeEnabled = true;
    public static float FallDesaturate = -34f;
    public static float FallContrast = 8f;
    public static float FallExposure = -0.3f;                                   // 曝光（檔位）
    public static Color FallTint = new Color(0.78f, 0.84f, 0.95f, 1f);          // 偏冷偏暗
    public static float FallReleasePerSecond = 0.5f;                            // 落地後色調退回的速度（越小退得越慢）

    // ★1115 B2 重落地的「眼前一黑」：落地瞬間曝光掉下去，約 3 秒慢慢亮回來（RuinsHeavyLandingFX 設 ImpactDip＝1 觸發）
    public static float ImpactDip;
    public static float ImpactDipExposure = -1.0f;
    public static float ImpactDipDesaturate = -12f;
    public static float ImpactDipSeconds = 3f;

    /// <summary>目前這個效果打算輸出的飽和度（驗收／除錯用）。</summary>
    public static float CurrentSaturation;

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
        if (FindFirstObjectByType<EmotionColorGradeFX>() != null) return;
        new GameObject("[顏色就是心情]").AddComponent<EmotionColorGradeFX>();
    }

    private VolumeProfile _profile;
    private ColorAdjustments _ca;
    private Volume _vol;
    private UniversalAdditionalCameraData _camData;
    private bool _weOpenedPost, _prevPost;
    private float _sat, _con, _tintK, _coolK;   // 平滑後的輸出
    private float _fall;                         // ★1115 墜落色調（0～1，平滑後）
    private PlayerMovement _pm;
    private float _nextPmFind;

    private void Awake()
    {
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _ca = _profile.Add<ColorAdjustments>(true);
        _ca.saturation.Override(0f);
        _ca.contrast.Override(0f);
        _ca.colorFilter.Override(Color.white);
        _ca.postExposure.Override(0f);
        _vol = gameObject.AddComponent<Volume>();
        _vol.isGlobal = true;
        _vol.priority = Priority;
        _vol.weight = 0f;
        _vol.sharedProfile = _profile;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float tension = EmotionDirector.Enabled ? EmotionDirector.Tension : 0f;
        float calm = EmotionDirector.Enabled ? EmotionDirector.Calm : 0f;

        // ★1115 B4 墜落色調：依「墜落進度」連續漸變。上升跟著進度走；落地後用比較慢的速度退回，不要啪一下變回原色
        if (_pm == null && Time.unscaledTime >= _nextPmFind)
        {
            _nextPmFind = Time.unscaledTime + 1f;
            _pm = FindFirstObjectByType<PlayerMovement>();
        }
        float fallT = 0f;
        if (Enabled && FallGradeEnabled && _pm != null)
            fallT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 1f, _pm.FallProgress01));
        float fallRate = fallT > _fall ? 1.6f : Mathf.Max(0.05f, FallReleasePerSecond);
        _fall = Mathf.Lerp(_fall, fallT, 1f - Mathf.Exp(-fallRate * dt));
        if (_fall < 0.002f && fallT <= 0f) _fall = 0f;

        // ★1115 B2 重落地眼前一黑：ImpactDip 由 RuinsHeavyLandingFX 在落地瞬間設成 1，這裡負責退回 0
        if (ImpactDip > 0f) ImpactDip = Mathf.MoveTowards(ImpactDip, 0f, dt / Mathf.Max(0.3f, ImpactDipSeconds));
        float dip = ImpactDip * ImpactDip;   // 一開始掉得深、後面慢慢亮回來

        // 墜落中（以及剛落地、色調還沒退完）不套「安心」的暖色：安心感是落地、緩過來之後才慢慢長出來的
        calm *= 1f - _fall;

        // 目標值（0 ＝ 不動畫面）
        float satT = Mathf.Lerp(0f, FearDesaturate, Mathf.SmoothStep(0f, 1f, EmotionDirector.Fear))
                   + Mathf.Lerp(0f, SuffocationDesaturate, Mathf.SmoothStep(0f, 1f, EmotionDirector.Suffocation))
                   + CalmSaturate * calm;
        float conT = TensionContrast * Mathf.SmoothStep(0f, 1f, tension);
        float tintT = calm * (1f - tension);
        float coolT = Mathf.SmoothStep(0f, 1f, EmotionDirector.Suffocation);
        if (!Enabled) { satT = conT = tintT = coolT = 0f; }

        // 慢進慢出：顏色不要一下子變，變化要像呼吸（約 2 秒）
        float k = 1f - Mathf.Exp(-1.6f * dt);
        _sat = Mathf.Lerp(_sat, satT, k);
        _con = Mathf.Lerp(_con, conT, k);
        _tintK = Mathf.Lerp(_tintK, tintT, k);
        _coolK = Mathf.Lerp(_coolK, coolT, k);
        CurrentSaturation = _sat;

        bool need = Mathf.Abs(_sat) > 0.4f || Mathf.Abs(_con) > 0.2f || _tintK > 0.01f || _coolK > 0.01f
                    || _fall > 0.01f || dip > 0.01f;
        if (need)
        {
            if (!OpenPost()) return;
            Color f = Color.Lerp(Color.white, CalmTint, _tintK);
            f = Color.Lerp(f, SuffocationTint, _coolK);
            f *= Color.Lerp(Color.white, FallTint, _fall);   // ★1115 墜落偏冷偏暗
            f.a = 1f;
            _ca.saturation.value = _sat + FallDesaturate * _fall + ImpactDipDesaturate * dip;
            _ca.contrast.value = _con + FallContrast * _fall;
            _ca.colorFilter.value = f;
            _ca.postExposure.value = FallExposure * _fall + ImpactDipExposure * dip;
            _vol.weight = 1f;
        }
        else if (_weOpenedPost || _vol.weight > 0f)
        {
            _vol.weight = 0f;
            ClosePost();
        }
    }

    /// <summary>把鏡頭的後處理打開（如果本來是關的，記下來之後還原）。回傳能不能用。</summary>
    private bool OpenPost()
    {
        if (_camData == null || _camData.Equals(null))
        {
            Camera c = Camera.main;
            if (c == null) return false;
            _camData = c.GetUniversalAdditionalCameraData();
            if (_camData == null) return false;
            _weOpenedPost = false;
        }
        if (!_camData.renderPostProcessing)
        {
            _prevPost = false;
            _camData.renderPostProcessing = true;
            _weOpenedPost = true;
        }
        return true;
    }

    private void ClosePost()
    {
        if (_camData != null && _weOpenedPost)
        {
            // 場上有別人的 Volume（啟用中、有權重）就別關：人家可能剛好需要後處理開著
            bool othersNeed = false;
            Volume[] all = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Volume v = all[i];
                if (v == null || v == _vol || !v.isActiveAndEnabled) continue;
                if (v.weight > 0.001f && v.sharedProfile != null) { othersNeed = true; break; }
            }
            if (!othersNeed) _camData.renderPostProcessing = _prevPost;
        }
        _weOpenedPost = false;
        _camData = null;
    }

    private void OnDestroy()
    {
        if (_camData != null && _weOpenedPost) ClosePost();
        if (_profile != null) Destroy(_profile);
        CurrentSaturation = 0f;
        ImpactDip = 0f;
    }
}
