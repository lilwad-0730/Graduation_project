using UnityEngine;

public class WolfProceduralAnimator2D : MonoBehaviour
{
    public Transform body;
    public Transform headPivot, tailPivot;
    public Transform legFL, legFR, legBL, legBR;

    [Header("動態設定")]
    public float swingSpeed = 10f;
    public float legAngle = 30f;
    public float bodyBob = 0.1f;

    void Update()
    {
        float t = Time.time * swingSpeed;
        
        // 身體上下起伏
        body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(t)) * bodyBob, 0);

        // 2D 骨架關節旋轉 (Z軸)
        float swing1 = Mathf.Sin(t) * legAngle;
        float swing2 = Mathf.Sin(t + Mathf.PI) * legAngle;

        if (legFL) legFL.localRotation = Quaternion.Euler(0, 0, swing2);
        if (legFR) legFR.localRotation = Quaternion.Euler(0, 0, swing1);
        if (legBL) legBL.localRotation = Quaternion.Euler(0, 0, swing1);
        if (legBR) legBR.localRotation = Quaternion.Euler(0, 0, swing2);

        // 頭部與尾巴擺動
        if (headPivot) headPivot.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t) * 5f);
        if (tailPivot) tailPivot.localRotation = Quaternion.Euler(0, 0, -20f + Mathf.Sin(t * 2f) * 15f);
    }
}
