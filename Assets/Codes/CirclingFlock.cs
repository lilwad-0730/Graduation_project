using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 我你他　荒原　高空盤旋的鳥群
///
/// 【要解決什麼】
///   荒原原本的 57 隻鳥（組員的 IndividualBirdEnemy）掛在 BirdEnemy 底下，
///   世界 Y 只有 1.3–7.4，差不多在她眼睛的高度，而且 detectionRange=10，
///   要玩家走進 10 公尺內才會動作。在那之前牠們只是一排幾乎不動的黑點——
///   玩家「看不到鳥群」，只會突然被俯衝。分鏡 Q5-S5「鳥影」→ Q5-S6「她抬頭」
///   那一拍在遊戲裡是不存在的。這支補上那一拍。
///
/// 【兩條規矩】
///   1　軸心永遠在她身上。整團的圈心鎖在玩家身上（不是鏡頭），
///      她走到哪、跳多高，這圈就跟到哪——甩不掉。
///   2　動得不規則。不是一個乾淨的橢圓：每隻的角度、半徑、還有各自的
///      漂移都疊了 Perlin 噪聲，所以沒有兩隻走同一條線，同一隻也不會
///      走第二遍同樣的路。整團的圈心也會在她身上小幅晃，不會像釘死的儀器。
///
/// 【安全】
///   生出來的鳥會被拔掉所有 Collider／Rigidbody，永遠碰不到玩家；
///   用 MaterialPropertyBlock 調暗，不碰共用材質，所以不會把組員的鳥一起改色；
///   用 Time.deltaTime（不是 unscaled），開設定選單暫停時鳥也跟著停。
/// </summary>
[DisallowMultipleComponent]
public class CirclingFlock : MonoBehaviour
{
    public enum AnchorMode { Player, Camera, Fixed }

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

    // ══════════════════════════════════════════
    // 軸心：永遠跟著玩家
    // ══════════════════════════════════════════
    [Header("軸心（圈心）")]
    [Tooltip("圈心跟著誰。Player＝鎖在玩家身上（預設）；Camera＝鎖在畫面上；Fixed＝不動")]
    public AnchorMode anchor = AnchorMode.Player;

    [Tooltip("圈心相對玩家的位移。\n(0, 0) ＝完全貼在她身上（鳥會繞過她腳下，半個圈會埋進地面）。\n預設抬高 8 公尺，讓整圈在她頭上。")]
    public Vector2 centerOffset = new Vector2(0f, 8f);

    [Tooltip("圈心追上玩家的遲滯秒數。0＝硬鎖；大一點會像被拖著走。她跑得快時這個值決定鳥群會不會被拉出一條尾巴")]
    public float followLag = 0.55f;

    [Tooltip("找不到玩家時退而用鏡頭的哪個位置（0.5, 0.82 ＝畫面中上）")]
    public Vector2 cameraFallbackViewport = new Vector2(0.5f, 0.82f);

    [Tooltip("放在哪一層深度。荒原：背景 z=0.5、玩法 z=0，所以 0.3 是「天上、在背景前面」")]
    public float depthZ = 0.3f;

    // ══════════════════════════════════════════
    // 不規則運動
    // ══════════════════════════════════════════
    [Header("圈的大小")]
    [Tooltip("橫半徑（公尺）")]
    public float radiusX = 15f;

    [Tooltip("縱半徑。要扁，扁才像「從下面看一個平的圈」")]
    public float radiusY = 3.4f;

    [Tooltip("繞一圈幾秒（平均值，每隻還會再差一點）")]
    public float periodSeconds = 13f;

    [Tooltip("順時針轉？（預設逆時針）")]
    public bool clockwise = false;

    [Header("不規則：讓牠們不像機器")]
    [Tooltip("每隻的基準半徑差多少（比例）")]
    [Range(0f, 0.6f)] public float radiusJitter = 0.3f;

    [Tooltip("每隻的基準速度差多少（比例）")]
    [Range(0f, 0.5f)] public float periodJitter = 0.22f;

    [Tooltip("角度上的搖擺（弧度）。牠們會忽快忽慢，不是等速繞圈")]
    [Range(0f, 1.5f)] public float wanderAngle = 0.55f;

    [Tooltip("半徑上的呼吸（比例）。圈會忽大忽小，不是固定的一圈")]
    [Range(0f, 0.6f)] public float wanderRadius = 0.26f;

    [Tooltip("每隻自己的亂飄幅度（公尺）。這條最影響「像不像活的」")]
    public float driftAmplitude = 2.6f;

    [Tooltip("整團圈心在她身上的小幅晃動（公尺）。0＝圈心死死釘在她身上")]
    public float flockWander = 1.4f;

    [Tooltip("噪聲跑多快。大＝慌張，小＝慵懶")]
    [Range(0.02f, 1.5f)] public float noiseSpeed = 0.16f;

    // ══════════════════════════════════════════
    [Header("看起來像遠的")]
    [Tooltip("把鳥調暗成剪影（用 MaterialPropertyBlock，不會動到共用材質）")]
    public bool tintDistant = true;
    public Color distantTint = new Color(0.42f, 0.40f, 0.46f, 1f);

    // ── 內部 ─────────────────────────────────
    private class Member
    {
        public Transform tr;
        public float phase;       // 起始角度
        public float rScale;      // 基準半徑倍率
        public float speed;       // 基準角速度倍率
        public float zNudge;      // 避免兩隻完全重疊時閃爍
        public Vector2 seedA;     // 角度噪聲的取樣點
        public Vector2 seedR;     // 半徑噪聲
        public Vector2 seedDX;    // 漂移 X
        public Vector2 seedDY;    // 漂移 Y
        public Vector3 lastPos;   // 算朝向用（實際位移，不是理論切線）
        public bool hasLast;
    }

    private readonly List<Member> _birds = new List<Member>();
    private Transform _player;
    private Camera _cam;
    private Vector3 _vel;
    private Vector2 _flockSeedX, _flockSeedY;
    private bool _spawned, _snapped;
    private static readonly int _ColorId = Shader.PropertyToID("_Color");

    /// <summary>-0.5 ~ +0.5 的平滑噪聲</summary>
    private static float N(Vector2 seed, float t)
    {
        return Mathf.PerlinNoise(seed.x + t, seed.y) - 0.5f;
    }

    private void Start()
    {
        _flockSeedX = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
        _flockSeedY = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
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
            m.phase = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.35f, 0.35f);
            m.rScale = 1f + Random.Range(-radiusJitter, radiusJitter);
            m.speed = 1f / Mathf.Max(0.05f, 1f + Random.Range(-periodJitter, periodJitter));
            m.zNudge = (i - count * 0.5f) * 0.01f;
            m.seedA = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
            m.seedR = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
            m.seedDX = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
            m.seedDY = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
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
        MoveAxis();
        Fly();
    }

    // ══════════════════════════════════════════
    // 軸心：鎖在玩家身上
    // ══════════════════════════════════════════
    private void MoveAxis()
    {
        if (anchor == AnchorMode.Fixed) return;

        Vector3 want;
        if (!TryGetAnchorPoint(out want)) return;

        // 整團的圈心在她身上小幅晃，不會像釘死的儀器
        float t = Time.time * noiseSpeed * 0.6f;
        want.x += N(_flockSeedX, t) * 2f * flockWander;
        want.y += N(_flockSeedY, t) * 2f * flockWander * 0.55f;
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

    private bool TryGetAnchorPoint(out Vector3 p)
    {
        p = transform.position;

        if (anchor == AnchorMode.Player)
        {
            if (_player == null) _player = FindPlayer();
            if (_player != null)
            {
                p = _player.position + new Vector3(centerOffset.x, centerOffset.y, 0f);
                return true;
            }
            // 找不到人就先用鏡頭頂著，等她出現（重生、換關的空窗）
        }

        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) return false;
        float dist = depthZ - _cam.transform.position.z;
        p = _cam.ViewportToWorldPoint(
            new Vector3(cameraFallbackViewport.x, cameraFallbackViewport.y, dist));
        return true;
    }

    private static Transform FindPlayer()
    {
        PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
        if (pm != null) return pm.transform;
        GameObject go = GameObject.FindWithTag("Player");
        return go != null ? go.transform : null;
    }

    // ══════════════════════════════════════════
    // 飛：橢圓只是骨架，真正的路徑由噪聲決定
    // ══════════════════════════════════════════
    private void Fly()
    {
        if (_birds.Count == 0) return;
        float baseW = (Mathf.PI * 2f) / Mathf.Max(0.1f, periodSeconds);
        float dir = clockwise ? -1f : 1f;
        float t = Time.time;
        float nt = t * noiseSpeed;

        for (int i = 0; i < _birds.Count; i++)
        {
            Member m = _birds[i];
            if (m.tr == null) continue;

            // 角度：等速繞圈 ＋ 忽快忽慢
            float a = m.phase + dir * baseW * m.speed * t + N(m.seedA, nt) * 2f * wanderAngle;

            // 半徑：基準 ＋ 呼吸
            float rk = m.rScale * (1f + N(m.seedR, nt) * 2f * wanderRadius);
            float rx = radiusX * rk;
            float ry = radiusY * rk;

            // 各自亂飄
            float dx = N(m.seedDX, nt * 1.37f) * 2f * driftAmplitude;
            float dy = N(m.seedDY, nt * 1.11f) * 2f * driftAmplitude * 0.5f;

            Vector3 pos = new Vector3(Mathf.Cos(a) * rx + dx, Mathf.Sin(a) * ry + dy, m.zNudge);
            m.tr.localPosition = pos;

            // 朝向用「實際位移」算，不是理論切線——不規則之後兩者已經不一樣了
            if (m.hasLast)
            {
                Vector3 v = pos - m.lastPos;
                v.z = 0f;
                if (v.sqrMagnitude > 0.000001f)
                {
                    Quaternion want = Quaternion.LookRotation(v.normalized, Vector3.up);
                    m.tr.localRotation = Quaternion.Slerp(m.tr.localRotation, want, 1f - Mathf.Exp(-8f * Time.deltaTime));
                }
            }
            m.lastPos = pos;
            m.hasLast = true;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 c = transform.position;
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.7f);
        const int seg = 48;
        Vector3 prev = c + new Vector3(radiusX, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusY, 0f);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        // 軸心
        Gizmos.color = new Color(1f, 0.45f, 0.35f, 0.9f);
        Gizmos.DrawLine(c + new Vector3(-1.2f, 0f, 0f), c + new Vector3(1.2f, 0f, 0f));
        Gizmos.DrawLine(c + new Vector3(0f, -1.2f, 0f), c + new Vector3(0f, 1.2f, 0f));
    }
#endif
}
