using UnityEngine;

public enum WindState { Blowing, Calm }

/// <summary>
/// 管理荒原關卡的逆風（陣風）循環：吹風 3 秒、停風 1 秒。
/// 在吹風期間對未處於掩體保護下的玩家觸發石化或施加推力。
/// </summary>
public class WindGustSystem : MonoBehaviour, IResettable
{
    public static bool IsPlayerSheltered = false;

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

    public WindState CurrentState => currentState;

    private void Start()
    {
        // 尋找玩家
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerRb = playerObj.GetComponent<Rigidbody>();
        }

        // 核心修復：開局無條件由 Calm (平靜無風) 狀態開始，確保玩家落地上腳並有時間反應！
        ResetWindCycle();
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
                if (!petrify.isPetrified)
                {
                    petrify.Petrify();
                }
            }
            else
            {
                // 備用方案：若尚無石化腳本，則維持舊版風力推力
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
            Debug.Log("【陣風系統】風停了！抓緊時間前進！");
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
        SwitchToState(WindState.Calm); // 預設由風平浪靜開始，給予玩家反應時間
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        ResetWindCycle();
    }
}
