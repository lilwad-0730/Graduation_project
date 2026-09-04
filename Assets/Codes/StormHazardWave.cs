using UnityEngine;

/// <summary>
/// 實體風暴沙塵危害組件 (Physical Storm Hazard Wave)
/// 掛載於風暴/風沙物件 (WindParticles / DesertWindDustFX) 上。
/// 只有當玩家物理碰撞體真正進入風暴沙塵的 Trigger 範圍內，且【未處於掩體背風面】時，
/// 才會受到逆風推力，徹底擺脫全場景隔空必中的問題！
/// ※ 石化已改制：不再由風暴強制觸發，而是玩家按住 ⬇/S 的主動自保（見 PlayerPetrification）。
/// </summary>
public class StormHazardWave : MonoBehaviour
{
    [Header("🌪️ 風暴推力與判定")]
    [Tooltip("逆風向左推力 (預設 18)")]
    public float windForce = 18f;

    [Tooltip("風向 (預設向左 -1)")]
    public float windDirectionX = -1f;

    [Tooltip("是否對未受掩體保護的玩家施加逆風推力")]
    public bool enableWindPush = true;

    [Header("🪨 風暴強制石化開關")]
    [Tooltip("【風暴被動石化開關】：吹風時若玩家未在掩體內（且未主動按住 S/↓ 石化硬撐），是否自動對玩家觸發強制石化？(預設開啟)")]
    public bool enableStormPassivePetrify = false;   // 依團隊決議：荒原石化改為玩家主動（按 ⬇/S）；風暴不再被動強制石化。保留開關供日後切換

    private Collider hazardCollider;
    private bool hasAppliedPetrifyThisGust = false;

    private void Awake()
    {
        EnsureTriggerCollider();
    }

    private void Start()
    {
        EnsureTriggerCollider();
    }

    public void EnsureTriggerCollider()
    {
        if (hazardCollider == null) hazardCollider = GetComponent<Collider>();
        if (hazardCollider == null)
        {
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(60f, 25f, 10f); // 覆蓋風沙主視野區域
            hazardCollider = box;
        }
        else
        {
            hazardCollider.isTrigger = true;
        }
    }

    /// <summary>
    /// 當陣風週期重置或進入新的一波吹風時調用
    /// </summary>
    public void ResetGustFlag()
    {
        hasAppliedPetrifyThisGust = false;
    }

    private GameObject cachedPlayer;

    private void Update()
    {
        if (WindGustSystem.Instance != null && WindGustSystem.Instance.CurrentState != WindState.Blowing) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;

        if (cachedPlayer == null)
        {
            cachedPlayer = GameObject.FindWithTag("Player");
            if (cachedPlayer == null)
            {
                PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
                if (pm != null) cachedPlayer = pm.gameObject;
            }
        }

        if (cachedPlayer != null)
        {
            EnsureTriggerCollider();
            if (hazardCollider != null)
            {
                // 檢查玩家中心座標是否落在風暴碰撞範圍內
                Vector3 playerPos = cachedPlayer.transform.position;
                Bounds b = hazardCollider.bounds;
                if (playerPos.x >= b.min.x && playerPos.x <= b.max.x &&
                    playerPos.y >= b.min.y && playerPos.y <= b.max.y)
                {
                    CheckAndApplyHazard(cachedPlayer);
                }
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        CheckAndApplyHazard(other.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckAndApplyHazard(other.gameObject);
    }

    private void CheckAndApplyHazard(GameObject hitObj)
    {
        // 1. 僅在當前確實處於吹風狀態、且前搖已過時才生效（風痕先出現、推力晚 0.8 秒才來）
        if (WindGustSystem.Instance != null && !WindGustSystem.Instance.IsPushActive)
        {
            return;
        }

        // 2. 判斷是否為玩家本體
        if (!hitObj.CompareTag("Player") && !hitObj.name.ToLower().Contains("player") && hitObj.GetComponentInParent<PlayerMovement>() == null)
        {
            return;
        }

        // 3. 檢查玩家是否躲在掩體背風面內 (有掩體保護 100% 免疫風暴推力與石化！)
        if (WindGustSystem.IsPlayerSheltered)
        {
            return;
        }

        // 3.5 主動石化硬撐中：她就是一顆石頭，風推不動、也不會受到額外被動石化計次懲罰
        PlayerPetrification petr = hitObj.GetComponent<PlayerPetrification>();
        if (petr == null) petr = hitObj.GetComponentInParent<PlayerPetrification>();
        if (petr == null) petr = hitObj.GetComponentInChildren<PlayerPetrification>();
        if (petr != null && petr.isPetrified)
        {
            return;
        }

        // 4. 對未受掩體保護且接觸到實體風暴的玩家施加向左平滑逆風推力 (杜絕 AddForce 物理抽搐)
        //    ★WindGustSystem.globalPush 開著時推力由它全域施加（風痕在哪推力就在哪），這裡不再重複推
        if (enableWindPush && !(WindGustSystem.Instance != null && WindGustSystem.Instance.globalPush))
        {
            PlayerMovement pm = hitObj.GetComponent<PlayerMovement>();
            if (pm == null) pm = hitObj.GetComponentInParent<PlayerMovement>();
            if (pm != null)
            {
                float strength = WindGustSystem.Instance != null ? WindGustSystem.Instance.PushStrength01 : 1f;
                float pushSpeed = Mathf.Sign(windDirectionX) * (windForce * 0.22f) * strength; // 12.5 → 約 -2.75 m/s，前搖後 0.5 秒內漸強
                pm.ApplyWindPush(pushSpeed);
            }
        }

        // 5. 風暴強制被動石化（可由 enableStormPassivePetrify 開關自由切換）
        if (enableStormPassivePetrify && !hasAppliedPetrifyThisGust)
        {
            if (petr != null)
            {
                hasAppliedPetrifyThisGust = true;
                petr.Petrify();
            }
        }
    }
}
