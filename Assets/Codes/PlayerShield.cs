using UnityEngine;
using System.Collections;

public class PlayerShield : MonoBehaviour, IResettable
{
    [Header("按鍵與位置設定")]
    [Tooltip("開啟護盾的按鍵 (預設 Q)")]
    public KeyCode shieldKey = KeyCode.Q;

    [Tooltip("護盾相對於主角的 Y 軸鋪設高度 (預設 2)")]
    public float yOffset = 2f;

    [Tooltip("護盾的 GameObject 物件 (美術做好的藍色護盾)")]
    public GameObject shieldObject;

    [Header("防禦與時效設定")]
    [Tooltip("按下按鍵後護盾出現的持續時間 (秒，預設 3)")]
    public float shieldDuration = 3f;

    [Tooltip("擊退鳥類敵人的力道 (預設 15)")]
    public float knockbackForce = 15f;

    public bool IsShieldActive => shieldObject != null && shieldObject.activeSelf;

    private Coroutine shieldCoroutine;

    private void Start()
    {
        if (shieldObject != null)
        {
            shieldObject.SetActive(false);
            
            // 0. 核心修復：防止護盾子物件帶有未鎖定物理的 Rigidbody，導致主角物理全體卡死與無法下落！
            Rigidbody[] childRbs = shieldObject.GetComponentsInChildren<Rigidbody>(true);
            foreach (var childRb in childRbs)
            {
                childRb.isKinematic = true;
            }

            // 1. 確保為主角子物件，設定相對位置 (Z 軸設為 -0.5f 確保不會被背景圖遮擋)
            shieldObject.transform.SetParent(this.transform);
            shieldObject.transform.localPosition = new Vector3(0, yOffset, -0.5f);
            shieldObject.transform.localRotation = Quaternion.identity;

            // 2. 提升 Sprite 渲染圖層 Order，解決「護盾沒出現」的視覺隱形問題
            SpriteRenderer[] sprites = shieldObject.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in sprites)
            {
                sr.sortingOrder = 100; // 高圖層，確保顯示在背景與主角前方
            }

            // 3. 核心 Bug 修復：將護盾的所有碰撞體強制設為 isTrigger = true！
            // 避免護盾啟動時實體碰撞體卡住地面或環境，造成主角無法移動！
            Collider[] shieldColliders3D = shieldObject.GetComponentsInChildren<Collider>(true);
            foreach (var sCol in shieldColliders3D)
            {
                sCol.isTrigger = true;
                
                // 若為 MeshCollider，在 dynamic Rigidbody 下必須設為 convex
                if (sCol is MeshCollider mc)
                {
                    mc.convex = true;
                }
            }

            Collider2D[] shieldColliders2D = shieldObject.GetComponentsInChildren<Collider2D>(true);
            foreach (var sCol2D in shieldColliders2D)
            {
                sCol2D.isTrigger = true;
            }

            // 4. 強制忽略主角與護盾體之間的所有物理碰撞
            Collider[] playerColliders3D = GetComponentsInChildren<Collider>(true);
            foreach (var pCol in playerColliders3D)
            {
                foreach (var sCol in shieldColliders3D)
                {
                    if (pCol != sCol)
                    {
                        Physics.IgnoreCollision(pCol, sCol, true);
                    }
                }
            }

            Collider2D[] playerColliders2D = GetComponentsInChildren<Collider2D>(true);
            foreach (var pCol2D in playerColliders2D)
            {
                foreach (var sCol2D in shieldColliders2D)
                {
                    if (pCol2D != sCol2D)
                    {
                        Physics2D.IgnoreCollision(pCol2D, sCol2D, true);
                    }
                }
            }

            Debug.Log("[PlayerShield] 護盾系統初始化完成：已修復圖層可見度與物理防卡死。");
        }
        else
        {
            Debug.LogWarning("[PlayerShield] 尚未設定護盾 GameObject！請在 Inspector 中將藍色護盾拖入 shieldObject 欄位。");
        }
    }

    private void Update()
    {
        if (shieldObject != null)
        {
            // 按下 Q 鍵觸發護盾
            if (Input.GetKeyDown(shieldKey))
            {
                ActivateShield();
            }

            // 護盾顯示期間維持在頭頂位置
            if (shieldObject.activeSelf)
            {
                shieldObject.transform.localPosition = new Vector3(0, yOffset, -0.5f);
            }
        }
    }

    public void ActivateShield()
    {
        if (shieldObject == null) return;
        
        if (shieldCoroutine != null)
        {
            StopCoroutine(shieldCoroutine);
        }
        shieldCoroutine = StartCoroutine(ShieldActiveCoroutine());
    }

    private IEnumerator ShieldActiveCoroutine()
    {
        shieldObject.SetActive(true);
        Debug.Log($"【護盾系統】按下 Q 鍵，護盾顯現，持續 {shieldDuration} 秒！");
        
        yield return new WaitForSeconds(shieldDuration);
        
        shieldObject.SetActive(false);
        Debug.Log("【護盾系統】3 秒時間到，護盾自動隱形關閉。");
        shieldCoroutine = null;
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        if (shieldCoroutine != null)
        {
            StopCoroutine(shieldCoroutine);
            shieldCoroutine = null;
        }
        if (shieldObject != null)
        {
            shieldObject.SetActive(false);
        }
    }
}
