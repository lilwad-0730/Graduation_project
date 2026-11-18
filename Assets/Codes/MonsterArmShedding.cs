using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1118b 書頭怪吃燭火不再縮小，改成「牠多出來的四隻手臂，一根燭火掉一隻」。
///
/// 怪物的 Body 網格 = 原本兩隻手的身體 + 四隻另外長出來的手臂（各自是一座獨立的網格島，100% 綁在 mixamorig:Spine2）。
/// 掉的順序（由下到上、先 −x 再 +x）：腰 −x、腰 +x、肩 −x、肩 +x。四隻掉完＝原本兩隻手的剪影。
///
/// 做法：
///   ・開局一次：複製 Body 的 sharedMesh（絕不動原始資產），用「座標合併＋三角形相連」找出網格島，
///     選出 ≥1000 個頂點、幾乎全綁 Spine2、|中心 x|＞0.15 的島，記錄每隻手臂的三角形。
///   ・掉一隻：把那隻手臂的頂點用「目前骨架姿勢」算出世界座標（這隻手臂 100% 綁 Spine2，等於逐頂點做一次蒙皮，
///     跟 BakeMesh 的結果一樣但不用猜縮放座標），做成獨立的 MeshRenderer 物件（同材質、同 UV／法線），
///     再把那幾個三角形從活的網格複本拿掉。獨立手臂往外彈一下、受重力掉下去、翻滾，約 2.5 秒後縮小消失。
///   ・SetShedCount(n, animate)：讓「剛好前 n 隻手臂不見」。animate＝演一次掉落；否則直接換網格（重生／存檔點／測試鍵）。
/// 殘影、動態網格（ShadowMonsterDread 每次都用 Body 即時烘焙）會自動吃到掉手臂後的狀態。
/// 由 ShadowMonsterController.EnsureInitialized 自動掛上；ShadowMonsterController.shedArmsInsteadOfShrink 關掉＝照舊縮小。
/// </summary>
[DisallowMultipleComponent]
public class MonsterArmShedding : MonoBehaviour
{
    /// <summary>會掉的手臂數。</summary>
    public const int ArmCount = 4;

    [Header("找手臂")]
    [Tooltip("要動的 SkinnedMeshRenderer 名稱（留空或找不到＝頂點最多、不是眼睛／牙齒的那個）")]
    public string bodyRendererName = "Body";
    [Tooltip("多出來的手臂綁的骨頭名稱")]
    public string armBoneName = "mixamorig:Spine2";
    [Tooltip("一隻手臂（網格島）至少幾個頂點")]
    public int minArmVertices = 1000;
    [Tooltip("手臂中心離身體中線至少多遠（網格本地座標），原本的身體在中線上")]
    public float minArmCenterX = 0.15f;

    [Header("掉落演出（尺寸會乘上怪物視覺縮放，約 21.7）")]
    [Tooltip("吃到燭火後，過幾秒手臂才斷（等 gethit3 受擊動作甩出去）")]
    public float detachDelay = 0.3f;
    [Tooltip("掉下去的手臂存在幾秒（最後 0.5 秒縮小到消失）")]
    public float armLife = 2.5f;
    [Tooltip("往身後（追逐方向的反方向）彈的速度，單位＝視覺高度的倍數／秒")]
    public float backPop = 0.30f;
    [Tooltip("往上踢的速度（視覺高度的倍數／秒）")]
    public float upKick = 0.55f;
    [Tooltip("往手臂那一側（鏡頭深度方向）彈的速度")]
    public float sidePop = 0.12f;
    [Tooltip("重力（視覺高度的倍數／秒²）")]
    public float gravityMul = 1.1f;
    [Tooltip("翻滾角速度（度／秒）")]
    public float spinDegrees = 220f;
    [Tooltip("斷掉時噴的暗色碎屑數量（0＝不噴）")]
    public int burstCount = 60;

    [Header("血（★1118c）")]
    [Tooltip("掉一隻手臂就多染一層血、斷口噴血、掉下去的手臂帶血。關掉＝只有碎屑")]
    public bool bloodEnabled = true;

    [Header("聲音")]
    [Tooltip("手臂斷掉的聲音（留空＝Resources/Sfx/Glass_BridgeCrack，壓低音調當成扯斷聲；找不到就沒聲音）")]
    public AudioClip shedSfx;
    [Range(0f, 1f)] public float shedVolume = 0.8f;

    // ───────── 手臂資料 ─────────
    private class Arm
    {
        public List<int> verts = new List<int>();
        public int[][] tris;          // [子網格] → 這隻手臂在該子網格的三角形索引
        public Vector3 center;        // 網格本地座標中心
        public int vertCount;
    }

    private class Flying
    {
        public Transform t;
        public Mesh mesh;
        public Vector3 v, spin;
        public float age;
    }

    private SkinnedMeshRenderer _body;
    private Mesh _orig, _live;
    private readonly List<Arm> _arms = new List<Arm>();
    private int[][] _keepTris;         // 非手臂的三角形（每個子網格）
    private int[] _vArm;               // 每個頂點屬於第幾隻手臂（−1＝不是）
    private int _shed;                 // 目前已經拿掉的手臂數
    private bool _ready, _failed;
    private Coroutine _pending;
    private readonly List<Flying> _flying = new List<Flying>();
    private MonsterBloodLook _blood;   // ★1118c 染血＋噴血
    private AudioSource _src;
    private Vector3 _lastPos; private float _vx;

    /// <summary>測試／驗證用：偵測到幾隻手臂。</summary>
    public int DetectedArms => _arms.Count;
    /// <summary>目前拿掉幾隻。</summary>
    public int ShedCount => _shed;
    /// <summary>目前還在空中掉的手臂數（測試用）。</summary>
    public int FlyingCount => _flying.Count;

    private void Start() { Prepare(); _lastPos = transform.position; }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt > 0f) { _vx = Mathf.Lerp(_vx, (transform.position.x - _lastPos.x) / dt, 0.2f); }
        _lastPos = transform.position;
        UpdateFlying(dt);
    }

    // ───────── 準備：複製網格、找手臂 ─────────
    private bool Prepare()
    {
        if (_ready) return true;
        if (_failed) return false;
        if (!gameObject.activeInHierarchy) return false;   // 場景裡那隻關掉的舊影子怪物（不同模型）不用管，等它真的啟用再說

        _body = FindBody();
        if (_body == null || _body.sharedMesh == null) { Fail("找不到 Body 的 SkinnedMeshRenderer／網格"); return false; }
        _orig = _body.sharedMesh;
        if (!_orig.isReadable) { Fail($"網格 {_orig.name} 沒開 Read/Write（匯入設定 isReadable），讀不到頂點，手臂不會掉"); return false; }

        Mesh m = Instantiate(_orig);
        m.name = _orig.name + "_ArmShed";
        if (!Analyze(m)) { Destroy(m); Fail("沒找到足夠的手臂網格島"); return false; }

        _live = m;
        _body.sharedMesh = _live;
        _ready = true;
        ApplyShedMesh(_shed);
        if (bloodEnabled)
        {
            _blood = GetComponent<MonsterBloodLook>();
            if (_blood == null) _blood = gameObject.AddComponent<MonsterBloodLook>();
            _blood.SetLevel(BloodLevel(_shed), false);
        }
        return true;
    }

    private void Fail(string why)
    {
        _failed = true;
        Debug.LogWarning("【手臂】★1118b " + why + "：掉手臂停用（怪物維持完整網格）");
    }

    private SkinnedMeshRenderer FindBody()
    {
        SkinnedMeshRenderer best = null; int bestV = -1;
        foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (s == null || s.sharedMesh == null) continue;
            if (!string.IsNullOrEmpty(bodyRendererName) && s.name == bodyRendererName) return s;
            string n = s.name.ToLowerInvariant();
            if (n.Contains("eye") || n.Contains("teeth") || n.Contains("tooth")) continue;
            if (s.sharedMesh.vertexCount > bestV) { bestV = s.sharedMesh.vertexCount; best = s; }
        }
        return best;
    }

    private static int Find(int[] p, int x)
    {
        while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; }
        return x;
    }

    private bool Analyze(Mesh mesh)
    {
        Vector3[] pos = mesh.vertices;
        int n = pos.Length;
        BoneWeight[] bw = mesh.boneWeights;
        if (bw == null || bw.Length != n) return false;

        // 1) 座標相同的頂點合併成同一點（FBX 為了 UV／法線切開的頂點）
        var map = new Dictionary<Vector3Int, int>(n);
        int[] merged = new int[n];
        for (int i = 0; i < n; i++)
        {
            Vector3Int k = new Vector3Int(Mathf.RoundToInt(pos[i].x * 10000f), Mathf.RoundToInt(pos[i].y * 10000f), Mathf.RoundToInt(pos[i].z * 10000f));
            if (!map.TryGetValue(k, out int id)) { id = map.Count; map[k] = id; }
            merged[i] = id;
        }
        int[] parent = new int[map.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        // 2) 同一個三角形的三點相連
        int subCount = mesh.subMeshCount;
        int[][] idx = new int[subCount][];
        for (int s = 0; s < subCount; s++)
        {
            idx[s] = mesh.GetTriangles(s);
            int[] t = idx[s];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = Find(parent, merged[t[i]]), b = Find(parent, merged[t[i + 1]]), c = Find(parent, merged[t[i + 2]]);
                if (a != b) parent[b] = a;
                int a2 = Find(parent, a), c2 = Find(parent, c);
                if (a2 != c2) parent[c2] = a2;
            }
        }

        // 3) 每座島：頂點數、中心、有多少頂點主要綁在手臂骨頭上
        Transform[] bones = _body.bones;
        var armBoneIdx = new HashSet<int>();
        for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == armBoneName) armBoneIdx.Add(i);
        if (armBoneIdx.Count == 0) { Debug.LogWarning("【手臂】★1118b 骨架裡沒有 " + armBoneName); return false; }

        var count = new Dictionary<int, int>();
        var sum = new Dictionary<int, Vector3>();
        var onArm = new Dictionary<int, int>();
        int[] comp = new int[n];
        for (int i = 0; i < n; i++)
        {
            int r = Find(parent, merged[i]);
            comp[i] = r;
            count.TryGetValue(r, out int c0); count[r] = c0 + 1;
            sum.TryGetValue(r, out Vector3 s0); sum[r] = s0 + pos[i];
            float w = 0f;
            if (armBoneIdx.Contains(bw[i].boneIndex0)) w += bw[i].weight0;
            if (armBoneIdx.Contains(bw[i].boneIndex1)) w += bw[i].weight1;
            if (armBoneIdx.Contains(bw[i].boneIndex2)) w += bw[i].weight2;
            if (armBoneIdx.Contains(bw[i].boneIndex3)) w += bw[i].weight3;
            if (w >= 0.5f) { onArm.TryGetValue(r, out int o0); onArm[r] = o0 + 1; }
        }

        var picked = new List<KeyValuePair<int, Vector3>>();   // 島代表點 → 中心
        var log = new System.Text.StringBuilder();
        foreach (var kv in count)
        {
            Vector3 center = sum[kv.Key] / kv.Value;
            onArm.TryGetValue(kv.Key, out int on);
            float frac = on / (float)kv.Value;
            bool ok = kv.Value >= minArmVertices && frac >= 0.98f && Mathf.Abs(center.x) > minArmCenterX;
            if (kv.Value >= minArmVertices) log.Append($"\n    島 {kv.Value} 頂點，中心 ({center.x:F2}, {center.y:F2}, {center.z:F2})，綁 {armBoneName} {frac * 100f:F0}% → {(ok ? "手臂" : "身體／略過")}");
            if (ok) picked.Add(new KeyValuePair<int, Vector3>(kv.Key, center));
        }
        if (picked.Count == 0) { Debug.LogWarning("【手臂】★1118b 沒有符合條件的網格島" + log); return false; }

        // 掉的順序：y 由低到高，每兩隻一組，組內 x 由小到大
        picked.Sort((a, b) => a.Value.y.CompareTo(b.Value.y));
        var order = new List<KeyValuePair<int, Vector3>>();
        for (int i = 0; i < picked.Count; i += 2)
        {
            var grp = picked.GetRange(i, Mathf.Min(2, picked.Count - i));
            grp.Sort((a, b) => a.Value.x.CompareTo(b.Value.x));
            order.AddRange(grp);
        }
        if (order.Count > ArmCount) order.RemoveRange(ArmCount, order.Count - ArmCount);

        // 4) 分配頂點、三角形
        _vArm = new int[n];
        for (int i = 0; i < n; i++) _vArm[i] = -1;
        var rootToArm = new Dictionary<int, int>();
        _arms.Clear();
        for (int a = 0; a < order.Count; a++)
        {
            rootToArm[order[a].Key] = a;
            _arms.Add(new Arm { center = order[a].Value, vertCount = count[order[a].Key], tris = new int[subCount][] });
        }
        for (int i = 0; i < n; i++)
            if (rootToArm.TryGetValue(comp[i], out int a)) { _vArm[i] = a; _arms[a].verts.Add(i); }

        _keepTris = new int[subCount][];
        var armTri = new List<int>[_arms.Count];
        for (int s = 0; s < subCount; s++)
        {
            var keep = new List<int>(idx[s].Length);
            for (int a = 0; a < armTri.Length; a++) armTri[a] = new List<int>();
            int[] t = idx[s];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = _vArm[t[i]];
                if (a >= 0) { armTri[a].Add(t[i]); armTri[a].Add(t[i + 1]); armTri[a].Add(t[i + 2]); }
                else { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
            }
            _keepTris[s] = keep.ToArray();
            for (int a = 0; a < _arms.Count; a++) _arms[a].tris[s] = armTri[a].ToArray();
        }

        var sb = new System.Text.StringBuilder();
        for (int a = 0; a < _arms.Count; a++)
            sb.Append($"\n    第 {a + 1} 隻掉：{_arms[a].vertCount} 頂點，中心 ({_arms[a].center.x:F2}, {_arms[a].center.y:F2}, {_arms[a].center.z:F2}) {(_arms[a].center.y < 1.3f ? "腰" : "肩")} {(_arms[a].center.x < 0f ? "−x" : "+x")}");
        Debug.Log($"【手臂】★1118b 偵測到 {_arms.Count} 隻多出來的手臂（網格 {_orig.name}，{n} 頂點，{subCount} 個子網格）：{log}{sb}");
        return true;
    }

    // ───────── 對外：讓剛好前 n 隻不見 ─────────
    public void SetShedCount(int n, bool animate)
    {
        if (!Prepare()) return;
        n = Mathf.Clamp(n, 0, _arms.Count);
        if (_pending != null) { StopCoroutine(_pending); _pending = null; }

        bool canAnimate = animate && n > _shed && _body != null && _body.enabled && _body.gameObject.activeInHierarchy && isActiveAndEnabled;
        if (!animate) ClearFlying();
        if (!canAnimate)
        {
            if (n != _shed || !animate) { ApplyShedMesh(n); _shed = n; }
            if (_blood != null) _blood.SetLevel(BloodLevel(n), false);   // 重生／存檔點／測試鍵：直接換成對應的血量，不噴血
            return;
        }
        _pending = StartCoroutine(ShedRoutine(n));
    }

    private IEnumerator ShedRoutine(int target)
    {
        if (detachDelay > 0f) yield return new WaitForSeconds(detachDelay);
        while (_shed < target)
        {
            int k = _shed;
            DetachArm(k);
            _shed = k + 1;
            ApplyShedMesh(_shed);
            if (_blood != null) _blood.SetLevel(BloodLevel(_shed), true);
            if (_shed < target) yield return new WaitForSeconds(0.15f);
        }
        _pending = null;
    }

    private float BloodLevel(int shed) => _arms.Count > 0 ? Mathf.Clamp01(shed / (float)_arms.Count) : 0f;

    /// <summary>把網格複本的三角形換成「前 n 隻手臂不在」。</summary>
    private void ApplyShedMesh(int n)
    {
        if (_live == null) return;
        var list = new List<int>();
        for (int s = 0; s < _keepTris.Length; s++)
        {
            list.Clear();
            list.AddRange(_keepTris[s]);
            for (int a = n; a < _arms.Count; a++) list.AddRange(_arms[a].tris[s]);
            _live.SetTriangles(list, s, false);
        }
    }

    // ───────── 掉下去的那一隻 ─────────
    private void DetachArm(int k)
    {
        Arm arm = _arms[k];
        Mesh src = _live;
        Vector3[] pos = src.vertices;
        Vector3[] nrm = src.normals;
        Vector4[] tan = src.tangents;
        Vector2[] uv = src.uv;
        BoneWeight[] bw = src.boneWeights;
        Matrix4x4[] bind = src.bindposes;
        Transform[] bones = _body.bones;

        // 每個骨頭的蒙皮矩陣（骨頭世界矩陣 × bindpose）
        var skin = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) skin[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;

        int vc = arm.verts.Count;
        var wp = new Vector3[vc];
        var wn = new Vector3[vc];
        var wt = new Vector4[vc];
        var uvs = new Vector2[vc];
        var remap = new Dictionary<int, int>(vc);
        Vector3 centroid = Vector3.zero;
        for (int j = 0; j < vc; j++)
        {
            int i = arm.verts[j];
            remap[i] = j;
            BoneWeight w = bw[i];
            Vector3 p = Vector3.zero, nn = Vector3.zero, tt = Vector3.zero;
            Accum(skin, w.boneIndex0, w.weight0, pos[i], nrm, tan, i, ref p, ref nn, ref tt);
            Accum(skin, w.boneIndex1, w.weight1, pos[i], nrm, tan, i, ref p, ref nn, ref tt);
            Accum(skin, w.boneIndex2, w.weight2, pos[i], nrm, tan, i, ref p, ref nn, ref tt);
            Accum(skin, w.boneIndex3, w.weight3, pos[i], nrm, tan, i, ref p, ref nn, ref tt);
            wp[j] = p;
            wn[j] = nn.sqrMagnitude > 1e-10f ? nn.normalized : Vector3.up;
            wt[j] = tan != null && tan.Length == pos.Length ? new Vector4(tt.normalized.x, tt.normalized.y, tt.normalized.z, tan[i].w) : new Vector4(1, 0, 0, 1);
            uvs[j] = uv != null && uv.Length == pos.Length ? uv[i] : Vector2.zero;
            centroid += p;
        }
        centroid /= Mathf.Max(1, vc);
        // ★1118c 手臂斷口＝這隻手臂最靠近身體中線的那一小撮頂點（網格本地 |x| 最小的 5%）的平均位置
        Vector3 socket = centroid;
        {
            int take = Mathf.Max(8, vc / 20);
            var order = new List<int>(vc);
            for (int j = 0; j < vc; j++) order.Add(j);
            order.Sort((a, b) => Mathf.Abs(pos[arm.verts[a]].x).CompareTo(Mathf.Abs(pos[arm.verts[b]].x)));
            Vector3 sum = Vector3.zero; int cnt = Mathf.Min(take, vc);
            for (int q = 0; q < cnt; q++) sum += wp[order[q]];
            socket = sum / Mathf.Max(1, cnt);
        }
        for (int j = 0; j < vc; j++) wp[j] -= centroid;

        Mesh m = new Mesh();
        m.name = "掉落的手臂" + (k + 1);
        if (vc > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = wp;
        m.normals = wn;
        if (tan != null && tan.Length == pos.Length) m.tangents = wt;
        m.uv = uvs;
        m.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            int[] t = arm.tris[s];
            var nt = new int[t.Length];
            for (int i = 0; i < t.Length; i++) nt[i] = remap[t[i]];
            m.SetTriangles(nt, s, false);
        }
        m.RecalculateBounds();

        var go = new GameObject("掉落的手臂" + (k + 1) + "（自動生成）");
        go.layer = _body.gameObject.layer;
        go.transform.position = centroid;
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = _blood != null ? _blood.BloodSharedMaterials(_body) : _body.sharedMaterials;   // ★1118c 掉下去的手臂用染血貼圖
        mr.shadowCastingMode = _body.shadowCastingMode;
        mr.receiveShadows = _body.receiveShadows;
        mr.lightProbeUsage = _body.lightProbeUsage;
        mr.sortingLayerID = _body.sortingLayerID;
        mr.sortingOrder = _body.sortingOrder;
        var mpb = new MaterialPropertyBlock();
        _body.GetPropertyBlock(mpb);
        mr.SetPropertyBlock(mpb);

        // 彈出去的方向：往追逐的反方向（身後）彈、往上踢、往手臂那一側（鏡頭深度方向）推
        float S = Mathf.Max(0.5f, Mathf.Abs(_body.transform.lossyScale.y));
        Vector3 bodyCenter = _body.bounds.center;
        float backDir = Mathf.Abs(_vx) > 0.5f ? -Mathf.Sign(_vx) : -1f;
        var ctrl = GetComponent<ShadowMonsterController>();
        if (Mathf.Abs(_vx) <= 0.5f && ctrl != null && ctrl.player != null) backDir = ctrl.player.position.x >= transform.position.x ? -1f : 1f;
        float sideSign = Mathf.Abs(centroid.z - bodyCenter.z) > 0.01f ? Mathf.Sign(centroid.z - bodyCenter.z) : (arm.center.x < 0f ? -1f : 1f);
        float j1 = Random.Range(0.8f, 1.2f), j2 = Random.Range(0.85f, 1.15f);
        var fl = new Flying
        {
            t = go.transform,
            mesh = m,
            v = new Vector3(backDir * backPop * S * j1 + _vx * 0.6f, upKick * S * j2, sideSign * sidePop * S),
            spin = new Vector3(Random.Range(-60f, 60f), Random.Range(-60f, 60f), -backDir * spinDegrees * Random.Range(0.7f, 1.2f)),
        };
        _flying.Add(fl);

        SpawnBurst(socket, S);
        if (_blood != null && bloodEnabled)
        {
            Vector3 outDir = socket - bodyCenter; outDir.z *= 0.35f;   // 往外噴，深度方向收一點（鏡頭是側視）
            outDir += Vector3.up * 0.25f * outDir.magnitude;
            _blood.SprayBlood(socket, outDir, S);
        }
        PlayShedSfx(centroid, k);
        Debug.Log($"【手臂】★1118b 第 {k + 1} 隻手臂斷掉（{vc} 頂點，世界 {centroid.x:F1}, {centroid.y:F1}, {centroid.z:F1}），剩 {_arms.Count - k - 1} 隻會掉");
    }

    private static void Accum(Matrix4x4[] skin, int bi, float w, Vector3 p, Vector3[] nrm, Vector4[] tan, int i, ref Vector3 outP, ref Vector3 outN, ref Vector3 outT)
    {
        if (w <= 0f || bi < 0 || bi >= skin.Length) return;
        Matrix4x4 M = skin[bi];
        outP += M.MultiplyPoint3x4(p) * w;
        if (nrm != null && nrm.Length > i) outN += M.MultiplyVector(nrm[i]) * w;
        if (tan != null && tan.Length > i) outT += M.MultiplyVector(new Vector3(tan[i].x, tan[i].y, tan[i].z)) * w;
    }

    private void UpdateFlying(float dt)
    {
        if (_flying.Count == 0) return;
        float S = _body != null ? Mathf.Max(0.5f, Mathf.Abs(_body.transform.lossyScale.y)) : 20f;
        float g = gravityMul * S;
        for (int i = _flying.Count - 1; i >= 0; i--)
        {
            Flying f = _flying[i];
            f.age += dt;
            if (f.t == null || f.age >= armLife) { KillFlying(f); _flying.RemoveAt(i); continue; }
            f.v.y -= g * dt;
            f.t.position += f.v * dt;
            f.t.Rotate(f.spin * dt, Space.World);
            float shrink = Mathf.Clamp01((armLife - f.age) / 0.5f);   // 最後 0.5 秒縮到消失
            f.t.localScale = Vector3.one * (shrink * shrink * (3f - 2f * shrink));
        }
    }

    private static void KillFlying(Flying f)
    {
        if (f.t != null) Destroy(f.t.gameObject);
        if (f.mesh != null) Destroy(f.mesh);
    }

    private void ClearFlying()
    {
        for (int i = _flying.Count - 1; i >= 0; i--) KillFlying(_flying[i]);
        _flying.Clear();
    }

    // ───────── 碎屑、聲音 ─────────
    private void SpawnBurst(Vector3 at, float S)
    {
        if (burstCount <= 0) return;
        Material fxMat = MonsterBloodLook.FxMaterial();   // ★1118c Resources 裡的材質，打包不會被剔除
        if (fxMat == null) return;
        var go = new GameObject("手臂斷裂碎屑（自動生成）");
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = fxMat;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f * S, 0.55f * S);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f * S, 0.04f * S);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.06f, 0.1f, 0.95f), new Color(0.45f, 0.1f, 0.1f, 0.95f));
        main.gravityModifier = 2.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(burstCount, 10);

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(burstCount, 1, 500)) });

        var sp = ps.shape;
        sp.enabled = true;
        sp.shapeType = ParticleSystemShapeType.Sphere;
        sp.radius = 0.18f * S;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        ps.Play();
        Destroy(go, 3f);
    }

    private void PlayShedSfx(Vector3 at, int k)
    {
        if (shedSfx == null) shedSfx = Resources.Load<AudioClip>("Sfx/Glass_BridgeCrack");
        if (shedSfx == null) return;
        if (_src == null)
        {
            var g = new GameObject("手臂斷裂_聲音（自動生成）");
            g.transform.SetParent(transform, false);
            _src = g.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
        }
        _src.pitch = Mathf.Clamp(0.62f + 0.05f * k, 0.3f, 1.2f);
        _src.PlayOneShot(shedSfx, AudioManager.ScaleSfx(shedVolume));
    }

    private void OnDestroy()
    {
        ClearFlying();
        if (_body != null && _orig != null && _body.sharedMesh == _live) _body.sharedMesh = _orig;
        if (_live != null) Destroy(_live);
        if (_blood != null) Destroy(_blood);
    }
}
