using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★0905 荒原四拍導演（依《我你他_荒原研究_遊戲性與故事性_0905》第四、五節）。
///
/// 進到有 WindGustSystem 的場景（＝荒原）自動生成，**不動場景檔**；只把場景裡已經存在的東西依 x 座標排成四拍：
///   拍一  0～45    掩體全部改真、會攻擊的鳥全部拿掉（背景鳥群 ScatteredFlock 照飛）
///   拍二  45～135  真假掩體照舊（★0909：風堆掩體已關掉，見 enableDriftShelters 的說明）；
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
    [Tooltip("★0909 關掉：循環消失的掩體跟關卡其他規則打架，詳見下面的說明。\n想開回來的話，先修好 DynamicFadeShelter 的 alpha 0.2 保護斷點（看得到柱子卻躲不到）")]
    // ★0909 關掉。原因（三座在 x≈70.8／98.2／114.3，本來就是真掩體，被這個功能改成會循環消失）：
    //   1. 消失跟玩家無關：假掩體是「玩家躲進去＋正在吹風」才垮，是玩家自己選擇的後果，看得懂；
    //      風堆掩體是自己的 12 秒碼表在跑，玩家在不在都一樣消失，學不到規則只能背時間。
    //   2. 跟關卡的信號語言矛盾：風要來有 1 秒沙塵線前兆（教玩家看信號），掩體消失卻沒有前兆；
    //      而且 DynamicFadeShelter 在 alpha 0.2 就切掉保護，柱子還看得見卻躲不到。
    //   3. 會長回來，把「掩體垮了」的重量洗掉：假掩體垮掉不可逆（hasCollapsed 擋死），
    //      那個不可逆才是它有分量的原因。同一個視覺事件在同一關有兩種相反意義，玩家沒辦法解讀。
    //   4. 真／假／風堆三種外觀一樣，超過玩家能分辨的上限，只剩純試錯。
    //   5. 第一次進關卡時兩個碼表的相位是隨機的（看元件哪一幀初始化），
    //      運氣壞的話那座掩體每次風來都剛好不在，等於白放。
    //
    //   ※ 補充一件重要的事：0904 定案寫的是「三座掩體掛 DynamicFadeShelter 變風堆掩體（對齊 6 秒風週期）」。
    //     「對齊」從來沒有被實作出來——DynamicFadeShelter 對 WindGustSystem 一次引用都沒有，
    //     實際只是把 driftActiveSeconds 設成 6（數字上等於一輪風 2.5+3.5），但兩個計時器各跑各的。
    //     週期長度一樣不等於相位對齊。所以上面 1、2、5 點其實是「沒對齊」造成的，不是這個設計本身爛。
    //     要重做的話，正確做法是讓 DynamicFadeShelter 跟著 WindGustSystem.CurrentState 走，不要自己算時間。
    //     但即使對齊了，它還是會「玩家沒碰它也自己消失」而且「會長回來」，
    //     跟本關卡定下的掩體規則（外觀一樣→躲進去被風吹才知道真假→假的垮了永遠不回來）衝突，
    //     所以 0909 決定維持關閉。要改回來請先跟企劃確認規則要幾條。
    //
    //   下面的參數全部留著，改回 true 就會恢復原本行為。
    public bool enableDriftShelters = false;
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

    [Header("拍三：前搖錯開（三隻一組 1.0／1.3／1.6）")]
    public bool beat3StaggerWarnings = true;
    [Tooltip("★0916 1.2 → 1.0：沒掩體時玩家一路跑，前搖越長她跑越遠，鳥就插在她身後；縮短一點讓鳥打得到，紅線仍亮 1 秒以上")]
    public float beat3WarningBase = 1.0f;
    public float beat3WarningStep = 0.3f;

    [Header("拍三開場鳥影、拍四風停、散落物、表現")]
    public bool enableGiantShadow = true;
    public float giantShadowX = 137f;
    public bool enableWindStop = true;
    public bool enableRelics = true;
    public bool enableTelegraphHum = true;
    public bool enableBraceFrost = true;

    [Header("★1006 鳥攻擊排程（Request Queue：偵測 → 排隊 → 放行）")]
    [Tooltip("攻擊類型循環：C＝定點（鎖她發現當下的位置）、P＝預判（鎖她前方）。照字串順序一直循環，固定可學習、沒有亂數。\n" +
             "預設 CPCPPCCPCPPC：沒有連續超過 2 個同類型。想純交替就填 CP")]
    public string attackPattern = "CPCPPCCPCPPC";
    [Tooltip("兩次放行之間至少間隔幾秒（全場鳥共用一條時間軸）。越大越不會同時衝")]
    public float slotIntervalSeconds = 0.2f;
    [Tooltip("每幾次放行算一輪，輪與輪之間有一段空檔")]
    [Range(2, 8)] public int attacksPerRound = 4;
    [Tooltip("每輪結束後的短暫空檔 (秒)，玩家可以趁這時候前進／停頓")]
    public float restAfterRoundSeconds = 0.4f;
    [Tooltip("同時「前搖＋俯衝中」的鳥最多幾隻。1＝一次只有一隻；3＝最多三隻，仍不會整群一起衝")]
    [Range(1, 10)] public int maxSimultaneousAttacks = 6;
    [Tooltip("★1008 佇列很長時，同時攻擊上限會自動增加，但最多到這個數字")]
    [Range(1, 12)] public int maxSimultaneousHard = 10;
    [Tooltip("★1008 佇列每多這麼多張需求，同時攻擊上限就 +1（鳥多時自動多放幾隻，鳥少時維持基本值）。0＝不自動增加")]
    public int queuePressureStep = 5;
    [Tooltip("同一區（以玩家為中心分 左／中／右）連續放行超過這個次數，就優先挑別區的鳥")]
    [Range(1, 5)] public int maxSameZoneInRow = 2;
    [Tooltip("中區半寬 (公尺)：鳥離玩家水平距離在 ± 這個值以內算中區，其餘分左右")]
    public float zoneHalfWidth = 5f;
    [Tooltip("需求保留範圍＝該鳥偵測範圍 × 這個倍率。被風／鳥影擋住時需求會一直保留，玩家跑出這個範圍才取消")]
    public float requestKeepRangeMultiplier = 1.5f;
    [Tooltip("★1007 優先分數：鳥在玩家「身後」時加的罰分 (公尺)。越大，前方的鳥越優先於身後的鳥")]
    public float behindPenaltyMeters = 8f;
    [Tooltip("★1007 優先分數：每等待 1 秒扣多少公尺的分，避免排很久的鳥餓死。0＝純粹看距離")]
    public float agingMetersPerSecond = 2f;

    [Tooltip("★1008 開局時若存檔裡的排程數值是舊版本，就自動換成程式裡最新的建議值（Inspector 存的舊值會蓋掉程式預設，這是上次放行名額一直不夠的原因）。勾選＝完全照你 Inspector 的數字，不再自動換")]
    public bool keepMyInspectorValues = false;
    [HideInInspector] public int tuningVersion = 0;
    private const int CurrentTuningVersion = 3;

    [Header("★1006 Debug（預設關閉；事件才印 log，沒有每幀 log）")]
    [Tooltip("開啟後印 [BIRD DETECT]／[QUEUED]／[BLOCKED-WIND]／[BLOCKED-SHADOW]／[SLOT]／[LOCK]／[WARNING]／[DIVE]／[HIT]／[RETREAT]／[CANCEL]／[REMOVED]，" +
             "並在每次重生與離開場景時印 [BIRD STATS]（偵測→排隊→放行→俯衝→命中 各階段轉換率）")]
    public bool enableBirdAttackDebug = false;

    [Header("★1005 全部鳥統一套用（場景裡每隻鳥各自存了舊值，改這裡一次生效）")]
    [Tooltip("偵測範圍 (公尺)。玩家進入這個水平距離，鳥就建立攻擊需求。場景原本存 10。0＝不覆蓋，沿用每隻鳥自己的值")]
    public float birdDetectionRange = 14f;
    [Tooltip("預判時間上限 (秒)。預判時間＝前搖＋俯衝飛行時間，場景存的 1.2 秒比實際到達時間短太多。0＝不覆蓋")]
    public float birdMaxPredictionTime = 3f;
    [Tooltip("預判落點離玩家最遠幾公尺。場景存的 7 偏小。0＝不覆蓋")]
    public float birdPredictionDistanceLimit = 14f;
    [Tooltip("（只影響示範俯衝等不走排程器的鳥）排隊太久就放棄的秒數")]
    public float birdMaxQueueWaitSeconds = 8f;
    [Tooltip("★1009 被鳥俯衝命中一次就重生（0904 定案「鳥維持殺死」）。關閉＝改成只被逼退（1001 的實驗做法）。石化硬撐、護盾、無敵、演出鎖定、示範俯衝（harmless）的既有豁免不受影響")]
    public bool birdHitRespawnsPlayer = true;
    [Tooltip("★1013 重生後，存檔點之前（玩家已經過了）還沒攻擊過的鳥是否還會從身後攻擊。取消＝只盤旋不攻擊（預設）。攻擊過的鳥一律永久消失、不刷新")]
    public bool birdsBehindCheckpointCanAttack = false;
    [Tooltip("★1012 最後一座掩體之後的鳥，不受風起風停限制（風吹時也會放行攻擊）。掩體區是「躲風＋躲鳥」，掩體之後只剩「躲鳥」。取消勾選＝全場都受風限制")]
    public bool birdsIgnoreWindAfterLastShelter = true;
    [Tooltip("★1012 不受風限制的起點 = 最後一座掩體的 x + 這個數字（公尺）。想讓更早的鳥也不受限制就填負數")]
    public float windFreeMargin = 0f;

    [Header("★1010 預判準度（微調）")]
    [Tooltip("預判攻擊中，打「剛好攔截點」的比例 (0～1)。0.7＝七成很準、三成隨便。想更難調高，想更好躲調低")]
    [Range(0f, 1f)] public float predictionPreciseChance = 0.7f;
    [Tooltip("「很準」的攻擊相對攔截點再多（正）或少（負）幾公尺，沿玩家前進方向。0＝剛好；+1＝比剛好再前面 1 公尺")]
    public float predictionPreciseOffset = 0f;
    [Tooltip("「寬鬆」攻擊的偏移範圍下限 (公尺)，負＝落在她身後")]
    public float predictionLooseMin = -5f;
    [Tooltip("「寬鬆」攻擊的偏移範圍上限 (公尺)，正＝落在她前面")]
    public float predictionLooseMax = 5f;
    [Tooltip("俯衝命中玩家的距離 (公尺)，原本固定 1.1。稍微加大＝「剛好又稍微多一點打到」；不建議超過 2")]
    public float birdHitRadius = 1.4f;

    [Header("★1015 垂直慢速墜落（開局統一覆蓋每隻鳥）")]
    [Tooltip("下降階段垂直速度 (公尺/秒)。0＝不覆蓋。3＝慢速彈幕，想更慢 2.5、更快 4")]
    public float birdDescentSpeed = 3f;
    [Tooltip("下降前水平對準的最高速度 (公尺/秒)。0＝不覆蓋。不建議超過 10")]
    public float birdAlignSpeed = 7f;
    [Tooltip("是否顯示既有的紅色攻擊輔助線。企劃決定先不做提示，預設取消勾選（所有鳥統一覆蓋）")]
    public bool birdShowTelegraph = false;

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

        if (!keepMyInspectorValues && tuningVersion < CurrentTuningVersion)
        {
            Debug.Log($"[DesertBeatDirector] 排程數值是舊版本（v{tuningVersion}），已換成最新建議值：v3 含「超出範圍的鳥也繼續排隊攻擊」（保留範圍 3 倍、預判上限 5 秒／30 公尺）；間隔 {slotIntervalSeconds}→0.2、每輪空檔 {restAfterRoundSeconds}→0.4、每輪 {attacksPerRound}→4 次、同時上限 {maxSimultaneousAttacks}→6（壓力加成最多到 10）。想用自己的數字請勾 Keep My Inspector Values。");
            slotIntervalSeconds = 0.2f;
            restAfterRoundSeconds = 0.4f;
            attacksPerRound = 4;
            maxSimultaneousAttacks = 6;
            maxSimultaneousHard = 10;
            queuePressureStep = 5;
            behindPenaltyMeters = 8f;
            agingMetersPerSecond = 2f;
            // v3：偵測到但已經超出範圍的鳥（被玩家甩在身後）也繼續留在佇列、照常放行，不再 1.5 倍範圍就取消
            requestKeepRangeMultiplier = 3f;
            birdMaxPredictionTime = 5f;
            birdPredictionDistanceLimit = 30f;
            tuningVersion = CurrentTuningVersion;
        }

        System.Text.StringBuilder log = new System.Text.StringBuilder();
        log.Append("[DesertBeatDirector] 套用四拍（").Append(beat1End).Append("／").Append(beat2End).Append("／").Append(beat3End).Append("）：");

        BirdAttackScheduler.debugEnabled = enableBirdAttackDebug;
        IndividualBirdEnemy.BehindCheckpointBirdsCanAttack = birdsBehindCheckpointCanAttack;
        BirdAttackScheduler.attackPattern = attackPattern;
        BirdAttackScheduler.slotInterval = slotIntervalSeconds;
        BirdAttackScheduler.attacksPerRound = attacksPerRound;
        BirdAttackScheduler.restSeconds = restAfterRoundSeconds;
        BirdAttackScheduler.maxSimultaneous = maxSimultaneousAttacks;
        BirdAttackScheduler.maxSimultaneousHard = maxSimultaneousHard;
        BirdAttackScheduler.queuePressureStep = queuePressureStep;
        BirdAttackScheduler.maxSameZoneInRow = maxSameZoneInRow;
        BirdAttackScheduler.zoneHalfWidth = zoneHalfWidth;
        BirdAttackScheduler.requestKeepRangeMultiplier = requestKeepRangeMultiplier;
        BirdAttackScheduler.behindPenaltyMeters = behindPenaltyMeters;
        BirdAttackScheduler.agingMetersPerSecond = agingMetersPerSecond;

        ApplyShelters(log);
        ApplyWindFreeZone(log);
        ApplyBirds(log);

        if (enableGiantShadow) { GiantShadowPass.Install(giantShadowX); log.Append(" 鳥影@").Append(giantShadowX).Append('；'); }
        if (enableWindStop)    { WindStopZone.Install(beat3End);      log.Append(" 風停@").Append(beat3End).Append('；'); }
        if (enableRelics)      { DesertRelics.Install();               }
        if (enableTelegraphHum){ WindTelegraphHum.Install();           log.Append(" 前兆低鳴；"); }
        if (enableBraceFrost)  { BraceFrostFX.Install();               log.Append(" 硬撐結霜；"); }

        Debug.Log(log.ToString());
    }

    private void ApplyWindFreeZone(System.Text.StringBuilder log)
    {
        IndividualBirdEnemy.WindFreeFromX = float.PositiveInfinity;
        if (!birdsIgnoreWindAfterLastShelter) return;
        float last = float.NegativeInfinity;
        foreach (WindShelter ws in FindObjectsByType<WindShelter>(FindObjectsSortMode.None))
        {
            if (ws != null && ws.transform.position.x > last) last = ws.transform.position.x;
        }
        if (float.IsNegativeInfinity(last)) { log.Append(" 找不到掩體，鳥全場受風限制；"); return; }
        IndividualBirdEnemy.WindFreeFromX = last + windFreeMargin;
        log.Append(" 最後一座掩體 x=").Append(last.ToString("F1")).Append("，x>").Append((last + windFreeMargin).ToString("F1")).Append(" 的鳥不受風限制；");
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

        if (enableBirdAttackDebug) AuditBirds(birds);

        float demoX = ResolveDemoDiveX();
        int removed1 = 0, thinned = 0, beat2Kept = 0, staggered = 0, removed4 = 0;
        int beat2Index = 0, beat3Index = 0;
        IndividualBirdEnemy demoBird = null;
        IndividualBirdEnemy demoNearest = null;
        float demoNearestDist = float.MaxValue;

        foreach (IndividualBirdEnemy b in birds)
        {
            float x = b.transform.position.x;

            // ★1005 統一套用偵測範圍／預判參數／排隊上限（場景裡每隻鳥存的是舊值，改鳥身上沒用）
            if (birdDetectionRange > 0f) b.detectionRange = birdDetectionRange;
            if (birdMaxPredictionTime > 0f) b.maximumPredictionTime = birdMaxPredictionTime;
            if (birdPredictionDistanceLimit > 0f) b.predictionDistanceLimit = birdPredictionDistanceLimit;
            if (birdMaxQueueWaitSeconds > 0f) b.maxQueueWaitSeconds = birdMaxQueueWaitSeconds;
            b.retreatInsteadOfKill = !birdHitRespawnsPlayer;   // 場景裡每隻鳥存的是 true（逼退），統一由導演決定
            b.predictionPreciseChance = predictionPreciseChance;
            b.predictionPreciseOffset = predictionPreciseOffset;
            b.predictionLooseMin = predictionLooseMin;
            b.predictionLooseMax = predictionLooseMax;
            b.hitRadius = birdHitRadius;
            if (birdDescentSpeed > 0f) b.diveDescentSpeed = birdDescentSpeed;
            if (birdAlignSpeed > 0f) b.diveAlignSpeed = birdAlignSpeed;
            b.showAttackTelegraph = birdShowTelegraph;

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

        // ★1006 鳥的分類（一次講清楚哪些是會攻擊的鳥、哪些是裝飾）
        int totalComps = FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None).Length;
        int flocks = FindObjectsByType<ScatteredFlock>(FindObjectsSortMode.None).Length;
        int flockBirds = 0;
        foreach (ScatteredFlock f in FindObjectsByType<ScatteredFlock>(FindObjectsSortMode.None)) if (f != null) flockBirds += f.BirdCount;
        int legacyOffset = 0;
        foreach (IndividualBirdEnemy b in birds) if (b != null && b.behaviorType == BirdBehavior.PlayerOffset) legacyOffset++;
        log.Append(" 分類：攻擊鳥（IndividualBirdEnemy）").Append(birds.Count).Append(" 隻（含子層重複元件共 ").Append(totalComps)
           .Append(" 個）；裝飾鳥群（ScatteredFlock，不攻擊）").Append(flocks).Append(" 團共 ").Append(flockBirds)
           .Append(" 隻；舊版 PlayerOffset 型 ").Append(legacyOffset).Append(" 隻（預設已改走統一的定點／預判）；");
    }

    /// <summary>
    /// ★1008 逐隻檢查鳥的設定（Director 覆寫之前的「原始存檔值」）。只在 enableBirdAttackDebug 時印一次：
    /// 每個欄位列出「多數值」與「異常的鳥」，另外檢查位置重疊、間距、高度、缺少元件。
    /// </summary>
    private void AuditBirds(List<IndividualBirdEnemy> birds)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("[BIRD AUDIT] 攻擊鳥 ").Append(birds.Count).Append(" 隻（依 x 排序）。欄位為 Director 覆寫前的原始值。\n");

        AuditField(sb, birds, "detectionRange", b => b.detectionRange.ToString("F1"));
        AuditField(sb, birds, "warningDuration", b => b.warningDuration.ToString("F2"));
        AuditField(sb, birds, "diveSpeed(legacy)", b => b.diveSpeed.ToString("F1"));
        AuditField(sb, birds, "diveDescentSpeed", b => b.diveDescentSpeed.ToString("F1"));
        AuditField(sb, birds, "diveAlignSpeed", b => b.diveAlignSpeed.ToString("F1"));
        AuditField(sb, birds, "behaviorType", b => b.behaviorType.ToString());
        AuditField(sb, birds, "triggerMode", b => b.triggerMode.ToString());
        AuditField(sb, birds, "autoDetectPlayer", b => b.autoDetectPlayer.ToString());
        AuditField(sb, birds, "harmless", b => b.harmless.ToString());
        AuditField(sb, birds, "overrideTarget", b => b.overrideTarget != null ? b.overrideTarget.name : "null");
        AuditField(sb, birds, "enableRoutePrediction", b => b.enableRoutePrediction.ToString());
        AuditField(sb, birds, "maximumPredictionTime", b => b.maximumPredictionTime.ToString("F1"));
        AuditField(sb, birds, "predictionDistanceLimit", b => b.predictionDistanceLimit.ToString("F0"));
        AuditField(sb, birds, "retreatInsteadOfKill", b => b.retreatInsteadOfKill.ToString());
        AuditField(sb, birds, "showAttackTelegraph", b => b.showAttackTelegraph.ToString());
        AuditField(sb, birds, "enableIdleHover", b => b.enableIdleHover.ToString());
        AuditField(sb, birds, "scale", b => b.transform.lossyScale.x.ToString("F2"));
        AuditField(sb, birds, "y(高度)", b => b.transform.position.y.ToString("F0"));
        AuditField(sb, birds, "z", b => b.transform.position.z.ToString("F1"));

        // 位置：重疊、間距、空洞
        int overlaps = 0;
        float minGap = float.MaxValue;
        int holes = 0;
        for (int i = 0; i < birds.Count; i++)
        {
            for (int j = i + 1; j < birds.Count; j++)
            {
                float dxx = birds[j].transform.position.x - birds[i].transform.position.x;
                if (dxx > 0.3f) break;                       // 已排序，往後只會更遠
                float d = Vector2.Distance(birds[i].transform.position, birds[j].transform.position);
                if (d < 0.3f) { overlaps++; sb.Append("  重疊：").Append(birds[i].name).Append(" 與 ").Append(birds[j].name).Append(" 距離 ").Append(d.ToString("F2")).Append("m @x=").Append(birds[i].transform.position.x.ToString("F1")).Append('\n'); }
            }
            if (i > 0)
            {
                float gap = birds[i].transform.position.x - birds[i - 1].transform.position.x;
                if (gap < minGap) minGap = gap;
                if (gap > 12f) { holes++; sb.Append("  空洞：x=").Append(birds[i - 1].transform.position.x.ToString("F1")).Append(" → ").Append(birds[i].transform.position.x.ToString("F1")).Append("（無鳥 ").Append(gap.ToString("F1")).Append("m）\n"); }
            }
        }
        sb.Append("  位置：重疊 ").Append(overlaps).Append(" 組、相鄰最小間距 ").Append(minGap.ToString("F2")).Append("m、大於 12m 的空洞 ").Append(holes).Append(" 處\n");

        // 元件
        int noRb = 0, noAnim = 0, inactive = 0, noCol = 0;
        foreach (IndividualBirdEnemy b in birds)
        {
            if (!b.gameObject.activeInHierarchy) { inactive++; sb.Append("  未啟用：").Append(b.name).Append('\n'); }
            if (b.GetComponent<Rigidbody>() == null) noRb++;
            if (b.GetComponentInChildren<Animator>() == null) noAnim++;
            if (b.GetComponentInChildren<Collider>() == null) noCol++;
        }
        sb.Append("  元件：無 Rigidbody ").Append(noRb).Append("（執行時會自動補）、無 Animator ").Append(noAnim).Append("、無 Collider ").Append(noCol).Append("、未啟用 ").Append(inactive).Append('\n');

        // 每隻一行（x 排序）
        sb.Append("  逐隻：name | x | y | 偵測 | 前搖 | 下降速 | 對準速 | legacy俯衝速 | behavior\n");
        foreach (IndividualBirdEnemy b in birds)
        {
            Vector3 p = b.transform.position;
            sb.Append("  ").Append(b.name).Append(" | ").Append(p.x.ToString("F1")).Append(" | ").Append(p.y.ToString("F1")).Append(" | ")
              .Append(b.detectionRange.ToString("F0")).Append(" | ").Append(b.warningDuration.ToString("F1")).Append(" | ")
              .Append(b.diveDescentSpeed.ToString("F1")).Append(" | ").Append(b.diveAlignSpeed.ToString("F1")).Append(" | ")
              .Append(b.diveSpeed.ToString("F0")).Append(" | ").Append(b.behaviorType).Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    private static void AuditField(System.Text.StringBuilder sb, List<IndividualBirdEnemy> birds, string label, System.Func<IndividualBirdEnemy, string> sel)
    {
        Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();
        foreach (IndividualBirdEnemy b in birds)
        {
            string v = sel(b);
            List<string> l;
            if (!groups.TryGetValue(v, out l)) { l = new List<string>(); groups[v] = l; }
            l.Add(b.name);
        }
        string mode = null; int modeCount = -1;
        foreach (var kv in groups) if (kv.Value.Count > modeCount) { mode = kv.Key; modeCount = kv.Value.Count; }
        sb.Append("  ").Append(label).Append(": 多數=").Append(mode).Append("（").Append(modeCount).Append(" 隻）");
        if (groups.Count > 1)
        {
            sb.Append(" ★有不同值：");
            foreach (var kv in groups)
            {
                if (kv.Key == mode) continue;
                sb.Append(kv.Key).Append("→").Append(kv.Value.Count).Append(" 隻[");
                for (int i = 0; i < kv.Value.Count && i < 6; i++) sb.Append(i > 0 ? "," : "").Append(kv.Value[i]);
                if (kv.Value.Count > 6) sb.Append("…");
                sb.Append("] ");
            }
        }
        sb.Append('\n');
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
