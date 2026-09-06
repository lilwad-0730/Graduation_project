using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 深海背景巨影生物遊蕩系統 (Background Shadow Creature / Leviathan Roamer)
/// 1. 【嚴格視野鎖定 (Strict Proximity Focus)】：
///    - ★ 只要主角視野半徑內有路徑點，100% 優先在主角周圍徘徊，徹底杜絕巨獸飛到遙遠頂部！
///    - ★ 巨獸若在遠處，會被強制引導游回主角身邊！
///    - ★ 深海深度加權：Y 軸越深的海底區域，出沒機率越高！
/// 2. 【視野半徑 Scene 視覺化】：編輯器即時繪製主角視野金黃色感應圈與路徑點狀態。
/// 3. 【生物自然游動】：平滑轉向 (Slerp Turn)、波浪式游動起伏 (Swimming Wave)。
/// 4. 【純背景深度】：固定在背景層 (Z = 5 ~ 20)，100% 零碰撞零穿模。
/// </summary>
public class BackgroundShadowCreature : MonoBehaviour
{
    public enum PatrolMode
    {
        RandomWaypoints, // 智慧動態視野鎖定隨機 (強烈推薦！巨獸永遠在主角身邊遊蕩)
        Loop,            // 順序循環 (0 -> 1 -> 2 -> 3 -> 0)
        PingPong         // 來回折返 (0 -> 1 -> 2 -> 1 -> 0)
    }

    [Header("🧭 路徑與巡邏模式")]
    [Tooltip("遊蕩巡邏模式：RandomWaypoints (智慧視野鎖定隨機)、Loop (順序循環)、PingPong (來回折返)")]
    public PatrolMode patrolMode = PatrolMode.RandomWaypoints;

    [Tooltip("巡邏路徑點清單 (可在場景中放置多個空物件並拖入此處)")]
    public Transform[] waypoints;

    [Tooltip("到達路徑點的判定距離 (米)")]
    public float reachDistance = 2.5f;

    [Tooltip("抵達路徑點後的徘徊/停留時間 (秒，0 代表不停頓流暢游過)")]
    [Range(0f, 5f)]
    public float waypointWaitTime = 0.5f;

    [Header("🎯 主角視野鎖定與深度加權 (Player Proximity & Depth Focus)")]
    [Tooltip("是否嚴格鎖定主角視野周圍 (打勾後，巨獸絕不會亂跑到地圖最遠處，永遠在主角周邊遊蕩)")]
    public bool strictProximityLock = true;

    [Tooltip("主角視野感應半徑 (米，在此圓圈範圍內的路徑點會被優先選取)")]
    [Range(15f, 100f)]
    public float playerProximityRadius = 38.0f;

    [Tooltip("深海深度加權強度 (數值越高，Y 軸越深的海底路徑點被選取的機率越高)")]
    [Range(0f, 8f)]
    public float depthBiasWeight = 2.5f;

    [Header("🏊 移動與游動物理")]
    [Tooltip("基礎游動速度 (米/秒)")]
    [Range(1f, 15f)]
    public float swimSpeed = 4.0f;

    [Tooltip("轉向平滑度 (數值越小轉向越沉重緩慢，越具深海巨獸感，建議 1.0 ~ 2.5)")]
    [Range(0.5f, 6f)]
    public float turnSpeed = 1.8f;

    [Tooltip("固定景深 Z 座標 (確保位於背景層，建議 6 ~ 18)")]
    public float fixedZ = 10f;

    [Header("🌊 生物游動波浪起伏 (Natural Wave Dynamics)")]
    [Tooltip("是否開啟波浪式游動起伏")]
    public bool enableWaveMotion = true;

    [Tooltip("游動時上下波動的幅度 (米)")]
    public float waveAmplitude = 0.6f;

    [Tooltip("游動時波動的頻率")]
    public float waveFrequency = 1.5f;

    [Header("🎵 空間氛圍音效 (可選)")]
    [Tooltip("游經主角附近時播放的低頻音效 (例如 水下_巨影過頂.wav 或 水下_深處drone_loop.wav)")]
    public AudioClip proximitySFX;
    [Tooltip("音效感應觸發距離 (公尺)")]
    public float hearDistance = 18f;
    [Range(0f, 1f)] public float sfxVolume = 0.85f;
    [Tooltip("音效冷卻時間 (秒，防止反覆播放)")]
    public float sfxCooldown = 15f;

    [Header("👤 自動佔位黑影 (Stand-in Silhouette)")]
    [Tooltip("若本物件底下沒有任何子模型，是否自動生成臨時暗黑巨影佔位體？")]
    public bool autoCreateSilhouetteIfEmpty = true;
    [Tooltip("佔位黑影的長度與大小")]
    public Vector3 standinScale = new Vector3(8f, 2.5f, 2.5f);

    private int _currentWaypointIndex = 0;
    private int _pingPongDirection = 1;
    private bool _isWaiting = false;
    private float _lastSFXPlayTime = -999f;
    private Transform _playerTransform;
    private AudioSource _audioSource;
    private float _waveTimer = 0f;

    // ── 鰻魚剪影（程式生成）──
    private MeshFilter _eelMeshFilter;
    private Vector3[] _eelBaseVerts;
    private Vector3[] _eelWorkVerts;
    private float _eelLength;
    private float _eelPhase;

    private void Start()
    {
        Vector3 pos = transform.position;
        pos.z = fixedZ;
        transform.position = pos;

        EnsurePlayerReference();

        if (proximitySFX != null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.clip = proximitySFX;
            _audioSource.volume = sfxVolume;
            _audioSource.spatialBlend = 0.5f;
            _audioSource.maxDistance = hearDistance * 1.5f;
        }

        CheckAndCreateStandin();

        if (waypoints != null && waypoints.Length > 0)
        {
            SelectNextWaypoint();
        }
    }

    private void Update()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        EnsurePlayerReference();

        Transform targetWp = waypoints[_currentWaypointIndex];
        if (targetWp == null)
        {
            SelectNextWaypoint();
            return;
        }

        if (!_isWaiting)
        {
            Vector3 targetPos = targetWp.position;
            targetPos.z = fixedZ;

            Vector3 currentPos = transform.position;
            currentPos.z = fixedZ;

            Vector3 moveDir = (targetPos - currentPos);
            float distToTarget = moveDir.magnitude;

            if (distToTarget > 0.01f)
            {
                moveDir.Normalize();

                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * turnSpeed);

                float waveOffset = 0f;
                if (enableWaveMotion)
                {
                    _waveTimer += Time.deltaTime * waveFrequency;
                    waveOffset = Mathf.Sin(_waveTimer) * waveAmplitude;
                }

                Vector3 stepMove = moveDir * (swimSpeed * Time.deltaTime);
                Vector3 nextPos = currentPos + stepMove;
                nextPos.y += waveOffset * Time.deltaTime;
                nextPos.z = fixedZ;
                transform.position = nextPos;
            }

            if (distToTarget <= reachDistance)
            {
                StartCoroutine(WaypointReachedRoutine());
            }
        }

        UpdateEelUndulation();
        UpdateProximityAudio();
    }

    private IEnumerator WaypointReachedRoutine()
    {
        _isWaiting = true;

        if (waypointWaitTime > 0.01f)
        {
            yield return new WaitForSeconds(waypointWaitTime);
        }

        SelectNextWaypoint();
        _isWaiting = false;
    }

    private void SelectNextWaypoint()
    {
        if (waypoints == null || waypoints.Length <= 1) return;

        int count = waypoints.Length;

        switch (patrolMode)
        {
            case PatrolMode.RandomWaypoints:
                _currentWaypointIndex = CalculateStrictProximityWaypoint();
                break;

            case PatrolMode.Loop:
                _currentWaypointIndex = (_currentWaypointIndex + 1) % count;
                break;

            case PatrolMode.PingPong:
                _currentWaypointIndex += _pingPongDirection;
                if (_currentWaypointIndex >= count)
                {
                    _currentWaypointIndex = count - 2;
                    _pingPongDirection = -1;
                }
                else if (_currentWaypointIndex < 0)
                {
                    _currentWaypointIndex = 1;
                    _pingPongDirection = 1;
                }
                _currentWaypointIndex = Mathf.Clamp(_currentWaypointIndex, 0, count - 1);
                break;
        }
    }

    /// <summary>
    /// ★ 嚴格視野鎖定演算法：100% 優先鎖定主角視野半徑內的路徑點！
    /// </summary>
    private int CalculateStrictProximityWaypoint()
    {
        int count = waypoints.Length;
        Vector2 playerPos2D = _playerTransform != null 
            ? new Vector2(_playerTransform.position.x, _playerTransform.position.y) 
            : new Vector2(transform.position.x, transform.position.y);

        // 1. 篩選出落在主角視野感應圈內的所有候選點
        List<int> nearbyIndices = new List<int>();
        for (int i = 0; i < count; i++)
        {
            Transform wp = waypoints[i];
            if (wp == null || i == _currentWaypointIndex) continue;

            float dist = Vector2.Distance(new Vector2(wp.position.x, wp.position.y), playerPos2D);
            if (dist <= playerProximityRadius)
            {
                nearbyIndices.Add(i);
            }
        }

        // 2. 如果主角視野內有可用點（>= 1 個），100% 嚴格在此範圍內挑選！
        if (strictProximityLock && nearbyIndices.Count > 0)
        {
            // 取得候選點中 Y 軸範圍
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (int idx in nearbyIndices)
            {
                float y = waypoints[idx].position.y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float yRange = Mathf.Max(0.01f, maxY - minY);

            // 依深度加權挑選
            float[] weights = new float[nearbyIndices.Count];
            float totalWeight = 0f;

            for (int k = 0; k < nearbyIndices.Count; k++)
            {
                int wpIdx = nearbyIndices[k];
                Transform wp = waypoints[wpIdx];
                float depthFactor = (maxY - wp.position.y) / yRange; // 越深越大
                float w = 1.0f + depthFactor * depthBiasWeight;
                weights[k] = w;
                totalWeight += w;
            }

            float roll = Random.Range(0f, totalWeight);
            float accum = 0f;
            for (int k = 0; k < nearbyIndices.Count; k++)
            {
                accum += weights[k];
                if (roll <= accum)
                {
                    return nearbyIndices[k];
                }
            }

            return nearbyIndices[Random.Range(0, nearbyIndices.Count)];
        }

        // 3. 如果巨獸當前在遠處，或者主角周圍暫無點：強制尋找最接近主角視野的路徑點引導游回
        int closestIdx = -1;
        float minDistanceToPlayer = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Transform wp = waypoints[i];
            if (wp == null || i == _currentWaypointIndex) continue;

            float dist = Vector2.Distance(new Vector2(wp.position.x, wp.position.y), playerPos2D);
            if (dist < minDistanceToPlayer)
            {
                minDistanceToPlayer = dist;
                closestIdx = i;
            }
        }

        return closestIdx != -1 ? closestIdx : Random.Range(0, count);
    }

    private void EnsurePlayerReference()
    {
        if (_playerTransform != null) return;
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) _playerTransform = p.transform;
    }

    private void UpdateProximityAudio()
    {
        if (proximitySFX == null || _audioSource == null) return;
        EnsurePlayerReference();
        if (_playerTransform == null) return;

        float dist = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.y),
            new Vector2(_playerTransform.position.x, _playerTransform.position.y)
        );

        if (dist <= hearDistance && Time.time - _lastSFXPlayTime > sfxCooldown)
        {
            _lastSFXPlayTime = Time.time;
            _audioSource.PlayOneShot(proximitySFX, AudioManager.ScaleSfx(sfxVolume));
            Debug.Log($"🐋【深海巨影】巨獸游經主角附近 (距離 {dist:F1}m)，播放巨影氛圍音效！");
        }
    }

    private void CheckAndCreateStandin()
    {
        if (!autoCreateSilhouetteIfEmpty) return;

        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);
        if (childRenderers.Length > 0 && !(childRenderers.Length == 1 && childRenderers[0].gameObject == gameObject))
        {
            return;
        }

        // 生成「鰻魚」剪影：圓頭、飽滿身體、背鰭帶、收尖長尾；游動時 UpdateEelUndulation 讓牠擺尾
        GameObject standinObj = new GameObject("[Standin_ShadowSilhouette]");
        standinObj.transform.SetParent(transform, false);

        _eelLength = Mathf.Max(2f, standinScale.x);
        float maxHalfHeight = Mathf.Max(0.35f, standinScale.y * 0.45f);

        int segs = 36;
        Vector3[] verts = new Vector3[(segs + 1) * 2];
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;                                           // 0 = 頭, 1 = 尾
            float z = Mathf.Lerp(_eelLength * 0.5f, -_eelLength * 0.5f, t);      // 頭在前 (+Z＝游動方向)

            // 身體厚度：頭圓、身體飽滿、尾巴收尖成絲
            float h;
            if (t < 0.12f) h = maxHalfHeight * Mathf.Sin((t / 0.12f) * Mathf.PI * 0.5f);
            else if (t < 0.40f) h = maxHalfHeight;
            else h = maxHalfHeight * Mathf.Pow(1f - (t - 0.40f) / 0.60f, 1.25f);
            h = Mathf.Max(h, _eelLength * 0.004f);

            // 背鰭帶：從前段一路延伸到尾巴的薄鰭（鰻魚的招牌輪廓）
            float fin = maxHalfHeight * 0.35f * Mathf.Sin(Mathf.Clamp01((t - 0.15f) / 0.80f) * Mathf.PI);

            verts[i * 2]     = new Vector3(0f, h + fin, z);   // 背側（含鰭）
            verts[i * 2 + 1] = new Vector3(0f, -h, z);        // 腹側
        }

        int[] tris = new int[segs * 6 * 2]; // 正反兩面都建，左右轉向時都看得到
        int ti = 0;
        for (int i = 0; i < segs; i++)
        {
            int a = i * 2, b = i * 2 + 1, c2 = (i + 1) * 2, d = (i + 1) * 2 + 1;
            tris[ti++] = a; tris[ti++] = c2; tris[ti++] = b;
            tris[ti++] = b; tris[ti++] = c2; tris[ti++] = d;
            tris[ti++] = a; tris[ti++] = b; tris[ti++] = c2;   // 反面
            tris[ti++] = b; tris[ti++] = d; tris[ti++] = c2;
        }

        Mesh mesh = new Mesh();
        mesh.name = "EelSilhouette";
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.MarkDynamic();   // UpdateEelUndulation 每幀改頂點，提示驅動用動態頂點緩衝區
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(4f, maxHalfHeight * 8f + 4f, _eelLength + 4f)); // 撐大避免擺動時被視錐剔除

        _eelMeshFilter = standinObj.AddComponent<MeshFilter>();
        _eelMeshFilter.sharedMesh = mesh;
        _eelBaseVerts = verts;
        _eelWorkVerts = new Vector3[verts.Length];

        MeshRenderer mr = standinObj.AddComponent<MeshRenderer>();
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh != null)
        {
            Material shadowMat = new Material(sh);
            shadowMat.color = new Color(0.04f, 0.08f, 0.16f, 0.85f);
            mr.material = shadowMat;
        }
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>鰻魚游動擺尾：沿身體跑一道行進波，越靠尾巴擺幅越大。</summary>
    private void UpdateEelUndulation()
    {
        if (_eelMeshFilter == null || _eelBaseVerts == null) return;

        _eelPhase += Time.deltaTime * (2.2f + swimSpeed * 0.35f);
        float amp = Mathf.Max(0.15f, standinScale.y * 0.28f);

        for (int i = 0; i < _eelBaseVerts.Length; i++)
        {
            Vector3 v = _eelBaseVerts[i];
            float t = 0.5f - v.z / _eelLength;   // 頭 0 → 尾 1
            float wave = Mathf.Sin(t * Mathf.PI * 2f * 1.6f - _eelPhase);
            _eelWorkVerts[i] = new Vector3(v.x, v.y + wave * amp * Mathf.Pow(Mathf.Clamp01(t), 1.2f), v.z);
        }

        Mesh m = _eelMeshFilter.sharedMesh;
        m.vertices = _eelWorkVerts;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scene 編輯器可視化 Gizmos (支援主角感應圈即時顯示)
    // ──────────────────────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        EnsurePlayerReference();

        // 1. 繪製生物自身標籤
        Gizmos.color = new Color(0.1f, 0.6f, 1f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, 1.8f);

        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f, $"🐋 深海巨影 (嚴格視野鎖定: {(strictProximityLock ? "開啟" : "關閉")})");
        #endif

        Vector2 playerPos2D = _playerTransform != null 
            ? new Vector2(_playerTransform.position.x, _playerTransform.position.y) 
            : new Vector2(transform.position.x, transform.position.y);

        // 2. ★ 繪製主角視野感應圈 (金黃色圓形)
        if (_playerTransform != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
            DrawWireCircle2D(_playerTransform.position, playerProximityRadius, fixedZ);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(_playerTransform.position + Vector3.up * (playerProximityRadius + 1.2f), $"🎯 主角視野感應圈 (半徑: {playerProximityRadius}m)");
            #endif
        }

        if (waypoints == null || waypoints.Length == 0) return;

        // 3. 繪製所有路徑點 (圈內顯示鮮明綠光，圈外顯示暗藍色)
        for (int i = 0; i < waypoints.Length; i++)
        {
            Transform wp = waypoints[i];
            if (wp == null) continue;

            Vector3 wpPos = wp.position;
            wpPos.z = fixedZ;

            float dist = Vector2.Distance(new Vector2(wp.position.x, wp.position.y), playerPos2D);
            bool isInside = dist <= playerProximityRadius;

            if (i == _currentWaypointIndex)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.1f, 1f); // 當前目標點：橘紅色
                Gizmos.DrawSphere(wpPos, 0.9f);
            }
            else if (isInside)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f); // 圈內點：鮮綠色
                Gizmos.DrawSphere(wpPos, 0.7f);
            }
            else
            {
                Gizmos.color = new Color(0.3f, 0.45f, 0.7f, 0.45f); // 圈外遠處點：暗淡藍
                Gizmos.DrawSphere(wpPos, 0.5f);
            }

            #if UNITY_EDITOR
            string tagText = (i == _currentWaypointIndex) ? "【當前目標】" : (isInside ? "【視野範圍內】" : "【圈外遠處】");
            UnityEditor.Handles.Label(wpPos + Vector3.up * 0.9f, $"📍 WP {i} {tagText}");
            #endif

            // 連線軌跡
            if (patrolMode == PatrolMode.Loop)
            {
                Transform nextWp = waypoints[(i + 1) % waypoints.Length];
                if (nextWp != null)
                {
                    Vector3 nextPos = nextWp.position;
                    nextPos.z = fixedZ;
                    Gizmos.color = new Color(0.1f, 0.9f, 0.9f, 0.35f);
                    Gizmos.DrawLine(wpPos, nextPos);
                }
            }
            else if (i < waypoints.Length - 1)
            {
                Transform nextWp = waypoints[i + 1];
                if (nextWp != null)
                {
                    Vector3 nextPos = nextWp.position;
                    nextPos.z = fixedZ;
                    Gizmos.color = new Color(0.1f, 0.9f, 0.9f, 0.35f);
                    Gizmos.DrawLine(wpPos, nextPos);
                }
            }
        }
    }

    private void DrawWireCircle2D(Vector3 center, float radius, float z)
    {
        int segments = 48;
        float angleStep = 360f / segments;
        Vector3 prevPoint = center + new Vector3(Mathf.Cos(0) * radius, Mathf.Sin(0) * radius, 0f);
        prevPoint.z = z;

        for (int i = 1; i <= segments; i++)
        {
            float rad = Mathf.Deg2Rad * (i * angleStep);
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0f);
            nextPoint.z = z;
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
}
