using UnityEngine;

/// <summary>
/// 掛載於廢墟門上，與 Destructible 協同運作。
/// 當巨石碰撞到該門且速度達到閥值時，門會崩塌碎裂以供通過。
/// </summary>
[RequireComponent(typeof(Destructible))]
public class RuinsDoor : MonoBehaviour
{
    [Header("碰撞偵測設定")]
    [Tooltip("指定只能被此特定物件撞壞 (例如：把 rock-new 拖進來，主角或其他物體碰觸就絕對不會破壞門)。若為空則使用 Tag/名字判定。")]
    public GameObject specificDestructionObject;

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

        // 1. 如果有指定特定破壞物件，只認該物件，其餘碰觸（如主角）皆不破壞
        if (specificDestructionObject != null)
        {
            if (hitObject == specificDestructionObject || hitObject.transform.IsChildOf(specificDestructionObject.transform))
            {
                isTarget = true;
            }
        }
        else
        {
            // 2. 沒有指定特定物件時，才使用 Tag 與名稱匹配邏輯
            if (!string.IsNullOrEmpty(targetTag) && hitObject.CompareTag(targetTag))
            {
                isTarget = true;
            }
            else if (hitObject.GetComponent<RollingRockVisual>() != null || hitObject.name.ToLower().Contains("rock"))
            {
                isTarget = true;
            }
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
