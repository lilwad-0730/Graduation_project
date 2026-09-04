using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 我你他　鳥群系統（提案版）　散落盤旋的鳥群
///
/// ═══════════════════════════════════════════════════
/// ★ 這是提案，不是取代。組員原本的 BirdEnemy／IndividualBirdEnemy
///   一根寒毛都沒動，現在照常運作。這一團只是多出來的、在天上散著繞的鳥。
///   等大家同意了，再用同資料夾的 FlockTakeover 一鍵接手。
/// ═══════════════════════════════════════════════════
///
/// 【散落，不規則】
///   不是一圈排整齊的鳥。每隻有自己的散落位置（高低遠近都不同）、
///   自己的繞行半徑、自己的速度，路徑再疊三層 Perlin 噪聲：
///   角度忽快忽慢、圈忽大忽小、每隻再各自亂飄。
///   沒有兩隻走同一條線，同一隻也不會走第二遍同樣的路。
///
/// 【軸心永遠在她身上】
///   整團的軸心鎖在玩家的 transform（不是鏡頭）。
///   所有散落位移在生成時會被扣掉平均值，所以「軸心的中點」
///   在數學上就是玩家本人，不管牠們飄得多亂，整團不會漂走。
///
/// 【俯衝攻擊：預設關閉】
///   attackEnabled 打開後，這團就有能力做組員那批鳥的工作：
///   挑一隻脫隊 → 叫聲警告 → 俯衝 → 命中觸發重生 → 拉起歸隊。
///   護盾、雨傘、無敵、重生中的判定全部沿用專案既有的規則。
///   在大家點頭之前，這個開關一直是關的。
/// </summary>
[DisallowMultipleComponent]
public class ScatteredFlock : MonoBehaviour
{
    public enum AnchorMode { Player, Camera, Fixed }

    [Header("生成")]
    [Tooltip("鳥的模型（荒原用的是 living birds 的 crow）")]
    public GameObject birdPrefab;

    [Tooltip("鳥的 Animator Controller（living birds 的 birdAnimatorController）")]
    public RuntimeAnimatorController birdAnimator;

    [Tooltip("平飛動畫狀態名")]
    public string flyStateName = "flying";

    [Tooltip("警戒動畫狀態名（俯衝前）")]
    public string warnStateName = "worried";

    [Tooltip("幾隻")]
    [Range(1, 40)] public int count = 11;

    [Tooltip("每隻的縮放基準。組員那批俯衝鳥的世界縮放約 21.5")]
    public float birdScale = 9.5f;

    [Tooltip("每隻大小再差多少（比例）。有大有小才像散在不同距離")]
    [Range(0f, 0.6f)] public float scaleJitter = 0.3f;

    // ══════════════════════════════════════════
    [Header("軸心：鎖在她身上")]
    [Tooltip("軸心跟著誰。Player＝鎖在玩家身上（預設）")]
    public AnchorMode anchor = AnchorMode.Player;

    [Tooltip("軸心追上玩家的遲滯秒數。0＝硬鎖；大一點會像被拖著走")]
    public float followLag = 0.55f;

    [Tooltip("找不到玩家時退而用鏡頭的哪個位置")]
    public Vector2 cameraFallbackViewport = new Vector2(0.5f, 0.8f);

    [Tooltip("深度。荒原：背景 z=0.5、玩法 z=0，所以 0.3 是「天上、在背景前面」")]
    public float depthZ = 0.3f;

    // ══════════════════════════════════════════
    [Header("散落（不是排成一圈）")]
    [Tooltip("散落區域的中心，相對軸心。y 抬高才會整團在她頭上")]
    public Vector2 scatterCenter = new Vector2(0f, 18f);   // 拉高：在高空盤旋，不接觸主角

    [Tooltip("散落區域的大小（寬 × 高）。每隻在這個範圍內各自找位置")]
    public Vector2 scatterSize = new Vector2(30f, 10f);

    [Tooltip("保證每隻至少高過她多少公尺（防止有鳥掉進地面）")]
    public float minHeightAbovePlayer = 9f;   // 任何一隻都不會低於主角頭頂 9 單位

    [Header("每隻自己的小圈")]
    [Tooltip("繞行半徑（橫、縱）的基準")]
    public Vector2 orbitRadius = new Vector2(4.5f, 1.6f);

    [Tooltip("每隻半徑差多少（比例）")]
    [Range(0f, 0.8f)] public float radiusJitter = 0.45f;

    [Tooltip("繞一圈幾秒（平均值）")]
    public float periodSeconds = 9f;

    [Tooltip("每隻速度差多少（比例）")]
    [Range(0f, 0.6f)] public float periodJitter = 0.35f;

    [Header("不規則：讓牠們不像機器")]
    [Tooltip("角度上的搖擺（弧度）。忽快忽慢，不是等速繞圈")]
    [Range(0f, 1.5f)] public float wanderAngle = 0.6f;

    [Tooltip("半徑上的呼吸（比例）。圈忽大忽小")]
    [Range(0f, 0.8f)] public float wanderRadius = 0.35f;

    [Tooltip("每隻自己的亂飄幅度（公尺）。這條最影響「像不像活的」")]
    public float driftAmplitude = 3.2f;

    [Tooltip("整團軸心在她身上的小幅晃動（公尺）。0＝軸心死死釘在她身上")]
    public float flockWander = 1.2f;

    [Tooltip("噪聲跑多快。大＝慌張，小＝慵懶")]
    [Range(0.02f, 1.5f)] public float noiseSpeed = 0.18f;

    // ══════════════════════════════════════════
    [Header("★ 俯衝攻擊（預設關閉，等大家同意再開）")]
    [Tooltip("打開後這團就能做組員那批鳥的工作。沒有人同意之前請保持關閉")]
    public bool attackEnabled = false;

    [Tooltip("兩次俯衝之間至少隔幾秒")]
    public float attackInterval = 7f;

    [Tooltip("玩家在幾公尺內才會考慮攻擊")]
    public float attackRange = 16f;

    [Tooltip("叫聲警告幾秒後才真的衝下來（給玩家反應時間）")]
    public float warningSeconds = 1.2f;

    [Tooltip("俯衝速度（公尺／秒）")]
    public float diveSpeed = 22f;

    [Tooltip("命中判定半徑")]
    public float hitRadius = 1.1f;

    [Tooltip("俯衝最長幾秒（超過就自己拉起，不會卡住）")]
    public float diveTimeout = 2.2f;

    [Tooltip("拉起歸隊要幾秒")]
    public float returnSeconds = 1.6f;

    [Tooltip("警告叫聲（選填）")]
    public AudioClip warningCry;
    [Range(0f, 1f)] public float cryVolume = 0.8f;

    // ══════════════════════════════════════════
    [Header("看起來像遠的")]
    [Tooltip("把鳥調暗成剪影（用 MaterialPropertyBlock，不會動到共用材質）")]
    public bool tintDistant = true;
    public Color distantTint = new Color(0.42f, 0.40f, 0.46f, 1f);

    // ── 內部 ─────────────────────────────────
    private class Member
    {
        public Transform tr;
        public Animator anim;
        public Vector2 home;      // 散落位置（相對軸心，已扣掉平均值）
        public float phase, rScale, speed, zNudge;
        public Vector2 seedA, seedR, seedDX, seedDY;
        public Vector3 lastPos;
        public bool hasLast;
        public bool busy;         // 正在俯衝，暫時不歸隊形管
    }

    private readonly List<Member> _birds = new List<Member>();
    private Transform _player;
    private Camera _cam;
    private Vector3 _vel;
    private Vector2 _flockSeedX, _flockSeedY;
    private float _nextAttackAt;
    private bool _spawned, _snapped;
    private static readonly int _ColorId = Shader.PropertyToID("_Color");
    private static readonly int _BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _MainTexId = Shader.PropertyToID("_MainTex");

    /// <summary>把不是 URP 的材質換成 URP Simple Lit，貼圖與顏色照搬（否則 URP 下整隻粉紅）。</summary>
    private static void MakeUrpSafe(GameObject go)
    {
        Shader urp = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (urp == null) urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null) urp = Shader.Find("Universal Render Pipeline/Unlit");
        if (urp == null) return;

        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] == null) continue;
            Material[] mats = rs[i].sharedMaterials;
            bool changed = false;
            for (int k = 0; k < mats.Length; k++)
            {
                Material m = mats[k];
                if (m == null || m.shader == null) continue;
                if (m.shader.name.StartsWith("Universal Render Pipeline/")) continue;

                Material nm = new Material(urp);
                nm.name = m.name + "_URP";
                Texture tex = m.HasProperty(_MainTexId) ? m.GetTexture(_MainTexId) : null;
                if (tex == null) tex = m.mainTexture;
                if (tex != null && nm.HasProperty(_BaseMapId)) nm.SetTexture(_BaseMapId, tex);
                Color c = m.HasProperty(_ColorId) ? m.GetColor(_ColorId) : Color.white;
                if (nm.HasProperty(_BaseColorId)) nm.SetColor(_BaseColorId, c);
                mats[k] = nm;
                changed = true;
            }
            if (changed) rs[i].sharedMaterials = mats;
        }
    }

    /// <summary>-0.5 ~ +0.5 的平滑噪聲</summary>
    private static float N(Vector2 seed, float t)
    {
        return Mathf.PerlinNoise(seed.x + t, seed.y) - 0.5f;
    }

    public int BirdCount { get { return _birds.Count; } }

    private void Start()
    {
        _flockSeedX = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
        _flockSeedY = new Vector2(Random.Range(0f, 500f), Random.Range(0f, 500f));
        _nextAttackAt = Time.time + attackInterval;
        Spawn();
    }

    // ══════════════════════════════════════════
    // 生成：先散落，再把平均值扣掉
    // ══════════════════════════════════════════
    private void Spawn()
    {
        if (_spawned) return;
        _spawned = true;

        if (birdPrefab == null)
        {
            Debug.LogWarning("[ScatteredFlock] 沒有指定鳥的模型，這團不會生出東西。", this);
            return;
        }

        List<Vector2> homes = new List<Vector2>();
        for (int i = 0; i < count; i++)
        {
            // 散落：不是等分排一圈，是各自在區域裡找位置（再推開一點避免擠在一起）
            Vector2 h = new Vector2(
                Random.Range(-0.5f, 0.5f) * scatterSize.x,
                Random.Range(-0.5f, 0.5f) * scatterSize.y);
            for (int k = 0; k < homes.Count; k++)
            {
                Vector2 dv = h - homes[k];
                float min = orbitRadius.x * 0.9f;
                if (dv.sqrMagnitude < min * min && dv.sqrMagnitude > 0.0001f)
                    h = homes[k] + dv.normalized * min;
            }
            homes.Add(h);
        }

        // ★把平均值扣掉：整團散落位移的中點＝零＝軸心＝玩家本人
        Vector2 mean = Vector2.zero;
        for (int i = 0; i < homes.Count; i++) mean += homes[i];
        mean /= Mathf.Max(1, homes.Count);
        for (int i = 0; i < homes.Count; i++) homes[i] = homes[i] - mean + scatterCenter;

        for (int i = 0; i < count; i++)
        {
            GameObject go = null;
            try
            {
                Object spawnedObj = Object.Instantiate((Object)birdPrefab, transform);
                if (spawnedObj is GameObject g) go = g;
                else if (spawnedObj is Component c) go = c.gameObject;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ScatteredFlock] 生成模型例外: {ex.Message}");
            }

            if (go == null) continue;
            go.name = "FlockBird_" + i;

            // ★安全：這團是純景，不參與任何碰撞（攻擊用距離判定，不用 collider）
            StripPhysics(go);
            MakeUrpSafe(go);   // living birds 的原始材質是內建管線 shader，URP 下會整隻粉紅

            Transform t = go.transform;
            t.localScale = Vector3.one * (birdScale * (1f + Random.Range(-scaleJitter, scaleJitter)));

            Animator anim = go.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                if (birdAnimator != null) anim.runtimeAnimatorController = birdAnimator;
                anim.applyRootMotion = false;
                if (anim.runtimeAnimatorController != null && !string.IsNullOrEmpty(flyStateName))
                {
                    AnimStateResolver.SetFlying(anim, true);                            // living birds 的 controller：flying=false 會馬上 fly→landing→Idle（變成站姿）
                    AnimStateResolver.PlaySafe(anim, flyStateName, Random.value);   // 拍翅相位錯開（flying/fly 別名自動對）
                }
            }

            if (tintDistant) Tint(go);

            Member m = new Member();
            m.tr = t;
            m.anim = anim;
            m.home = homes[i];
            m.phase = Random.Range(0f, Mathf.PI * 2f);
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
        // 移除 living birds 原生 AI 腳本（避免與純視覺鳥群系統衝突並缺少 Rigidbody 報錯）
        MonoBehaviour[] scripts = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < scripts.Length; i++)
        {
            if (scripts[i] == null) continue;
            string sName = scripts[i].GetType().Name.ToLower();
            if (sName.StartsWith("lb_") || sName.Contains("birdcontroller"))
            {
                Object.Destroy(scripts[i]);
            }
        }

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
            mpb.SetColor(_BaseColorId, distantTint);   // URP 材質看的是 _BaseColor
            rs[i].SetPropertyBlock(mpb);
        }
    }

    private void LateUpdate()
    {
        MoveAxis();
        Fly();
        TryAttack();
    }

    // ══════════════════════════════════════════
    // 軸心
    // ══════════════════════════════════════════
    private void MoveAxis()
    {
        if (anchor == AnchorMode.Fixed) return;

        Vector3 want;
        if (!TryGetAnchorPoint(out want)) return;

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
            if (_player != null) { p = _player.position; p.z = depthZ; return true; }
        }
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) return false;
        float dist = depthZ - _cam.transform.position.z;
        p = _cam.ViewportToWorldPoint(new Vector3(cameraFallbackViewport.x, cameraFallbackViewport.y, dist));
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
    // 飛
    // ══════════════════════════════════════════
    private void Fly()
    {
        if (_birds.Count == 0) return;
        float baseW = (Mathf.PI * 2f) / Mathf.Max(0.1f, periodSeconds);
        float t = Time.time;
        float nt = t * noiseSpeed;
        float floorY = (_player != null ? _player.position.y : transform.position.y) + minHeightAbovePlayer;

        for (int i = 0; i < _birds.Count; i++)
        {
            Member m = _birds[i];
            if (m.tr == null || m.busy) continue;

            float a = m.phase + baseW * m.speed * t + N(m.seedA, nt) * 2f * wanderAngle;
            float rk = m.rScale * (1f + N(m.seedR, nt) * 2f * wanderRadius);
            float dx = N(m.seedDX, nt * 1.37f) * 2f * driftAmplitude;
            float dy = N(m.seedDY, nt * 1.11f) * 2f * driftAmplitude * 0.5f;

            Vector3 pos = new Vector3(
                m.home.x + Mathf.Cos(a) * orbitRadius.x * rk + dx,
                m.home.y + Mathf.Sin(a) * orbitRadius.y * rk + dy,
                m.zNudge);

            // 安全網：誰都不准掉到她腳下
            float worldY = transform.position.y + pos.y;
            if (worldY < floorY) pos.y += (floorY - worldY);

            m.tr.localPosition = pos;
            FaceMotion(m, pos);
        }
    }

    private void FaceMotion(Member m, Vector3 pos)
    {
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

    // ══════════════════════════════════════════
    // 俯衝攻擊（預設關閉）
    // ══════════════════════════════════════════
    private void TryAttack()
    {
        if (!attackEnabled) return;
        if (Time.time < _nextAttackAt) return;
        if (_birds.Count == 0) return;
        if (!CanAttackNow()) { _nextAttackAt = Time.time + 0.5f; return; }

        if (_player == null) _player = FindPlayer();
        if (_player == null) return;
        if ((_player.position - transform.position).sqrMagnitude > attackRange * attackRange) return;

        // 挑一隻沒在忙的
        int start = Random.Range(0, _birds.Count);
        for (int k = 0; k < _birds.Count; k++)
        {
            Member m = _birds[(start + k) % _birds.Count];
            if (m.tr != null && !m.busy)
            {
                _nextAttackAt = Time.time + attackInterval;
                StartCoroutine(DiveRoutine(m));
                return;
            }
        }
    }

    /// <summary>沿用專案既有規則：重生中、剛重生還沒動、躲在傘下都不攻擊。</summary>
    private static bool CanAttackNow()
    {
        if (PlayerRespawnSystem.IsAnyRespawning) return false;
        if (!PlayerRespawnSystem.IsPlayerMovingAfterRespawn) return false;
        if (UmbrellaZone.IsPlayerUnderUmbrella) return false;
        return true;
    }

    private IEnumerator DiveRoutine(Member m)
    {
        m.busy = true;
        Vector3 startLocal = m.tr.localPosition;

        // 1　叫聲警告：先讓她知道有東西要下來
        if (m.anim != null && !string.IsNullOrEmpty(warnStateName)) AnimStateResolver.PlaySafe(m.anim, warnStateName, 0f);
        if (warningCry != null) AudioSource.PlayClipAtPoint(warningCry, m.tr.position, AudioManager.ScaleSfx(cryVolume));   // ★走 SFX 通道
        float wt = 0f;
        while (wt < warningSeconds)
        {
            wt += Time.deltaTime;
            if (!CanAttackNow()) break;
            yield return null;
        }

        // 2　俯衝
        if (m.anim != null && !string.IsNullOrEmpty(flyStateName)) { AnimStateResolver.SetFlying(m.anim, true); AnimStateResolver.PlaySafe(m.anim, flyStateName, 0f); }
        Vector3 target = _player != null ? _player.position : m.tr.position;
        target.z = m.tr.position.z;
        float dt = 0f;
        bool hit = false;
        while (dt < diveTimeout && CanAttackNow())
        {
            dt += Time.deltaTime;
            Vector3 to = target - m.tr.position;
            float dist = to.magnitude;
            if (dist <= hitRadius) { hit = true; break; }
            m.tr.position += to.normalized * diveSpeed * Time.deltaTime;
            Vector3 f = to; f.z = 0f;
            if (f.sqrMagnitude > 0.0001f) m.tr.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            yield return null;
        }

        if (hit) Strike();

        // 3　拉起歸隊
        Vector3 fromLocal = m.tr.localPosition;
        float rt = 0f;
        float dur = Mathf.Max(0.1f, returnSeconds);
        while (rt < dur)
        {
            rt += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, rt / dur);
            Vector3 p = Vector3.Lerp(fromLocal, startLocal, u);
            p.y += Mathf.Sin(u * Mathf.PI) * 2.2f;    // 拉起來的弧
            m.tr.localPosition = p;
            FaceMotion(m, p);
            yield return null;
        }

        m.hasLast = false;
        m.busy = false;
    }

    /// <summary>命中：護盾擋下就算了，無敵不處理，其餘觸發專案原本的重生流程。</summary>
    private void Strike()
    {
        if (_player == null) return;

        PlayerShield shield = _player.GetComponentInChildren<PlayerShield>();
        if (shield == null) shield = _player.GetComponentInParent<PlayerShield>();
        if (shield != null && shield.IsShieldActive) return;

        if (PlayerPetrification.IsGodMode) return;

        PlayerRespawnSystem respawn = _player.GetComponentInChildren<PlayerRespawnSystem>();
        if (respawn == null) respawn = _player.GetComponentInParent<PlayerRespawnSystem>();
        if (respawn != null) respawn.TriggerRespawn();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 c = transform.position;
        Vector3 sc = c + new Vector3(scatterCenter.x, scatterCenter.y, 0f);
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.55f);
        Gizmos.DrawWireCube(sc, new Vector3(scatterSize.x, scatterSize.y, 0.1f));
        Gizmos.color = new Color(1f, 0.45f, 0.35f, 0.95f);
        Gizmos.DrawLine(c + new Vector3(-1.4f, 0f, 0f), c + new Vector3(1.4f, 0f, 0f));
        Gizmos.DrawLine(c + new Vector3(0f, -1.4f, 0f), c + new Vector3(0f, 1.4f, 0f));
        Gizmos.DrawLine(c, sc);
    }
#endif
}
