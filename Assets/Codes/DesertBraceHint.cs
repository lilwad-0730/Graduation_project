using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1017 M：荒原「按住 S」怎麼教 →「出現文字引導玩家」
///
/// 荒原的風（WindGustSystem）每一陣起來之前，遠處會先出現沙塵線（約 1 秒）。
/// 這時在畫面下方亮一行「起風了　按住 S 硬撐」，一直到那一陣風吹完；
/// 她在吹風時按住 S 撐住（主動石化）超過 learnSeconds，就算學會了，之後不再出現。
/// 她撐著、風已經停了還沒放開：提示「風停了　放開 S 繼續走」（硬撐的時候不能動，第一次的人不知道要放開）。
/// 還沒學會前被風吹成石頭（被動石化），下一陣風照樣會再提示。
///
/// 樣子用既有的 InputHint（畫面下方一行骨白字，跟「按下 E」「W／S　上浮與下潛」同一套），
/// 播文字卡、過場、重生、設定選單打開時它自己會讓路。
/// 只在有 WindGustSystem 的場景（荒原）自己掛上；場景不動。開關：DesertBraceHint.Enabled。
/// </summary>
[DisallowMultipleComponent]
public class DesertBraceHint : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("文字")]
    public string braceText = "起風了　按住 S 硬撐";
    public string releaseText = "風停了　放開 S 繼續走";

    [Header("什麼時候算學會")]
    [Tooltip("吹風時按住 S 撐住超過幾秒，就不再提示")]
    public float learnSeconds = 0.8f;
    [Tooltip("風停了她還撐著，過幾秒提示放開")]
    public float releaseAfterSeconds = 1.0f;
    [Tooltip("「放開 S」最多提示幾次")]
    public int maxReleaseHints = 2;

    private const string KeyBrace = "desert_brace";
    private const string KeyRelease = "desert_release";

    private PlayerPetrification _pet;
    private float _nextLookup;
    private bool _learned;
    private float _bracedInWind;
    private float _bracedInCalm;
    private int _releaseHints;
    private bool _releaseShowing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedStatic;
        SceneManager.sceneLoaded += OnSceneLoadedStatic;
        TryInstall();
    }

    private static void OnSceneLoadedStatic(Scene s, LoadSceneMode m) { TryInstall(); }

    private static void TryInstall()
    {
        WindGustSystem wind = Object.FindAnyObjectByType<WindGustSystem>();
        if (wind == null) return;
        if (Object.FindAnyObjectByType<DesertBraceHint>() != null) return;
        GameObject go = new GameObject("DesertBraceHint (自動生成)");
        go.AddComponent<DesertBraceHint>();
        Debug.Log("【荒原提示】★1017 已安裝：起風時畫面下方提示「按住 S 硬撐」，撐過一陣風就不再出現。");
    }

    private void OnDisable()
    {
        InputHint.Hide(KeyBrace);
        InputHint.Hide(KeyRelease);
    }

    private void Update()
    {
        WindGustSystem wind = WindGustSystem.Instance;
        if (!Enabled || wind == null || wind.IsStoppedForever)
        {
            InputHint.Hide(KeyBrace);
            InputHint.Hide(KeyRelease);
            return;
        }

        if (_pet == null && Time.unscaledTime >= _nextLookup)
        {
            _pet = Object.FindAnyObjectByType<PlayerPetrification>();
            _nextLookup = Time.unscaledTime + 1f;
        }
        bool bracing = _pet != null && _pet.IsBracing;
        bool blowing = wind.CurrentState == WindState.Blowing && !wind.IsHoldingCalm;
        bool windComing = (wind.IsTelegraphing || blowing) && !wind.IsHoldingCalm;

        // 學會：吹風時撐住夠久
        if (blowing && bracing)
        {
            _bracedInWind += Time.deltaTime;
            if (!_learned && _bracedInWind >= learnSeconds)
            {
                _learned = true;
                Debug.Log("【荒原提示】她在風裡撐住了：之後不再提示「按住 S」。");
            }
        }
        else if (!blowing)
        {
            _bracedInWind = 0f;
        }

        // 「按住 S」：還沒學會，風要來或正在吹
        if (!_learned && windComing && !bracing) InputHint.Show(KeyBrace, braceText);
        else InputHint.Hide(KeyBrace);

        // 「放開 S」：風停了還撐著
        if (bracing && !windComing) _bracedInCalm += Time.deltaTime; else _bracedInCalm = 0f;
        bool wantRelease = bracing && !windComing && _bracedInCalm >= releaseAfterSeconds;
        if (wantRelease && !_releaseShowing && _releaseHints < maxReleaseHints)
        {
            _releaseShowing = true;
            _releaseHints++;
            InputHint.Show(KeyRelease, releaseText);
        }
        else if (_releaseShowing && (!bracing || windComing))
        {
            _releaseShowing = false;
            InputHint.Hide(KeyRelease);
        }
    }
}
