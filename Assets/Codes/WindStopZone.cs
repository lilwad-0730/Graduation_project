using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★0905 拍四「風停」（Q5-S7：全片第一次完全靜止）。
/// 玩家走進 x≈225 之後：風永久停止（不再起風、不再前兆）、風沙與環境風嘯停、鳥不再出現。
/// 綠洲的「假安全」要靠這個靜——之後接 M3 卡與潛入水下。
/// 重生會由 ResetToInitialState 解除（風系統自己也會 Reset），再走進來會再觸發。
/// 另附提案中的「順風段」（enableTailwind，預設關）：從 tailwindStartX 起風向反轉成順風，把她推向水——
/// TXT-03「風往一個方向吹／不必決定要去哪」的手感版；跟 GDD「恆逆風」正典不同，先實測拍三、拍四再決定要不要開。
/// </summary>
[DisallowMultipleComponent]
public class WindStopZone : MonoBehaviour, IResettable
{
    [Header("風停區")]
    public float zoneX = 225f;
    [Tooltip("這些名字的物件底下的粒子系統會一起停（環境風嘯）")]
    public string[] ambientWindObjectNames = new string[] { "Desert_StylizedWindSwoosh_System", "WindDirector" };

    [Header("提案：順風段（預設關）")]
    public bool enableTailwind = false;
    public float tailwindStartX = 195f;

    private bool _fired = false;
    private bool _tailwindOn = false;
    private readonly List<ParticleSystem> _stoppedAmbient = new List<ParticleSystem>();
    private Transform _player;
    private DesertWindDustFX _dust;
    private float _dustOriginalDir = -1f;

    public static WindStopZone Install(float x)
    {
        WindStopZone existing = FindFirstObjectByType<WindStopZone>();
        if (existing != null) return existing;

        GameObject go = new GameObject("WindStopZone (自動生成)");
        go.transform.position = new Vector3(x, 0f, 0f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4f, 80f, 30f);
        WindStopZone z = go.AddComponent<WindStopZone>();
        z.zoneX = x;
        return z;
    }

    private void Update()
    {
        if (!enableTailwind || _tailwindOn) return;
        if (_player == null)
        {
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) _player = pm.transform;
        }
        if (_player == null) return;
        if (_player.position.x >= tailwindStartX && _player.position.x < zoneX)
        {
            SetTailwind(true);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_fired) return;
        if (!DesertBeatDirector.IsPlayerObject(other.gameObject)) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;
        Fire();
    }

    public void Fire()
    {
        _fired = true;

        WindGustSystem wind = WindGustSystem.Instance;
        if (wind != null) wind.StopForever();

        IndividualBirdEnemy.SuppressAllUntil = float.PositiveInfinity;

        // 環境風嘯（不歸 WindGustSystem 管的那些粒子）一起停
        _stoppedAmbient.Clear();
        if (ambientWindObjectNames != null)
        {
            foreach (string n in ambientWindObjectNames)
            {
                if (string.IsNullOrEmpty(n)) continue;
                GameObject go = GameObject.Find(n);
                if (go == null) continue;
                foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps == null || !ps.isPlaying) continue;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    _stoppedAmbient.Add(ps);
                }
            }
        }

        if (_tailwindOn) SetTailwind(false);   // 順風段到此為止：接下來是靜

        Debug.Log("[WindStopZone] 風停。之後不再起風、不再有鳥——綠洲前的靜（Q5-S7）。");
    }

    private void SetTailwind(bool on)
    {
        if (_dust == null) _dust = FindFirstObjectByType<DesertWindDustFX>();
        if (_dust == null) return;
        if (on)
        {
            _dustOriginalDir = _dust.windDirectionX;
            _dust.windDirectionX = Mathf.Abs(_dustOriginalDir) > 0.01f ? -Mathf.Sign(_dustOriginalDir) : 1f;
        }
        else
        {
            _dust.windDirectionX = _dustOriginalDir;
        }
        _dust.ApplyVFXSettings();
        _tailwindOn = on;
        Debug.Log(on ? "[WindStopZone] 順風段開始：風把她往水推。" : "[WindStopZone] 順風段結束。");
    }

    // --- IResettable：重生時解除（風系統自己也會 Reset） ---
    public void ResetToInitialState()
    {
        if (_fired)
        {
            foreach (ParticleSystem ps in _stoppedAmbient)
            {
                if (ps != null) ps.Play(true);
            }
            _stoppedAmbient.Clear();
        }
        if (_tailwindOn) SetTailwind(false);
        _fired = false;
        IndividualBirdEnemy.SuppressAllUntil = -1f;
    }
}
