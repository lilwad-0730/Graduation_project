using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1017 M：玻璃館黑影「要有猙獰的，無法注視的角落裡有著令人窒息的恐怖怪物的感覺」
///
/// 紅線照守：沒有眼睛、沒有牙齒、沒有嘴；不出聲；不被消滅。恐怖感從下面四件事來（各有開關）：
///   1. 臉被吞掉：模型原本的頭（人的側臉：額頭、鼻子、嘴唇、下巴）整顆縮成一點不畫，
///      脖子上只剩一大團不停蠕動的黑線團（跟她頭上的毛線團是同一種東西，只是大、亂、會甩出觸鬚），
///      也就看出它是「她的影子」。線團畫在黑影的紅色光暈之後，是純黑的，不會被染成暗紅。
///      模型裡的眼睛、牙齒網格一律不畫（攻擊動作張嘴也不會露出來）。
///      （第一版只把線團疊在臉上：頭的網格在線團前面，側臉輪廓還是透出來，所以改成把頭縮掉。）
///   2. 無法注視：身體邊緣一直剝落黑色碎屑（舞台腳本「邊緣碎成粒子」）；每隔一下抽搐一次
///      （頭猛地歪掉又彈回），抽搐時留下一個暗紅的殘影，眼睛對不準它。
///   3. 角落裡：它在她身後時，畫面靠它那一側先暗下來。
///   4. 窒息：越靠近越暗、四邊越往內收，畫面起顆粒，暗角像心跳一樣一縮一縮。
///
/// 不改 ShadowMonsterController、不改場景、不改模型檔：場景載入時自己掛到黑影上；
/// 加出來的東西放在另一個物件（不在黑影底下），黑影原本的顯示／淡出規則照舊（它看不見，這些也跟著看不見）。
/// 開關：ShadowMonsterDread.Enabled（全部）＋ Inspector 上每一項。
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class ShadowMonsterDread : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("開關")]
    public bool faceVoid = true;
    [Tooltip("把模型原本的頭（人的側臉：鼻子、嘴唇、下巴）縮成一點不畫，只留黑線團。關掉＝線團疊在臉上，側臉輪廓會透出來")]
    public bool collapseHead = true;
    public bool hideEyesAndTeeth = true;
    public bool shedding = true;
    public bool twitching = true;
    public bool afterimages = true;
    public bool dreadOverlay = true;

    [Header("1　臉被吞掉（黑線團）")]
    [Tooltip("線團半徑＝頭高的幾倍（頭已經縮掉，不用再大到蓋住鼻子、下巴）")]
    public float massRadiusOfHead = 0.82f;
    [Tooltip("線團中心往臉的方向推多少（頭高的幾倍）")]
    public float forwardShift = 0.12f;
    public int strandCount = 46;
    public int strandPoints = 18;
    public int tendrilCount = 8;
    public int tendrilPoints = 20;
    public Color inkColor = new Color(0.012f, 0.010f, 0.018f, 1f);

    [Header("2　抽搐與殘影")]
    [Tooltip("遠的時候幾秒抽一次（最短、最長）")]
    public Vector2 twitchIntervalFar = new Vector2(1.6f, 3.4f);
    [Tooltip("靠近的時候幾秒抽一次（最短、最長）")]
    public Vector2 twitchIntervalNear = new Vector2(0.45f, 1.1f);
    public Vector2 twitchDuration = new Vector2(0.07f, 0.15f);
    [Tooltip("抽搐時頭歪的角度（度）")]
    public float twitchAngle = 32f;
    public Color afterimageColor = new Color(0.32f, 0.02f, 0.05f, 0.42f);
    public float afterimageLife = 0.5f;

    [Header("3、4　角落與窒息（畫面）")]
    [Tooltip("它離她多近（米，算到它身體的邊）時最壓迫")]
    public float nearDistance = 2f;
    [Tooltip("它離她多遠以外完全不影響畫面")]
    public float farDistance = 30f;
    [Range(0f, 1f)] public float maxDarkness = 0.75f;
    [Range(0f, 0.5f)] public float maxGrain = 0.07f;

    // ── 黑影 ──
    private ShadowMonsterController _ctrl;
    private Transform _head, _headTop, _neck;
    private SkinnedMeshRenderer _body;
    private float _headH = 1f, _bodyH = 10f;
    private Vector3 _topDirLocal = Vector3.up;   // 頭頂方向（頭骨自己的座標）：頭縮掉以後頭頂骨的位置會跟著縮到一點，改用方向算
    private float _refAlpha = 1f;                // 黑影材質原本的透明度：完全現身時線團要全黑，不是半透明
    private readonly List<Renderer> _hidden = new List<Renderer>();
    private readonly List<Transform> _collapseBones = new List<Transform>();
    private readonly List<Vector3> _collapseOrig = new List<Vector3>();
    private bool _collapsedNow;
    private const float CollapsedScale = 0.001f;
    private MaterialPropertyBlock _mpb;
    private static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ID_Color = Shader.PropertyToID("_Color");

    // ── 線團 ──
    private GameObject _root;
    private Material _inkMat;
    private Mesh _coreMesh;
    private Vector3[] _coreUnit, _coreVerts;
    private MeshRenderer _coreMR;
    private LineRenderer[] _strands;
    private Strand[] _strandDefs;
    private Vector3[] _strandBuf;
    private LineRenderer[] _tendrils;
    private Tendril[] _tendrilDefs;
    private Vector3[] _tendrilBuf;
    private ParticleSystem _shed, _smoke;
    private Vector3 _faceDir = Vector3.right;
    private Vector3 _center;
    private float _radius = 1f;

    private struct Strand { public Vector3 axis, u, v; public float rf, arc, phase, speed, seed, wob; }
    private struct Tendril { public Vector3 dir, p1, p2; public float baseLen, lash, period, phase, seed; }

    // ── 抽搐、殘影 ──
    private float _nextTwitch = 2f, _twitchLeft;
    private Quaternion _twitchRot = Quaternion.identity;
    private bool _twApplied;
    private Quaternion _twBaseHead, _twSetHead, _twBaseNeck, _twSetNeck;
    private class Ghost { public GameObject go; public Mesh mesh; public MeshRenderer mr; public Material mat; public float life; public Vector3 drift; }
    private readonly List<Ghost> _ghosts = new List<Ghost>();
    private int _bakeMode;   // 0＝還沒試、1＝BakeMesh(縮放)＋localToWorld、2＝BakeMesh(不縮放)＋TRS(不含縮放)、-1＝都不對

    // ── 畫面 ──
    private Canvas _ov;
    private RawImage _vig, _grain;
    private float _dread, _beatPhase;
    private PlayerMovement _pm;
    private float _nextPmLookup;
    private bool _inited;

    // ══════════════════════════════════════════
    // 自己掛到黑影上
    // ══════════════════════════════════════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedStatic;
        SceneManager.sceneLoaded += OnSceneLoadedStatic;
        AttachAll();
    }

    private static void OnSceneLoadedStatic(Scene s, LoadSceneMode m) { AttachAll(); }

    private static void AttachAll()
    {
        ShadowMonsterController[] all = Object.FindObjectsByType<ShadowMonsterController>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].GetComponent<ShadowMonsterDread>() == null)
            {
                all[i].gameObject.AddComponent<ShadowMonsterDread>();
                Debug.Log("【黑影】★1017 已掛上：臉被黑線團吞掉、碎屑、抽搐殘影、靠近時畫面窒息（ShadowMonsterDread）");
            }
        }
    }

    private void Start()
    {
        _ctrl = GetComponent<ShadowMonsterController>();
        _mpb = new MaterialPropertyBlock();
        FindParts();
        HideEyesAndTeethMeshes();
        _inited = true;
    }

    private void OnDisable()
    {
        if (_root != null) _root.SetActive(false);
        if (_ov != null) _ov.enabled = false;
        RestoreHidden();
        if (_collapsedNow) ApplyCollapse(false);
    }

    private void OnEnable()
    {
        if (_inited) HideEyesAndTeethMeshes();
    }

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root);
        if (_ov != null) Destroy(_ov.gameObject);
        for (int i = 0; i < _ghosts.Count; i++) if (_ghosts[i].go != null) Destroy(_ghosts[i].go);
        RestoreHidden();
        if (_collapsedNow) ApplyCollapse(false);
    }

    // ══════════════════════════════════════════
    // 找頭、身體、眼睛牙齒
    // ══════════════════════════════════════════
    private void FindParts()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        Transform best = null;
        for (int i = 0; i < all.Length; i++)
        {
            string n = all[i].name;
            if (n == "mixamorig:Head") { best = all[i]; break; }
        }
        if (best == null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (!n.EndsWith("Head")) continue;
                if (n.Contains("Ctrl") || n.Contains("Effector") || n.Contains("Top")) continue;
                best = all[i];
                break;
            }
        }
        _head = best;
        if (_head != null)
        {
            _neck = _head.parent;
            for (int i = 0; i < _head.childCount; i++)
            {
                Transform c = _head.GetChild(i);
                if (c.name.Contains("HeadTop") || c.name.EndsWith("_End") || c.name.EndsWith("_end")) { _headTop = c; break; }
            }
        }

        SkinnedMeshRenderer[] smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        float bestVol = -1f;
        for (int i = 0; i < smrs.Length; i++)
        {
            SkinnedMeshRenderer s = smrs[i];
            if (s == null || s.sharedMesh == null) continue;
            string n = s.name.ToLowerInvariant();
            if (n.Contains("eye") || n.Contains("teeth") || n.Contains("tooth") || n.Contains("nail")) continue;
            if (_head != null && !UsesBone(s, _head)) continue;
            Vector3 sz = s.bounds.size;
            float vol = sz.x * sz.y * Mathf.Max(0.01f, sz.z);
            if (vol > bestVol) { bestVol = vol; _body = s; }
        }
        if (_body == null)
        {
            for (int i = 0; i < smrs.Length; i++)
                if (smrs[i] != null && smrs[i].sharedMesh != null && !smrs[i].name.ToLowerInvariant().Contains("eye")) { _body = smrs[i]; break; }
        }

        if (_body != null) _bodyH = Mathf.Max(0.5f, _body.bounds.size.y);
        if (_head != null && _headTop != null) _headH = Vector3.Distance(_head.position, _headTop.position);
        else _headH = _bodyH * 0.11f;
        _headH = Mathf.Clamp(_headH, _bodyH * 0.05f, _bodyH * 0.25f);

        if (_head != null)
        {
            Vector3 up = _headTop != null ? _headTop.position - _head.position : Vector3.up;
            if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
            _topDirLocal = Quaternion.Inverse(_head.rotation) * up.normalized;
        }

        _refAlpha = 1f;
        if (_body != null && _body.sharedMaterial != null)
        {
            Material m = _body.sharedMaterial;
            if (m.HasProperty("_BaseColor")) _refAlpha = m.GetColor("_BaseColor").a;
            else if (m.HasProperty("_Color")) _refAlpha = m.GetColor("_Color").a;
        }
        _refAlpha = Mathf.Clamp(_refAlpha, 0.05f, 1f);

        // 要縮掉的頭：這隻黑影底下所有叫 mixamorig:Head 的骨頭（多出來的紅色手臂那份如果有自己的骨架，也一起）
        _collapseBones.Clear();
        _collapseOrig.Clear();
        for (int i = 0; i < all.Length; i++) if (all[i].name == "mixamorig:Head") AddCollapseBone(all[i]);
        if (_collapseBones.Count == 0 && _head != null) AddCollapseBone(_head);

        Debug.Log($"【黑影】頭骨：{(_head != null ? _head.name : "沒找到")}，頭頂：{(_headTop != null ? _headTop.name : "沒找到")}，身體：{(_body != null ? _body.name : "沒找到")}，身高約 {_bodyH:F1}、頭高約 {_headH:F2}，材質透明度 {_refAlpha:F2}，要縮掉的頭 {_collapseBones.Count} 個");
    }

    private void AddCollapseBone(Transform t)
    {
        if (t == null || _collapseBones.Contains(t)) return;
        if (t.GetComponentInChildren<Collider>(true) != null)
        {
            Debug.LogWarning("【黑影】" + t.name + " 底下有碰撞體，這顆頭不縮（避免動到玩法判定）");
            return;
        }
        _collapseBones.Add(t);
        _collapseOrig.Add(t.localScale);
    }

    /// <summary>
    /// 頭縮成一點：網格上綁在頭骨的部分（整張側臉、眼睛、牙齒）跟著縮掉，脖子留著接線團。
    /// 在動畫之後（LateUpdate）每幀設一次；動畫有沒有寫頭骨的縮放都不影響。關掉時放回原本的大小。
    /// </summary>
    private void ApplyCollapse(bool on)
    {
        for (int i = 0; i < _collapseBones.Count; i++)
        {
            Transform t = _collapseBones[i];
            if (t == null) continue;
            t.localScale = on ? _collapseOrig[i] * CollapsedScale : _collapseOrig[i];
        }
        _collapsedNow = on;
    }

    private static bool UsesBone(SkinnedMeshRenderer s, Transform bone)
    {
        Transform[] b = s.bones;
        if (b == null) return false;
        for (int i = 0; i < b.Length; i++) if (b[i] == bone) return true;
        return false;
    }

    /// <summary>紅線：眼睛、牙齒的網格不畫。用 forceRenderingOff，黑影自己開關 enabled 也蓋不掉。</summary>
    private void HideEyesAndTeethMeshes()
    {
        if (!hideEyesAndTeeth || !Enabled) return;
        Renderer[] rs = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null || r is ParticleSystemRenderer) continue;
            string n = r.name.ToLowerInvariant();
            if (n.Contains("eye") || n.Contains("teeth") || n.Contains("tooth"))
            {
                if (!r.forceRenderingOff) { r.forceRenderingOff = true; _hidden.Add(r); }
            }
        }
        if (_hidden.Count > 0) Debug.Log("【黑影】紅線：眼睛／牙齒網格不畫 " + _hidden.Count + " 個");
    }

    private void RestoreHidden()
    {
        for (int i = 0; i < _hidden.Count; i++) if (_hidden[i] != null) _hidden[i].forceRenderingOff = false;
        _hidden.Clear();
    }

    // ══════════════════════════════════════════
    // 每幀（在動畫之後）
    // ══════════════════════════════════════════
    private void LateUpdate()
    {
        if (!_inited) return;
        if (!Enabled)
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            if (_ov != null) _ov.enabled = false;
            if (_hidden.Count > 0) RestoreHidden();
            if (_collapsedNow) ApplyCollapse(false);
            return;
        }
        if (_hidden.Count == 0 && hideEyesAndTeeth) HideEyesAndTeethMeshes();

        float dt = Time.deltaTime;
        UndoTwitchIfNotOverwritten();
        if (faceVoid && collapseHead && _head != null) ApplyCollapse(true);
        else if (_collapsedNow) ApplyCollapse(false);
        bool visible = BodyVisible();
        // 材質本身是半透明的話，完全現身時 BodyAlpha 也不到 1；線團要以「完全現身＝全黑」為準
        float alpha = visible ? Mathf.Clamp01(BodyAlpha() / _refAlpha) : 0f;

        if (_pm == null && Time.unscaledTime >= _nextPmLookup)
        {
            _pm = Object.FindAnyObjectByType<PlayerMovement>();
            _nextPmLookup = Time.unscaledTime + 1f;
        }

        float dread = ComputeDread(visible);
        _dread = Mathf.MoveTowards(_dread, dread, dt * 0.9f);

        if (twitching && visible && _head != null) UpdateTwitch(dt, _dread);

        if (_head != null && _body != null && (faceVoid || shedding))
        {
            EnsureVisual();
            bool showRoot = visible && alpha > 0.01f;
            if (_root.activeSelf != showRoot) _root.SetActive(showRoot);
            if (showRoot) UpdateVisual(alpha);
        }

        UpdateGhosts(dt);
        if (dreadOverlay) UpdateOverlay(dt);
        else if (_ov != null) _ov.enabled = false;
    }

    private bool BodyVisible()
    {
        if (_body == null) return false;
        if (!_body.enabled || _body.forceRenderingOff || !_body.gameObject.activeInHierarchy) return false;
        if (_ctrl != null && _ctrl.currentState == ShadowMonsterController.MonsterState.Dormant) return false;
        return true;
    }

    private float BodyAlpha()
    {
        _body.GetPropertyBlock(_mpb);
        if (_mpb.HasColor(ID_BaseColor)) return Mathf.Clamp01(_mpb.GetColor(ID_BaseColor).a);
        if (_mpb.HasColor(ID_Color)) return Mathf.Clamp01(_mpb.GetColor(ID_Color).a);
        return 1f;
    }

    private float ComputeDread(bool visible)
    {
        if (!visible || _pm == null || _body == null) return 0f;
        if (_ctrl != null)
        {
            var st = _ctrl.currentState;
            if (st == ShadowMonsterController.MonsterState.Vanishing || st == ShadowMonsterController.MonsterState.Dormant) return 0f;
        }
        Bounds b = _body.bounds;
        float dx = Mathf.Max(0f, Mathf.Abs(_pm.transform.position.x - b.center.x) - b.extents.x);
        float t = Mathf.InverseLerp(farDistance, nearDistance, dx);
        return t * t * (3f - 2f * t);
    }

    // ── 抽搐 ──────────────────────────────────
    /// <summary>
    /// 頭猛地歪一下。動畫每幀會重寫骨頭；如果這支動畫沒有動到頭骨（沒有重寫），
    /// 上一幀加上去的角度要先拿掉，不然會越轉越歪、抽完回不來。
    /// </summary>
    private void UndoTwitchIfNotOverwritten()
    {
        if (!_twApplied || _head == null) return;
        _twApplied = false;
        if (_head.localRotation == _twSetHead) _head.localRotation = _twBaseHead;
        if (_neck != null && _neck.localRotation == _twSetNeck) _neck.localRotation = _twBaseNeck;
    }

    private void UpdateTwitch(float dt, float dread)
    {
        if (_twitchLeft > 0f)
        {
            _twitchLeft -= dt;
            _twBaseHead = _head.localRotation;
            _head.localRotation = _twBaseHead * _twitchRot;
            _twSetHead = _head.localRotation;
            if (_neck != null)
            {
                _twBaseNeck = _neck.localRotation;
                _neck.localRotation = _twBaseNeck * Quaternion.Slerp(Quaternion.identity, _twitchRot, 0.35f);
                _twSetNeck = _neck.localRotation;
            }
            _twApplied = true;
            return;
        }
        _nextTwitch -= dt;
        if (_nextTwitch > 0f) return;

        Vector2 iv = Vector2.Lerp(twitchIntervalFar, twitchIntervalNear, dread);
        _nextTwitch = Random.Range(iv.x, iv.y);
        _twitchLeft = Random.Range(twitchDuration.x, twitchDuration.y);
        float a = twitchAngle * Random.Range(0.55f, 1f);
        _twitchRot = Quaternion.Euler(Random.Range(-a, a), Random.Range(-a * 0.7f, a * 0.7f), Random.Range(-a, a));
        if (afterimages) SpawnGhost();
    }

    // ══════════════════════════════════════════
    // 線團、觸鬚、碎屑
    // ══════════════════════════════════════════
    private void EnsureVisual()
    {
        if (_root != null) return;
        _root = new GameObject("ShadowDread_黑線團（自動生成）");
        Shader sh = Shader.Find("Sprites/Default");
        _inkMat = new Material(sh);
        _inkMat.color = Color.white;

        int sortLayer = _body.sortingLayerID;
        // 畫在黑影的紅色光暈（ShadowMonsterController：貼圖 999、粒子 1000）之後：線團是純黑的，不會被染成暗紅
        int baseOrder = Mathf.Max(_body.sortingOrder, 1000);

        _radius = Mathf.Max(0.05f, _headH * massRadiusOfHead);

        // 核心：一顆會沸騰的黑色團塊
        if (faceVoid)
        {
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "核心";
            Collider col = core.GetComponent<Collider>();
            if (col != null) Destroy(col);
            core.transform.SetParent(_root.transform, false);
            MeshFilter mf = core.GetComponent<MeshFilter>();
            _coreMesh = Instantiate(mf.sharedMesh);
            _coreMesh.MarkDynamic();
            mf.sharedMesh = _coreMesh;
            _coreUnit = _coreMesh.vertices;
            for (int i = 0; i < _coreUnit.Length; i++) _coreUnit[i] = _coreUnit[i].normalized;
            _coreVerts = new Vector3[_coreUnit.Length];
            Color[] cols = new Color[_coreUnit.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = inkColor;
            _coreMesh.colors = cols;
            _coreMR = core.GetComponent<MeshRenderer>();
            _coreMR.sharedMaterial = _inkMat;
            _coreMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _coreMR.receiveShadows = false;
            _coreMR.sortingLayerID = sortLayer;
            _coreMR.sortingOrder = baseOrder + 1;

            // 纏繞的線
            _strands = new LineRenderer[strandCount];
            _strandDefs = new Strand[strandCount];
            _strandBuf = new Vector3[Mathf.Max(4, strandPoints)];
            for (int i = 0; i < strandCount; i++)
            {
                _strands[i] = MakeLine("線" + i, _root.transform, Mathf.Max(4, strandPoints), sortLayer, baseOrder + 2);
                Strand s = new Strand();
                s.axis = Random.onUnitSphere;
                s.u = Vector3.Cross(s.axis, Mathf.Abs(s.axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                s.v = Vector3.Cross(s.axis, s.u).normalized;
                s.rf = Random.Range(0.8f, 1.2f);
                s.arc = Random.Range(1.1f, 2.4f) * Mathf.PI;
                s.phase = Random.Range(0f, Mathf.PI * 2f);
                s.speed = Random.Range(-1.8f, 1.8f);
                s.seed = Random.Range(0f, 100f);
                s.wob = Random.Range(1.5f, 3.5f);
                _strandDefs[i] = s;
                float w = _radius * Random.Range(0.035f, 0.075f);
                _strands[i].widthMultiplier = w;
                _strands[i].widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.25f));
            }

            // 甩出來的觸鬚
            _tendrils = new LineRenderer[tendrilCount];
            _tendrilDefs = new Tendril[tendrilCount];
            _tendrilBuf = new Vector3[Mathf.Max(4, tendrilPoints)];
            for (int i = 0; i < tendrilCount; i++)
            {
                _tendrils[i] = MakeLine("觸鬚" + i, _root.transform, Mathf.Max(4, tendrilPoints), sortLayer, baseOrder + 2);
                _tendrils[i].widthMultiplier = _radius * Random.Range(0.06f, 0.1f);
                _tendrils[i].widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.45f), new Keyframe(1f, 0.02f));
                Tendril t = new Tendril();
                t.dir = Random.onUnitSphere;
                t.p1 = Vector3.Cross(t.dir, Vector3.forward).normalized;
                if (t.p1.sqrMagnitude < 0.01f) t.p1 = Vector3.up;
                t.p2 = Vector3.Cross(t.dir, t.p1).normalized;
                t.baseLen = _radius * Random.Range(0.35f, 0.9f);
                t.lash = _radius * Random.Range(0.9f, 2.0f);
                t.period = Random.Range(1.3f, 3.2f);
                t.phase = Random.Range(0f, 10f);
                t.seed = Random.Range(0f, 100f);
                _tendrilDefs[i] = t;
            }

            // 線團冒出的黑煙
            _smoke = MakeParticles("黑煙", _root.transform, sortLayer, baseOrder + 3, MakeSoftDot(64), false);
            var m = _smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(_radius * 0.15f, _radius * 0.45f);
            m.startSize = new ParticleSystem.MinMaxCurve(_radius * 0.12f, _radius * 0.32f);
            m.startColor = new Color(inkColor.r, inkColor.g, inkColor.b, 0.55f);
            var e = _smoke.emission; e.rateOverTime = 16f;
            var sh2 = _smoke.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.Sphere; sh2.radius = _radius * 0.8f;
            var sol = _smoke.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));
            _smoke.Play();
        }

        // 身體剝落的黑色碎屑
        if (shedding)
        {
            _shed = MakeParticles("碎屑", _root.transform, sortLayer, baseOrder + 3, MakeFlake(32), true);
            var m = _shed.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.9f);
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(_bodyH * 0.004f, _bodyH * 0.012f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(inkColor.r, inkColor.g, inkColor.b, 0.9f);
            var e = _shed.emission; e.rateOverTime = 70f;
            var sh2 = _shed.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer; sh2.skinnedMeshRenderer = _body;
            var rot = _shed.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            _shed.Play();
        }
    }

    private LineRenderer MakeLine(string name, Transform parent, int points, int layer, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = points;
        lr.sharedMaterial = _inkMat;
        lr.startColor = inkColor;
        lr.endColor = inkColor;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 1;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingLayerID = layer;
        lr.sortingOrder = order;
        return lr;
    }

    private ParticleSystem MakeParticles(string name, Transform parent, int layer, int order, Texture2D tex, bool fadeShrink)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = 800;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        if (fadeShrink)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));
        }
        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        Material m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        pr.sharedMaterial = m;
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sortingLayerID = layer;
        pr.sortingOrder = order;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        return ps;
    }

    private void UpdateVisual(float alpha)
    {
        float t = Time.time;
        // 頭頂：用頭骨的方向＋頭高算（頭縮掉以後，頭頂骨的位置也縮到頭骨上，不能直接用）
        Vector3 top = _head.position + (_head.rotation * _topDirLocal) * _headH;
        Vector3 mid = (_head.position + top) * 0.5f;
        if (_pm != null)
        {
            float sx = Mathf.Sign(_pm.transform.position.x - mid.x);
            if (Mathf.Abs(_pm.transform.position.x - mid.x) > 0.5f) _faceDir = new Vector3(sx, 0f, 0f);
        }
        _center = mid + _faceDir * (forwardShift * _headH) + Vector3.up * (0.04f * _headH);
        _radius = Mathf.Max(0.05f, _headH * massRadiusOfHead);

        if (faceVoid && _coreMesh != null)
        {
            _root.transform.position = Vector3.zero;
            Transform core = _coreMR.transform;
            core.position = _center;
            core.rotation = Quaternion.identity;
            core.localScale = Vector3.one;
            float R = _radius * 0.86f;
            for (int i = 0; i < _coreUnit.Length; i++)
            {
                Vector3 n = _coreUnit[i];
                float noise = Mathf.PerlinNoise(n.x * 1.7f + t * 0.9f + 11f, n.y * 1.7f + n.z * 1.3f - t * 0.7f);
                float noise2 = Mathf.PerlinNoise(n.z * 3.1f - t * 1.6f + 3f, n.x * 2.3f + t * 1.1f);
                _coreVerts[i] = n * (R * (0.9f + 0.22f * noise + 0.08f * noise2));
            }
            _coreMesh.vertices = _coreVerts;
            _coreMesh.RecalculateBounds();
            _inkMat.color = new Color(1f, 1f, 1f, alpha);

            // 纏繞的線
            int P = _strandBuf.Length;
            for (int i = 0; i < _strands.Length; i++)
            {
                Strand s = _strandDefs[i];
                float ph = s.phase + t * s.speed;
                for (int j = 0; j < P; j++)
                {
                    float f = j / (float)(P - 1);
                    float th = ph + s.arc * f;
                    Vector3 dir = s.u * Mathf.Cos(th) + s.v * Mathf.Sin(th) + s.axis * (0.35f * Mathf.Sin(th * 2f + t * s.wob));
                    float rad = _radius * s.rf * (0.92f + 0.3f * (Mathf.PerlinNoise(s.seed + j * 0.31f, t * 1.4f) - 0.5f));
                    _strandBuf[j] = _center + dir.normalized * rad;
                }
                _strands[i].SetPositions(_strandBuf);
            }

            // 觸鬚：偏向臉的方向，一下一下往外甩
            int Q = _tendrilBuf.Length;
            for (int i = 0; i < _tendrils.Length; i++)
            {
                Tendril d = _tendrilDefs[i];
                Vector3 dir = (d.dir + _faceDir * 0.9f + Vector3.down * 0.25f).normalized;
                float p = Mathf.Repeat((t + d.phase) / d.period, 1f);
                float lash = p < 0.1f ? Mathf.SmoothStep(0f, 1f, p / 0.1f) : Mathf.Exp(-(p - 0.1f) * 6f);
                float len = d.baseLen + d.lash * lash * (0.6f + 0.6f * _dread);
                for (int j = 0; j < Q; j++)
                {
                    float f = j / (float)(Q - 1);
                    Vector3 along = dir * (_radius * 0.8f + f * len);
                    Vector3 wig = (d.p1 * Mathf.Sin(f * Mathf.PI * 2.2f + t * 5f + d.seed) +
                                   d.p2 * Mathf.Cos(f * Mathf.PI * 1.7f + t * 4.1f + d.seed)) * (_radius * 0.22f * f);
                    _tendrilBuf[j] = _center + along + wig;
                }
                _tendrils[i].SetPositions(_tendrilBuf);
            }

            if (_smoke != null)
            {
                _smoke.transform.position = _center;
                var m = _smoke.main;
                m.startColor = new Color(inkColor.r, inkColor.g, inkColor.b, 0.55f * alpha);
            }
        }

        if (_shed != null)
        {
            var m = _shed.main;
            m.startColor = new Color(inkColor.r, inkColor.g, inkColor.b, 0.9f * alpha);
            var vol = _shed.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            float back = -_faceDir.x * _bodyH * 0.03f;
            vol.x = new ParticleSystem.MinMaxCurve(back * 0.4f, back * 1.2f);
            vol.y = new ParticleSystem.MinMaxCurve(_bodyH * 0.004f, _bodyH * 0.02f);
            vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }
    }

    // ══════════════════════════════════════════
    // 殘影
    // ══════════════════════════════════════════
    private void SpawnGhost()
    {
        if (_body == null || _bakeMode < 0 || !BodyVisible()) return;
        if (_bakeMode == 0) _bakeMode = DetectBakeMode();
        if (_bakeMode < 0) return;

        Ghost g = null;
        for (int i = 0; i < _ghosts.Count; i++) if (_ghosts[i].life <= 0f) { g = _ghosts[i]; break; }
        if (g == null)
        {
            if (_ghosts.Count >= 3) g = _ghosts[0];
            else
            {
                g = new Ghost();
                g.go = new GameObject("黑影殘影（自動生成）");
                g.mesh = new Mesh();
                g.mesh.MarkDynamic();
                g.go.AddComponent<MeshFilter>().sharedMesh = g.mesh;
                g.mr = g.go.AddComponent<MeshRenderer>();
                g.mat = new Material(Shader.Find("Sprites/Default"));
                g.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                g.mr.receiveShadows = false;
                g.mr.sortingLayerID = _body.sortingLayerID;
                g.mr.sortingOrder = _body.sortingOrder - 1;
                _ghosts.Add(g);
            }
        }

        Transform tr = _body.transform;
        _body.BakeMesh(g.mesh, _bakeMode == 1);
        Material[] mats = new Material[Mathf.Max(1, g.mesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = g.mat;
        g.mr.sharedMaterials = mats;
        if (_bakeMode == 1)
        {
            g.go.transform.SetPositionAndRotation(tr.position, tr.rotation);
            g.go.transform.localScale = tr.lossyScale;
        }
        else
        {
            g.go.transform.SetPositionAndRotation(tr.position, tr.rotation);
            g.go.transform.localScale = Vector3.one;
        }
        g.life = afterimageLife;
        g.drift = (-_faceDir * 0.35f + Random.insideUnitSphere * 0.2f) * _headH;
        g.go.SetActive(true);
    }

    /// <summary>烘出來的網格要怎麼擺才對得上模型：兩種都試，挑外框最接近的那種（跟 RollingRockVisual 的做法一樣）。</summary>
    private int DetectBakeMode()
    {
        Mesh m = new Mesh();
        Bounds target = _body.bounds;
        Transform tr = _body.transform;
        float best = float.MaxValue; int pick = -1;
        for (int mode = 1; mode <= 2; mode++)
        {
            _body.BakeMesh(m, mode == 1);
            Matrix4x4 mtx = mode == 1 ? tr.localToWorldMatrix : Matrix4x4.TRS(tr.position, tr.rotation, Vector3.one);
            Bounds b = TransformBounds(m.bounds, mtx);
            float err = (b.center - target.center).magnitude / Mathf.Max(0.01f, target.size.magnitude)
                      + Mathf.Abs(b.size.magnitude / Mathf.Max(0.01f, target.size.magnitude) - 1f);
            if (err < best) { best = err; pick = mode; }
        }
        Destroy(m);
        if (best > 0.6f) pick = -1;
        Debug.Log($"【黑影】殘影烘焙座標：{(pick == 1 ? "含縮放" : pick == 2 ? "不含縮放" : "對不上，殘影關掉")}（誤差 {best:F2}）");
        return pick;
    }

    private static Bounds TransformBounds(Bounds b, Matrix4x4 m)
    {
        Vector3 c = m.MultiplyPoint3x4(b.center);
        Vector3 e = b.extents;
        Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0, 0));
        Vector3 ay = m.MultiplyVector(new Vector3(0, e.y, 0));
        Vector3 az = m.MultiplyVector(new Vector3(0, 0, e.z));
        Vector3 ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                                  Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                                  Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
        return new Bounds(c, ext * 2f);
    }

    private void UpdateGhosts(float dt)
    {
        for (int i = 0; i < _ghosts.Count; i++)
        {
            Ghost g = _ghosts[i];
            if (g.go == null) continue;
            if (g.life <= 0f) { if (g.go.activeSelf) g.go.SetActive(false); continue; }
            g.life -= dt;
            float k = Mathf.Clamp01(g.life / Mathf.Max(0.01f, afterimageLife));
            g.mat.color = new Color(afterimageColor.r, afterimageColor.g, afterimageColor.b, afterimageColor.a * k * k);
            g.go.transform.position += g.drift * dt * 2f;
            if (g.life <= 0f) g.go.SetActive(false);
        }
    }

    // ══════════════════════════════════════════
    // 畫面：角落先暗、越近越窒息
    // ══════════════════════════════════════════
    private void UpdateOverlay(float dt)
    {
        if (_dread <= 0.002f)
        {
            if (_ov != null && _ov.enabled) _ov.enabled = false;
            return;
        }
        EnsureOverlay();
        _ov.enabled = true;

        // 心跳：越近越快（一下重、一下輕）
        float bpm = Mathf.Lerp(62f, 118f, _dread);
        _beatPhase = Mathf.Repeat(_beatPhase + dt * bpm / 60f, 1f);
        float b1 = Mathf.Exp(-Mathf.Pow((_beatPhase - 0.06f) * 10f, 2f));
        float b2 = 0.55f * Mathf.Exp(-Mathf.Pow((_beatPhase - 0.30f) * 10f, 2f));
        float beat = Mathf.Clamp01(b1 + b2);

        // 暗角往內收：遠＝在畫面外面，近＝收到畫面裡；靠黑影那一側更暗
        // 暗角貼圖中間透明、邊緣黑；把取樣範圍放大（uvRect 變大）黑邊就往畫面裡收，超出貼圖的地方全黑
        float side = (_pm != null && _body != null) ? Mathf.Sign(_body.bounds.center.x - _pm.transform.position.x) : -1f;
        float close = _dread * (0.85f + 0.15f * beat);
        float s = Mathf.Lerp(0.55f, 1.28f, close);
        float shift = 0.13f * -side;   // 黑影在左 → 亮的中心往右偏 → 左邊（它那一側）更暗
        _vig.uvRect = new Rect(0.5f - s * 0.5f - shift * s, 0.5f - s * 0.5f, s, s);
        _vig.color = new Color(0.025f, 0.004f, 0.012f, Mathf.Clamp01(maxDarkness * _dread * (0.88f + 0.12f * beat)));

        // 顆粒：每幀亂跳
        float gw = Screen.width / 256f * 0.6f, gh = Screen.height / 256f * 0.6f;
        _grain.uvRect = new Rect(Random.value, Random.value, gw, gh);
        _grain.color = new Color(1f, 1f, 1f, maxGrain * _dread);
    }

    private void EnsureOverlay()
    {
        if (_ov != null) return;
        GameObject go = new GameObject("ShadowDread_窒息畫面（自動生成）");
        _ov = go.AddComponent<Canvas>();
        _ov.renderMode = RenderMode.ScreenSpaceOverlay;
        _ov.sortingOrder = -100;   // 在所有介面（設定齒輪、提示、文字卡）底下

        _vig = new GameObject("暗角").AddComponent<RawImage>();
        _vig.transform.SetParent(go.transform, false);
        _vig.texture = MakeVignette(256);
        _vig.raycastTarget = false;
        RectTransform v = _vig.rectTransform;
        v.anchorMin = Vector2.zero; v.anchorMax = Vector2.one; v.offsetMin = v.offsetMax = Vector2.zero;

        _grain = new GameObject("顆粒").AddComponent<RawImage>();
        _grain.transform.SetParent(go.transform, false);
        _grain.texture = MakeGrain(256);
        _grain.raycastTarget = false;
        RectTransform g = _grain.rectTransform;
        g.anchorMin = Vector2.zero; g.anchorMax = Vector2.one; g.offsetMin = g.offsetMax = Vector2.zero;
    }

    // ══════════════════════════════════════════
    // 程式畫的貼圖
    // ══════════════════════════════════════════
    private static Texture2D MakeVignette(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx * 0.9f + dy * dy * 1.1f);
                float a = Mathf.Clamp01((d - 0.38f) / 0.62f);
                a = Mathf.Pow(a, 1.35f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }

    private static Texture2D MakeGrain(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Repeat;
        t.filterMode = FilterMode.Point;
        Color32[] px = new Color32[size * size];
        System.Random r = new System.Random(1017);
        for (int i = 0; i < px.Length; i++)
        {
            // 大多是暗的顆粒（深色場景上幾乎看不到、紅光上像粗砂），偶爾一點亮的
            double v = r.NextDouble();
            if (v < 0.06) px[i] = new Color32(220, 214, 230, 110);
            else px[i] = new Color32(6, 4, 10, (byte)r.Next(0, 210));
        }
        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }

    private static Texture2D MakeSoftDot(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }

    /// <summary>不規則的小碎片（像剝落的紙灰），不是圓點。</summary>
    private static Texture2D MakeFlake(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float ang = Mathf.Atan2(dy, dx);
                float edge = 0.62f + 0.22f * Mathf.Sin(ang * 3f + 0.7f) + 0.12f * Mathf.Sin(ang * 7f + 2.1f);
                float d = Mathf.Sqrt(dx * dx * 1.6f + dy * dy * 0.7f);
                float a = Mathf.Clamp01((edge - d) * 6f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }
}
