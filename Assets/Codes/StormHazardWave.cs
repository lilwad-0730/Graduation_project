using UnityEngine;

/// <summary>
/// 實體風暴沙塵危害組件 (Physical Storm Hazard Wave)
/// 掛載於風暴/風沙物件 (WindParticles / DesertWindDustFX) 上。
/// 只有當玩家物理碰撞體真正進入風暴沙塵的 Trigger 範圍內，且【未處於掩體背風面】時，
/// 才會受到逆風推力並觸發石化懲罰，徹底擺脫全場景隔空必中的問題！
/// </summary>
public class StormHazardWave : MonoBehaviour
{
    [Header("🌪️ 風暴推力與判定")]
    [Tooltip("逆風向左推力 (預設 18)")]
    public float windForce = 18f;

    [Tooltip("風向 (預設向左 -1)")]
    public float windDirectionX = -1f;

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
        // 1. 僅在當前確實處於吹風狀態時才生效
        if (WindGustSystem.Instance != null && WindGustSystem.Instance.CurrentState != WindState.Blowing)
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

        // 4. 對未受掩體保護且接觸到實體風暴的玩家施加向左平滑逆風推力 (杜絕 AddForce 物理抽搐)
        PlayerMovement pm = hitObj.GetComponent<PlayerMovement>();
        if (pm == null) pm = hitObj.GetComponentInParent<PlayerMovement>();
        if (pm != null)
        {
            float pushSpeed = Mathf.Sign(windDirectionX) * (windForce * 0.22f); // 約 -4.0 m/s 平滑逆風阻力
            pm.ApplyWindPush(pushSpeed);
        }

        // 5. 觸發石化懲罰 (一次陣風期間只會觸發一次，防止連續重複石化)
        if (!hasAppliedPetrifyThisGust)
        {
            PlayerPetrification petrify = hitObj.GetComponent<PlayerPetrification>();
            if (petrify == null) petrify = hitObj.GetComponentInParent<PlayerPetrification>();
            if (petrify == null) petrify = hitObj.GetComponentInChildren<PlayerPetrification>();

            if (petrify != null && !petrify.isPetrified)
            {
                hasAppliedPetrifyThisGust = true;
                Debug.LogWarning("🌪️【實體風暴石化】玩家身體接觸到風暴沙塵且無掩體保護，觸發石化！");
                petrify.Petrify();
            }
        }
    }
}
