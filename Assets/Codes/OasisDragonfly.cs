using UnityEngine;

// ★1109e 綠洲蜻蜓：Perlin 噪聲柔和漫遊、偶爾懸停或急衝；玩家靠近就往上／往外逃，過幾秒再飛回來
// 美術朝右；兩張圖交替拍翅。用 Time.deltaTime (timeScale 0 時會跟著凍結)
[RequireComponent(typeof(SpriteRenderer))]
public class OasisDragonfly : MonoBehaviour
{
    public Sprite frameA;   // 翅膀上
    public Sprite frameB;   // 翅膀下

    [Header("活動範圍 (世界座標)")]
    public Vector2 areaMin = new Vector2(271f, -7f);
    public Vector2 areaMax = new Vector2(291f, -2.5f);

    [Header("飛行")]
    public float cruiseSpeed = 2.2f;
    public float dartSpeed = 9f;
    [Tooltip("速度追上目標的快慢 (越大越敏捷)")] public float accel = 4f;
    public Vector2 hoverTime = new Vector2(0.5f, 1.5f);
    public Vector2 hoverInterval = new Vector2(3f, 7f);
    public Vector2 dartInterval = new Vector2(5f, 11f);
    public float bobAmp = 0.06f;

    [Header("拍翅")]
    public float flapHz = 28f;
    public float hoverFlapHz = 18f;

    [Header("躲玩家")]
    public float fleeDistance = 3.5f;
    public float fleeSpeed = 8f;
    public Vector2 awayTime = new Vector2(3f, 5f);

    public int seed = 1;

    enum State { Cruise, Hover, Dart, Flee }
    State state = State.Cruise;
    float stateEnd, nextHover, nextDart;
    Vector2 pos, vel, dartDir, awayOffset;
    float awayUntil = -1f;
    float phase, flapAcc;
    bool faceRight = true;
    SpriteRenderer sr;
    Transform player;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        var rnd = new System.Random(seed * 7919 + 13);
        phase = (float)rnd.NextDouble() * 100f;
        pos = new Vector2(
            Mathf.Lerp(areaMin.x, areaMax.x, (float)rnd.NextDouble()),
            Mathf.Lerp(areaMin.y, areaMax.y, (float)rnd.NextDouble()));
        vel = Vector2.right * cruiseSpeed;
        nextHover = Time.time + Random.Range(hoverInterval.x, hoverInterval.y);
        nextDart = Time.time + Random.Range(dartInterval.x, dartInterval.y);
        Apply();
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        float now = Time.time;

        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
        }

        // 玩家太近 -> 逃 (往遠離玩家的方向，並偏向上方)
        if (player != null && state != State.Flee)
        {
            Vector2 pc = (Vector2)player.position + Vector2.up * 1.8f;
            Vector2 d = pos - pc;
            if (d.magnitude < fleeDistance)
            {
                Vector2 dir = d.sqrMagnitude < 0.01f ? Vector2.up : d.normalized;
                dir = (dir + Vector2.up * 0.9f).normalized;
                dartDir = dir;
                state = State.Flee;
                stateEnd = now + 1.1f;
                awayUntil = now + Random.Range(awayTime.x, awayTime.y);
                awayOffset = new Vector2((dir.x < 0f ? -1f : 1f) * 7f, 6f);
            }
        }

        Vector2 desired;
        float acc = accel;
        switch (state)
        {
            case State.Hover:
                desired = new Vector2(Mathf.PerlinNoise(phase, now * 3f) - 0.5f, Mathf.PerlinNoise(now * 3f, phase) - 0.5f) * 0.6f;
                if (now >= stateEnd) { state = State.Cruise; nextHover = now + Random.Range(hoverInterval.x, hoverInterval.y); }
                break;
            case State.Dart:
                desired = dartDir * dartSpeed; acc = 14f;
                if (now >= stateEnd) { state = State.Cruise; nextDart = now + Random.Range(dartInterval.x, dartInterval.y); }
                break;
            case State.Flee:
                desired = dartDir * fleeSpeed; acc = 10f;
                if (now >= stateEnd) state = State.Cruise;
                break;
            default:
                desired = CruiseVelocity(now);
                if (now >= nextHover) { state = State.Hover; stateEnd = now + Random.Range(hoverTime.x, hoverTime.y); }
                else if (now >= nextDart)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    dartDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f).normalized;
                    // 急衝若會衝出範圍，就改往中心
                    Vector2 center = (areaMin + areaMax) * 0.5f;
                    if (!InArea(pos + dartDir * 2f) && now >= awayUntil) dartDir = (center - pos).normalized;
                    state = State.Dart; stateEnd = now + 0.25f;
                }
                break;
        }

        vel = Vector2.Lerp(vel, desired, 1f - Mathf.Exp(-acc * dt));
        pos += vel * dt;
        Apply();

        // 翅膀
        float hz = state == State.Hover ? hoverFlapHz : flapHz;
        flapAcc += dt * hz;
        sr.sprite = ((int)flapAcc & 1) == 0 ? frameA : frameB;
    }

    Vector2 CruiseVelocity(float now)
    {
        // Perlin 角度 -> 平滑轉彎
        float n = Mathf.PerlinNoise(phase, now * 0.35f);
        float ang = n * Mathf.PI * 4f;
        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f);

        // 逃走後一段時間內，活動中心被推到遠處
        Vector2 center = (areaMin + areaMax) * 0.5f;
        Vector2 target = now < awayUntil ? center + awayOffset : center;
        Vector2 toC = target - pos;
        Vector2 half = (areaMax - areaMin) * 0.5f;
        float outX = Mathf.Max(0f, Mathf.Abs(pos.x - center.x) / half.x - 0.8f);
        float outY = Mathf.Max(0f, Mathf.Abs(pos.y - center.y) / half.y - 0.8f);
        float pull = now < awayUntil ? 1.5f : Mathf.Clamp01((outX + outY) * 3f) * 3f;
        if (pull > 0f) dir = (dir + toC.normalized * pull).normalized;
        return dir * cruiseSpeed;
    }

    bool InArea(Vector2 p) { return p.x > areaMin.x && p.x < areaMax.x && p.y > areaMin.y && p.y < areaMax.y; }

    void Apply()
    {
        if (Mathf.Abs(vel.x) > 0.4f) faceRight = vel.x > 0f;
        sr.flipX = !faceRight;
        float bob = Mathf.Sin(Time.time * 7f + phase) * bobAmp;
        transform.position = new Vector3(pos.x, pos.y + bob, transform.position.z);
    }
}
