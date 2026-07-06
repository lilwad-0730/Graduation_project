using UnityEngine;

/// <summary>
/// 掛載於廢墟門上，與 Destructible 協同運作。
/// 當巨石碰撞到該門且速度達到閥值時，門會崩塌碎裂以供通過。
/// </summary>
[RequireComponent(typeof(Destructible))]
public class RuinsDoor : MonoBehaviour
{
    [Header("碰撞偵測設定")]
    [Tooltip("可撞壞此門的物件 Tag。預設為 RollingRock。")]
    public string targetTag = "RollingRock";

    [Tooltip("撞擊門的最低速度，若速度太慢則不會撞開。")]
    public float minImpactSpeed = 1.0f;

    private Destructible destructible;

    private void Start()
    {
        destructible = GetComponent<Destructible>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckShatter(collision.gameObject, collision.relativeVelocity.magnitude);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        CheckShatter(collision.gameObject, collision.relativeVelocity.magnitude);
    }

    private void CheckShatter(GameObject hitObject, float relativeVelocityMagnitude)
    {
        bool isTarget = false;

        // 1. 檢查 Tag 匹配
        if (!string.IsNullOrEmpty(targetTag) && hitObject.CompareTag(targetTag))
        {
            isTarget = true;
        }
        // 2. 後備方案：如果物件名字含有 "rock" 或者是滾動巨石腳本
        else if (hitObject.GetComponent<RollingRockVisual>() != null || hitObject.name.ToLower().Contains("rock"))
        {
            isTarget = true;
        }

        if (isTarget)
        {
            // 嘗試取得 Rigidbody 來獲取真實速度，否則使用相對碰撞速度
            Rigidbody rb = hitObject.GetComponent<Rigidbody>();
            float speed = rb != null ? rb.linearVelocity.magnitude : relativeVelocityMagnitude;

            if (speed >= minImpactSpeed)
            {
                Debug.Log($"【廢墟機關門】偵測到巨石 '{hitObject.name}' 撞擊，撞擊速度：{speed:F2}。觸發門的碎裂崩塌！");
                destructible.Shatter();
            }
            else
            {
                Debug.Log($"【廢墟機關門】巨石 '{hitObject.name}' 碰撞速度過低 ({speed:F2} < {minImpactSpeed:F2})，未達崩塌閥值。");
            }
        }
    }
}
