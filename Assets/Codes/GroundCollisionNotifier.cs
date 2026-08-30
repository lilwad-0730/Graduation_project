using UnityEngine;

/// <summary>
/// 掛載在地面物件上，偵測鳥隻碰撞並通知其 OnHitGround 以完成「落地後卡住5秒並漸漸消失」之機制。
/// </summary>
public class GroundCollisionNotifier : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        NotifyBird(collision.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        NotifyBird(other.gameObject);
    }

    private void NotifyBird(GameObject obj)
    {
        // 防呆保護：若此腳本被誤掛在烏鴉自身身上，直接忽略，絕不誤殺自己或其它烏鴉！
        if (GetComponent<IndividualBirdEnemy>() != null || GetComponentInParent<IndividualBirdEnemy>() != null)
        {
            return;
        }

        if (obj == null) return;

        IndividualBirdEnemy bird = obj.GetComponent<IndividualBirdEnemy>();
        if (bird == null) bird = obj.GetComponentInParent<IndividualBirdEnemy>();
        
        if (bird != null && bird.gameObject != gameObject)
        {
            bird.OnHitGround();
        }
    }
}
