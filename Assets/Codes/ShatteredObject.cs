using System.Collections;
using UnityEngine;

public class ShatteredObject : MonoBehaviour
{
    [Header("Shatter Physics")]
    public float explosionForce = 5f;

    [Header("Disappear Settings")]
    public float disappearDelay = 1f; // 碎片維持實體的時間
    public float shrinkDuration = 1f;  // 縮小至消失的過程時間

    void Start()
    {
        ApplyShatterForce();
        StartCoroutine(DisappearRoutine());
    }

    private void ApplyShatterForce()
    {
        // 3D 物理碎片爆破與向下傾倒崩塌
        Rigidbody[] rbs3D = GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in rbs3D)
        {
            Vector3 randomDir = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(-3.5f, 0.5f), Random.Range(-1f, 1f));
            rb.AddForce(randomDir * explosionForce, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * explosionForce * 8f, ForceMode.Impulse);
        }

        // 2D 物理碎片噴射
        Rigidbody2D[] rbs2D = GetComponentsInChildren<Rigidbody2D>();
        foreach (Rigidbody2D rb in rbs2D)
        {
            Vector2 randomDir = new Vector2(Random.Range(-2.5f, 2.5f), Random.Range(-3.5f, 0.5f));
            rb.AddForce(randomDir * explosionForce, ForceMode2D.Impulse);
            rb.AddTorque(Random.Range(-explosionForce * 8f, explosionForce * 8f), ForceMode2D.Impulse);
        }
    }

    private IEnumerator DisappearRoutine()
    {
        // 等待設定的延遲時間（用戶指定為 1 秒）
        yield return new WaitForSeconds(disappearDelay);

        float elapsed = 0f;
        int childCount = transform.childCount;
        Vector3[] originalScales = new Vector3[childCount];
        Transform[] children = new Transform[childCount];

        for (int i = 0; i < childCount; i++)
        {
            children[i] = transform.GetChild(i);
            originalScales[i] = children[i].localScale;
            
            // 關閉碰撞器避免在縮小時卡住玩家或其他物體
            Collider col = children[i].GetComponent<Collider>();
            if (col != null) col.enabled = false;
            
            Collider2D col2D = children[i].GetComponent<Collider2D>();
            if (col2D != null) col2D.enabled = false;
        }

        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;
            
            // 漸變曲線：三次立方淡出，前半段慢，後半段快
            float scaleFactor = Mathf.Clamp01(1f - (t * t * t)); 

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null)
                {
                    children[i].localScale = originalScales[i] * scaleFactor;
                }
            }
            yield return null;
        }

        // 最後銷毀碎片容器
        Destroy(gameObject);
    }
}
