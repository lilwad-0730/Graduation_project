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
    [Tooltip("每次吹風的持續時間 (秒)。0902 可玩性調整：2.5（原 3）")]
    public float blowDuration = 2.5f;
    [Tooltip("每次風停的持續時間 (秒)。0902 可玩性調整：3.5（原 1，只有一秒能前進，等於一直被吹回去）")]
    public float pauseDuration = 3.5f;
    [Tooltip("逆風的推力強度。推力＝windForce×0.22 m/s；主角走速 5 m/s。12.5 → 逆風 2.75 m/s：頂風走還能慢慢前進，站著不動會被推回去，硬撐（⬇/S）不動")]
    public float windForce = 12.5f;
    [Tooltip("前搖：起風後風痕與風聲先出現，推力晚這麼多秒才來，讓玩家看得到、來得及躲或硬撐")]
    public float windupSeconds = 0.8f;
    [Tooltip("推力從 0 升到全速要幾秒（前搖結束後）")]
    public float pushRampSeconds = 0.5f;

    [Header("🪨 風暴石化與危害設定")]
    [Tooltip("【風暴被動石化開關】：吹風時若玩家未在掩體內（且未主動按住 S/↓ 石化硬撐），是否自動對玩家觸發強制石化？(預設開啟)")]
    public bool enableStormPassivePetrify = false;   // 依團隊決議：荒原石化改為玩家主動（按 ⬇/S）；風暴不再被動強制石化。保留開關供日後切換
    [Tooltip("吹風時是否對未受掩體保護的玩家施加逆風推力？")]
    public bool enableWindPush = true;

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

    /// <summary>吹風中且前搖已過：推力才生效。</summary>
    public bool IsPushActive => currentState == WindState.Blowing && timer >= windupSeconds;

    /// <summary>推力強度 0～1：前搖期間 0，之後在 pushRampSeconds 內升到 1。</summary>
    public float PushStrength01
    {
        get
        {
            if (currentState != WindState.Blowing) return 0f;
            float t = timer - windupSeconds;
            if (t <= 0f) return 0f;
            return pushRampSeconds > 0.01f ? Mathf.Clamp01(t / pushRampSeconds) : 1f;
        }
    }

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
            wave.enableStormPassivePetrify = enableStormPassivePetrify;
            wave.enableWindPush = enableWindPush;
            wave.EnsureTriggerCollider();

            // 風痕鋪滿全屏：風沙特效跟著鏡頭走，發射盒撐得比畫面更大——狂風一起就是滿螢幕風痕
            DesertWindDustFX fxCfg = windParticles.GetComponent<DesertWindDustFX>();
            if (fxCfg != null)
            {
                Camera cam = Camera.main;
                float halfH = (cam != null && cam.orthographic) ? cam.orthographicSize : 17f;
                float halfW = halfH * (cam != null ? cam.aspect : 1.78f);
                fxCfg.followCamera = true;
                fxCfg.emitterSize = new Vector3(halfW * 2.5f, halfH * 2.5f, 1f);
                fxCfg.ApplyVFXSettings();
            }
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
        if (windAudioSource != null)
            windAudioSource.volume = AudioManager.SfxVolume;

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
