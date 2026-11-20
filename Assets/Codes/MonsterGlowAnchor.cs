using UnityEngine;

/// <summary>
/// ★1119 書頭怪背後那團紅光要「跟著身體」，不是跟著物件原點。
/// Root Cause（實測）：紅光（PlayerAuraGlow 的光暈貼圖、Shadow_HaloGlowSprite、Shadow_HaloLight、Shadow_HaloParticles）都掛在怪物物件原點上，
///   用「本地座標」往後偏（物件縮放約 20 倍，−0.2 本地 ＝ 往後 4 公尺）；而奔跑動畫會把身體擺在原點前面約 3 公尺、上下晃，
///   結果紅光中心離身體約 6～7 公尺、而且位置一直在身後晃——看起來就像紅光在追怪物。
///   光暈粒子還是「世界座標」模擬，怪物衝刺時粒子留在原地、拖出一條紅尾巴。
/// 做法：每幀在所有動畫、抽搐（ShadowMonsterDread 1000）、眼罩紅光（ShadowMonsterEyeGlow 2000）之後（LateUpdate，執行順序 3000），
///   把這幾個光暈物件的世界 X、Y 直接設到身體中心骨（mixamorig:Spine1）上，Z 不動（維持原本渲染的深度），不平滑、不延遲；
///   光暈粒子是世界座標模擬，每幀把「已經在場上的粒子」跟著錨點的位移一起搬，等於粒子完全不被留在後面。
/// 大小、顏色、亮度、粒子數量一律不動。
/// </summary>
[DefaultExecutionOrder(3000)]
[DisallowMultipleComponent]
public class MonsterGlowAnchor : MonoBehaviour
{
    [Tooltip("光暈對齊的身體骨頭（身體中心）")]
    public string bodyBoneName = "mixamorig:Spine1";
    [Tooltip("PlayerAuraGlow 的光暈貼圖（場景裡被美術擺在本地 (0,1.25,−5)＝怪物身後 100 多公尺、畫面外）要不要也釘到身體上。預設不釘：釘上去會多出一顆原本看不到的白紅光團")]
    public bool pinAuraSprite = false;
    [Tooltip("關掉＝回到掛在物件原點的舊行為（測試用）")]
    public bool anchorToBody = true;

    private Transform _bone;
    private Transform _haloSprite, _haloLight, _haloPs, _aura;
    private ParticleSystem _ps;
    private ParticleSystem.Particle[] _buf;
    private Vector2 _prev;
    private bool _hasPrev;
    private float _nextLookup;

    /// <summary>目前錨點（身體中心）的世界座標，測試用。</summary>
    public Vector3 AnchorWorld { get; private set; }

    private void OnEnable() { _hasPrev = false; }

    private void Lookup()
    {
        if (_bone == null)
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == bodyBoneName) { _bone = all[i]; break; }
            if (_bone == null)
                for (int i = 0; i < all.Length; i++) if (all[i].name.EndsWith("Spine1") || all[i].name.EndsWith("Spine2")) { _bone = all[i]; break; }
        }
        if (_haloSprite == null) _haloSprite = transform.Find("Shadow_HaloGlowSprite");
        if (_haloLight == null) _haloLight = transform.Find("Shadow_HaloLight");
        if (_haloPs == null)
        {
            _haloPs = transform.Find("Shadow_HaloParticles");
            if (_haloPs != null) _ps = _haloPs.GetComponent<ParticleSystem>();
        }
        if (_aura == null) _aura = transform.Find("PlayerAuraGlow");
    }

    private void LateUpdate()
    {
        if (!anchorToBody) return;
        if ((_bone == null || _haloSprite == null || _haloLight == null || _haloPs == null || _aura == null) && Time.unscaledTime >= _nextLookup)
        {
            _nextLookup = Time.unscaledTime + 0.5f;
            Lookup();
        }
        if (_bone == null) return;

        Vector3 a = _bone.position;
        AnchorWorld = a;
        Pin(_haloSprite, a);
        Pin(_haloLight, a);
        Pin(_haloPs, a);
        if (pinAuraSprite) Pin(_aura, a);

        // 世界座標模擬的粒子：跟著錨點一起搬（第一幀、剛啟用時不搬）
        Vector2 cur = new Vector2(a.x, a.y);
        if (_hasPrev && _ps != null)
        {
            Vector2 d = cur - _prev;
            if (d.sqrMagnitude > 1e-10f && _ps.main.simulationSpace == ParticleSystemSimulationSpace.World)
            {
                int max = _ps.main.maxParticles;
                if (_buf == null || _buf.Length < max) _buf = new ParticleSystem.Particle[max];
                int n = _ps.GetParticles(_buf);
                if (n > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = _buf[i].position;
                        p.x += d.x; p.y += d.y;
                        _buf[i].position = p;
                    }
                    _ps.SetParticles(_buf, n);
                }
            }
        }
        _prev = cur;
        _hasPrev = true;
    }

    private static void Pin(Transform t, Vector3 body)
    {
        if (t == null) return;
        Vector3 p = t.position;
        t.position = new Vector3(body.x, body.y, p.z);   // Z 不動：維持原本渲染的深度
    }
}
