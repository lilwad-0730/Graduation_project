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

    // ★0911：多隻狼「一起做出相同動作」的真正原因就在下面那行。
    //   Time.time 是全域時鐘，每隻狼各自算 Mathf.Sin(Time.time * swingSpeed)，
    //   同一瞬間算出來的值一模一樣，所以六隻狼的腿、身體、頭、尾巴永遠同一個相位。
    //   這不是共用狀態的問題——每隻狼都有自己的元件實例、自己的欄位，
    //   只是「同樣的公式吃同樣的輸入，當然吐出同樣的結果」。
    //   解法是每隻狼在出生時拿一個自己的相位偏移，之後就一直用那個值。
    //   ★只在 Awake 取一次亂數，不是每幀取——每幀 random 會讓動作變成抖動。
    private float _phaseOffset;

    void Awake()
    {
        _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        // 偏移用加的不是用乘的：乘的話相位差會跟著 swingSpeed 變，跑速一改又同步回去
        float t = Time.time * swingSpeed + _phaseOffset;
        
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
