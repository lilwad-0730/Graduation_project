using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 我你他　荒原　高空盤旋的鳥群
///
/// 【要解決什麼】
///   荒原原本的 57 隻鳥（組員的 IndividualBirdEnemy）掛在 BirdEnemy 底下，
///   高度大約在玩家眼睛的位置，而且要玩家走進 10 公尺內才會動作。
///   在那之前牠們只是一排幾乎不動的黑點——玩家「看不到鳥群」，
///   只會突然被俯衝。分鏡 Q5-S5「鳥影」→ Q5-S6「她抬頭」的那一拍不存在。
///
///   這支補上那一拍：天上一直有一圈鳥在繞，看得到、追不到、也不會傷人。
///   等到組員那批真的俯衝下來時，玩家才知道剛剛那圈是什麼。
///
/// 【做法】
///   自己生鳥（不動組員那 57 隻一根寒毛），沿一個扁橢圓繞圈，
///   整團用 SmoothDamp 慢慢跟著鏡頭——跟得慢一點才像遠方的東西，
///   黏太緊會看起來像貼在螢幕上的貼紙。
///
/// 【安全】
///   生出來的鳥會被拔掉所有 Collider／Rigidbody，永遠碰不到玩家；
///   用 MaterialPropertyBlock 調暗，不碰共用材質，所以不會把組員的鳥一起改色。
///   用 Time.deltaTime（不是 unscaled），開設定選單暫停時鳥也跟著停。
/// </summary>
[DisallowMultipleComponent]
public class CirclingFlock : MonoBehaviour
{
    [Header("生成")]
    [Tooltip("鳥的模型（荒原用的是 living birds 的 crow）")]
    public GameObject birdPrefab;

    [Tooltip("鳥的 Animator Controller（living birds 的 birdAnimatorController）")]
    public RuntimeAnimatorController birdAnimator;

    [Tooltip("要播的飛行動畫狀態名")]
    public string flyStateName = "flying";

    [Tooltip("幾隻")]
    [Range(1, 40)] public int count = 9;

    [Tooltip("每隻的縮放。組員那批俯衝鳥的世界縮放約 21.5；盤旋的在高空，要明顯小一號")]
    public float birdScale = 9.5f;

    [Header("盤旋的圈")]
    [Tooltip("橢圓的橫半徑（公尺）")]
    public float radiusX = 15f;

    [Tooltip("橢圓的縱半徑。要扁，扁才像「從下面看一個平的圈」")]
    public float radiusY = 3.4f;

    [Tooltip("繞一圈幾秒")]
    public float periodSeconds = 13f;

    [Tooltip("每隻半徑差多少（比例，0＝排成完美的一圈，太整齊會很假）")]
    [Range(0f, 0.6f)] public float radiusJitter = 0.3f;

    [Tooltip("每隻速度差多少（比例）")]
    [Range(0f, 0.5f)] public float periodJitter = 0.22f;

    [Tooltip("各自上下微浮的幅度（公尺）")]
    public float bobAmplitude = 0.5f;

    [Tooltip("順時針轉？（預設逆時針）")]
    public bool clockwise = false;

    [Header("跟著鏡頭（玩家走到哪都看得到）")]
    public bool followCamera = true;

    [Tooltip("圈心要落在畫面的哪裡。y 越大越靠上（抬頭才看得到）")]
    public Vector2 viewportPosition = new Vector2(0.5f, 0.82f);

    [Tooltip("跟隨的遲滯秒數。越大跟得越慢、越像遠方；0 ＝黏死在畫面上（很假）")]
    public float followLag = 1.6f;

    [Tooltip("放在哪一層深度。荒原：背景 z=0.5、玩法 z=0，所以 0.3 是「天上、在背景前面」")]
    public float depthZ = 0.3f;

    [Header("看起來像遠的")]
    [Tooltip("把鳥調暗成剪影（用 MaterialPropertyBlock，不會動到共用材質）")]
    public bool tintDistant = true;
    public Color distantTint = new Color(0.42f, 0.40f, 0.46f, 1f);

    // ── 內部 ─────────────────────────────────
    private class Member
    {
        public Transform tr;
        public float phase;      // 起始角度
        public float rScale;     // 半徑倍率
        public float speed;      // 角速度倍率
        public float bobPhase;
        public float zNudge;     // 避免兩隻完全重疊時閃爍
    }

    private readonly List<Member> _birds = new List<Member>();
    private Camera _cam;
    private Vector3 _vel;         // SmoothDamp 用
    private bool _spawned;
    private bool _snapped;      // 第一幀直接就位，不要讓玩家看到整團從別的地方飄過來
    private static readonly int _ColorId = Shader.PropertyToID("_Color");

    private void Start()
    {
        Spawn();
    }

    private void Spawn()
    {
        if (_spawned) return;
        _spawned = true;

        if (birdPrefab == null)
        {
            Debug.LogWarning("[CirclingFlock] 沒有指定鳥的模型，這團不會生出東西。", this);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            GameObject go = Object.Instantiate(birdPrefab, transform);
            go.name = "FlockBird_" + i;

            // ★安全：盤旋的鳥是純景，不參與任何碰撞
            StripPhysics(go);

            Transform t = go.transform;
            t.localScale = Vector3.one * birdScale;

            Animator anim = go.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                if (birdAnimator != null) anim.runtimeAnimatorController = birdAnimator;
                anim.applyRootMotion = false;
                if (anim.runtimeAnimatorController != null && !string.IsNullOrEmpty(flyStateName))
                {
                    // 每隻拍翅的相位錯開，不然九隻會像同一隻複製九份
                    anim.Play(flyStateName, 0, Random.value);
                }
            }

            if (tintDistant) Tint(go);

            Member m = new Member();
            m.tr = t;
            m.phase = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
            m.rScale = 1f + Random.Range(-radiusJitter, radiusJitter);
            m.speed = 1f / Mathf.Max(0.05f, 1f + Random.Range(-periodJitter, periodJitter));
            m.bobPhase = Random.Range(0f, Mathf.PI * 2f);
            m.zNudge = (i - count * 0.5f) * 0.01f;
            _birds.Add(m);
        }
    }

    private static void StripPhysics(GameObject go)
    {
        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) Object.Destroy(cols[i]);
        Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++) Object.Destroy(rbs[i]);
    }

    private void Tint(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] == null) continue;
            rs[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rs[i].receiveShadows = false;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            rs[i].GetPropertyBlock(mpb);
            mpb.SetColor(_ColorId, distantTint);
            rs[i].SetPropertyBlock(mpb);
        }
    }

    private void LateUpdate()
    {
        FollowCamera();
        Orbit();
    }

    private void FollowCamera()
    {
        if (!followCamera) return;
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) return;

        // 正交相機：ViewportToWorldPoint 的 z 是「離相機多遠」，換算成想要的世界 z
        float dist = depthZ - _cam.transform.position.z;
        Vector3 want = _cam.ViewportToWorldPoint(
            new Vector3(viewportPosition.x, viewportPosition.y, dist));
        want.z = depthZ;

        if (followLag <= 0.001f || !_snapped)
        {
            _snapped = true;
            transform.position = want;
            _vel = Vector3.zero;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, want, ref _vel, followLag);
        }
    }

    private void Orbit()
    {
        if (_birds.Count == 0) return;
        float baseW = (Mathf.PI * 2f) / Mathf.Max(0.1f, periodSeconds);
        float dir = clockwise ? -1f : 1f;
        float t = Time.time;

        for (int i = 0; i < _birds.Count; i++)
        {
            Member m = _birds[i];
            if (m.tr == null) continue;

            float a = m.phase + dir * baseW * m.speed * t;
            float rx = radiusX * m.rScale;
            float ry = radiusY * m.rScale;

            float x = Mathf.Cos(a) * rx;
            float y = Mathf.Sin(a) * ry + Mathf.Sin(t * 1.3f + m.bobPhase) * bobAmplitude;
            m.tr.localPosition = new Vector3(x, y, m.zNudge);

            // 面向前進方向（跟組員的鳥同一套：模型 +Z 當鼻子，不加偏移）
            Vector3 v = new Vector3(-Mathf.Sin(a) * rx * dir, Mathf.Cos(a) * ry * dir, 0f);
            if (v.sqrMagnitude > 0.0001f)
                m.tr.localRotation = Quaternion.LookRotation(v.normalized, Vector3.up);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.7f);
        Vector3 c = transform.position;
        const int seg = 48;
        Vector3 prev = c + new Vector3(radiusX, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusY, 0f);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
#endif
}
