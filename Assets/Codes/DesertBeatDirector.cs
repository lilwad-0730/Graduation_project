using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★0905 荒原四拍導演（依《我你他_荒原研究_遊戲性與故事性_0905》第四、五節）。
///
/// 進到有 WindGustSystem 的場景（＝荒原）自動生成，**不動場景檔**；只把場景裡已經存在的東西依 x 座標排成四拍：
///   拍一  0～45    掩體全部改真、會攻擊的鳥全部拿掉（背景鳥群 ScatteredFlock 照飛）
///   拍二  45～135  真假掩體照舊，另挑三座掛 DynamicFadeShelter 變「風堆掩體」（會消失又長回來）；
///                  鳥減半、前搖拉到 1.5 秒；第一隻鳥示範俯衝——衝手套旁的地面、不打她
///   拍三  135～225 沒有掩體（現況）；開場一道巨鳥影子掠地（GiantShadowPass）；鳥前搖三隻一組錯開
///   拍四  225～    風永久停（WindStopZone）、鳥不再出現；散落物與門框（DesertRelics）
///
/// 整包關掉：DesertBeatDirector.Enabled = false，或直接刪這個檔（其餘新腳本都由它啟動）。
/// 想調參：把 DesertBeatDirector 手動掛到場景任一物件上改 Inspector 值，自動生成就會讓位。
/// 每次套用都會在 Console 印一行摘要「[DesertBeatDirector] …」，跟場景檔對不上時先看那一行。
/// </summary>
[DisallowMultipleComponent]
public class DesertBeatDirector : MonoBehaviour
{
    /// <summary>總開關（程式碼層級）。</summary>
    public static bool Enabled = true;

    public static DesertBeatDirector Instance { get; private set; }

    [Header("四拍邊界（世界 x）")]
    public float beat1End = 45f;
    public float beat2End = 135f;
    public float beat3End = 225f;

    [Header("拍一：掩體全真、鳥清空")]
    public bool beat1AllTrueShelters = true;
    public bool beat1RemoveBirds = true;

    [Header("拍二：風堆掩體（在這些 x 附近找掩體掛 DynamicFadeShelter）")]
    public bool enableDriftShelters = true;
    public float[] driftShelterXs = new float[] { 70.8f, 98.1f, 114.3f };
    public float driftShelterSearchRadius = 2.5f;
    [Tooltip("亮著（可躲）秒數。6＝正好一輪風（吹 2.5＋停 3.5）；整個週期 12 秒＝每隔一陣風消失一次：「下一陣風可能就帶走它」")]
    public float driftActiveSeconds = 6f;
    public float driftFadeOutSeconds = 1.5f;
    public float driftInactiveSeconds = 3f;
    public float driftFadeInSeconds = 1.5f;

    [Header("拍二：鳥減半、前搖 1.5、第一隻示範俯衝")]
    public bool beat2ThinBirds = true;
    public float beat2WarningSeconds = 1.5f;
    public bool enableDemoDive = true;
    [Tooltip("示範俯衝的落點＝第一座假掩體的背風面（手套旁）。找不到假掩體就用這個 x")]
    public float demoDiveFallbackX = 62.5f;
    [Tooltip("落點相對假掩體中心的偏移（負＝左邊＝背風面）")]
    public float demoDiveOffsetFromShelter = -1.0f;
    public float demoDiveDetectionRange = 12f;

    [Header("拍三：前搖錯開（三隻一組 1.2／1.5／1.8）")]
    public bool beat3StaggerWarnings = true;
    public float beat3WarningBase = 1.2f;
    public float beat3WarningStep = 0.3f;

    [Header("拍三開場鳥影、拍四風停、散落物、表現")]
    public bool enableGiantShadow = true;
    public float giantShadowX = 137f;
    public bool enableWindStop = true;
    public bool enableRelics = true;
    public bool enableTelegraphHum = true;
    public bool enableBraceFrost = true;

    [Header("找地面")]
    [Tooltip("射線找不到地面時用的 y（掩體柱腳大約在 -6.3）")]
    public float groundFallbackY = -6.3f;
    public float groundRayZ = -1f;

    private bool _applied = false;

    // ─────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryInstall();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryInstall();
    }

    private static void TryInstall()
    {
        IndividualBirdEnemy.SuppressAllUntil = -1f;   // 換場景一律歸零（static 會跨場景留著）
        if (!Enabled) return;
        if (WindGustSystem.Instance == null && FindFirstObjectByType<WindGustSystem>() == null) return;   // 只有荒原有風系統
        if (FindFirstObjectByType<DesertBeatDirector>() != null) return;                                   // 場景已手動掛了就讓位
        new GameObject("DesertBeatDirector (自動生成)").AddComponent<DesertBeatDirector>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        StartCoroutine(ApplyNextFrame());
    }

    private IEnumerator ApplyNextFrame()
    {
        yield return null;   // 等場景所有 Awake／Start 跑完（鳥的重複元件會在第一幀末被銷毀）
        Apply();
    }

    // ─────────────────────────────────────────────────────────────
    public void Apply()
    {
        if (_applied) return;
        _applied = true;

        System.Text.StringBuilder log = new System.Text.StringBuilder();
        log.Append("[DesertBeatDirector] 套用四拍（").Append(beat1End).Append("／").Append(beat2End).Append("／").Append(beat3End).Append("）：");

        ApplyShelters(log);
        ApplyBirds(log);

        if (enableGiantShadow) { GiantShadowPass.Install(giantShadowX); log.Append(" 鳥影@").Append(giantShadowX).Append('；'); }
        if (enableWindStop)    { WindStopZone.Install(beat3End);      log.Append(" 風停@").Append(beat3End).Append('；'); }
        if (enableRelics)      { DesertRelics.Install();               }
        if (enableTelegraphHum){ WindTelegraphHum.Install();           log.Append(" 前兆低鳴；"); }
        if (enableBraceFrost)  { BraceFrostFX.Install();               log.Append(" 硬撐結霜；"); }

        Debug.Log(log.ToString());
    }

    private void ApplyShelters(System.Text.StringBuilder log)
    {
        WindShelter[] shelters = FindObjectsByType<WindShelter>(FindObjectsSortMode.None);
        int flipped = 0;
        if (beat1AllTrueShelters)
        {
            foreach (WindShelter ws in shelters)
            {
                if (ws == null) continue;
                if (ws.transform.position.x < beat1End && !ws.isTrueShelter)
                {
                    ws.isTrueShelter = true;   // 教學序＝先真後假（GDD 正典）
                    flipped++;
                }
            }
        }

        int drift = 0;
        if (enableDriftShelters && driftShelterXs != null)
        {
            foreach (float targetX in driftShelterXs)
            {
                WindShelter best = null;
                float bestDist = driftShelterSearchRadius;
                foreach (WindShelter ws in shelters)
                {
                    if (ws == null) continue;
                    float d = Mathf.Abs(ws.transform.position.x - targetX);
                    if (d <= bestDist) { bestDist = d; best = ws; }
                }
                if (best == null) continue;
                if (best.GetComponent<DynamicFadeShelter>() != null) continue;

                best.isTrueShelter = true;   // 風堆掩體：真的能擋，只是會被下一陣風帶走
                DynamicFadeShelter fade = best.gameObject.AddComponent<DynamicFadeShelter>();
                fade.activeDuration = driftActiveSeconds;
                fade.fadeOutDuration = driftFadeOutSeconds;
                fade.inactiveDuration = driftInactiveSeconds;
                fade.fadeInDuration = driftFadeInSeconds;
                drift++;
            }
        }

        log.Append(" 掩體 ").Append(shelters.Length).Append(" 座（拍一改真 ").Append(flipped).Append("、風堆 ").Append(drift).Append("）；");
    }

    private void ApplyBirds(System.Text.StringBuilder log)
    {
        List<IndividualBirdEnemy> birds = new List<IndividualBirdEnemy>();
        foreach (IndividualBirdEnemy b in FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None))
        {
            if (b == null) continue;
            // 子物件上的重複元件（鳥自己的 Awake 會銷毀）不算一隻
            if (b.transform.parent != null && b.transform.parent.GetComponentInParent<IndividualBirdEnemy>() != null) continue;
            IndividualBirdEnemy[] same = b.GetComponents<IndividualBirdEnemy>();
            if (same.Length > 1 && same[0] != b) continue;
            birds.Add(b);
        }
        birds.Sort((a, c) => a.transform.position.x.CompareTo(c.transform.position.x));

        float demoX = ResolveDemoDiveX();
        int removed1 = 0, thinned = 0, beat2Kept = 0, staggered = 0, removed4 = 0;
        int beat2Index = 0, beat3Index = 0;
        IndividualBirdEnemy demoBird = null;
        IndividualBirdEnemy demoNearest = null;
        float demoNearestDist = float.MaxValue;

        foreach (IndividualBirdEnemy b in birds)
        {
            float x = b.transform.position.x;

            if (x < beat1End)
            {
                if (beat1RemoveBirds) { Destroy(b.gameObject); removed1++; }
                continue;
            }

            if (x < beat2End)
            {
                if (beat2ThinBirds && (beat2Index % 2) == 1)
                {
                    beat2Index++;
                    Destroy(b.gameObject);
                    thinned++;
                    continue;
                }
                beat2Index++;
                beat2Kept++;
                b.warningDuration = beat2WarningSeconds;

                if (enableDemoDive)
                {
                    if (demoBird == null && x >= demoX && x <= demoX + 15f) demoBird = b;   // 手套之後 15 單位內的第一隻
                    float dist = Mathf.Abs(x - demoX);
                    if (dist < demoNearestDist) { demoNearestDist = dist; demoNearest = b; }
                }
                continue;
            }

            if (x < beat3End)
            {
                if (beat3StaggerWarnings)
                {
                    b.warningDuration = beat3WarningBase + (beat3Index % 3) * beat3WarningStep;
                    staggered++;
                }
                beat3Index++;
                continue;
            }

            // 拍四：風停之後不該還有鳥
            Destroy(b.gameObject);
            removed4++;
        }

        if (enableDemoDive)
        {
            if (demoBird == null) demoBird = demoNearest;
            if (demoBird != null)
            {
                GameObject target = new GameObject("DemoDiveTarget (手套旁地面)");
                target.transform.SetParent(transform, false);
                target.transform.position = new Vector3(demoX, GroundYAt(demoX) + 0.1f, demoBird.transform.position.z);
                demoBird.overrideTarget = target.transform;
                demoBird.harmless = true;
                demoBird.autoDetectPlayer = true;
                demoBird.triggerMode = BirdTriggerMode.Both;
                demoBird.detectionRange = demoDiveDetectionRange;
                demoBird.warningDuration = beat2WarningSeconds;
                demoBird.name = demoBird.name + " [示範俯衝]";
            }
        }

        log.Append(" 鳥 ").Append(birds.Count).Append(" 隻（拍一移除 ").Append(removed1)
           .Append("、拍二留 ").Append(beat2Kept).Append(" 去 ").Append(thinned)
           .Append("、拍三錯開 ").Append(staggered).Append("、拍四移除 ").Append(removed4)
           .Append("、示範俯衝 ").Append(demoBird != null ? demoBird.name : "無").Append("）；");
    }

    /// <summary>示範俯衝落點：第一座假掩體（x ≥ beat1End）的背風面。</summary>
    private float ResolveDemoDiveX()
    {
        WindShelter first = FirstFakeShelter();
        if (first != null) return first.transform.position.x + demoDiveOffsetFromShelter;
        return demoDiveFallbackX;
    }

    /// <summary>拍一之後的第一座假掩體（散落物的手套也放在它的背風面）。</summary>
    public WindShelter FirstFakeShelter()
    {
        WindShelter best = null;
        foreach (WindShelter ws in FindObjectsByType<WindShelter>(FindObjectsSortMode.None))
        {
            if (ws == null || ws.isTrueShelter) continue;
            float x = ws.transform.position.x;
            if (x < beat1End) continue;
            if (best == null || x < best.transform.position.x) best = ws;
        }
        return best;
    }

    /// <summary>往下打射線找地面高度（忽略 Trigger）；找不到回 groundFallbackY。</summary>
    public float GroundYAt(float x)
    {
        RaycastHit hit;
        Vector3 origin = new Vector3(x, 30f, groundRayZ);
        if (Physics.Raycast(origin, Vector3.down, out hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return groundFallbackY;
    }

    /// <summary>判斷碰撞物是不是主角（跟 StormHazardWave 同一套）。</summary>
    public static bool IsPlayerObject(GameObject obj)
    {
        if (obj == null) return false;
        if (obj.CompareTag("Player")) return true;
        if (obj.GetComponentInParent<PlayerMovement>() != null) return true;
        return obj.name.ToLower().Contains("player");
    }
}
