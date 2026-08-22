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

    [Header("時間與強度設定")]
    [Tooltip("每次吹風的持續時間 (秒，預設 3)")]
    public float blowDuration = 3.0f;
    [Tooltip("每次風停的持續時間 (秒，預設 1)")]
    public float pauseDuration = 1.0f;
    [Tooltip("逆風的推力強度 (建議 15 ~ 25，可配合玩家質量調整)")]
    public float windForce = 18.0f;

    [Header("視覺與音效回饋")]
    [Tooltip("吹風時啟動的風力粒子系統 (可為空)")]
    public ParticleSystem windParticles;
    [Tooltip("播放風聲的 AudioSource (可為空)")]
    public AudioSource windAudioSource;

    private float timer = 0f;
    private WindState currentState = WindState.Calm;
    private Rigidbody playerRb;
    private bool hasAppliedWindThisGust = false;

    public WindState CurrentState => currentState;

    private void Start()
    {
        EnsurePlayerReference();
        ResetWindCycle();
    }

    private void EnsurePlayerReference()
    {
        if (playerRb == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerRb = playerObj.GetComponent<Rigidbody>();
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

    private void FixedUpdate()
    {
        // 僅在吹風狀態且玩家未被掩體保護時，觸發石化或施加向左推力
        if (currentState == WindState.Blowing && !IsPlayerSheltered && playerRb != null)
        {
            PlayerPetrification petrify = playerRb.GetComponent<PlayerPetrification>();
            if (petrify != null)
            {
                // 一次陣風期間只會觸發一次石化懲罰，防止下一幀無縫連鎖石化
                if (!petrify.isPetrified && !hasAppliedWindThisGust)
                {
                    hasAppliedWindThisGust = true;
                    petrify.Petrify();
                }
            }
            else
            {
                playerRb.AddForce(Vector3.left * windForce, ForceMode.Acceleration);
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
            if (windParticles != null && !windParticles.isPlaying)
            {
                windParticles.Play();
            }
            if (windAudioSource != null && !windAudioSource.isPlaying)
            {
                windAudioSource.Play();
            }
        }
        else
        {
            Debug.Log("【陣风系統】風停了！抓緊時間前進！");
            if (windParticles != null && windParticles.isPlaying)
            {
                windParticles.Stop();
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
