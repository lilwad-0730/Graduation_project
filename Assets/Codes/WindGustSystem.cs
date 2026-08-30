using UnityEngine;

public enum WindState { Blowing, Calm }

/// <summary>
/// 管理荒原關卡的逆風（陣風）循環：吹風 3 秒、停風 1 秒。
/// 在吹風期間對未處於掩體保護下的玩家觸發石化或施加推力。
/// </summary>
public class WindGustSystem : MonoBehaviour, IResettable
{
    private static int _shelteredCount = 0;
    public static bool IsPlayerSheltered
    {
        get => _shelteredCount > 0;
        set
        {
            if (!value) _shelteredCount = 0;
            else if (_shelteredCount == 0) _shelteredCount = 1;
        }
    }

    public static void RegisterPlayerShelter()
    {
        _shelteredCount++;
    }

    public static void UnregisterPlayerShelter()
    {
        _shelteredCount = Mathf.Max(0, _shelteredCount - 1);
    }

    public static void ClearShelterCount()
    {
        _shelteredCount = 0;
    }

    public static WindGustSystem Instance { get; private set; }

    [Header("時間與強度設定")]
    [Tooltip("每次吹風的持續時間 (秒，預設 3)")]
    public float blowDuration = 3.0f;
    [Tooltip("每次風停的持續時間 (秒，預設 2.5)")]
    public float pauseDuration = 2.5f;
    [Tooltip("逆風的推力強度 (建議 15 ~ 25，可配合玩家質量調整)")]
    public float windForce = 18.0f;

    [Header("視覺與音效回饋")]
    [Tooltip("吹風時啟動的風力粒子系統 (可為空，系統自動搜尋)")]
    public ParticleSystem windParticles;

    [Tooltip("吹風時播放的風聲音效檔 (請直接拖入 強風3.mp3 或 風聲2.mp3，切勿放入石化音效)")]
    public AudioClip windSoundClip;

    [Tooltip("播放風聲的 AudioSource (可為空，系統自動建立)")]
    public AudioSource windAudioSource;

    private float timer = 0f;
    private WindState currentState = WindState.Calm;
    private Rigidbody playerRb;
    private PlayerPetrification playerPetrify;
    private bool hasAppliedWindThisGust = false;

    public WindState CurrentState => currentState;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoEnsureInDesertScene()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToLower();
        if (sceneName.Contains("desert") || sceneName.Contains("荒漠") || sceneName.Contains("荒原"))
        {
            if (Instance == null && FindFirstObjectByType<WindGustSystem>() == null)
            {
                GameObject go = new GameObject("WindGustSystem_AutoCreated");
                go.AddComponent<WindGustSystem>();
                Debug.LogWarning("【陣風系統】荒漠關卡未掛載 WindGustSystem，已自動生成並啟動陣風吹風石化機制！");
            }
        }
    }

    private void Start()
    {
        EnsureComponents();
        ResetWindCycle();
    }

    private void EnsureComponents()
    {
        EnsurePlayerReference();

        if (windParticles == null)
        {
            GameObject windObj = GameObject.Find("WindParticles");
            if (windObj != null) windParticles = windObj.GetComponent<ParticleSystem>();
            if (windParticles == null)
            {
                DesertWindDustFX fx = FindFirstObjectByType<DesertWindDustFX>();
                if (fx != null) windParticles = fx.GetComponent<ParticleSystem>();
            }
        }

        // 自動為風暴粒子物件掛載 StormHazardWave 實體 Trigger 組件
        if (windParticles != null)
        {
            StormHazardWave wave = windParticles.GetComponent<StormHazardWave>();
            if (wave == null) wave = windParticles.gameObject.AddComponent<StormHazardWave>();
            wave.windForce = windForce;
            wave.EnsureTriggerCollider();
        }

        if (windAudioSource == null)
        {
            windAudioSource = GetComponent<AudioSource>();
            if (windAudioSource == null) windAudioSource = gameObject.AddComponent<AudioSource>();
            windAudioSource.playOnAwake = false;
            windAudioSource.loop = true;
        }

        // 防呆校驗：若風聲音效被誤設為石化音效，強制清除並提示
        if (windSoundClip != null && (windSoundClip.name.Contains("石化") || windSoundClip.name.Contains("Petrif")))
        {
            Debug.LogError($"[WindGustSystem 錯誤防呆] 偵測到 windSoundClip 誤設為石化音效 '{windSoundClip.name}'！已自動重置，防止循環播放石化聲！");
            windSoundClip = null;
        }

        if (windSoundClip != null && windAudioSource != null)
        {
            windAudioSource.clip = windSoundClip;
        }
    }

    private void EnsurePlayerReference()
    {
        if (playerRb == null || playerPetrify == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj == null)
            {
                PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
                if (pm != null) playerObj = pm.gameObject;
            }
            if (playerObj == null)
            {
                PlayerRespawnSystem respawn = FindFirstObjectByType<PlayerRespawnSystem>();
                if (respawn != null) playerObj = respawn.gameObject;
            }

            if (playerObj != null)
            {
                playerRb = playerObj.GetComponent<Rigidbody>();
                if (playerRb == null) playerRb = playerObj.GetComponentInParent<Rigidbody>();
                if (playerRb == null) playerRb = playerObj.GetComponentInChildren<Rigidbody>();

                playerPetrify = playerObj.GetComponent<PlayerPetrification>();
                if (playerPetrify == null) playerPetrify = playerObj.GetComponentInParent<PlayerPetrification>();
                if (playerPetrify == null) playerPetrify = playerObj.GetComponentInChildren<PlayerPetrification>();

                // 若玩家身上缺少 PlayerPetrification，自動為其掛載
                if (playerPetrify == null)
                {
                    playerPetrify = playerObj.AddComponent<PlayerPetrification>();
                    Debug.LogWarning($"[WindGustSystem] 偵測到玩家 '{playerObj.name}' 未掛載 PlayerPetrification，已自動掛載！");
                }
            }
        }
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (currentState == WindState.Calm)
        {
            if (timer >= pauseDuration)
            {
                SwitchToState(WindState.Blowing);
            }
        }
        else if (currentState == WindState.Blowing)
        {
            if (timer >= blowDuration)
            {
                SwitchToState(WindState.Calm);
            }
        }
    }

    private void SwitchToState(WindState newState)
    {
        currentState = newState;
        timer = 0f;

        if (currentState == WindState.Blowing)
        {
            hasAppliedWindThisGust = false; // 重置此趟陣風的石化標記
            Debug.Log("【陣風系統】起風了！逆風來襲！只能在掩體內躲避！");

            // 通知所有實體風暴危害區域重置陣風判定
            StormHazardWave[] waves = FindObjectsByType<StormHazardWave>(FindObjectsSortMode.None);
            foreach (var wave in waves)
            {
                if (wave != null) wave.ResetGustFlag();
            }

            if (windParticles != null)
            {
                DesertWindDustFX fx = windParticles.GetComponent<DesertWindDustFX>();
                if (fx != null) fx.Play();
                else if (!windParticles.isPlaying) windParticles.Play(true);
            }
            if (windAudioSource != null)
            {
                if (windSoundClip != null) windAudioSource.clip = windSoundClip;
                if (windAudioSource.clip != null && !windAudioSource.isPlaying)
                {
                    windAudioSource.Play();
                }
            }
        }
        else
        {
            Debug.Log("【陣風系統】風停了！抓緊時間前進！");
            if (windParticles != null)
            {
                DesertWindDustFX fx = windParticles.GetComponent<DesertWindDustFX>();
                if (fx != null) fx.Stop();
                else if (windParticles.isPlaying) windParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (windAudioSource != null && windAudioSource.isPlaying)
            {
                windAudioSource.Stop();
            }
        }
    }

    private void ResetWindCycle()
    {
        IsPlayerSheltered = false;
        hasAppliedWindThisGust = false;
        SwitchToState(WindState.Calm); // 預設由風平浪靜開始
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        ResetWindCycle();
    }
}
