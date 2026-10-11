using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 雲朵著陸棉絮特效 (Cloud Landing Fluff VFX)。
/// 玩家跳躍/墜落後著陸在 CloudPlatform 相關物件上時，於腳下蹦發出棉絮粒子，營造踩在棉花上的感覺。
/// 行走、站立時不生成；已生成的棉絮輕飄飄地散開後消失。
/// 粒子使用 Fluff (Stylized VFX) 的棉絮貼圖 (3x3 共 9 種形狀)，以永遠面向鏡頭的 Billboard 繪製，
/// 避免 Fluff.vfx 的 3D 翻轉平面在側面視角下被看成細長針狀。
/// 判定方式：場景中的 CloudPlatform 為純視覺 Sprite (實際碰撞為共用地板)，
/// 因此以「腳底位置是否落在雲朵 Sprite 範圍內」為主，並輔以腳下碰撞體名稱比對。
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class CloudStepFluffVFX : MonoBehaviour
{
    [Header("特效來源 (VFX Source)")]
    [Tooltip("棉絮粒子材質 (Mat_FluffParticle：使用 Stylized VFX 的 Fluff 貼圖，3x3 共 9 種棉絮形狀)")]
    public Material fluffMaterial;

    [Header("雲朵判定 (Cloud Detection)")]
    [Tooltip("物件名稱包含任一關鍵字即視為雲朵平台 (不分大小寫、忽略底線與空白)")]
    public string[] cloudNameKeywords = { "CloudPlatform" };
    [Tooltip("腳底高於雲朵 Sprite 頂端多少以內仍算踩在雲上")]
    public float cloudTopTolerance = 0.5f;

    [Header("著陸噴發 (Landing Burst)")]
    [Tooltip("每次著陸噴出的棉絮數量")]
    public int burstCount = 12;
    [Tooltip("騰空超過此時間 (秒) 後著陸才觸發，避免斜坡/接縫上的瞬間離地誤觸發")]
    public float minAirTime = 0.12f;
    [Tooltip("腳下生成範圍的半寬 (世界單位)")]
    public float spawnHalfWidth = 0.6f;
    [Tooltip("相對腳底中心的位置偏移 (角色的腳陷在雲朵圖片裡，Y 需抬到雲朵表面附近棉絮才看得到)")]
    public Vector3 feetOffset = new Vector3(0f, 0.45f, 0f);
    [Tooltip("前後 (Z) 隨機散佈範圍：負值在角色前方、正值在角色後方，讓棉絮有層次")]
    public Vector2 depthRange = new Vector2(-0.6f, 0.3f);

    [Header("棉絮動態 (Motion)")]
    [Tooltip("噴出初速 (最小; 最大)")]
    public Vector2 burstSpeed = new Vector2(0.8f, 2.2f);
    [Tooltip("空氣阻力 (越大噴出後越快停下來漂浮；噴出距離約為 初速 / 阻力)")]
    public float drag = 3.5f;
    [Tooltip("重力倍率 (0 = 無重力，負值會緩緩上飄，正值會下墜)")]
    public float gravityModifier = -0.01f;
    [Tooltip("隨機飄動強度")]
    public float turbulence = 0.15f;

    [Header("粒子外觀 (Particle Look)")]
    [Tooltip("棉絮尺寸 (最小; 最大，世界單位；貼圖四周有留白，實際看到的棉絮約為此數值的一半)")]
    public Vector2 particleSize = new Vector2(0.6f, 1.2f);
    [Tooltip("棉絮存活時間 (最小; 最大)")]
    public Vector2 particleLifetime = new Vector2(1.4f, 2.2f);
    [Tooltip("棉絮顏色與透明度 (雲朵為米白色，偏藍白才有對比)")]
    public Color fluffColor = new Color(0.72f, 0.83f, 1f, 1f);
    [Tooltip("排序順序 (需高於雲朵 Sprite 才不會被遮住)")]
    public int sortingOrder = 25;
    [Tooltip("粒子物件相對噴發點的 Z 偏移 (負值 = 朝鏡頭)，用來確保棉絮畫在雲朵圖片前面")]
    public float sortDepthOffset = -1f;

    // 供除錯/驗收觀察的即時狀態
    public bool IsOnCloud { get; private set; }
    public int LandingBurstCount { get; private set; }
    public int AliveParticleCount => _ps != null ? _ps.particleCount : 0;

    private PlayerMovement _player;
    private Collider _col;
    private ParticleSystem _ps;
    private List<SpriteRenderer> _cloudSprites = new List<SpriteRenderer>();
    private float _nextRecacheTime;
    private bool _wasGrounded;
    private float _airTime;

    private const float CloudRecacheInterval = 1f;

    private void Start()
    {
        _player = GetComponent<PlayerMovement>();
        _col = GetComponent<Collider>();

        CacheCloudSprites();
        CreateParticleSystem();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CacheCloudSprites();
    }

    private void OnDestroy()
    {
        if (_ps != null) Destroy(_ps.gameObject);
    }

    private void CacheCloudSprites()
    {
        if (_cloudSprites == null) _cloudSprites = new List<SpriteRenderer>();
        _cloudSprites.Clear();
        _nextRecacheTime = Time.time + CloudRecacheInterval;
        SpriteRenderer[] all = FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include);
        foreach (SpriteRenderer sr in all)
        {
            if (IsCloudName(sr.gameObject.name)) _cloudSprites.Add(sr);
        }
    }

    private void CreateParticleSystem()
    {
        if (fluffMaterial == null)
        {
            Debug.LogWarning("[CloudStepFluffVFX] 尚未指定 fluffMaterial，無法產生著陸棉絮特效。", this);
            return;
        }

        // 不掛在玩家底下：玩家為非等比縮放，且粒子需留在世界空間原地飄散
        GameObject go = new GameObject("CloudLandingFluff_Particles");
        _ps = go.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _ps.main;
        main.playOnAwake = false;
        // 需保持循環播放狀態：系統一旦播完停止，之後 Emit 的粒子就不會被模擬 (停在透明的初始狀態)
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        // 只由程式在著陸時 Emit，不自動發射
        var emission = _ps.emission;
        emission.enabled = false;
        var shape = _ps.shape;
        shape.enabled = false;

        // 9 種棉絮形狀 (3x3)，生成時隨機抽一種後固定
        var tsa = _ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 3;
        tsa.numTilesY = 3;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        // 上限需略小於 1：frame = floor(startFrame * 9)，剛好 1 會超出最後一格
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 8.99f / 9f);

        // 淡入 -> 停留 -> 淡出
        var colOverLife = _ps.colorOverLifetime;
        colOverLife.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) }
        );
        colOverLife.color = grad;

        // 噴出後略微膨脹，像棉花鬆開
        var sizeOverLife = _ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.15f)));

        var rotOverLife = _ps.rotationOverLifetime;
        rotOverLife.enabled = true;
        rotOverLife.z = new ParticleSystem.MinMaxCurve(-25f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad);

        var psRenderer = go.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        psRenderer.sharedMaterial = fluffMaterial;
        psRenderer.sortingOrder = sortingOrder;

        ApplyTunableSettings();
        _ps.Play();
    }

    // 每次噴發前套用，讓 Inspector 在 Play 模式中的調整即時反映
    private void ApplyTunableSettings()
    {
        var main = _ps.main;
        main.gravityModifier = gravityModifier;

        var limit = _ps.limitVelocityOverLifetime;
        // 使用以秒為單位的 drag (與幀率無關)，不用逐幀衰減的 dampen
        limit.enabled = drag > 0f;
        limit.limit = 1000f;
        limit.dampen = 0f;
        limit.drag = drag;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;

        var noise = _ps.noise;
        noise.enabled = turbulence > 0f;
        noise.strength = turbulence;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.3f;

        var psRenderer = _ps.GetComponent<ParticleSystemRenderer>();
        psRenderer.sortingOrder = sortingOrder;
    }

    private void LateUpdate()
    {
        if (_ps == null || _player == null) return;

        // 清單為空或雲朵物件已被銷毀 (場景重載、腳本熱重載) 時定期重新搜尋
        if (Time.time >= _nextRecacheTime && (_cloudSprites == null || _cloudSprites.Count == 0 || _cloudSprites[0] == null))
        {
            CacheCloudSprites();
        }

        Vector3 feet = GetFeetPosition();
        bool grounded = _player.isGrounded;
        IsOnCloud = grounded && IsFeetOnCloud(feet);

        // 著陸瞬間 (騰空夠久 -> 踩上雲朵) 蹦發棉絮
        if (!grounded) _airTime += Time.deltaTime;
        if (grounded && !_wasGrounded && IsOnCloud && _airTime >= minAirTime)
        {
            EmitBurst(feet + feetOffset);
            LandingBurstCount++;
        }
        if (grounded) _airTime = 0f;

        _wasGrounded = grounded;
    }

    private void EmitBurst(Vector3 origin)
    {
        ApplyTunableSettings();
        if (!_ps.isPlaying) _ps.Play();

        // 透明物件的前後順序會參考粒子物件本身的位置：把它移到噴發點並朝鏡頭靠近，
        // 棉絮才會畫在雲朵圖片前面 (粒子為世界空間模擬，移動物件不影響已噴出的棉絮)
        _ps.transform.position = origin + new Vector3(0f, 0f, sortDepthOffset);

        for (int i = 0; i < burstCount; i++)
        {
            // 往左右兩側與上方散開 (角度 15°~165°，避免往地板裡噴)
            float angle = Random.Range(15f, 165f) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f, 0f);

            var p = new ParticleSystem.EmitParams
            {
                position = origin + new Vector3(Random.Range(-spawnHalfWidth, spawnHalfWidth), Random.Range(0f, 0.15f), Random.Range(depthRange.x, depthRange.y)),
                velocity = dir * Random.Range(burstSpeed.x, burstSpeed.y),
                startSize = Random.Range(particleSize.x, particleSize.y),
                startLifetime = Random.Range(particleLifetime.x, particleLifetime.y),
                startColor = fluffColor,
                rotation = Random.Range(0f, 360f)
            };
            _ps.Emit(p, 1);
        }
    }

    private Vector3 GetFeetPosition()
    {
        if (_col != null)
        {
            Bounds b = _col.bounds;
            return new Vector3(b.center.x, b.min.y, b.center.z);
        }
        return transform.position;
    }

    private bool IsFeetOnCloud(Vector3 feet)
    {
        // 1. 腳底落在任一雲朵 Sprite 的範圍內
        for (int i = 0; i < _cloudSprites.Count; i++)
        {
            SpriteRenderer sr = _cloudSprites[i];
            if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy) continue;

            Bounds b = sr.bounds;
            if (feet.x < b.min.x || feet.x > b.max.x) continue;
            if (feet.y < b.min.y || feet.y > b.max.y + cloudTopTolerance) continue;
            return true;
        }

        // 2. 腳下碰撞體 (或其父物件) 本身就是雲朵平台
        Vector3 origin = feet + Vector3.up * 0.2f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            for (Transform t = hit.collider.transform; t != null; t = t.parent)
            {
                if (t == transform) break;
                if (IsCloudName(t.name)) return true;
            }
        }

        return false;
    }

    private bool IsCloudName(string objectName)
    {
        if (cloudNameKeywords == null || string.IsNullOrEmpty(objectName)) return false;

        string normalized = Normalize(objectName);
        foreach (string keyword in cloudNameKeywords)
        {
            if (string.IsNullOrEmpty(keyword)) continue;
            if (normalized.Contains(Normalize(keyword))) return true;
        }
        return false;
    }

    private static string Normalize(string s)
    {
        return s.Replace("_", "").Replace(" ", "").ToLowerInvariant();
    }
}
