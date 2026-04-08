using UnityEngine;

public class WolfProceduralAnimator : MonoBehaviour
{
    public Rigidbody rb;
    public Transform bodyParent;
    public Transform headPivot;
    public Transform tailPivot;
    public Transform legFL_Pivot, legFR_Pivot, legBL_Pivot, legBR_Pivot;

    [Header("Animation Settings")]
    public float runSpeedMultiplier = 2f;
    public float maxSwingAngle = 45f;
    public float headBobAngle = 10f;
    public float bodyBobAmount = 0.15f;

    private void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        // 抓取 X 軸的移動速度來判斷是否在奔跑
        float speed = Mathf.Abs(rb.linearVelocity.x);
        bool isRunning = speed > 0.5f;

        if (isRunning)
        {
            // 動態調整動畫速度
            float time = Time.time * runSpeedMultiplier * speed;

            // 前後腳交替擺動的角度 (類似 Sine 波)
            // 腳FL 與 腳BR 同步，腳FR 與 腳BL 同步
            float swing1 = Mathf.Sin(time) * maxSwingAngle;
            float swing2 = Mathf.Sin(time + Mathf.PI) * maxSwingAngle; 

            legFL_Pivot.localRotation = Quaternion.Euler(swing2, 0, 0);
            legFR_Pivot.localRotation = Quaternion.Euler(swing1, 0, 0);
            legBL_Pivot.localRotation = Quaternion.Euler(swing1, 0, 0);
            legBR_Pivot.localRotation = Quaternion.Euler(swing2, 0, 0);

            // 身體跑步時會稍微上下起伏
            float bob = Mathf.Abs(Mathf.Sin(time)) * bodyBobAmount;
            bodyParent.localPosition = new Vector3(0, bob, 0);

            // 頭部隨著步伐點頭
            headPivot.localRotation = Quaternion.Euler(Mathf.Sin(time) * headBobAngle, 0, 0);

            // 尾巴在跑動時會揚起並擺動
            tailPivot.localRotation = Quaternion.Euler(-20f + Mathf.Sin(time * 2f) * 15f, 0, 0);
        }
        else
        {
            // 閒置 (Idle) 狀態，平滑過渡回原位
            SmoothReset(legFL_Pivot, Quaternion.identity);
            SmoothReset(legFR_Pivot, Quaternion.identity);
            SmoothReset(legBL_Pivot, Quaternion.identity);
            SmoothReset(legBR_Pivot, Quaternion.identity);

            bodyParent.localPosition = Vector3.Lerp(bodyParent.localPosition, Vector3.zero, Time.deltaTime * 5f);
            SmoothReset(headPivot, Quaternion.identity);
            SmoothReset(tailPivot, Quaternion.Euler(-45f, 0, 0)); // 尾巴自然下垂
        }
    }

    private void SmoothReset(Transform t, Quaternion targetRot)
    {
        t.localRotation = Quaternion.Lerp(t.localRotation, targetRot, Time.deltaTime * 8f);
    }
}
