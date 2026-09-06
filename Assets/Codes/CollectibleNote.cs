using System.Collections;
using UnityEngine;

public class CollectibleNote : MonoBehaviour
{
    [Header("拾取設定")]
    [Tooltip("塌縮消失的時間(秒)")]
    public float shrinkDuration = 0.35f;

    [Tooltip("觸發碰撞箱相對於圖片尺寸的倍率。\n" +
             "1 = 完全等於整張圖片的邊界 (含透明留白)；\n" +
             "若圖片四周留白很多，觸發範圍會比看到的紙大一圈，玩家會覺得「還沒碰到就被撿走」，\n" +
             "此時把這個值調小 (例如 0.6) 讓觸發框貼合實際看得到的紙")]
    [Range(0.1f, 1f)]
    public float colliderSizeMultiplier = 1f;

    private bool _isCollected = false;

    void Start()
    {
        FitColliderToSprite();
    }

    /// <summary>
    /// 精確將觸發碰撞箱對齊圖片的實際視覺邊界
    /// </summary>
    public void FitColliderToSprite()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
        {
            box = gameObject.AddComponent<BoxCollider>();
        }

        box.isTrigger = true;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            // 將碰撞箱的中心與 X/Y 大小完全貼合圖片邊界
            Vector3 spriteSize = sr.sprite.bounds.size;
            Vector3 spriteCenter = sr.sprite.bounds.center;

            box.center = spriteCenter;
            // X, Y 與圖片尺寸 100% 相同；Z 軸給予適當厚度(如 20f)，確保能捕捉到主角的 3D 碰撞箱
            float zThicknessInLocal = 20f;
            if (transform.lossyScale.z > 0f)
            {
                zThicknessInLocal = 1.2f / transform.lossyScale.z;
            }

            box.size = new Vector3(
                spriteSize.x * colliderSizeMultiplier,
                spriteSize.y * colliderSizeMultiplier,
                zThicknessInLocal);
        }
    }

    /// <summary>
    /// 在 Scene 視窗畫出實際的觸發範圍，方便直接目視比對「觸發框」與「看得到的紙」差多少。
    /// 若框明顯比紙大一圈，就是圖片四周留白造成的，把 colliderSizeMultiplier 調小即可。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.center, box.size);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_isCollected) return;

        // 必須是主角的碰撞箱接觸到便條紙邊界才觸發
        if (other.CompareTag("Player") || other.name == "Player" || other.GetComponentInParent<PlayerMovement>() != null)
        {
            CollectItem();
        }
    }

    public void CollectItem()
    {
        if (_isCollected) return;
        _isCollected = true;
        UnderwaterCheckpoint.MarkHere(this, "撿到日誌 " + gameObject.name);

        StartCoroutine(ShrinkAndDestroyRoutine());
    }

    private IEnumerator ShrinkAndDestroyRoutine()
    {
        Vector3 initialScale = transform.localScale;
        float elapsed = 0f;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        Color initialColor = sr != null ? sr.color : Color.white;

        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;
            
            // 向中央塌縮 (EaseIn 效果)
            float scaleT = Mathf.Clamp01(1f - (t * t)); 

            transform.localScale = initialScale * scaleT;

            if (sr != null)
            {
                Color c = initialColor;
                c.a = Mathf.Lerp(initialColor.a, 0f, t);
                sr.color = c;
            }

            yield return null;
        }

        // 動畫結束後銷毀父物件(連同定位容器)或本物件
        if (transform.parent != null && transform.parent.name.EndsWith("_Parent"))
        {
            Destroy(transform.parent.gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
