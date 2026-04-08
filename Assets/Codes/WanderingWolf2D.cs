using UnityEngine;

public class WanderingWolf2D : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float patrolDistance = 5f;
    
    private float startPosX;
    private bool movingRight = true;

    void Start()
    {
        startPosX = transform.position.x;
    }

    void Update()
    {
        // 持續往前移動
        transform.Translate(Vector3.right * moveSpeed * Time.deltaTime, Space.Self);

        // 判斷是否需要折返
        if (movingRight && transform.position.x > startPosX + patrolDistance)
        {
            FlipAndTurn(false);
        }
        else if (!movingRight && transform.position.x < startPosX - patrolDistance)
        {
            FlipAndTurn(true);
        }
    }

    private void FlipAndTurn(bool right)
    {
        movingRight = right;
        // 旋轉 180 度來折返 (2D 左右翻轉)
        transform.rotation = right ? Quaternion.Euler(0, 0, 0) : Quaternion.Euler(0, 180f, 0);
    }
}
