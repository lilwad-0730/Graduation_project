using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;

/// <summary>
/// 影子怪物（MonsterMutant7_Run1）主控制器。
/// 
/// 【動畫控制】
///   - 走路：walk4
///   - 遠距離奔跑：run2
///   - 吃到燭火受擊：gethit3 (播放受擊動態並「漸漸變小」)
///   - 捕獲玩家攻擊：attack2 (動態快結束時觸發重生)
/// 
/// 【狀態流程】
///   Dormant → (觸發點) → Appearing (Alpha漸顯登場) → Chasing → (全部燭火) → Vanishing (勝利)
///                                                            → (最後燭火未全集) → Punishing → Devouring → 重生→Dormant
/// </summary>
public class ShadowMonsterController : MonoBehaviour, IResettable
{
    // ──────────── Singleton ────────────
    public static ShadowMonsterController Instance { get; private set; }

    // ──────────── 狀態機 ────────────
    public enum MonsterState { Dormant, Appearing, Chasing, Punishing, Devouring, Vanishing }

    [Header("【狀態監控 (勿手動修改)】")]
    public MonsterState currentState = MonsterState.Dormant;

    // ──────────── Inspector 設定 ────────────
    [Header("目標設定")]
    [Tooltip("玩家 Transform（留空時自動透過 Tag 或 PlayerMovement 腳本尋找）")]
    public Transform player;

    [Header("燭火清單 (⚠ 請手動拖曳所有燭火物件到此陣列)")]
    public CandleCollectible[] candles;

    [Header("★ 結局：收完最後一根燭火就進結局")]
    [Tooltip("收完所有燭火後是否直接進結局（原本掛在鏡牆演出上，已改到這裡）")]
    public bool playEndingAfterAllCandles = true;

    [Tooltip("進結局前要不要再播一張卡。M5 已在鏡牆演出後播過，這裡預設留空＝不重播")]
    public string endingCardId = "";

    [Tooltip("結局要載入的場景（繪本）")]
    public string endingBookScene = "Book";

    [Tooltip("★1118d 怪物倒下後，畫面淡成全黑的秒數（沒有文字卡時才用這個淡黑，取代碎裂轉場）")]
    public float endingFadeOutSeconds = 1.5f;
    [Tooltip("★1118d 全黑之後、繪本載入完再多停幾秒才開始淡入")]
    public float endingBlackHoldSeconds = 0.5f;
    [Tooltip("★1118d 繪本從全黑淡入的秒數")]
    public float endingFadeInSeconds = 1.5f;

    [Header("★ 登場運鏡：先看怪物，再回到主角")]
    [Tooltip("觸發追逐時，先把鏡頭帶到怪物身上演一段，再把鏡頭交還主角")]
    public bool useRevealCutscene = true;

    [Tooltip("登場運鏡整場遊戲只演一次。追逐段的存檔點就在觸發器前面 1 個單位，不勾的話每次死亡重生走兩步就要重看一次六秒演出，期間完全不能動")]
    public bool revealOnlyOnce = true;

    [Tooltip("鏡頭飛去怪物、以及飛回主角，各自等多久（留時間給 Cinemachine 的阻尼追上）")]
    public float revealCameraTravel = 1.2f;

    [Tooltip("鏡頭飛回主角時，畫面中心離主角在這個左右距離內就把控制權還給玩家（不用等滿上面的秒數）")]
    public float revealReleaseDistance = 1.0f;

    [Tooltip("怪物完全浮現之後、開始移動之前的停頓")]
    public float revealHoldBeforeMove = 0.4f;

    [Tooltip("怪物開始移動後，鏡頭還要停在牠身上看多久")]
    public float revealWatchMoveSeconds = 0.9f;

    [Tooltip("鏡頭對準點的微調。對準點預設取全身 Renderer 的中心（怪物 Pivot 在腳底，直接看 Pivot 會對到地板下面）")]
    public Vector2 revealFocusOffset = Vector2.zero;

    [Tooltip("演出期間暫時把 Cinemachine 的位置阻尼調小，鏡頭才追得上。場景預設是 1，太黏會飛不到怪物身上就被叫回來。設負數＝不動它")]
    public float revealCameraDamping = 0.4f;

    [Tooltip("演出時把鏡頭拉遠讓整隻怪物進畫面，留多少邊。1.2＝上下各留 20%。這隻怪物約 45 單位高、鏡頭只看得到 27 單位，不拉遠只會看到牠的肚子。設 0＝不拉遠")]
    public float revealZoomPadding = 1.2f;

    [Tooltip("鏡頭要退到怪物背面多遠。怪物很厚（前後約 29 單位）又擺在 z=-13.2，相機只在焦點後方 15 單位，近裁面會直接切進牠身體裡，畫面上就是破圖、看得到內部")]
    public float revealDepthMargin = 3f;

    [Header("★ 開場距離：怪物要離主角多遠")]
    [Tooltip("觸發追逐時，把怪物擺到主角後方固定距離，玩家才有跑的空間。關掉＝用怪物在場景裡擺的位置")]
    public bool repositionOnActivate = true;

    [Tooltip("怪物出現在主角後方幾個世界單位。鏡頭一次看得到 48 單位寬，所以 45 大約是一個畫面外")]
    public float spawnDistanceBehindPlayer = 45f;

    [Header("★ 追逐速度智慧調整")]
    [Tooltip("開啟後怪物會依落後距離自動調速，維持在理想距離帶內。關掉＝回到原本的 chaseSpeed／runSpeedMultiplier 二段式")]
    public bool smartChaseSpeed = true;

    [Tooltip("理想落後距離的下限。比這個近，怪物會放慢腳步（要大於 attackTriggerDistance，不然牠會直接貼上來）")]
    public float idealLagMin = 16f;

    [Tooltip("理想落後距離的上限。比這個遠，怪物會加速追上來")]
    public float idealLagMax = 26f;

    [Tooltip("落後很多時允許的最高速度。要大於玩家速度（預設 5）牠才追得回來，也才走得到後面的燭火")]
    public float catchUpMaxSpeed = 7.5f;

    [Tooltip("貼太近時降到的速度")]
    public float easeOffSpeed = 2.2f;

    [Tooltip("速度變化的平滑度，每秒最多變化多少。太大會忽快忽慢")]
    public float speedSmooth = 3f;

    [Header("★ 縮小與燭火判定")]
    [Tooltip("怪物的 Pivot 在腳底，直接縮放會整隻沉到玻璃地板下面、也就吃不到燭火了。開啟後縮放時會補償 Y，讓視覺中心維持在同一個高度")]
    public bool keepVisualCenterHeight = true;

    [Tooltip("燭火判定的水平範圍")]
    public float candleReachX = 6f;

    [Tooltip("燭火判定的垂直額外寬容量（實際判定＝怪物目前半高 ＋ 這個值）")]
    public float candleReachYMargin = 3f;

    [Tooltip("★1119 以怪物「身體最前緣」碰到已點亮的燭台就算受傷（★1119b 只算軀幹＋頭，見 candleHitTorsoOnly）。\n關掉＝舊行為：要等身體中心離燭台 candleReachX 以內，手已經伸過燭台十公尺左右才觸發")]
    public bool candleHitByFrontEdge = true;
    [Tooltip("★1119 前緣離燭台中心多近算碰到（公尺）。約等於燭台本體的半寬；0＝手要伸到燭台中心線，調大＝更早觸發")]
    public float candleFrontTouchMargin = 0.4f;
    [Tooltip("★1119 只看燭台這個高度範圍內的身體（燭台底部往下／燭台頂端往上各多少公尺）。從燭台下方或上方經過的手不算碰到")]
    public float candleFrontYMargin = 0.5f;
    [Tooltip("★1119 燭台本體的高度（公尺，從燭台的 Pivot 往上算），用來決定判定的高度範圍")]
    public float candleBodyHeight = 3f;

    [Header("⚔️ 揮爪攻擊距離自由微調 (Attack Distance Settings)")]
    [Tooltip("怪物登場出現時，是否同步朝玩家追擊？")]
    public bool chaseWhileAppearing = true;
    [Tooltip("一般追逐速度（走路狀態）")]
    public float chaseSpeed = 3.5f;
    [Tooltip("懲罰階段追逐速度")]
    public float punishChaseSpeed = 4.8f;
    [Tooltip("★【起手距離】：黑影怪在此距離時會停止跑步、高舉雙爪開始揮擊 (預設 6.0，數值越小越靠近才出招)")]
    public float attackTriggerDistance = 6.0f;
    [Tooltip("★【命中傷害距離】：爪子揮下時，主角必須在此距離內才會真正受到傷害死亡 (預設 4.8)")]
    public float clawHitDistance = 4.8f;
    [Tooltip("垂直高度容許差 (預設 12.0)")]
    public float verticalCatchTolerance = 12.0f;

    [Header("自動貼地與隱形地板設定")]
    [Tooltip("是否開啟自動射線貼地 (讓怪物隨地形/隱形地板平滑貼合)")]
    public bool enableGroundSnap = true;
    [Tooltip("地面與專屬隱形地板的 LayerMask (預設 Everything)")]
    public LayerMask groundLayers = ~0;
    [Tooltip("向下偵測地面的最大射線距離 (預設 25)")]
    public float groundRaycastDistance = 25.0f;
    [Tooltip("腳底貼地高度微調偏移")]
    public float feetOffsetY = 0.0f;

    [Header("MonsterMutant7_Run1 動作動畫設定")]
    [Tooltip("待機動畫名稱 (預設 idle1)")]
    public string idleAnimationName = "idle1";
    [Tooltip("走路追擊動畫名稱 (預設 walk4)")]
    public string walkAnimationName = "walk4";
    [Tooltip("遠距離奔跑動畫名稱 (預設 run2)")]
    public string runAnimationName = "run2";
    [Tooltip("吃到燭火受擊動畫名稱 (預設 gethit3)")]
    public string hitAnimationName = "gethit3";
    [Tooltip("捕獲玩家攻擊動畫名稱 (預設 attack2)")]
    public string attackAnimationName = "attack2";

    [Tooltip("一般動作之間切換的混合時間 (秒)，0＝直接硬切")]
    public float animCrossFade = 0.2f;
    [Tooltip("受擊動作接回走路／跑步的混合時間 (秒)。會提前這麼久開始混合，讓受擊最後幾格和移動動作疊在一起")]
    public float hitExitCrossFade = 0.45f;

    [Header("🎵 怪物追逐專屬背景音樂 (Chase BGM)")]
    [Tooltip("怪物追逐時播放的專屬背景音樂 (例如 玻璃館_追逐_loop.wav 或 EyesInTheVoid.mp3，留空時自動載入 玻璃館_追逐_loop)")]
    public AudioClip chaseBGM;
    [Range(0f, 1f)]
    [Tooltip("追逐音樂音量 (預設 0.85)")]
    public float chaseBGMVolume = 0.85f;
    [Tooltip("追逐音樂淡入時間 (秒，預設 1.0)")]
    public float chaseBGMFadeInDuration = 1.0f;
    [Tooltip("怪物死亡/消散時追逐音樂淡出時間 (秒，預設 1.5)")]
    public float chaseBGMFadeOutDuration = 1.5f;
    [Tooltip("空間 2D/3D 混合 (0 = 全景環繞電影級BGM，1 = 純3D定位音效，建議 0.15 保持音樂清晰且具方向感)")]
    [Range(0f, 1f)]
    public float spatialBlend = 0.15f;
    [Tooltip("★1119b 怪物每撞到（吃掉）一根燭火，追逐音樂的基準音量再降掉原本的幾成：第 1 根→80%、第 2 根→60%、第 3 根→40%、第 4 根→20%（0.2＝每根降 20%）。和逼近緊張度、喘息壓低、熄燈壓低相乘。最後一根（倒下）另外淡出到 0")]
    [Range(0f, 0.5f)] public float chaseBgmStepPerCandle = 0.2f;
    [Tooltip("★1119b 撞到燭火後，追逐音樂降到新音量要花幾秒（平滑下降，不跳）")]
    public float chaseBgmStepRampSeconds = 1.0f;
    [Tooltip("★1119b 最後一根燭火倒下時，追逐音樂淡出時間的下限（實際淡出長度＝倒下動畫剩餘時間，讓音樂和動畫、最後一句語音一起結束）")]
    public float finaleBgmFadeMinSeconds = 1.5f;
    [Tooltip("★1119b 倒下動畫的播放速度範圍：動畫會被拉快／放慢到剛好和最後一句語音一起結束，超出這個範圍就夾住（會在 Console 提示）")]
    public Vector2 finaleDeathSpeedRange = new Vector2(0.5f, 1.6f);

    [Header("💡 音效進階增強 (Proximity & Victory)")]
    [Tooltip("當怪物極度逼近主角時 (距離 < 8米)，是否自動增強追逐音樂緊張度/音量")]
    public bool enableProximityTension = true;
    [Tooltip("怪物全收集死亡消散時播放的解脫勝利氛圍音效 (例如 玻璃館_追逐_脫離.wav)")]
    public AudioClip victoryReliefSFX;

    [Header("🎵 怪物音效設定 (Monster SFX)")]
    [Tooltip("怪物登場 / 巨影過頂音效 (例如 水下_巨影過頂.wav)")]
    public AudioClip appearSFX;
    [Tooltip("怪物追擊咆哮 / 獸吼音效 (例如 玻璃館_怪物吼叫_01.wav, 獸吼.mp3)")]
    public AudioClip roarSFX;
    [Tooltip("怪物揮爪攻擊音效 (例如 獸吼.mp3)")]
    public AudioClip attackSFX;
    [Tooltip("抓到主角受擊音效 (例如 玻璃館_被抓住.wav)")]
    public AudioClip catchPlayerSFX;
    [Tooltip("吃到燭火受擊縮小音效 (例如 玻璃館_降級_1色散 / 2顫抖 / 3暈眩)")]
    public AudioClip hitSFX;
    [Tooltip("漏吃燭火懲罰送回音效 (例如 玻璃館_送回上一盞.wav)")]
    public AudioClip punishTeleportSFX;
    [Tooltip("玩家全收集勝利、怪物消散脫離音效 (例如 玻璃館_追逐_脫離.wav)")]
    public AudioClip vanishSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.95f;

    [Header("動態追逐與距離設定")]
    [Tooltip("與玩家X距離大於此門檻時，切換為奔跑動畫 run2（預設30）")]
    public float runDistanceThreshold = 30.0f;
    [Tooltip("奔跑狀態時的速度倍率")]
    public float runSpeedMultiplier = 1.35f;

    [Header("玩家減速設定（黑影怪物靠近時）")]
    [Tooltip("與玩家X距離小於此值時，玩家移動速度會被降低")]
    public float slowdownDistance = 5.0f;
    [Tooltip("減速倍率（0.3 = 降為原速的 30%）")]
    [Range(0.05f, 1.0f)]
    public float slowdownFactor = 0.3f;
    [Tooltip("開始追逐後幾秒才會啟動 run2（確保一開始是 walk4，預設4秒）")]
    public float runActivationDelay = 4.0f;

    [Header("轉場與變小時間設定")]
    [Tooltip("怪物漸漸透明顯示登場的時間（秒）")]
    public float appearDuration = 2.0f;
    [Tooltip("怪物勝利後漸漸消失的時間（秒）")]
    public float vanishDuration = 2.0f;
    [Tooltip("吃到燭火後平滑漸漸變小的持續時間（秒）")]
    public float smoothShrinkDuration = 1.2f;

    [Header("★1118 結局：怪物倒下（death1）")]
    [Tooltip("最後一根燭火點完後：她自動走上右邊的觀景台、轉身看著怪物，怪物播 death1 倒下、淡出，然後才接結局漫畫。關掉＝回到原本的縮小消散")]
    public bool deathFinale = true;
    [Tooltip("死亡動畫的狀態名（要在 Animator Controller 裡）")]
    public string deathAnimationName = "death1";
    [Tooltip("她自動走到燭火右邊幾公尺停下、轉身看怪物（觀景台上）")]
    public float finaleWatchOffsetX = 12f;
    [Tooltip("死亡動畫播完後多停幾秒再淡出")]
    public float deathHoldSeconds = 0.8f;
    [Tooltip("怪物倒下後淡出的秒數")]
    public float deathFadeSeconds = 1.2f;
    [Tooltip("★1118c 倒下時把鏡頭往下拉遠，框住她和倒下的怪物（怪物站在橋下的深坑，倒下去會躺到畫面外）")]
    public bool deathFrameCamera = true;
    [Tooltip("★1119c 倒下後：肢體與身體逐漸碎成粒子 → 聚成白色光球 → 飛向鏡頭蓋滿畫面變全白 → 從白淡入結局漫畫（MonsterDisintegrateFinale 調細節）。關掉＝倒下後直接淡黑")]
    public bool crumbleToOrbFinale = true;
    [Tooltip("★1119e 倒下後先播結局漫畫（疊加載入，玻璃館留在背景不動），翻完回玻璃館再碎裂成光球；光球飛向鏡頭全白後直接捲名單。關掉＝倒下後直接碎裂、全白後才進漫畫")]
    public bool comicBeforeCrumble = true;
    [Tooltip("★1118c 往左（身後）倒下，會躺到離怪物原點多遠（怪物站著高度的倍數）")]
    public float finaleFallReach = 1.15f;
    [Tooltip("★1118c 倒下鏡頭最多拉到多大（正交半高）")]
    public float finaleMaxOrthoSize = 30f;
    [Tooltip("懲罰時光暈增大的漸變時間（秒）")]
    public float punishHaloGrowDuration = 1.5f;

    [Header("燭火縮放設定")]
    [Range(0.05f, 0.5f)]
    [Tooltip("每收集一根燭火，怪物縮小的比例（0.15 = 15%）")]
    public float shrinkPerCandle = 0.15f;
    [Range(0.1f, 0.6f)]
    [Tooltip("怪物縮小的下限，避免縮到消失（0.2 = 最小為原本的 20%）")]
    public float minScaleMultiplier = 0.2f;
    [Tooltip("★1118b 開著＝吃燭火不再縮小，改成牠多出來的四隻手臂一根燭火掉一隻（由下到上、先左後右，掉完剩原本兩隻手）；關掉＝照舊縮小")]
    public bool shedArmsInsteadOfShrink = true;

    [Header("光暈設定")]
    [Tooltip("光暈基礎半徑/倍率")]
    public float haloBaseRadius = 3.5f;
    [Tooltip("光暈視覺顏色（暗灰紅光環）")]
    public Color haloColor = new Color(0.95f, 0.25f, 0.25f, 0.75f);
    [Tooltip("點光源顏色")]
    public Color lightColor = new Color(1.0f, 0.2f, 0.2f, 1f);
    [Tooltip("點光源強度")]
    public float lightBaseIntensity = 8.0f;
    [Tooltip("點光源影響範圍")]
    public float lightRange = 15.0f;
    [Tooltip("懲罰時光暈增大倍率")]
    public float punishHaloMultiplier = 1.2f;

    [Header("恐懼懲罰設定")]
    [Range(0.1f, 0.9f)]
    [Tooltip("懲罰期間玩家速度倍率")]
    public float fearSpeedMultiplier = 0.5f;
    [Range(0.1f, 0.9f)]
    [Tooltip("懲罰期間玩家跳躍力倍率")]
    public float fearJumpMultiplier = 0.65f;

    [Header("★1013 最後一根燭火：突然衝刺嚇人（不攻擊）")]
    [Tooltip("只剩最後一根燭火，而且「主角」離它這麼近（公尺）時，怪物開始把距離追回來")]
    public float finaleDashTriggerDistance = 22f;
    [Tooltip("距離拉很開時的最高追趕速度（公尺/秒）。主角約 5，設 7 左右是慢慢逼近；越接近目標距離會自動越慢")]
    public float finaleDashSpeed = 7f;
    [Tooltip("追趕速度每秒最多增加多少（公尺/秒²）。越小起速越緩，沒有暴衝感")]
    public float finaleAccel = 1.5f;
    private float _finaleSpeed = -1f;
    private float _finalePrevPx = float.NaN, _finalePlayerV = 5f;
    [Tooltip("★1118e 最後一根：她站住時，怪物走到身後 finaleStopDistance 之前的減速度（公尺/秒²），不再從全速一幀硬停")]
    public float finaleStopDecel = 12f;
    private float _finaleApproach = -1f;   // ★1118e 最後一根點燭火時的固定逼近速度
    [Tooltip("追到主角身後多遠停下來（公尺）。要大於 attackTriggerDistance 以外都安全；這段期間不會攻擊、也不會減速主角")]
    public float finaleStopDistance = 4f;
    [Tooltip("離目標距離多遠以內開始減速收尾（公尺）")]
    public float finaleEaseDistance = 20f;
    private bool _finaleDash;
    private bool _vanishDone;

    [Header("★1102 最後一根燭火：等玩家點完、卡關保險")]
    [Tooltip("最後一根還沒點亮時，怪物停在離它多遠（公尺，已含 candleReachX）。不然玩家點到一半怪物就碰到，燭火被打碎卡死")]
    public float finaleWaitMargin = 1.5f;
    [Tooltip("玩家走過最後一根燭火多遠（公尺）還沒點，就放怪物衝過去，照「漏點」流程懲罰")]
    public float finalePassMargin = 2.5f;
    [Tooltip("最後衝刺最多持續幾秒。超過就強制定案（點亮算收集、沒點算漏掉），避免怪物一直跟著、關卡卡死")]
    public float finaleMaxSeconds = 25f;
    private float _finaleStartTime;

    [Header("★1016 重生後：不在黑幕裡出場，等她動了才從身後出現")]
    [Tooltip("重生點就在觸發區旁邊，重生的黑幕期間她一被放回去就踩到觸發區，怪物在黑幕裡就出場開追，畫面一亮牠已經貼上來。\n" +
             "打開＝黑幕期間先不出場，等重生完成、她開始移動（或等 respawnAppearMaxWait 秒）才從她身後 spawnDistanceBehindPlayer 公尺出現。\n" +
             "關掉＝原本（黑幕裡就出場）。第一次進玻璃館不受影響（照常演登場運鏡）")]
    public bool deferActivationDuringRespawn = true;

    [Tooltip("重生完成後她一直不動，最多等幾秒怪物就出場")]
    public float respawnAppearMaxWait = 4f;

    [Tooltip("她開始移動之後，再等幾秒怪物才出場（給一點起跑的空間）")]
    public float respawnAppearDelayAfterMove = 0.6f;

    [Header("★1016 追逐的耐心：她一直在往前，就只跟在身後")]
    [Tooltip("打開＝她最近 chasePatienceSeconds 秒內有往前進，怪物最近只跟到她身後 hoverLag 公尺，不再逼近、不出爪；\n" +
             "她停下來（或往回走）超過這個時間，怪物才逼近出爪。等移動平台（最久約 6.4 秒）、上下輸送平台都夠用。\n" +
             "關掉＝原本（停下來等平台就會被追上）")]
    public bool holdBackWhilePlayerProgressing = true;

    [Tooltip("耐心期間怪物離她最近幾公尺（要大於 attackTriggerDistance，不然會直接出爪）")]
    public float hoverLag = 16f;

    [Tooltip("她多久沒往前進，怪物就不再等、逼近出爪（秒）")]
    public float chasePatienceSeconds = 9f;

    [Tooltip("往前超過幾公尺才算「有往前進」")]
    public float progressStep = 0.5f;

    [Header("★1031 點燭火時怪物不停下（10/29 老師：要有怪物的壓迫感）")]
    [Tooltip("打開＝她在點燭火（按 S、進度條在跑）的時候，怪物不停在身後等，照樣朝她逼近；點完才回到原本的跟隨。\n" +
             "關掉＝原本（她停下來點燭火時，怪物也停在身後 hoverLag 公尺）")]
    public bool keepChasingWhileLighting = true;

    [Tooltip("點燭火期間怪物用多快逼近（公尺/秒，固定，不受遠近加速影響）。點一根約 1.5 秒：3.2＝約走近 5 公尺。\n" +
             "★1031 審查：原本取「平常追速」與此值的較大者，16 公尺外平常追速約 5.3、落後更遠最多 7.5，點一根會被拉近 8～11 公尺，連點兩根就容易被抓；改成固定值，好預期也好調。")]
    public float lightingPressureSpeed = 3.2f;

    private CandleAuraGlow[] _candleAuras;

    [Header("★1102 怪物靠近時不停下（M 11/2：「怪物靠近玩家時會停下來的機制，改成不要停」）")]
    [Tooltip("打開＝怪物永遠不站著不動。她一直往前走：照舊跟在身後 hoverLag 公尺（跟著她走，不逼近）；她停下來、等平台、往回走：不再停在身後等，而是以 Creep Speed 慢慢逼近，超過 chasePatienceSeconds 沒前進才全速逼近；\n" +
             "怪物已經進到 hoverLag 以內時也不會定住，她往前走就照「太近放慢」的速度跟著（距離慢慢拉回 hoverLag），她一停就繼續慢慢逼近。點燭火期間照 ★1031 的固定速度逼近。\n" +
             "關掉＝1016 的做法（她一停，怪物停在身後 hoverLag 公尺站著等 9 秒）")]
    public bool neverStopNearPlayer = true;

    [Tooltip("她沒在前進時怪物慢慢逼近的速度（公尺/秒）。等移動平台最久約 6.4 秒：1.2×6.4≈7.7 公尺，從 16 公尺逼到約 8 公尺，還沒到 6 公尺的出爪距離；站著不動約 8 秒就會被出爪")]
    public float creepSpeed = 1.2f;

    private float _playerPrevX = float.NaN;
    private float _playerForwardSpeed = 0f;   // ★1102 她這一幀往「遠離怪物」方向的速度（公尺/秒），往回走算 0

    /// <summary>★1031 她現在是不是正在點某一根燭火（任何一根的進度條在跑）。</summary>
    private bool IsPlayerLightingCandle()
    {
        if (candles == null || candles.Length == 0) return false;
        if (_candleAuras == null || _candleAuras.Length != candles.Length)
        {
            _candleAuras = new CandleAuraGlow[candles.Length];
            for (int i = 0; i < candles.Length; i++)
                _candleAuras[i] = candles[i] != null ? candles[i].GetComponent<CandleAuraGlow>() : null;
        }
        for (int i = 0; i < _candleAuras.Length; i++)
            if (_candleAuras[i] != null && _candleAuras[i].IsLighting) return true;
        return false;
    }
    [Header("★1102 難度與節奏：悄悄調速，不讓怪物停住放水")]
    [Tooltip("打開＝點燭火時的安全網：照 ★1031 怪物照樣逼近，但預估她被放開時怪物會離她不到 safeLagAtRelease 公尺，才悄悄壓速度到剛好夠；本來夠遠就完全不干預。關掉＝只剩 ★1031 的固定逼近速度")]
    public bool smartPacing = true;

    [Tooltip("追逐速度整體倍率（只影響追逐／登場／最後衝刺，不含懲罰）。<1＝比原本慢")]
    public float difficultyScale = 0.9f;

    [Tooltip("追逐時速度上限（公尺/秒）。玩家約 5，6 左右追得回來但不會一直暴衝")]
    public float maxChaseSpeed = 6.2f;

    [Tooltip("玩家被放開（點完燭火）那一刻，怪物最少還離她幾公尺。要大於 attackTriggerDistance + 一段起跑空間")]
    public float safeLagAtRelease = 12f;

    [Tooltip("放開後多算幾秒（她起跑加速的時間）")]
    public float postLightGrace = 0.8f;

    [Tooltip("調節時最低速度 ＝ 玩家速度 × 這個值。不會降到 0，所以不會看起來停住")]
    [Range(0.1f, 1f)] public float paceMinSpeedFactor = 0.35f;

    [Tooltip("調節速度每秒最多變化多少（公尺/秒²）。越小越看不出來在調")]
    public float paceAccel = 2.5f;

    [Tooltip("★1104 預先拉開：她接近還沒點的燭火時，怪物提前、很緩地把距離拉開到「點完剛好還安全」，而不是點燭火那一刻才突然掉速。這是開始預備的距離（公尺），0＝關閉")]
    public float anticipateDistance = 40f;

    [Tooltip("預先拉開時，怪物最多比她慢多少（公尺/秒）。1.6 以內肉眼看不出在放慢")]
    public float anticipateMaxSlow = 1.6f;

    [Tooltip("預估點一根燭火她被鎖住多久（秒）")]
    public float anticipateLockSeconds = 2.6f;

    private float _paceCap = -1f;
    private float _paceLastCapAt = -999f;
    private bool _paceBusyPrev;
    private float _paceBusyStartLag;
    private float _paceBusyStartAt;
    private float _paceMinLag = float.PositiveInfinity;
    /// <summary>探針用：怪物目前落後距離、調節後速度、是否在調節。</summary>
    public float DebugLag { get; private set; }
    public float DebugSpeed { get; private set; }
    public string DebugPace { get; private set; } = "-";

    // ══════════════════════════════════════════════════════════════════════════
    // ★1116 追逐節奏導演（M 11/16：拿掉耐心機制／hoverLag，改成有起伏的節奏）
    //   Root Cause：耐心機制（她一直往前就只跟在 16 公尺外）＋智慧調速＋預先拉開＋點燭安全網＋難度九折疊在一起，
    //   只要她一直往前走，程式上就保證抓不到——真正會死只有掉下去、站著超過 9 秒、漏點燭火，怪物只是跟著。
    // 做法：一條「該離她多遠」的曲線取代上面那幾層（只管追逐／登場，懲罰照舊）：
    //   ・距離隨關卡進度從 directorLagStart 收緊到 directorLagEnd（後半段明顯更緊）
    //   ・她停下、等平台、往回走：過了 stallGraceSeconds 秒，距離每秒縮短 stallCloseRate 公尺，縮到揮爪距離就被抓（沒有 9 秒耐心）
    //   ・她一直往前時距離最近只到「揮爪距離 + directorMinMovingLag」：跑著不會被抓，但很近
    //   ・點燭火時她被鎖住：★1119 怪物維持「開始點那一刻的速度」（至少 lightingPressureSpeed）照樣追，不放慢、不等她；離太近就會被抓
    //   ・追逐撲擊（★1118e）：走到 nearMissBeatsX（另有隨機）時怪物照事先算好的路徑猛撲，她一直跑就停在 ~12 公尺外，停下就會被抓
    //   ・燭火反擊：吃到燭火被光震退、追逐音樂與畫面壓迫放鬆幾秒
    //   ・漏點燭火：當下吼一聲、長大一點、逼近幾秒
    // 關掉 usePacingDirector＝回到原本那幾層。
    [Header("★1116 追逐節奏導演（取代耐心機制／hoverLag／預先拉開）")]
    [Tooltip("打勾＝用下面的節奏曲線控制怪物距離（拿掉耐心機制、hoverLag、預先拉開、點燭安全網）。取消＝原本的做法")]
    public bool usePacingDirector = true;
    [Tooltip("關卡開頭（第一根燭火）怪物離她多遠（公尺）")]
    public float directorLagStart = 20f;
    [Tooltip("關卡結尾（最後一根燭火）怪物離她多遠（公尺）。要大於揮爪距離（場景裡是 10）")]
    public float directorLagEnd = 13.5f;
    [Tooltip("她一直往前走時，怪物最近能到「揮爪距離 + 這個值」（公尺）。跑著不會被抓，但很近")]
    public float directorMinMovingLag = 1.5f;
    [Tooltip("她停下來多久之後開始逼近（秒）")]
    public float stallGraceSeconds = 1f;
    [Tooltip("她停著的時候，怪物每秒多逼近幾公尺。1.6：開頭停約 7 秒、結尾停約 3 秒會被抓")]
    public float stallCloseRate = 1.6f;
    [Tooltip("★1119 她停著時怪物主動加速撲上來：每秒多快多少（公尺/秒²）。取代原本照距離比例、越靠近越慢的爬行（看起來像在等她）")]
    public float stallChaseAccel = 1.5f;
    [Tooltip("★1119 她停著時怪物撲上來的最高速度（公尺/秒）")]
    public float stallChaseMaxSpeed = 5f;
    [Tooltip("距離差 1 公尺，速度差多少（公尺/秒）。越大越緊跟曲線")]
    public float directorGain = 0.9f;
    [Tooltip("怪物最低速度（公尺/秒）：永遠不站著不動")]
    public float directorMinSpeed = 1f;
    [Tooltip("怪物最高速度（公尺/秒）")]
    public float directorMaxSpeed = 8f;
    [Tooltip("怪物加速／減速（公尺/秒²）")]
    public float directorAccel = 4f;

    // ★1118e 追逐撲擊（取代 ★1116 的「撲到 attack+1.8 公尺就硬停、揮空氣、而且那段時間不抓人」）
    //   Root Cause：舊版撲過來後用 MoveTowards 在固定距離硬停，而且撲＋揮期間 CheckCatch 直接略過——
    //   看起來就是「怪物故意在碰到她之前停下來」。
    //   新版：觸發那一刻就在世界座標算好整條路徑（起跑→加速→巡航→煞車→追擊收尾），
    //   終點 X_end ＝ 她「照目前速度一直跑」到煞車結束時的位置往後 endLag 公尺；
    //   路徑固定、不看她之後在哪（沒有靠近就煞車），也沒有免疫：她一直跑＝終點離她約 12 公尺（險些被抓），
    //   她停下或往回走＝怪物照走完整條路，CheckCatch 正常抓人。
    [Header("★1116 險些被抓／★1118e 追逐撲擊")]
    [Tooltip("打勾＝她走到下面這些 X 時，怪物猛撲一次（真的會抓人）。也會有隨機的額外撲擊（見下面）")]
    public bool useNearMissBeats = true;
    [Tooltip("固定撲擊的位置（世界 X）。預設在每兩根燭火的中間")]
    public float[] nearMissBeatsX = new float[] { 20f, 101f, 181.5f, 263f };
    [Tooltip("撲擊結束後交還導演時的喘息強度（公尺，÷ candleReliefLag 換算成 0～1）。讓距離慢慢重新拉開")]
    public float nearMissRecoverLag = 6f;

    [Tooltip("★1118e 預告秒數：吼一聲＋撲擊音效，速度維持現在的速度（絕不站著）")]
    public float lungeTelegraphSeconds = 0.3f;
    [Tooltip("★1118e 撲擊最高速度（公尺/秒）")]
    public float lungePeakSpeed = 12f;
    [Tooltip("★1118e 加速度（公尺/秒²）。★1119 從約 6 加到 12 約 0.2 秒、1.9 公尺（原 45/16）")]
    public float lungeAccel = 30f;
    [Tooltip("★1118e 煞車減速度（公尺/秒²）。★1119 12 → 3.5 約 0.6 秒、4.7 公尺（原 20/16）；煞車開始就播 attack2 揮爪，讓減速看起來是為了出爪而踩穩")]
    public float lungeBrakeDecel = 14f;
    [Tooltip("★1118e 煞車結束時的速度＝她的速度 × 這個值（≈4.2 公尺/秒），之後繼續往前不停")]
    [Range(0.3f, 1f)] public float lungeEndSpeedFactor = 0.7f;
    [Tooltip("★1118e 終點離她多遠＝揮爪距離 + 這個值（公尺）。★1119 4＝爪子落在她身後約 4 公尺、煞車結束約離她 14 公尺（原 2）")]
    public float lungeEndExtraLag = 4f;
    [Tooltip("★1118e 煞車之後以煞車結束速度繼續往前走幾秒，才交還給導演")]
    public float lungeRecoverySeconds = 1.5f;
    [Tooltip("★1118e 她往前的速度（平滑後）至少多少（公尺/秒）才會撲。她沒在跑，預測不準，不撲")]
    public float lungeMinPlayerSpeed = 3f;
    [Tooltip("★1118e 落後距離超過這個就不撲（公尺）")]
    public float lungeMaxLag = 60f;
    [Tooltip("★1118e 打勾＝固定位置之外，隨機再撲幾次")]
    public bool useRandomLunges = true;
    [Tooltip("★1118e 隨機額外撲擊：離上一次撲擊（含固定位置）至少隔幾秒（最短）")]
    public float randomLungeCooldownMin = 70f;
    [Tooltip("★1118e 隨機額外撲擊：離上一次撲擊至少隔幾秒（最長）。每次在最短～最長之間隨機")]
    public float randomLungeCooldownMax = 100f;
    [Tooltip("★1118e 前方這麼多公尺以內有還沒點的燭火就不撲（公尺）")]
    public float lungeNoCandleAhead = 20f;

    [Header("★1116 燭火反擊（吃到燭火的回饋）")]
    [Tooltip("吃到燭火時被光震退幾公尺")]
    public float candleKnockback = 9f;
    [Tooltip("震退花幾秒")]
    public float candleKnockbackSeconds = 0.7f;
    [Tooltip("震退之後幾秒內多離她幾公尺（喘息）")]
    public float candleReliefLag = 8f;
    [Tooltip("喘息持續幾秒（慢慢回到原本的距離）")]
    public float candleReliefSeconds = 5f;
    [Tooltip("喘息時追逐音樂最多降多少（0.5＝降一半）")]
    [Range(0f, 1f)] public float candleReliefBgmDuck = 0.5f;
    [Tooltip("燭火爆出的光有多亮")]
    public float candleBurstIntensity = 40f;
    [Tooltip("燭火爆光的範圍（公尺）")]
    public float candleBurstRange = 30f;

    [Header("★1116 漏點燭火的當下反應")]
    [Tooltip("漏點時怪物逼近幾公尺（幾秒內慢慢恢復）")]
    public float missedSurgeLag = 6f;
    [Tooltip("逼近持續幾秒")]
    public float missedSurgeSeconds = 5f;
    [Tooltip("漏點一根，怪物長大多少（跟吃到燭火縮小同一個單位）")]
    public float missedRegrow = 0.15f;

    // 導演的內部狀態
    private float _dirSpeed = -1f;
    private float _playerFwdSmooth;
    private float _firstCandleX = float.NaN;
    private float _reliefStart = -999f, _reliefAmount;
    private float _surgeStart = -999f;
    private float _knockStart = -999f, _knockDir;
    private float _knockDone, _knockDist;
    private bool[] _beatDone;
    private int _beatTriedIdx = -1;    // ★1118e 已到位置但還在等條件（她停下、點燭火中…）的固定撲擊
    // ★1118e 撲擊狀態：0＝沒有、1＝預告、2＝加速、3＝巡航、4＝煞車＋揮爪、5＝收尾
    private int _lungePhase;
    private float _lungePhaseStart;
    private float _lungeSide;                          // 她在怪物哪一邊（+1＝右）
    private float _lungeV;                             // 現在的撲擊速度
    private float _lungeV0, _lungeVEnd, _lungePlayerV; // 起跑速度、煞車結束速度、規劃時她的速度
    private float _lungeXBrake, _lungeXEnd, _lungeXStart;
    private float _lungeLag0, _lungeMinLag, _lungePlanLen;
    private string _lungeTag = "";
    private float _lungeLast = -999f;                  // 上一次撲擊（開始）的時間
    private float _lungeRandomWait = -1f;              // 下一次隨機撲擊要隔多久
    private float _approachSpeed = -1f;                // ★1119 點燭火鎖定期間怪物維持的速度＝開始點的那一刻的速度（至少 lightingPressureSpeed）（<0＝這次還沒定）
    private static AudioClip _roarClip, _lungeClip;

    /// <summary>★1118e 撲擊進行中（預告／加速／巡航／煞車／收尾）。</summary>
    public bool IsLunging => _lungePhase != 0;
    /// <summary>★1119 現在碰到她會不會被抓（給子彈時間判斷「快被抓到」用）。</summary>
    public bool CanCatchNow => !_revealActive && !_finaleDash &&
        (currentState == MonsterState.Chasing || currentState == MonsterState.Punishing || currentState == MonsterState.Appearing);
    /// <summary>★1118e 撲擊階段：0＝沒有、1＝預告、2＝加速、3＝巡航、4＝煞車＋揮爪、5＝收尾。</summary>
    public int LungePhase => _lungePhase;
    /// <summary>★1118e 撲擊現在的速度（公尺/秒）。</summary>
    public float LungeSpeed => _lungeV;

    /// <summary>★1116 給鏡頭用：追逐框景現在要不要作用（1＝作用、0＝回到原本、-1＝別碰，例如登場運鏡或懲罰鎖鏡頭中）。</summary>
    public int ChaseFramingMode
    {
        get
        {
            if (_revealActive || _revealCamTaken || IsRevealRunning || _camLocked) return -1;
            if (player == null) return 0;
            return (currentState == MonsterState.Chasing || currentState == MonsterState.Appearing || currentState == MonsterState.Punishing) ? 1 : 0;
        }
    }
    /// <summary>★1116 她離怪物多遠（公尺，水平）。</summary>
    public float ChaseDistance => player != null ? Mathf.Abs(player.position.x - transform.position.x) : 999f;
    /// <summary>★1116 怪物在她哪一邊（−1＝左、+1＝右）。</summary>
    public float MonsterSide => player != null && transform.position.x < player.position.x ? -1f : 1f;
    /// <summary>★1116 吃到燭火後的喘息（0～1）。</summary>
    public float Relief01 => Decay01(_reliefStart, candleReliefSeconds) * Mathf.Clamp01(_reliefAmount);
    /// <summary>★1116 漏點後的逼近（0～1）。</summary>
    public float Surge01 => Decay01(_surgeStart, missedSurgeSeconds);
    /// <summary>★1116 給 ShadowMonsterDread：畫面壓迫的倍率（喘息時放鬆、漏點時加重）。</summary>
    public float DreadScale => Mathf.Clamp(1f - 0.6f * Relief01 + 0.35f * Surge01, 0.3f, 1.35f);

    private static float Decay01(float start, float seconds)
    {
        float t = (Time.time - start) / Mathf.Max(0.1f, seconds);
        if (t < 0f || t >= 1f) return 0f;
        return 1f - t * t * (3f - 2f * t);   // 先撐住、再慢慢退
    }

    [Header("★1016 追逐中途存檔點")]
    [Tooltip("打開＝她站上下面這些位置之後，死掉就從那裡重生（不用從玻璃館起點重追 340 公尺），\n" +
             "怪物等她動了再從她身後出現；已經在怪物身後、牠吃不到的燭火算牠吃過。\n" +
             "關掉＝原本（每次都回到起點、燭火全部復原）")]
    public bool useChaseCheckpoints = true;

    [Tooltip("中途存檔點（世界座標 X, Y；Y 會由重生系統往下貼地）。預設：過了移動平台、過了上下輸送平台、翻過高台玻璃牆")]
    public Vector2[] chaseCheckpoints = new Vector2[]
    {
        new Vector2(55f, -34.5f),
        new Vector2(142.5f, -34.5f),
        new Vector2(229.5f, -34.5f),
    };

    [Header("重生系統參考")]
    [Tooltip("留空時自動尋找場景中的 PlayerRespawnSystem")]
    public PlayerRespawnSystem respawnSystem;

    [Header("【視覺地表錨點補償設定】")]
    [Tooltip("允許的最大世界 Y 軸向上補償值 (防止極端情況下怪物浮空)")]
    public float maxWorldVisualYOffset = 22.0f;

    [Header("【燭火精準接觸收集設定】")]
    [Tooltip("怪物收集燭火的物理觸發寬度 (X 軸米數，緊貼前胸/嘴巴接觸線，杜絕提前觸發)")]
    public float candleTriggerWidth = 0.8f;
    [Tooltip("怪物收集燭火的物理觸發高度 (Y 軸米數，涵蓋路面與燭火高度)")]
    public float candleTriggerHeight = 4.0f;

    // ──────────── 內部狀態 ────────────
    private int _candlesCollected = 0;

    [Header("★1021 沒點燃的燭火（漏掉）")]
    [Tooltip("怪物走過最後一根燭火、但燭火數量沒湊齊（有漏點）時，過了這麼多秒玩家還沒被抓到，就強制讓怪物抓人、重生。")]
    public float doomedRespawnSeconds = 15f;
    private bool _doomed = false;
    private Coroutine _doomRoutine;

    /// <summary>★1021 現在有幾根燭火「漏掉」（直接數燭火的狀態，重生時燭火一刷新就自動重算，不另存計數）。</summary>
    private int MissedCount()
    {
        int n = 0;
        if (candles != null) foreach (var c in candles) if (c != null && c.missed) n++;
        return n;
    }
    /// <summary>★1102 直接數燭火的狀態，不靠累加的計數。重生、被懲罰中收集等情況下累加計數可能少算，少一根就永遠通不了關。</summary>
    private int RecountCollected()
    {
        int n = 0;
        if (candles != null) foreach (var c in candles) if (c != null && c.isCollected) n++;
        return n;
    }

    private int _totalCandles = 0;
    private CandleCollectible _lastCandleByX;

    private Vector3 _initialPosition;
    private Vector3 _baseScale;
    private float _currentScaleMultiplier = 1f;
    private bool _isHitShrinking = false;
    private float _hitAnimLockUntil = 0f;   // 受擊動畫播完的時間點，之前不切回移動動畫
    private float _chaseTimer = 0f;  // 追逐計時，用於延遲 run2 啟動

    // ──────────── 解耦架構 (Decoupled Visual & Gameplay) ────────────
    private Transform _visualContainer;
    private Transform _gameplayContainer;
    private Transform _candleTriggerTransform;
    private float _surfaceReferenceY;
    private float _submergedDepth;

    // 光暈組件
    private SpriteRenderer _haloSpriteRenderer;
    private ParticleSystem _haloPs;
    private Light _haloLight;

    // 視覺與動畫組件快取
    private Renderer[] _renderers;
    private Color[] _originalColors;
    private MaterialPropertyBlock _mpb;
    private Animator _animator;

    // 玩家組件快取
    private PlayerMovement _pm;
    private float _origSpeed;
    private float _origJumpForce;
    private bool _fearActive = false;

    // 相機鎖定
    private Camera _mainCam;
    private Transform _camLockDummy;
    private bool _camLocked = false;
    private List<CinemachineCamera> _vcams3 = new List<CinemachineCamera>();
    private List<CinemachineVirtualCamera> _vcamsLegacy = new List<CinemachineVirtualCamera>();
    private List<Transform> _origFollow = new List<Transform>();

    // 登場運鏡（跟 LockCamera 分開記錄，免得兩邊互相蓋掉還原值）
    private bool _revealActive = false;          // 整段演出期間為 true：不准抓人
    /// <summary> ★給 PlayerMovement 用：登場運鏡期間要硬鎖玩家，按鍵不能自己解鎖。 </summary>
    public static bool IsRevealRunning = false;
    private static bool _revealPlayed = false;   // 這場遊戲已經演過登場運鏡了
    private bool _revealCamTaken = false;
    private Transform _revealFocus;              // 鏡頭要看的點（怪物視覺中心）
    private List<CinemachineCamera> _revealVcams3 = new List<CinemachineCamera>();
    private List<CinemachineVirtualCamera> _revealVcamsLegacy = new List<CinemachineVirtualCamera>();
    private List<Transform> _revealOrigFollow = new List<Transform>();
    private List<CinemachineFollow> _revealFollows = new List<CinemachineFollow>();
    private List<Vector3> _revealOrigDamping = new List<Vector3>();
    private List<Vector3> _revealOrigFollowOffset = new List<Vector3>();
    private List<float> _revealOrigLens = new List<float>();
    private Bounds _visualLocalBounds = new Bounds(Vector3.zero, Vector3.one);
    private bool _visualBoundsMeasured = false;
    private float _visualAnchorY = 0f;      // 縮放時要維持的視覺中心高度
    private float _smoothedChaseSpeed = -1f;
    private float _revealTargetLens = 0f;
    private bool _revealZoomEnabled = false;

    /// <summary> 全域查詢：怪物登場演出中 </summary>
    public static bool IsAnyRevealRunning => Instance != null && (Instance._revealActive || Instance._revealCamTaken);

    // ★1016 重生後延後出場／耐心／中途存檔點
    private bool _activationPending = false;
    private bool _chaseEverActivated = false;   // 這個場景裡追逐開始過（重生後要不要自己重新開追）
    private Coroutine _deferCoroutine;
    private int _chaseCheckpointIndex = -1;   // 已經站上的最遠一個中途存檔點（-1＝還沒有）
    private float _progressMaxX = float.NegativeInfinity;
    private float _lastProgressTime = 0f;

    // Coroutine 追蹤
    private Coroutine _stateCoroutine;
    private Coroutine _hitShrinkCoroutine;
    private Coroutine _chaseBgmFadeCoroutine;

    // 追逐專屬 AudioSource
    private AudioSource _chaseAudioSource;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity 生命週期
    // ──────────────────────────────────────────────────────────────────────────

    private bool _isInitialized = false;

    public void EnsureInitialized()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        if (Instance == null) Instance = this;

        _baseScale = transform.localScale;
        if (_baseScale == Vector3.zero || Mathf.Abs(_baseScale.x) < 0.01f)
        {
            _baseScale = Vector3.one;
        }
        _initialPosition = transform.position;
        EnsureVisualBounds();
        _visualAnchorY = _initialPosition.y + _visualLocalBounds.center.y * Mathf.Abs(_baseScale.y);

        // 1. 地表基準線 (Surface Reference Anchor) 與沉降深度計算
        if (candles != null && candles.Length > 0 && candles[0] != null)
        {
            _surfaceReferenceY = candles[0].transform.position.y;
        }
        else
        {
            _surfaceReferenceY = _initialPosition.y + 23.575f;
        }
        _submergedDepth = Mathf.Max(0.1f, _surfaceReferenceY - _initialPosition.y);

        // 2. 建立/配置獨立的 Visual Container
        SetupVisualContainer();

        // 3. 建立/配置獨立的 Gameplay 碰撞體容器與專屬 CandleCollectionTrigger
        SetupGameplayColliders();

        // 自動修正動畫名稱設定，確保目標為 walk4 與 run2
        if (string.IsNullOrEmpty(walkAnimationName) || walkAnimationName.Equals("Walk", System.StringComparison.OrdinalIgnoreCase))
            walkAnimationName = "walk4";
        if (string.IsNullOrEmpty(runAnimationName) || runAnimationName.Equals("Run", System.StringComparison.OrdinalIgnoreCase))
            runAnimationName = "run2";

        if (_mainCam == null) _mainCam = Camera.main;

        if (_animator == null) _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        // 自動檢查並確保 Avatar (3D骨骼繫結) 存在，解決動畫有執行但模型不動問題
        if (_animator != null && _animator.avatar == null)
        {
            Animator[] childAnimators = GetComponentsInChildren<Animator>(true);
            foreach (var a in childAnimators)
            {
                if (a != null && a.avatar != null)
                {
                    _animator.avatar = a.avatar;
                    break;
                }
            }
        }

        // 自動檢查並確保套用 AnimatorController
        if (_animator != null && _animator.runtimeAnimatorController == null)
        {
#if UNITY_EDITOR
            var ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/MonsterMutant 7/MonsterMutant7 Animator Controller.controller");
            if (ctrl != null) _animator.runtimeAnimatorController = ctrl;
#endif
        }

        // ★ 關鍵修復：強制 Always Animate，防止模型超出畫面時動畫凍結
        if (_animator != null)
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 快取視覺組件
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        // ★ 關鍵修復：強制 SkinnedMeshRenderer 在離屏/相機移動時持續更新 Bounds，徹底杜絕動畫骨骼形變被 Frustum Culling 裁切切面
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr != null) smr.updateWhenOffscreen = true;
        }

        if (_renderers != null)
        {
            _originalColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Material mat = _renderers[i].sharedMaterial;
                if (mat != null && mat.HasProperty("_BaseColor"))
                    _originalColors[i] = mat.GetColor("_BaseColor");
                else if (mat != null && mat.HasProperty("_Color"))
                    _originalColors[i] = mat.GetColor("_Color");
                else
                    _originalColors[i] = Color.white;
            }
        }
        else
        {
            _originalColors = new Color[0];
        }

        EnsurePlayerReference();

        if (respawnSystem == null)
            respawnSystem = FindFirstObjectByType<PlayerRespawnSystem>();

        SetupCandles();
        CreateHaloEffect();
        SetupChaseAudioSource();
        ApplyCurrentScale();
        EnsureCameraSafeDepth();

        // ★1116 追逐鏡頭：怪物在中距離時鏡頭偏向牠、越近越往外拉（只在追逐中作用）
        ShadowChaseCamera chaseCam = GetComponent<ShadowChaseCamera>();
        if (chaseCam == null) chaseCam = gameObject.AddComponent<ShadowChaseCamera>();
        chaseCam.ctrl = this;

        // ★1116 眼罩透出的紅光、最後一根燭火前的熄燈
        if (GetComponent<ShadowMonsterEyeGlow>() == null) gameObject.AddComponent<ShadowMonsterEyeGlow>();
        if (GetComponent<GlassFinaleBlackout>() == null) gameObject.AddComponent<GlassFinaleBlackout>();
        // ★1117 倒數第二根到最後一根燭火之間：碎橋衝刺
        if (GetComponent<GlassBridgeCollapse>() == null) gameObject.AddComponent<GlassBridgeCollapse>();
        // ★1118b 吃燭火不縮小，改成多出來的四隻手臂一隻一隻掉下來
        if (GetComponent<MonsterArmShedding>() == null) gameObject.AddComponent<MonsterArmShedding>();
    }

    /// <summary>
    /// 確保場景中所有 Cinemachine 相機在正常追逐時，景深 (FollowOffset.z) 退至 -70.0f。
    /// 由於怪物 3D 身軀巨大 (厚達 25m 以上，且面向右側時胸腔朝 -Z 突出至 Z ≈ -26m)，
    /// 若相機維持在 Z = -15m，Near Clip 切面 (Z = -14.9m) 會直接將怪物的胸腔剖切開洞！
    /// 將正交相機後移至 Z = -70m 可 100% 完整容納整隻 3D 巨獸，且正交視角下像素尺寸完全不變！
    /// </summary>
    public void EnsureCameraSafeDepth()
    {
        foreach (var cf in FindObjectsByType<CinemachineFollow>(FindObjectsSortMode.None))
        {
            if (cf != null && cf.FollowOffset.z > -60f)
            {
                cf.FollowOffset = new Vector3(cf.FollowOffset.x, cf.FollowOffset.y, -70.0f);
            }
        }
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (cam != null)
            {
                cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.1f);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 5000f);
            }
        }
    }

    private void SetupVisualContainer()
    {
        Transform existingVisual = transform.Find("MonsterVisual");
        if (existingVisual != null)
        {
            _visualContainer = existingVisual;
        }
        else
        {
            GameObject visualGo = new GameObject("MonsterVisual");
            visualGo.transform.SetParent(transform, false);
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localRotation = Quaternion.identity;
            visualGo.transform.localScale = Vector3.one;
            _visualContainer = visualGo.transform;

            List<Transform> childrenToMove = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == _visualContainer || child.name.StartsWith("Shadow_") || child.name.StartsWith("Gameplay_") || child.name.StartsWith("Candle_"))
                    continue;
                childrenToMove.Add(child);
            }
            foreach (var c in childrenToMove)
            {
                c.SetParent(_visualContainer, false);
            }
        }
    }

    private void SetupGameplayColliders()
    {
        Transform existingGameplay = transform.Find("GameplayColliders");
        if (existingGameplay == null)
        {
            GameObject gpGo = new GameObject("GameplayColliders");
            gpGo.transform.SetParent(transform, false);
            gpGo.transform.localPosition = Vector3.zero;
            gpGo.transform.localRotation = Quaternion.identity;
            gpGo.transform.localScale = Vector3.one;
            _gameplayContainer = gpGo.transform;
        }
        else
        {
            _gameplayContainer = existingGameplay;
        }

        Transform candleTriggerTrans = _gameplayContainer.Find("CandleCollectionTrigger");
        GameObject triggerGo;
        if (candleTriggerTrans == null)
        {
            triggerGo = new GameObject("CandleCollectionTrigger");
            triggerGo.transform.SetParent(_gameplayContainer, false);
            _candleTriggerTransform = triggerGo.transform;

            BoxCollider triggerCol = triggerGo.AddComponent<BoxCollider>();
            triggerCol.isTrigger = true;

            MonsterCandleTrigger proxy = triggerGo.AddComponent<MonsterCandleTrigger>();
            proxy.controller = this;
        }
        else
        {
            triggerGo = candleTriggerTrans.gameObject;
            _candleTriggerTransform = candleTriggerTrans;
        }

        BoxCollider col = triggerGo.GetComponent<BoxCollider>();
        if (col != null)
        {
            float parentScaleX = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x));
            float parentScaleY = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.y));
            float parentScaleZ = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.z));

            // 精準物理碰撞盒 (除以 Root Scale 轉換為 Local Size)：
            // X 軸：candleTriggerWidth (預設 0.8m，半寬僅 0.4m，緊貼怪物前胸/嘴巴接觸線)
            // Y 軸：candleTriggerHeight (預設 4.0m，涵蓋走道路面與燭火高度)
            // Z 軸：35.0m (徹底無視 2.5D 深度落差)
            col.size = new Vector3(
                candleTriggerWidth / parentScaleX,
                candleTriggerHeight / parentScaleY,
                35.0f / parentScaleZ
            );
        }

        UpdateGameplayCollidersPosition();
    }

    private void UpdateGameplayCollidersPosition()
    {
        if (_candleTriggerTransform != null)
        {
            // 永遠保持世界座標對齊，徹底消除怪物自身旋轉 (90°/270°) 造成的座標軸扭曲：
            // X = transform.position.x (嚴格隨怪物本體水平移動)
            // Y = _surfaceReferenceY + 1.8f (精準鎖定在走道路面燭火高度)
            // Z = 0f (鎖定在走道深度 Z=0，配合 35m 碰撞盒深度實現完美 2.5D 接觸)
            _candleTriggerTransform.position = new Vector3(transform.position.x, _surfaceReferenceY + 1.8f, 0f);
            _candleTriggerTransform.rotation = Quaternion.identity;
        }
    }

    void Awake()
    {
        // ★每次載入本場景都把結局旗標歸零：
        //   不然玩家「通關 → 回主選單 → 再玩一次」時（同一個 Play 期間不會重置 static），
        //   _endingFired 還留著 true，第二輪收完最後一根燭火就不會進結局了。
        _endingFired = false;
        IsRevealRunning = false;
        _revealPlayed = false;
        _chaseCheckpointIndex = -1;

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        EnsureInitialized();
    }

    void OnEnable()
    {
        PlayerRespawnSystem.OnResettablesReset += HandleResettablesReset;
    }

    void OnDisable()
    {
        PlayerRespawnSystem.OnResettablesReset -= HandleResettablesReset;
    }

    private int _assistCheckpoint = -99;
    private int _deathsAtCheckpoint = 0;

    /// <summary>★1125 連死協助等級：第 1 次死＝0（照原本），之後每多死一次 +1，最高 4。</summary>
    public int AssistLevel { get { return Mathf.Clamp(_deathsAtCheckpoint - 1, 0, 4); } }
    private float EffectiveMaxSpeed { get { return directorMaxSpeed * (1f - 0.07f * AssistLevel); } }
    private float EffectiveSpawnGap { get { return spawnDistanceBehindPlayer + 8f * AssistLevel; } }

    /// <summary>
    /// ★1016 重生的黑幕中、全場景重置完之後：如果追逐已經過了中途存檔點，
    /// 她會在存檔點重生（不會踩到起點的觸發區），由這裡安排「等她動了再從身後出場」。
    /// </summary>
    private void HandleResettablesReset()
    {
        if (Instance != this) return;
        if (currentState != MonsterState.Dormant) return;

        bool fromCheckpoint = useChaseCheckpoints && _chaseCheckpointIndex >= 0;

        // ★1125 連死放水：同一個存檔點（或同一段起點）連續死亡，每多死一次就多一級協助，換到新的存檔點才歸零。
        //   起因：測試時 #3 存檔點（x=229.5）一路死下去，怪物每次都從 45 公尺外用 8 m/s 衝來，最後一段還有固定撲擊，
        //   連死三次以上就像「永遠過不了關」。協助內容見 AssistLevel／EffectiveMaxSpeed／EffectiveSpawnGap。
        if (_chaseEverActivated)
        {
            if (_assistCheckpoint == _chaseCheckpointIndex) _deathsAtCheckpoint++;
            else { _assistCheckpoint = _chaseCheckpointIndex; _deathsAtCheckpoint = 1; }
            Debug.Log($"【影子怪物】★1125 在存檔點 #{_chaseCheckpointIndex + 1} 已死 {_deathsAtCheckpoint} 次，協助等級 {AssistLevel}（≥1 怪物慢一點、離遠一點；≥2 不撲擊）");
        }
        // ★1016 追逐開始過之後死掉：不靠觸發區重新開追。
        //   她如果是站在觸發區裡被抓（例如一開追就站著不動），重生點又在觸發區裡，
        //   她從頭到尾都「在觸發區裡面」，不會再有「進入」事件，怪物就永遠不會再出現（1016 實測）。
        if (!fromCheckpoint && !(deferActivationDuringRespawn && _chaseEverActivated)) return;

        Debug.Log(fromCheckpoint
            ? $"【影子怪物】★1016 從中途存檔點 #{_chaseCheckpointIndex + 1} 重生：等她醒來、開始移動，再從她身後出場"
            : "【影子怪物】★1016 追逐中死掉重生：等她醒來、開始移動，再從她身後出場");
        BeginDeferredActivation();
    }

    void Start()
    {
        EnsureInitialized();

        // 初始狀態：播放待機動畫，保持全尺寸但隱藏 Alpha 透明度
        PlayAnimationByName(idleAnimationName);
        SetVisualAlpha(0f);
        ApplyCurrentScale();
    }

    void Update()
    {
        EnsurePlayerReference();
        UpdateGameplayCollidersPosition();
        UpdateChaseCheckpoints();
        UpdateFinaleCam();   // ★1118

        switch (currentState)
        {
            case MonsterState.Appearing:
                // ★登場運鏡進行中：怪物先站著讓鏡頭看，不移動也不抓人
                if (chaseWhileAppearing && !_revealActive)
                {
                    MoveTowardPlayer(chaseSpeed);
                    CheckCatch();
                }
                if (enableProximityTension) UpdateProximityAudioTension();
                break;

            case MonsterState.Chasing:
                if (!_finaleDash) TryStartFinaleDash();
                MoveTowardPlayer(chaseSpeed);
                // ★演出還沒收尾（鏡頭還在飛回來、主角還被凍住）時不准抓人
                if (!_revealActive) CheckCatch();
                if (enableProximityTension) UpdateProximityAudioTension();
                break;

            case MonsterState.Punishing:
                MoveTowardPlayer(punishChaseSpeed);
                CheckCatch();
                if (_camLocked) ClampPlayerToCameraView();
                if (enableProximityTension) UpdateProximityAudioTension();
                break;
        }
    }

    /// <summary>
    /// 保留作為防呆介面 (主要由 CandleCollectionTrigger 物理觸發單一權威入口處理)
    /// </summary>
    private void CheckCandleCollisions()
    {
        if (candles == null || candles.Length == 0) return;

        foreach (var c in candles)
        {
            if (c != null && !c.isCollected && OverlapsCandle(c.transform.position))
            {
                c.Collect();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckCollisionCatch(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckCollisionCatch(collision.gameObject);
    }

    private void CheckCollisionCatch(GameObject go)
    {
        if (_revealActive) return;   // ★登場運鏡期間主角是凍住的，不能被吃
        if (_finaleDash) return;     // ★最後衝刺只嚇人，不攻擊
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Punishing && currentState != MonsterState.Appearing) return;
        if (go.CompareTag("Player") || go.GetComponent<PlayerMovement>() != null || go.name.ToLower().Contains("player"))
        {
            TriggerDevour();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 公開介面
    // ──────────────────────────────────────────────────────────────────────────

    public void ActivateChase()
    {
        if (currentState != MonsterState.Dormant) return;

        // ★1016 重生的黑幕期間（她一被放回重生點就踩到觸發區）先不出場，等她醒來、開始移動
        if (deferActivationDuringRespawn && (PlayerRespawnSystem.IsAnyRespawning || _activationPending))
        {
            BeginDeferredActivation();
            return;
        }

        ActivateChaseNow();
    }

    /// <summary>★1016 安排「重生完成 → 她開始移動（或等夠久）→ 出場」。重複呼叫只算一次。</summary>
    private void BeginDeferredActivation()
    {
        if (_activationPending) return;
        if (!isActiveAndEnabled) return;
        _activationPending = true;
        _deferCoroutine = StartCoroutine(DeferredActivationRoutine());
    }

    private IEnumerator DeferredActivationRoutine()
    {
        Debug.Log("【影子怪物】★1016 重生中：先不出場，等她醒來、開始移動");

        // 1. 等重生整段演完（黑幕、甦醒、解鎖）
        while (PlayerRespawnSystem.IsAnyRespawning) yield return null;

        // 2. 等她開始移動（重生系統偵測左右移動），最多等 respawnAppearMaxWait 秒
        float waited = 0f;
        while (!PlayerRespawnSystem.IsPlayerMovingAfterRespawn && waited < Mathf.Max(0f, respawnAppearMaxWait))
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // 3. 再給一點起跑的空間
        if (respawnAppearDelayAfterMove > 0f) yield return new WaitForSeconds(respawnAppearDelayAfterMove);

        _activationPending = false;
        _deferCoroutine = null;
        if (currentState == MonsterState.Dormant && !PlayerRespawnSystem.IsAnyRespawning)
        {
            ActivateChaseNow();
        }
    }

    private void ActivateChaseNow()
    {
        if (currentState != MonsterState.Dormant) return;
        _chaseEverActivated = true;

        // ★開場先把怪物擺到主角後方一段距離。
        //   場景裡牠只離觸發點 23 單位，而鏡頭一次就看得到 48 單位寬——
        //   等於一開始怪物就貼在臉上，沒有跑的空間。
        //   只改 X，Y／Z 保持美術擺好的樣子（牠是半身埋在玻璃地板下的巨影）。
        EnsurePlayerReference();
        _smoothedChaseSpeed = -1f; _paceCap = -1f; _paceBusyPrev = false;   // 每次重新開追都從當下的目標速度起步
        _dirSpeed = -1f; _playerPrevX = float.NaN; _playerFwdSmooth = 0f;   // ★1116
        _lungePhase = 0; _lungeLast = Time.time; _lungeRandomWait = -1f; _approachSpeed = -1f;   // ★1118e 撲擊冷卻從開追起算
        if (repositionOnActivate && player != null && EffectiveSpawnGap > 0f)
        {
            Vector3 sp = transform.position;
            sp.x = player.position.x - EffectiveSpawnGap;   // 主角往 +X 前進，怪物在 -X 那側（★1125 連死時離遠一點）
            transform.position = sp;
            Debug.Log($"【影子怪物】開場定位：主角 x={player.position.x:F1}，怪物擺到 x={sp.x:F1}（後方 {EffectiveSpawnGap} 單位、協助 {AssistLevel}）");
        }

        // ★1016 從中途存檔點重生時，怪物身後（牠再也走不到）的燭火算牠吃過，
        //   不然最後一根會被當成「沒吃齊」而進懲罰、結局永遠不會來。第一次開追時怪物身後沒有燭火，這行不影響。
        SilentlyCollectCandlesBehind(transform.position.x - candleReachX);

        // ★1016 耐心計時從開追這一刻算起
        _progressMaxX = player != null ? player.position.x : float.NegativeInfinity;
        _lastProgressTime = Time.time;

        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(AppearSequence());
    }

    /// <summary>
    /// ★1118 測試鍵 F2 用：跳過前面的劇情直接測碎橋衝刺。
    /// 除了最後一根以外的燭火都安靜算成已吃、略過登場運鏡，怪物立刻從她身後開追（已經在追就把怪物搬到她身後）。
    /// </summary>
    public void TesterSkipToFinalStretch()
    {
        if (candles == null) return;
        EnsurePlayerReference();

        foreach (var c in candles)
        {
            if (c == null || c.isCollected || c == _lastCandleByX) continue;
            c.MarkCollectedSilently();
        }
        _candlesCollected = RecountCollected();
        _currentScaleMultiplier = CandleScaleMult(_candlesCollected);   // ★1118b 開著掉手臂時維持原大小
        ApplyCurrentScale();
        SyncArms(false);

        _revealPlayed = true;   // 登場運鏡當作已經演過
        _chaseStepRamp = false;  // ★1119b 追逐音樂階梯直接對齊已吃的燭火數

        if (currentState == MonsterState.Dormant)
        {
            ActivateChaseNow();
        }
        else if (player != null)
        {
            Vector3 sp = transform.position;
            sp.x = player.position.x - spawnDistanceBehindPlayer;
            transform.position = sp;
        }
        Debug.Log($"【影子怪物】★1118 測試鍵：跳到最後一段，燭火算吃過 {_candlesCollected}/{_totalCandles}，狀態 {currentState}");
    }

    /// <summary>★1016 把 X 小於 limitX、還沒被吃的燭火，安靜地算成已吃（不播受擊、不觸發懲罰），並同步怪物的大小。</summary>
    private void SilentlyCollectCandlesBehind(float limitX)
    {
        if (candles == null) return;
        int added = 0;
        foreach (var c in candles)
        {
            if (c == null || c.isCollected) continue;
            if (c.transform.position.x >= limitX) continue;
            c.MarkCollectedSilently();
            added++;
        }
        if (added == 0) return;

        _chaseStepRamp = false;   // ★1119b 存檔點／靜默算吃：追逐音樂階梯直接對齊
        _candlesCollected = 0;
        foreach (var c in candles) if (c != null && c.isCollected) _candlesCollected++;
        _currentScaleMultiplier = CandleScaleMult(_candlesCollected);   // ★1118b
        ApplyCurrentScale();
        SyncArms(false);
        Debug.Log($"【影子怪物】★1016 怪物身後的 {added} 根燭火算吃過（共 {_candlesCollected}/{_totalCandles}），大小 ×{_currentScaleMultiplier:F2}");
    }

    /// <summary>
    /// ★1024 這根燭火在目前的中途存檔點前面＝玩家已經過關：重生時不刷新（維持已完成），並且算進收集數量。
    /// 存檔點只會在「前面的燭火都點燃或吃過」時才存（見 UpdateChaseCheckpoints），所以存檔點前面的燭火一定是完成的。
    /// 存檔點之後的燭火代表玩家失敗了：重生時照舊刷新、不算數。
    /// </summary>
    public bool IsCandleKeptByCheckpoint(CandleCollectible c)
    {
        if (c == null || !useChaseCheckpoints || chaseCheckpoints == null) return false;
        if (_chaseCheckpointIndex < 0 || _chaseCheckpointIndex >= chaseCheckpoints.Length) return false;
        return c.transform.position.x < chaseCheckpoints[_chaseCheckpointIndex].x;
    }

    /// <summary>★1016 追逐中她站上中途存檔點 → 更新重生點。</summary>
    private bool CandlesBeforeAreLitOrCollected(float x)
    {
        if (candles == null) return true;
        foreach (var c in candles)
        {
            if (c == null || c.transform.position.x >= x) continue;
            if (c.isCollected) continue;
            if (!c.missed && c.IsLitByPlayer) continue;
            return false;
        }
        return true;
    }

    private void UpdateChaseCheckpoints()
    {
        if (!useChaseCheckpoints || chaseCheckpoints == null || chaseCheckpoints.Length == 0) return;
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Appearing) return;
        if (player == null || _pm == null || !_pm.isGrounded) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;

        float px = player.position.x;
        for (int i = chaseCheckpoints.Length - 1; i > _chaseCheckpointIndex; i--)
        {
            if (px < chaseCheckpoints[i].x) continue;
            // ★1021 存檔點前面的燭火都要「已點燃或已吃」才存。漏點的燭火會擋住後面的存檔點，
            //   這樣重生一定回到漏點那根的前面、可以重來；不然怪物重生時會把存檔點前的燭火當成吃過（免費算數），或陷入注定失敗的重生迴圈。
            if (!CandlesBeforeAreLitOrCollected(chaseCheckpoints[i].x)) continue;
            _chaseCheckpointIndex = i;
            Vector3 cp = new Vector3(chaseCheckpoints[i].x, chaseCheckpoints[i].y, 0f);
            PlayerRespawnSystem rs = PlayerRespawnSystem.Instance != null ? PlayerRespawnSystem.Instance : respawnSystem;
            if (rs != null) rs.SetSafeGroundPosition(cp);
            Debug.Log($"🚩【影子怪物】★1016 追逐中途存檔點 #{i + 1}：重生點改到 {cp}");
            break;
        }
    }

    /// <summary>★1016 耐心：記錄她往前（+X）最遠到哪、最後一次往前是什麼時候。</summary>
    private void UpdatePlayerProgress()
    {
        if (player == null) return;
        float px = player.position.x;
        if (float.IsNegativeInfinity(_progressMaxX) || px > _progressMaxX + Mathf.Max(0.05f, progressStep))
        {
            _progressMaxX = px;
            _lastProgressTime = Time.time;
        }
    }

    /// <summary>★1021 怪物經過一根玩家沒點燃的燭火：不受傷、不縮小、不計數。走過最後一根（吃到或漏掉都算）而數量沒湊齊，就進懲罰＋15 秒倒數。</summary>
    public void OnCandleMissed(CandleCollectible candle)
    {
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Appearing && currentState != MonsterState.Punishing)
        {
            Debug.Log($"【影子怪物】非追逐狀態（{currentState}），燭火漏掉不處理。");
            return;
        }
        Debug.Log($"【影子怪物】漏掉燭火 {candle.gameObject.name}（漏 {MissedCount()} 根、已吃 {_candlesCollected}/{_totalCandles}）：不受傷、不計數");
        if (candle == _lastCandleByX)
        {
            Debug.Log("【影子怪物】走過最後一根燭火但數量沒湊齊！觸發懲罰！");
            TriggerPunishment();
            EnterDoom();
        }
        else ReactToMissedCandle(candle);   // ★1116 不是最後一根：當下就有反應
    }

    /// <summary>★1021 這一輪注定通不了關：開始 doomedRespawnSeconds 倒數，時間到就直接抓人重生。</summary>
    private void EnterDoom()
    {
        if (_doomed) return;
        _doomed = true;
        _finaleDash = false;     // 注定失敗的不要進「只嚇人不攻擊」的最後衝刺
        if (_doomRoutine != null) StopCoroutine(_doomRoutine);
        _doomRoutine = StartCoroutine(DoomRoutine());
        Debug.Log($"【影子怪物】★1021 這一輪通不了關：{doomedRespawnSeconds:F0} 秒內沒被抓到也會強制重生");
    }

    private IEnumerator DoomRoutine()
    {
        float t = 0f;
        while (t < Mathf.Max(1f, doomedRespawnSeconds))
        {
            // 被抓、重生流程進行中就不用倒數了（重生會呼叫 ResetToInitialState，連這個協程一起停掉）
            if (currentState == MonsterState.Dormant || currentState == MonsterState.Devouring) { _doomRoutine = null; yield break; }
            t += Time.deltaTime;
            yield return null;
        }
        _doomRoutine = null;
        if (currentState == MonsterState.Dormant || currentState == MonsterState.Devouring) yield break;

        Debug.Log("【影子怪物】★1021 倒數結束，強制抓人重生");
        currentState = MonsterState.Devouring;
        StopChaseBGM(true);
        if (_pm != null)
        {
            _pm.isCutsceneFrozen = true;
            Rigidbody prb = _pm.GetComponent<Rigidbody>();
            if (prb != null) prb.linearVelocity = Vector3.zero;
        }
        if (ScreenFeedbackManager.Instance != null) ScreenFeedbackManager.Instance.TriggerHitFeedback();
        yield return new WaitForSeconds(0.85f);

        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance != null ? PlayerRespawnSystem.Instance : respawnSystem;
        if (rs != null) rs.TriggerRespawn();
        if (_pm != null) _pm.isCutsceneFrozen = false;
        yield return new WaitForSecondsRealtime(1.5f);
        ResetToInitialState();
    }

    public void OnCandleCollected(CandleCollectible candle)
    {
        if (currentState != MonsterState.Chasing && currentState != MonsterState.Appearing)
        {
            Debug.Log($"【影子怪物】非追逐狀態（{currentState}），燭火收集無效。");
            return;
        }

        // ★1102 重新數：這根的 isCollected 已經是 true，直接數就包含它；舊的計數若曾少算也會一併校正
        int before = _candlesCollected;
        _candlesCollected = RecountCollected();
        if (_candlesCollected != before + 1)
            Debug.LogWarning($"【影子怪物】★1102 燭火計數校正：累加是 {before + 1}，實際數到 {_candlesCollected}（{candle.gameObject.name}）");
        Debug.Log($"【影子怪物】燭火收集 {_candlesCollected}/{_totalCandles}（{candle.gameObject.name}）");
        _chaseStepRamp = true;   // ★1119b 追逐音樂基準音量再降一階（平滑），見 LateUpdate／ChaseStepTarget

        float targetScaleMultiplier = TargetScaleMult();

        // ★1118b 手臂：吃到燭火掉一隻（保留 gethit3 受擊，不縮小）。晚一點點（等受擊動作甩出去）才真的斷
        SyncArms(true);

        // ★1118 最後一根＋倒下演出：不縮小（維持原本大小才看得清楚倒下）。★1119 碰到的那一刻 DeathFinaleSequence 直接播 death1，沒有 gethit3 受擊、沒有震退
        bool deathFinaleNow = deathFinale && _candlesCollected >= _totalCandles && candle == _lastCandleByX;

        // 觸發 gethit3 受擊動畫與漸漸變小 (Smooth Shrink)
        if (!deathFinaleNow)
        {
            if (_hitShrinkCoroutine != null) StopCoroutine(_hitShrinkCoroutine);
            _hitShrinkCoroutine = StartCoroutine(HitAndSmoothShrinkRoutine(targetScaleMultiplier));
        }

        // ★1116 燭火反擊：爆光＋畫面震一下（每一根都有，含最後一根）
        if (usePacingDirector)
        {
            SpawnLightBurst(candle.transform.position);
            Shake(0.3f, 0.25f);
        }

        if (candle == _lastCandleByX && _candlesCollected < _totalCandles)
        {
            Debug.Log("【影子怪物】吃到最後一根燭火但未全部收集！觸發懲罰！");
            TriggerPunishment();
            EnterDoom();
            return;
        }

        if (_candlesCollected >= _totalCandles)
        {
            Debug.Log("【影子怪物】全部燭火收集完畢！怪物開始消失...");
            _finaleDash = false;
            _vanishDone = false;
            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(deathFinaleNow ? DeathFinaleSequence(candle) : VanishSequence());

            // ★最後一根燭火＝結局。不等消散動畫演完，立刻接。
            if (playEndingAfterAllCandles) StartCoroutine(PlayEndingRoutine());
            return;
        }

        // ★1116 還沒收完：被光震退、喘息幾秒（追逐音樂、畫面壓迫一起放鬆）
        if (usePacingDirector && !_finaleDash) StartCandleRecoil();
    }

    private static bool _endingFired;   // ★防重入：整場遊戲只進一次結局

    /// <summary>
    /// 最後一根燭火 → 結局。
    /// 凍結玩家 → 播 M5 文字卡（播完維持全黑，把畫面交給下一個場景）→ 載入繪本結局。
    /// 原本這段掛在 MirrorWallAbsorbCutscene，但鏡牆觸發區就在玩家起點右邊 2.7 公尺，
    /// 走一秒就會結束遊戲，整個 Boss 段跳過。改掛在這裡才是對的順序。
    /// </summary>
    private IEnumerator PlayEndingRoutine()
    {
        if (_endingFired) yield break;
        _endingFired = true;

        if (_pm != null) _pm.isCutsceneFrozen = true;

        // ★先讓怪物縮小消失演完，再轉場（最多等 6 秒保底，避免卡死）
        //   ★1118 倒下演出（她走上觀景台＋death1＋淡出）比較長，保底拉到 18 秒
        float waited = 0f, maxWait = deathFinale ? (crumbleToOrbFinale ? 40f : 18f) : 6f;   // ★1119c 碎裂＋光球飛向鏡頭再多約 10 秒
        if (deathFinale && crumbleToOrbFinale && comicBeforeCrumble && string.IsNullOrEmpty(endingCardId))
            maxWait = float.PositiveInfinity;   // ★1119e 中間要看完結局漫畫（玩家自己翻），不設保底時間
        while (!_vanishDone && waited < maxWait)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (StoryCardPlayer.Instance != null && !string.IsNullOrEmpty(endingCardId)
            && StoryCardPlayer.Instance.HasCard(endingCardId))
        {
            // (true, false) ＝自己淡入黑幕，播完維持全黑交給場景載入
            yield return StoryCardPlayer.Instance.Play(endingCardId, true, false);
        }

        EndCredits.EndingMode = true;
        if (string.IsNullOrEmpty(endingBookScene)) yield break;
        if (EndingFade.Instance != null) yield break;   // ★1119c 碎裂演出已經開始白幕轉場

        // ★1118d 沒播卡：怪物倒下後畫面慢慢淡成全黑 → 載入繪本 → 從全黑淡入漫畫（不再用碎裂轉場）
        if (string.IsNullOrEmpty(endingCardId))
        {
            EndingFade.Begin(endingBookScene, endingFadeOutSeconds, endingBlackHoldSeconds, endingFadeInSeconds);
            yield break;
        }
        SceneLoader.Load(endingBookScene);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：狀態機與協程邏輯
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary> 登場：保持尺寸漸漸顯示 (Alpha Fade)，不從小放大 </summary>
    // ★1021 遠處的吼聲：怪物登場時「先吼、再移鏡頭」。吼聲在登場一開始就播，運鏡版再多等 farRoarLeadSeconds 秒才把鏡頭飛向怪物。
    [Header("★1021 登場前的遠吼")]
    [Tooltip("登場一開始先播一聲遠處的吼（Resources/Sfx/Glass_MonsterFarRoar，原檔：玻璃館_怪物吼叫_02.wav）。")]
    public bool playFarRoar = true;
    [Range(0f, 1f), Tooltip("遠吼音量（小聲＝像從很遠的地方傳來）。")]
    public float farRoarVolume = 0.3f;
    [Tooltip("吼完再等幾秒才移動鏡頭到怪物身上（只有第一次的運鏡版會等）。")]
    public float farRoarLeadSeconds = 0.5f;
    private static AudioClip _farRoarClip;

    private void PlayFarRoar()
    {
        if (!playFarRoar) return;
        if (_farRoarClip == null) _farRoarClip = Resources.Load<AudioClip>("Sfx/Glass_MonsterFarRoar");
        if (_farRoarClip == null) { Debug.LogWarning("【影子怪物】找不到 Resources/Sfx/Glass_MonsterFarRoar，遠吼沒播。"); return; }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(_farRoarClip, farRoarVolume);
        else AudioSource.PlayClipAtPoint(_farRoarClip, Camera.main != null ? Camera.main.transform.position : transform.position, AudioManager.ScaleSfx(farRoarVolume));
    }

    private IEnumerator AppearSequence()
    {
        currentState = MonsterState.Appearing;
        Debug.Log("【影子怪物】開始登場（漸漸顯示）...");

        // 啟動追逐專屬背景音樂 (平滑淡入)
        StartChaseBGM();

        if (appearSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(appearSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(appearSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        ApplyCurrentScale();
        SetVisualAlpha(0f);

        PlayAnimationByName(walkAnimationName);

        // ★有運鏡就走運鏡版：鏡頭先看怪物 → 怪物動起來 → 鏡頭回到主角 → 還控制權
        //   但只演第一次。追逐段的存檔點在 x=-36.6、觸發器在 x=-35.7，
        //   死亡重生後往前走不到一個單位就會再踩到，每次都重演六秒、期間完全不能動——
        //   玩家會覺得「重生後不能跳」。第二次以後直接用原本的原地淡入。
        bool doReveal = useRevealCutscene && player != null && !(revealOnlyOnce && _revealPlayed);
        PlayFarRoar();     // ★1021 先吼（運鏡版接著在 RevealCutsceneRoutine 裡等一下才移鏡頭）
        if (doReveal)
        {
            _revealPlayed = true;
            yield return StartCoroutine(RevealCutsceneRoutine());
        }
        else
        {
            float t = 0f;
            while (t < appearDuration)
            {
                t += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, t / appearDuration);

                // 漸漸顯示登場
                SetVisualAlpha(progress);
                ApplyCurrentScale();

                yield return null;
            }

            SetVisualAlpha(1f);
            ApplyCurrentScale();

            PlayRoar();
        }

        currentState = MonsterState.Chasing;
        // ★1016 耐心從「她能動了」這一刻重新算（登場運鏡六秒期間她不能動，不能算她沒往前）
        if (player != null) _progressMaxX = player.position.x;
        _lastProgressTime = Time.time;
        Debug.Log("【影子怪物】登場完成，全力追逐！");
    }

    private void PlayRoar()
    {
        if (roarSFX == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(roarSFX, transform.position, sfxVolume);
        else AudioSource.PlayClipAtPoint(roarSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ★登場運鏡
    //   1. 凍住主角，鏡頭飛到怪物身上
    //   2. 怪物在鏡頭裡浮現、吼一聲
    //   3. 怪物在鏡頭前開始移動（讓玩家看清楚是什麼東西在追她）
    //   4. 鏡頭飛回主角，還原相機，解凍，開始遊戲
    //   全程 _revealActive = true：怪物抓不到被凍住的主角。
    // ══════════════════════════════════════════════════════════════════════════
    private IEnumerator RevealCutsceneRoutine()
    {
        _revealActive = true;
        IsRevealRunning = true;

        // 1) 凍住主角並把她停下來，免得放開時還帶著慣性
        if (_pm != null)
        {
            _pm.isCutsceneFrozen = true;
            Rigidbody prb = _pm.GetComponent<Rigidbody>();
            if (prb != null) prb.linearVelocity = new Vector3(0f, prb.linearVelocity.y, 0f);
        }

        // ★1021 先吼、再移鏡頭：玩家已凍住、吼聲已經響了，停一下才開始運鏡
        if (playFarRoar && farRoarLeadSeconds > 0f) yield return new WaitForSeconds(farRoarLeadSeconds);

        // 2) 先量出怪物的視覺範圍。
        //    ★不能用 Renderer.bounds：這時候 SetVisualAlpha(0) 已經把所有 Renderer 關掉，
        //      關掉的 Renderer（尤其 SkinnedMeshRenderer）回報的 bounds 是舊的／空的，
        //      鏡頭就會飛去一個沒有東西的地方——這就是「鏡頭鎖定怪物但怪物沒顯示」。
        //      改成從 Mesh 資產本身的 bounds 算，關著也算得出來。
        EnsureVisualBounds();
        EnsureRevealFocus();
        UpdateRevealFocus();

        // 3) 接管相機，飛向怪物
        RevealCameraTakeOver();
        RevealCameraLookAt(_revealFocus);

        // 4) ★怪物立刻開始現身，不能等鏡頭飛到才開始。
        //    鏡頭飛過去要一秒多，那段時間怪物還是隱形的話，玩家看到的就是一片空地。
        //    同時把鏡頭拉遠，這隻怪物比整個畫面還高，不拉遠只會看到牠的肚子。
        float dur = Mathf.Max(0.01f, appearDuration);
        float travel = Mathf.Max(0.01f, revealCameraTravel);
        float span = Mathf.Max(dur, travel);
        float e = 0f;
        while (e < span)
        {
            e += Time.deltaTime;
            SetVisualAlpha(Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, e / dur)));
            ApplyCurrentScale();
            UpdateRevealFocus();
            UpdateRevealZoom(Mathf.Clamp01(e / travel));
            yield return null;
        }
        SetVisualAlpha(1f);
        ApplyCurrentScale();
        UpdateRevealZoom(1f);

        PlayRoar();

        e = 0f;
        while (e < Mathf.Max(0f, revealHoldBeforeMove))
        {
            e += Time.deltaTime; UpdateRevealFocus(); yield return null;
        }

        // 5) ★怪物在鏡頭前開始移動
        currentState = MonsterState.Chasing;
        e = 0f;
        while (e < Mathf.Max(0f, revealWatchMoveSeconds))
        {
            e += Time.deltaTime; UpdateRevealFocus(); yield return null;
        }

        // 6) 鏡頭飛回主角，鏡頭大小同時收回原本的
        //    ★鏡頭一回到主角身上就先解凍，不要等滿 travel 秒（阻尼小時鏡頭早就到了，剩下的時間玩家會卡著不能走）
        RevealCameraLookAt(player != null ? player : transform);
        e = 0f;
        bool released = false;
        while (e < travel)
        {
            e += Time.deltaTime;
            UpdateRevealZoom(1f - Mathf.Clamp01(e / travel));
            if (!released && IsCameraBackOnPlayer())
            {
                released = true;
                if (_pm != null) _pm.isCutsceneFrozen = false;
            }
            yield return null;
        }
        UpdateRevealZoom(0f);

        // 7) 還原相機、解凍、開始遊戲
        RevealFinish();
        Debug.Log("【影子怪物】登場運鏡結束，控制權交還玩家。");
    }

    /// <summary> 鏡頭飛回來時，畫面中心離主角夠近就算回到主角身上了（只看左右）。 </summary>
    private bool IsCameraBackOnPlayer()
    {
        if (player == null) return true;
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return true;
        return Mathf.Abs(_mainCam.transform.position.x - player.position.x) <= revealReleaseDistance;
    }

    /// <summary> 演出正常收尾：還原相機、解凍主角。 </summary>
    private void RevealFinish()
    {
        RevealCameraRestore();
        if (_pm != null) _pm.isCutsceneFrozen = false;
        _revealActive = false;
        IsRevealRunning = false;
    }

    /// <summary> 演出被中斷（死亡重生 / 關卡重置）時，一定要把鏡頭和控制權還回去。 </summary>
    private void RevealCancel()
    {
        if (!_revealActive && !_revealCamTaken) return;
        UpdateRevealZoom(0f);
        RevealFinish();
        if (_revealFocus != null) Destroy(_revealFocus.gameObject);
        _revealFocus = null;
    }

    private void EnsureRevealFocus()
    {
        if (_revealFocus == null)
            _revealFocus = new GameObject("ShadowMonster_RevealFocus").transform;
    }

    /// <summary> 對準點＝視覺中心（不是 transform，牠的 Pivot 在腳底），Z 採用怪物本身的世界深度。 </summary>
    private void UpdateRevealFocus()
    {
        if (_revealFocus == null) return;
        EnsureVisualBounds();
        Vector3 c = transform.TransformPoint(_visualLocalBounds.center);

        // ★Z 不能直接沿用主角的。
        //   相機在焦點後方 15 單位（CinemachineFollow.FollowOffset.z），而這隻怪物
        //   前後厚約 29 單位、又擺在 z=-13.2。焦點用主角的 z=0 的話相機落在 z=-15，
        //   剛好切進怪物身體——正交投影的近裁面會把牠剖開，畫面上就是破圖、
        //   可以看穿到裡面去。這裡把焦點往後推到相機能整隻看完為止。
        float scaleXZ = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float halfDepth = Mathf.Max(_visualLocalBounds.extents.x, _visualLocalBounds.extents.z) * scaleXZ;

        float offsetZ = -15f;
        for (int i = 0; i < _revealFollows.Count; i++)
        {
            if (_revealFollows[i] != null) { offsetZ = _revealFollows[i].FollowOffset.z; break; }
        }

        float needZ = (c.z - halfDepth) - Mathf.Max(0f, revealDepthMargin) - offsetZ;
        float baseZ = player != null ? player.position.z : c.z;
        float z = Mathf.Min(baseZ, needZ);

        _revealFocus.position = new Vector3(c.x + revealFocusOffset.x, c.y + revealFocusOffset.y, z);
    }

    // ── 從 Mesh 資產量身高（Renderer 關著也算得出來）────────────────────────
    private void EnsureVisualBounds()
    {
        if (_visualBoundsMeasured) return;
        _visualBoundsMeasured = true;

        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        bool any = false;
        Bounds acc = new Bounds(Vector3.zero, Vector3.zero);

        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r == null || r.sharedMesh == null) continue;
            // SkinnedMeshRenderer 的 sharedMesh.bounds 是在 rootBone 的空間裡
            Transform space = r.rootBone != null ? r.rootBone : r.transform;
            AccumulateBounds(ref acc, ref any, r.sharedMesh.bounds, toLocal * space.localToWorldMatrix);
        }
        foreach (var f in GetComponentsInChildren<MeshFilter>(true))
        {
            if (f == null || f.sharedMesh == null) continue;
            if (f.GetComponent<MeshRenderer>() == null) continue;
            AccumulateBounds(ref acc, ref any, f.sharedMesh.bounds, toLocal * f.transform.localToWorldMatrix);
        }

        if (!any)
        {
            // 真的量不到就退回 Renderer.bounds（至少有個值），再不行就用自己的原點
            Renderer[] rs = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                if (r == null) continue;
                Vector3 lc = transform.InverseTransformPoint(r.bounds.center);
                if (!any) { acc = new Bounds(lc, Vector3.zero); any = true; }
                else acc.Encapsulate(lc);
            }
            if (!any) acc = new Bounds(Vector3.zero, Vector3.one);
        }

        _visualLocalBounds = acc;
        Debug.Log($"【影子怪物】量到的視覺範圍（本地）中心 {acc.center}，大小 {acc.size}；" +
                  $"世界高度約 {acc.size.y * Mathf.Abs(transform.lossyScale.y):F1} 單位。");
    }

    /// <summary> 怪物的視覺中心（世界座標）。牠的 Pivot 在腳底，所有判定都該用這個點。 </summary>
    public Vector3 VisualCenter
    {
        get
        {
            EnsureVisualBounds();
            return transform.TransformPoint(_visualLocalBounds.center);
        }
    }

    /// <summary> 目前縮放下的視覺半高。 </summary>
    public float VisualHalfHeight
    {
        get
        {
            EnsureVisualBounds();
            return _visualLocalBounds.extents.y * Mathf.Abs(transform.lossyScale.y);
        }
    }

    /// <summary>
    /// 燭火是否進入怪物的身體範圍。
    /// ★原本兩邊都是寫死的 dx&lt;=3.5、dy&lt;=6.5，而怪物腳底在 y≈-60、燭火在 y≈-36，
    ///   dy 差 23 個單位，那個判定從頭到尾就沒成立過——真正在收燭火的是碰撞體重疊，
    ///   所以怪物一縮小、身體構不到燭火，後面幾根就吃不到了。
    ///   改成用視覺中心＋目前半高判定，縮放多少都算得準。
    /// </summary>
    public bool OverlapsCandle(Vector3 candlePosition)
    {
        Vector3 c = VisualCenter;
        float dx = Mathf.Abs(candlePosition.x - c.x);
        if (dx > candleReachX) return false;
        float dy = Mathf.Abs(candlePosition.y - c.y);
        return dy <= VisualHalfHeight + candleReachYMargin;
    }

    // ── ★1119 身體前緣碰燭台 ─────────────────────────────────────────────
    //   怪物的 Pivot／視覺中心在身體中間，但牠是往前伸手的巨影：實測手在中心前方 16 公尺以上。
    //   OverlapsCandle 用「中心 ±candleReachX(6)」判定，畫面上手早就蓋過燭台十公尺才受傷。
    //   骨頭位置也不準（多出來的手臂頂點綁在別的骨頭上，畫面上的手比骨頭前面很多），
    //   所以直接把當下的動畫網格烘焙出來，用真正畫在畫面上的頂點算最前緣。
    //   被扯掉的手臂只是拿掉三角形、頂點還在，所以只算「還有三角形用到」的頂點。
    [Tooltip("★1119b 只有身體（軀幹＋頭）算碰撞，手臂、腿、多出來的手臂伸過燭台都不算")]
    public bool candleHitTorsoOnly = true;
    [Tooltip("★1119 身體前緣多久重算一次（秒）。烘焙一次網格約 0.3 毫秒")]
    public float candleFrontRefreshSeconds = 0.05f;

    private class FrontMeshCache { public SkinnedMeshRenderer smr; public bool[] used; public int sig = -1; }
    private readonly List<FrontMeshCache> _frontMeshes = new List<FrontMeshCache>();
    private readonly List<Vector3> _frontWorld = new List<Vector3>();
    private readonly List<Vector3> _frontTmp = new List<Vector3>();
    private Mesh _frontBake;
    private float _frontMeshesAt = -10f, _frontBakedAt = -10f;
    private float _frontReachHold = -1f;

    private static int MeshIndexSignature(Mesh m)
    {
        int sig = m.vertexCount * 31;
        for (int s = 0; s < m.subMeshCount; s++) sig = sig * 17 + (int)m.GetIndexCount(s);
        return sig;
    }

    private void RefreshFrontVertices()
    {
        if (Time.time - _frontBakedAt < Mathf.Max(0f, candleFrontRefreshSeconds) && _frontBakedAt >= 0f) return;
        _frontBakedAt = Time.time;

        if (Time.time - _frontMeshesAt > 1f || _frontMeshes.Count == 0)
        {
            _frontMeshesAt = Time.time;
            var keep = new List<FrontMeshCache>();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (r == null || r.sharedMesh == null) continue;
                var c = _frontMeshes.Find(x => x.smr == r) ?? new FrontMeshCache { smr = r };
                keep.Add(c);
            }
            _frontMeshes.Clear(); _frontMeshes.AddRange(keep);
        }

        if (_frontBake == null) _frontBake = new Mesh { name = "MonsterFrontBake" };
        _frontWorld.Clear();
        foreach (var c in _frontMeshes)
        {
            var r = c.smr;
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sharedMesh == null) continue;
            Mesh sm = r.sharedMesh;
            var shed = GetComponent<MonsterArmShedding>();
            bool armsKnown = shed != null && shed.BodyRenderer == r;
            int sig = MeshIndexSignature(sm) * 3 + (armsKnown ? 1 : 0);
            if (c.used == null || c.sig != sig || c.used.Length != sm.vertexCount)
            {
                c.sig = sig;
                c.used = new bool[sm.vertexCount];
                for (int s = 0; s < sm.subMeshCount; s++)
                    foreach (int i in sm.GetTriangles(s)) if (i >= 0 && i < c.used.Length) c.used[i] = true;
                if (candleHitTorsoOnly) KeepTorsoVertices(r, sm, c.used, armsKnown ? shed : null);
            }
            // 實測：BakeMesh(useScale:false) 出來的頂點已經是世界尺寸（骨架本身帶縮放），只差位置和旋轉
            r.BakeMesh(_frontBake, false);
            _frontBake.GetVertices(_frontTmp);
            Matrix4x4 m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
            int n = Mathf.Min(_frontTmp.Count, c.used.Length);
            for (int i = 0; i < n; i++) if (c.used[i]) _frontWorld.Add(m.MultiplyPoint3x4(_frontTmp[i]));
        }
    }

    /// <summary>
    /// ★1119b 只留軀幹＋頭的頂點：主要綁在 Hips／Spine／Neck／Head／Shoulder（鎖骨）骨頭上的才算，
    /// 手臂、腿（Arm／ForeArm／Hand／UpLeg／Leg／Foot…）不算；多出來的手臂整塊綁在 Spine2 上，另外用 MonsterArmShedding 的分析排除。
    /// </summary>
    private static void KeepTorsoVertices(SkinnedMeshRenderer r, Mesh sm, bool[] mask, MonsterArmShedding shed)
    {
        Transform[] bones = r.bones;
        BoneWeight[] bw = sm.boneWeights;
        if (bones == null || bones.Length == 0 || bw == null || bw.Length != mask.Length)
        {
            // 沒有骨頭權重的網格（例如只綁在頭上的眼睛、牙齒用單一骨頭）：看它自己掛在哪根骨頭
            Transform t = r.rootBone != null ? r.rootBone : r.transform;
            if (!IsTorsoBone(t)) for (int i = 0; i < mask.Length; i++) mask[i] = false;
            return;
        }
        var torso = new bool[bones.Length];
        for (int b = 0; b < bones.Length; b++) torso[b] = IsTorsoBone(bones[b]);
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            BoneWeight w = bw[i];
            int bi = w.boneIndex0; float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; bi = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; bi = w.boneIndex2; }
            if (w.weight3 > best) { bi = w.boneIndex3; }
            bool keep = bi >= 0 && bi < torso.Length && torso[bi];
            if (keep && shed != null && shed.IsExtraArmVertex(i)) keep = false;
            mask[i] = keep;
        }
    }

    private static bool IsTorsoBone(Transform t)
    {
        if (t == null) return false;
        string n = t.name;
        int colon = n.LastIndexOf(':');
        if (colon >= 0) n = n.Substring(colon + 1);
        return n.StartsWith("Hips") || n.StartsWith("Spine") || n.StartsWith("Neck") || n.StartsWith("Head")
            || n.EndsWith("Shoulder");
    }

    /// <summary>★1119 怪物在 dir 方向（+1＝往 +X）、高度 yMin～yMax 之間最前面那個頂點的世界 X。找不到回傳 NaN。</summary>
    private float FrontEdgeX(float dir, float yMin, float yMax)
    {
        RefreshFrontVertices();
        float best = float.NaN;
        for (int i = 0; i < _frontWorld.Count; i++)
        {
            Vector3 p = _frontWorld[i];
            if (p.y < yMin || p.y > yMax) continue;
            if (float.IsNaN(best) || p.x * dir > best * dir) best = p.x;
        }
        return best;
    }

    private float ChaseDir => (player != null && player.position.x < transform.position.x) ? -1f : 1f;

    /// <summary>★1119 怪物身體最前緣（軀幹、頭）有沒有碰到這根燭台。身體中心已經整個越過燭台 candleReachX 就不算（那是已經走過去了）。</summary>
    public bool FrontTouchesCandle(Vector3 candlePosition)
    {
        if (!candleHitByFrontEdge) return false;
        float dir = ChaseDir;
        float cx = VisualCenter.x;
        if ((cx - candlePosition.x) * dir > candleReachX) return false;      // 身體已經整個走過去了
        if ((candlePosition.x - cx) * dir > 80f) return false;              // 還很遠，不用烘焙網格
        float yMin = candlePosition.y - candleFrontYMargin;
        float yMax = candlePosition.y + candleBodyHeight + candleFrontYMargin;
        float front = FrontEdgeX(dir, yMin, yMax);
        if (float.IsNaN(front)) return false;
        return (front - candlePosition.x) * dir >= -Mathf.Max(0f, candleFrontTouchMargin);
    }

    /// <summary>★1119 前緣離 Pivot 多遠（公尺，朝追逐方向）。手臂擺動會讓它前後晃，這裡取「快升慢降」的值，給「停在燭台前等」用。</summary>
    private float FrontReachForHold(Vector3 candlePosition)
    {
        float dir = ChaseDir;
        float yMin = candlePosition.y - candleFrontYMargin;
        float yMax = candlePosition.y + candleBodyHeight + candleFrontYMargin;
        float front = FrontEdgeX(dir, yMin, yMax);
        float now = float.IsNaN(front) ? 0f : Mathf.Max(0f, (front - transform.position.x) * dir);
        if (_frontReachHold < 0f || now > _frontReachHold) _frontReachHold = now;
        else _frontReachHold = Mathf.MoveTowards(_frontReachHold, now, 1.5f * Time.deltaTime);
        return _frontReachHold;
    }

    private static void AccumulateBounds(ref Bounds acc, ref bool any, Bounds local, Matrix4x4 m)
    {
        Vector3 c = local.center, ex = local.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 pt = new Vector3(
                c.x + (((i & 1) == 0) ? -ex.x : ex.x),
                c.y + (((i & 2) == 0) ? -ex.y : ex.y),
                c.z + (((i & 4) == 0) ? -ex.z : ex.z));
            Vector3 w = m.MultiplyPoint3x4(pt);
            if (!any) { acc = new Bounds(w, Vector3.zero); any = true; }
            else acc.Encapsulate(w);
        }
    }

    // ── 相機接管 ──────────────────────────────────────────────────────────
    private void RevealCameraTakeOver()
    {
        if (_revealCamTaken) return;
        _revealCamTaken = true;

        _revealVcams3.Clear();
        _revealVcamsLegacy.Clear();
        _revealOrigFollow.Clear();
        _revealFollows.Clear();
        _revealOrigDamping.Clear();
        _revealOrigFollowOffset.Clear();
        _revealOrigLens.Clear();

        if (_mainCam == null) _mainCam = Camera.main;
        bool ortho = _mainCam == null || _mainCam.orthographic;

        // 這隻怪物比整個畫面還高，算出要把鏡頭拉多遠才裝得下
        float halfH = _visualLocalBounds.extents.y * Mathf.Abs(transform.lossyScale.y);
        _revealZoomEnabled = ortho && revealZoomPadding > 0f && halfH > 0.01f;
        _revealTargetLens = halfH * revealZoomPadding;

        foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            _revealVcams3.Add(v);
            _revealOrigFollow.Add(v.Follow);
            _revealOrigLens.Add(v != null ? v.Lens.OrthographicSize : 0f);

            // 場景的 PositionDamping 是 1，鏡頭會很黏；演出期間先調小，結束再還原
            CinemachineFollow cf = v != null ? v.GetComponent<CinemachineFollow>() : null;
            _revealFollows.Add(cf);
            _revealOrigDamping.Add(cf != null ? cf.TrackerSettings.PositionDamping : Vector3.zero);
            _revealOrigFollowOffset.Add(cf != null ? cf.FollowOffset : new Vector3(0, 2.2f, -15f));
            if (cf != null)
            {
                if (revealCameraDamping >= 0f)
                {
                    var ts = cf.TrackerSettings;
                    ts.PositionDamping = new Vector3(revealCameraDamping, revealCameraDamping, ts.PositionDamping.z);
                    cf.TrackerSettings = ts;
                }
                // ★ 關鍵修復：將相機拉到怪物前方 Z = -60，遠離 Near Clip 切面，徹底解決怪物被裁切與黑塊問題！
                cf.FollowOffset = new Vector3(cf.FollowOffset.x, cf.FollowOffset.y, -60.0f);
            }
        }
        foreach (var v in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None))
        {
            _revealVcamsLegacy.Add(v);
            _revealOrigFollow.Add(v.Follow);
        }
    }

    /// <summary> t: 0 ＝ 原本的鏡頭大小，1 ＝ 拉遠到整隻怪物裝得下。 </summary>
    private void UpdateRevealZoom(float t)
    {
        if (!_revealZoomEnabled || !_revealCamTaken) return;
        for (int i = 0; i < _revealVcams3.Count && i < _revealOrigLens.Count; i++)
        {
            var v = _revealVcams3[i];
            if (v == null) continue;
            float orig = _revealOrigLens[i];
            float target = Mathf.Max(orig, _revealTargetLens);   // 只拉遠，不拉近
            var lens = v.Lens;
            lens.OrthographicSize = Mathf.Lerp(orig, target, Mathf.Clamp01(t));
            v.Lens = lens;
        }
    }

    private void RevealCameraLookAt(Transform target)
    {
        if (target == null) return;

        foreach (var v in _revealVcams3)
        {
            if (v == null) continue;
            var tg = v.Target;
            tg.TrackingTarget = target;
            v.Target = tg;
            v.Follow = target;
        }
        foreach (var v in _revealVcamsLegacy)
        {
            if (v != null) v.Follow = target;
        }
    }

    private void RevealCameraRestore()
    {
        if (!_revealCamTaken) return;
        _revealCamTaken = false;

        // 鏡頭大小還原
        for (int i = 0; i < _revealVcams3.Count && i < _revealOrigLens.Count; i++)
        {
            var v = _revealVcams3[i];
            if (v == null) continue;
            var lens = v.Lens;
            lens.OrthographicSize = _revealOrigLens[i];
            v.Lens = lens;
        }

        // 阻尼與 FollowOffset 還原 (維持 -70.0f 安全景深，確保正常追逐時怪物身軀不會被裁切開洞)
        for (int i = 0; i < _revealFollows.Count; i++)
        {
            CinemachineFollow cf = _revealFollows[i];
            if (cf == null) continue;
            if (i < _revealOrigDamping.Count)
            {
                var ts = cf.TrackerSettings;
                ts.PositionDamping = _revealOrigDamping[i];
                cf.TrackerSettings = ts;
            }
            Vector3 origOffset = (i < _revealOrigFollowOffset.Count) ? _revealOrigFollowOffset[i] : new Vector3(0, 2.2f, -70f);
            cf.FollowOffset = new Vector3(origOffset.x, origOffset.y, Mathf.Min(origOffset.z, -70.0f));
        }

        int idx = 0;
        foreach (var v in _revealVcams3)
        {
            if (v != null && idx < _revealOrigFollow.Count)
            {
                // 接管前如果本來就沒有目標（場景裡 TrackingTarget 是空的，靠腳本在跑時指定），
                // 還原成 null 會讓相機從此不跟人。這種情況退回主角。
                Transform back = _revealOrigFollow[idx] != null ? _revealOrigFollow[idx] : player;
                var tg = v.Target;
                tg.TrackingTarget = back;
                v.Target = tg;
                v.Follow = back;
            }
            idx++;
        }
        foreach (var v in _revealVcamsLegacy)
        {
            if (v != null && idx < _revealOrigFollow.Count)
                v.Follow = _revealOrigFollow[idx] != null ? _revealOrigFollow[idx] : player;
            idx++;
        }
    }

    /// <summary> 吃到燭火：播放 gethit3 受擊動畫並漸漸變小 </summary>
    private IEnumerator HitAndSmoothShrinkRoutine(float targetMult)
    {
        _isHitShrinking = true;

        // 連續吃到燭火時也要從頭重播受擊動畫
        PlayAnimationByName(hitAnimationName, true);
        // ★受擊動畫要播完才能切回走路／跑步。先用保底時間鎖住，下一幀讀到實際長度再更新
        _hitAnimLockUntil = Time.time + Mathf.Max(smoothShrinkDuration, 0.1f);
        StartCoroutine(LockUntilHitAnimEnds());

        if (hitSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(hitSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(hitSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        float startMult = _currentScaleMultiplier;
        float elapsed = 0f;

        while (elapsed < smoothShrinkDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / smoothShrinkDuration);
            _currentScaleMultiplier = Mathf.Lerp(startMult, targetMult, progress);

            ApplyCurrentScale();
            yield return null;
        }

        _currentScaleMultiplier = targetMult;
        ApplyCurrentScale();
        _isHitShrinking = false;
    }

    /// <summary> 讀出受擊動畫實際長度（含 State 速度），鎖到它播完為止。 </summary>
    private IEnumerator LockUntilHitAnimEnds()
    {
        yield return null;
        if (_animator == null) yield break;
        // 用 CrossFade 進受擊時，這一幀受擊還是「下一個」狀態
        AnimatorStateInfo info = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0) : _animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(hitAnimationName)) info = _animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(hitAnimationName)) yield break;
        float remaining = info.length * (1f - Mathf.Repeat(info.normalizedTime, 1f));
        _hitAnimLockUntil = Mathf.Max(_hitAnimLockUntil, Time.time + remaining);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ★1118 結局：怪物倒下（她走上觀景台看著牠 death1）
    // ──────────────────────────────────────────────────────────────────────────
    private bool _finaleCamOn;
    private Transform _finaleCamDummy;
    private readonly List<CinemachineCamera> _finaleVcams = new List<CinemachineCamera>();
    private readonly List<Transform> _finaleOrigFollow = new List<Transform>();
    private float _finaleCamYOffset, _finaleCamVelX;
    private float _finaleCamMonsterX;
    // ★1118c 倒下時的拉遠：怪物是半身站在玻璃橋下面的深坑裡，倒下去會躺到畫面外，所以倒下那一刻把鏡頭往下、拉遠，框住「她＋倒下的怪物」
    private bool _finaleDeathFrame;
    private float _finaleBodyH, _finaleSizeVel, _finaleYVel;
    private readonly List<float> _finaleOrigLens = new List<float>();
    private bool _finaleLensRestoring;
    private readonly List<CinemachineDeathZoneFloor> _finaleDisabledFloors = new List<CinemachineDeathZoneFloor>();

    private float _finaleExtraRight, _finaleExtraTop;
    /// <summary>★1119d 倒下鏡頭在她右邊／上面多留幾公尺（白色光球繞她一圈時用），鏡頭會平滑拉開。</summary>
    public void SetFinaleFrameExtra(float right, float top)
    {
        _finaleExtraRight = Mathf.Max(0f, right);
        _finaleExtraTop = Mathf.Max(0f, top);
    }

    private void FinaleCamOn()
    {
        if (_finaleCamOn) return;
        _finaleCamOn = true;
        if (_finaleCamDummy == null) _finaleCamDummy = new GameObject("ShadowMonster_FinaleCamDummy").transform;
        _finaleVcams.Clear(); _finaleOrigFollow.Clear(); _finaleOrigLens.Clear();
        _finaleLensRestoring = false;
        Vector3 start = player != null ? player.position : Vector3.zero;
        bool first = true;
        foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            if (first && v.Follow != null) { start = v.Follow.position; first = false; }
            _finaleVcams.Add(v);
            _finaleOrigFollow.Add(v.Follow);
            _finaleOrigLens.Add(v.Lens.OrthographicSize);
            v.Follow = _finaleCamDummy;
            // ★1118c 「鏡頭最低只能看到死亡區上面」的限制先關掉（倒下的怪物在橋下的深坑，要看得到）；鏡頭縮回去之後再開
            foreach (var fl in v.GetComponents<CinemachineDeathZoneFloor>())
                if (fl != null && fl.enabled) { fl.enabled = false; _finaleDisabledFloors.Add(fl); }
        }
        _finaleCamDummy.position = start;
        _finaleCamYOffset = player != null ? start.y - player.position.y : 0f;
        _finaleCamVelX = 0f;
    }

    private void FinaleCamOff(bool instant = false)
    {
        if (!_finaleCamOn) return;
        _finaleCamOn = false;
        _finaleDeathFrame = false;
        _finaleAnchorInit = false;
        if (_finaleDampingZeroed)
        {   // ★1118d 還原 Cinemachine 的位置阻尼
            for (int i = 0; i < _finaleVcams.Count && i < _finaleSavedDamping.Count; i++)
            {
                var f0 = _finaleVcams[i] != null ? _finaleVcams[i].GetComponent<CinemachineFollow>() : null;
                if (f0 == null) continue;
                var ts = f0.TrackerSettings; ts.PositionDamping = _finaleSavedDamping[i]; f0.TrackerSettings = ts;
            }
            _finaleDampingZeroed = false;
            _finaleSavedDamping.Clear();
        }
        for (int i = 0; i < _finaleVcams.Count; i++)
            if (_finaleVcams[i] != null) _finaleVcams[i].Follow = _finaleOrigFollow[i];
        _finaleOrigFollow.Clear();
        if (instant)
        {
            for (int i = 0; i < _finaleVcams.Count && i < _finaleOrigLens.Count; i++)
            {
                if (_finaleVcams[i] == null) continue;
                var lens = _finaleVcams[i].Lens; lens.OrthographicSize = _finaleOrigLens[i]; _finaleVcams[i].Lens = lens;
            }
            _finaleVcams.Clear(); _finaleOrigLens.Clear(); _finaleLensRestoring = false;
            RestoreFinaleFloors();
        }
        else
        {
            _finaleLensRestoring = _finaleVcams.Count > 0;   // 鏡頭大小慢慢縮回去（UpdateFinaleCam 裡做）
            if (!_finaleLensRestoring) RestoreFinaleFloors();
        }
    }

    private void RestoreFinaleFloors()
    {
        foreach (var fl in _finaleDisabledFloors) if (fl != null) fl.enabled = true;
        _finaleDisabledFloors.Clear();
    }

    /// <summary>倒下演出的鏡頭：框住她和怪物（水平中點），高度跟著她。</summary>
    private void UpdateFinaleCam()
    {
        // 演出結束後，鏡頭大小慢慢縮回原本的
        if (!_finaleCamOn && _finaleLensRestoring)
        {
            bool done = true;
            for (int i = 0; i < _finaleVcams.Count && i < _finaleOrigLens.Count; i++)
            {
                var v = _finaleVcams[i];
                if (v == null) continue;
                var lens = v.Lens;
                lens.OrthographicSize = Mathf.SmoothDamp(lens.OrthographicSize, _finaleOrigLens[i], ref _finaleSizeVel, 0.9f);
                if (Mathf.Abs(lens.OrthographicSize - _finaleOrigLens[i]) < 0.02f) lens.OrthographicSize = _finaleOrigLens[i]; else done = false;
                v.Lens = lens;
            }
            if (done) { _finaleLensRestoring = false; _finaleVcams.Clear(); _finaleOrigLens.Clear(); RestoreFinaleFloors(); }
        }
        if (!_finaleCamOn || _finaleCamDummy == null || player == null) return;
        float mid = (player.position.x + _finaleCamMonsterX) * 0.5f;
        Vector3 p = _finaleCamDummy.position;
        float targetY = player.position.y + _finaleCamYOffset;

        if (_finaleDeathFrame && _finaleVcams.Count > 0 && _finaleVcams[0] != null && _finaleOrigLens.Count > 0)
        {
            // ★1118d 鏡頭底邊黏在怪物身體「最低的那一點」（站著＝腳、躺下＝背），背部以下不留空白。
            //   左邊界＝身體最左（手伸出去）往外留一點、右邊＝她；高度由下往上算，多出來的空間只會在上面。
            var cf = _finaleVcams[0].GetComponent<CinemachineFollow>();
            Vector3 off = cf != null ? cf.FollowOffset : Vector3.zero;
            float aspect = _mainCam != null ? Mathf.Max(0.5f, _mainCam.aspect) : 1.78f;

            if (!_finaleAnchorInit)
            {
                // 進入的那一刻：從「鏡頭現在實際的位置／大小」接手，不跳
                _finaleAnchorInit = true;
                Vector3 cp = _mainCam != null ? _mainCam.transform.position : _finaleCamDummy.position + off;
                float s0 = _finaleVcams[0].Lens.OrthographicSize;
                _finaleBottomSm = cp.y - s0;
                _finaleBottomVel = 0f;
                _finaleLeftMin = float.PositiveInfinity;
                _finaleMidSm = cp.x;
                _finaleCamVelX = 0f;
                // 位置阻尼歸零，之後由這裡自己做平滑（不然 Cinemachine 的阻尼會讓底邊慢半拍）
                _finaleSavedDamping.Clear();
                foreach (var v in _finaleVcams)
                {
                    var f0 = v != null ? v.GetComponent<CinemachineFollow>() : null;
                    if (f0 == null) { _finaleSavedDamping.Add(Vector3.zero); continue; }
                    var ts = f0.TrackerSettings;
                    _finaleSavedDamping.Add(ts.PositionDamping);
                    ts.PositionDamping = Vector3.zero;
                    f0.TrackerSettings = ts;
                }
                _finaleDampingZeroed = true;
            }

            bool ok = MeasureFinaleBody();
            float bodyBottom = ok ? _finaleBodyMinY + finaleBottomInset : transform.position.y - 1.5f;
            float bodyLeft = ok ? _finaleBodyMinX : _finaleCamMonsterX - Mathf.Max(1f, _finaleBodyH) * finaleFallReach;
            _finaleLeftMin = Mathf.Min(_finaleLeftMin, bodyLeft);   // 只往外長，不縮回來（手臂不會被切）
            _finaleBottomSm = Mathf.SmoothDamp(_finaleBottomSm, bodyBottom, ref _finaleBottomVel, 0.3f);

            float left = _finaleLeftMin - 3f;
            float right = player.position.x + 4f + _finaleExtraRight;
            float bottom = _finaleBottomSm;
            float top = Mathf.Max(player.position.y + 8f + _finaleExtraTop, bottom + 2f);
            float size = Mathf.Max((top - bottom) * 0.5f, (right - left) * 0.5f / aspect);
            float maxSize = Mathf.Max(_finaleOrigLens[0], finaleMaxOrthoSize);
            if (size > maxSize && !_finaleWarnedClamp) { _finaleWarnedClamp = true; Debug.LogWarning($"【影子怪物】★1118d 倒下鏡頭需要 {size:F1} 但上限 {maxSize:F1}（finaleMaxOrthoSize），會切到畫面"); }
            size = Mathf.Clamp(size, _finaleOrigLens[0], maxSize);
            float cur = Mathf.SmoothDamp(_finaleVcams[0].Lens.OrthographicSize, size, ref _finaleSizeVel, 0.6f);
            for (int i = 0; i < _finaleVcams.Count; i++)
            {
                if (_finaleVcams[i] == null) continue;
                var l = _finaleVcams[i].Lens; l.OrthographicSize = cur; _finaleVcams[i].Lens = l;
            }
            _finaleMidSm = Mathf.SmoothDamp(_finaleMidSm, (left + right) * 0.5f, ref _finaleCamVelX, 0.8f);
            // 鏡頭中心 y ＝ 底邊 ＋ 「目前」的半高 → 半高怎麼變，底邊都黏在背上
            p.x = _finaleMidSm - off.x;
            p.y = bottom + cur - off.y;
            _finaleCamDummy.position = p;
            return;
        }
        else p.y = targetY;
        p.x = Mathf.SmoothDamp(p.x, mid, ref _finaleCamVelX, 0.8f);
        _finaleCamDummy.position = p;
    }

    // ★1118d 倒下鏡頭：量怪物身體真正的最低點／最左點（每兩個畫面對 Body 的頂點手動蒙皮一次）
    private SkinnedMeshRenderer _finaleBodySmr;
    private Vector3[] _finaleVerts; private BoneWeight[] _finaleBw; private Matrix4x4[] _finaleBind, _finaleSkin; private Mesh _finaleVertsMesh;
    private readonly List<int> _finaleIdxTmp = new List<int>();
    private bool[] _finaleUsed;
    private Mesh _finaleUsedMesh;
    private int _finaleUsedKey = -1;
    private float _finaleBodyMinY, _finaleBodyMinX;
    private bool _finaleBodyValid;
    private int _finaleMeasureFrame = -999;
    private bool _finaleAnchorInit, _finaleDampingZeroed, _finaleWarnedClamp;
    private float _finaleBottomSm, _finaleBottomVel, _finaleLeftMin, _finaleMidSm;
    private readonly List<Vector3> _finaleSavedDamping = new List<Vector3>();
    [Tooltip("★1118d 倒下鏡頭的底邊比身體最低點再往上多少（公尺），避免背下面露出一條縫")]
    public float finaleBottomInset = 0.05f;

    /// <summary>身體最低／最左的世界座標；回傳 false＝量不到（網格不能讀），呼叫端改用保底框法。</summary>
    private bool MeasureFinaleBody()
    {
        if (Time.frameCount - _finaleMeasureFrame < 2) return _finaleBodyValid;
        _finaleMeasureFrame = Time.frameCount;
        if (_finaleBodySmr == null)
        {
            int best = -1;
            foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (s == null || s.sharedMesh == null) continue;
                if (s.name == "Body") { _finaleBodySmr = s; break; }
                string n = s.name.ToLowerInvariant();
                if (n.Contains("eye") || n.Contains("teeth") || n.Contains("tooth")) continue;
                if (s.sharedMesh.vertexCount > best) { best = s.sharedMesh.vertexCount; _finaleBodySmr = s; }
            }
        }
        if (_finaleBodySmr == null || _finaleBodySmr.sharedMesh == null || !_finaleBodySmr.sharedMesh.isReadable)
        {
            if (_finaleBodySmr != null)
            {   // 網格不能讀：退而求其次用 renderer 的包圍盒
                Bounds b = _finaleBodySmr.bounds;
                _finaleBodyMinY = b.min.y; _finaleBodyMinX = b.min.x; _finaleBodyValid = true;
                return true;
            }
            _finaleBodyValid = false; return false;
        }
        Mesh sm = _finaleBodySmr.sharedMesh;
        // 掉手臂只拿掉三角形、頂點還留在陣列裡（綁在 Spine2，會跟著骨頭飛）→ 只算還有被三角形用到的頂點
        int key = 0;
        for (int s = 0; s < sm.subMeshCount; s++) key = key * 31 + (int)sm.GetIndexCount(s);
        if (_finaleUsed == null || _finaleUsedMesh != sm || _finaleUsedKey != key || _finaleUsed.Length != sm.vertexCount)
        {
            _finaleUsed = new bool[sm.vertexCount];
            for (int s = 0; s < sm.subMeshCount; s++)
            {
                sm.GetIndices(_finaleIdxTmp, s);
                for (int i = 0; i < _finaleIdxTmp.Count; i++) _finaleUsed[_finaleIdxTmp[i]] = true;
            }
            _finaleUsedMesh = sm; _finaleUsedKey = key;
        }
        // 逐頂點手動蒙皮（骨頭世界矩陣 × bindpose，跟 MonsterArmShedding.DetachArm 一樣）：座標一定是世界座標，不用猜 BakeMesh 的縮放／座標系
        if (_finaleVerts == null || _finaleVertsMesh != sm)
        {
            _finaleVerts = sm.vertices; _finaleBw = sm.boneWeights; _finaleBind = sm.bindposes; _finaleVertsMesh = sm;
        }
        Transform[] bones = _finaleBodySmr.bones;
        if (_finaleBw == null || _finaleBw.Length != _finaleVerts.Length || bones == null || bones.Length != _finaleBind.Length) { _finaleBodyValid = false; return false; }
        if (_finaleSkin == null || _finaleSkin.Length != bones.Length) _finaleSkin = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) _finaleSkin[i] = bones[i] != null ? bones[i].localToWorldMatrix * _finaleBind[i] : Matrix4x4.identity;
        float minY = float.PositiveInfinity, minX = float.PositiveInfinity;
        int n2 = Mathf.Min(_finaleVerts.Length, _finaleUsed.Length);
        for (int i = 0; i < n2; i++)
        {
            if (!_finaleUsed[i]) continue;
            BoneWeight bw = _finaleBw[i];
            Vector3 v = _finaleVerts[i], w = Vector3.zero;
            if (bw.weight0 > 0f) w += _finaleSkin[bw.boneIndex0].MultiplyPoint3x4(v) * bw.weight0;
            if (bw.weight1 > 0f) w += _finaleSkin[bw.boneIndex1].MultiplyPoint3x4(v) * bw.weight1;
            if (bw.weight2 > 0f) w += _finaleSkin[bw.boneIndex2].MultiplyPoint3x4(v) * bw.weight2;
            if (bw.weight3 > 0f) w += _finaleSkin[bw.boneIndex3].MultiplyPoint3x4(v) * bw.weight3;
            if (w.y < minY) minY = w.y;
            if (w.x < minX) minX = w.x;
        }
        if (float.IsInfinity(minY)) { _finaleBodyValid = false; return false; }
        _finaleBodyMinY = minY; _finaleBodyMinX = minX; _finaleBodyValid = true;
        return true;
    }

    /// <summary>測試／驗證用：目前量到的身體最低點與最左點。</summary>
    public bool TryGetFinaleBodyExtents(out float minY, out float minX) { minY = _finaleBodyMinY; minX = _finaleBodyMinX; return _finaleBodyValid; }

    private float _deathAnimSpeedSaved = -1f;   // ★1119b 倒下期間改過 Animator 速度時，存原本的速度（-1＝沒改）
    private CandleVoiceDirector _voiceDirector;

    // ★1119b 測試／驗證用：最近一次倒下演出的對齊結果
    public float DebugDeathSpeed { get; private set; } = 1f;
    public float DebugDeathVoiceEndUnscaled { get; private set; } = -1f;   // 最後一句語音預計結束（Time.unscaledTime），-1＝沒有可對齊的語音

    private void RestoreDeathAnimSpeed()
    {
        if (_deathAnimSpeedSaved >= 0f && _animator != null) _animator.speed = _deathAnimSpeedSaved;
        _deathAnimSpeedSaved = -1f;
    }

    /// <summary>
    /// ★1119b 倒下動畫要用幾倍速，才會在「最後一句燭火語音」播完的那一刻剛好結束。
    /// animLeft＝倒下動畫剩下的長度（秒，1× 時）。最後一句還在播＝剩餘時間；還在排隊＝前面那句播完＋間隔＋它的長度。
    /// 沒語音檔、導演不在、已經播完／被停掉、剩不到 0.25 秒＝回傳 1（維持原本的速度）。結果夾在 finaleDeathSpeedRange，夾到會在 Console 提示。
    /// </summary>
    private float FinaleDeathSpeed(float animLeft)
    {
        DebugDeathSpeed = 1f; DebugDeathVoiceEndUnscaled = -1f;
        if (_voiceDirector == null) _voiceDirector = FindFirstObjectByType<CandleVoiceDirector>();
        if (_voiceDirector == null || _voiceDirector.voices == null || _voiceDirector.voices.Length == 0 || animLeft <= 0.05f)
        {
            Debug.Log("【影子怪物】★1119b 找不到燭火語音導演，倒下動畫維持 1×");
            return 1f;
        }
        int idx = Mathf.Min(Mathf.Max(1, _totalCandles), _voiceDirector.voices.Length) - 1;   // 最後一根燭火對應的那一句（第 5 根＝voices[4]）
        float endAt;
        if (!_voiceDirector.TryGetExpectedEndTime(idx, out endAt))
        {
            Debug.Log($"【影子怪物】★1119b 最後一句語音（voices[{idx}]）沒在播也沒在排隊（沒放語音檔／已經播完／還沒點亮），倒下動畫維持 1×");
            return 1f;
        }
        float remain = endAt - Time.unscaledTime;
        DebugDeathVoiceEndUnscaled = endAt;
        if (remain < 0.25f)
        {
            Debug.Log($"【影子怪物】★1119b 最後一句語音只剩 {remain:F2}s，倒下動畫維持 1×");
            return 1f;
        }
        float lo = Mathf.Max(0.1f, Mathf.Min(finaleDeathSpeedRange.x, finaleDeathSpeedRange.y));
        float hi = Mathf.Max(lo, Mathf.Max(finaleDeathSpeedRange.x, finaleDeathSpeedRange.y));
        float raw = animLeft / remain;
        float speed = Mathf.Clamp(raw, lo, hi);
        if (!Mathf.Approximately(raw, speed))
            Debug.LogWarning($"【影子怪物】★1119b 倒下動畫要對齊最後一句語音需要 {raw:F2}×（動畫剩 {animLeft:F2}s、語音剩 {remain:F2}s），超出 {lo:F2}～{hi:F2}，夾成 {speed:F2}×（動畫會比語音{(speed > raw ? "早" : "晚")}結束 {Mathf.Abs(animLeft / speed - remain):F2}s）");
        else
            Debug.Log($"【影子怪物】★1119b 倒下動畫對齊最後一句語音（voices[{idx}]）：動畫剩 {animLeft:F2}s、語音剩 {remain:F2}s → {speed:F2}×");
        DebugDeathSpeed = speed;
        return speed;
    }

    private IEnumerator DeathFinaleSequence(CandleCollectible candle)
    {
        RestoreDeathAnimSpeed();
        DebugDeathSpeed = 1f; DebugDeathVoiceEndUnscaled = -1f;
        // 1. 停追逐（Vanishing 狀態下 Update 不移動、不抓人），音樂、恐懼、減速一律收掉
        currentState = MonsterState.Vanishing;
        StopChaseBGM(false);   // ★1119b 先開始淡出；量到倒下動畫的實際長度（下面）後，會把淡出長度改成「剛好到動畫結束」
        if (victoryReliefSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(victoryReliefSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(victoryReliefSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }
        if (_pm != null) _pm.currentSpeed = _pm.baseSpeed;
        RemoveFear();
        UnlockCamera();
        EnsurePlayerReference();

        float candleX = candle != null ? candle.transform.position.x : (player != null ? player.position.x : transform.position.x);
        float watchX = candleX + Mathf.Max(3f, finaleWatchOffsetX);

        // 2. ★1119 碰到最後一根燭火的那一刻就直接倒下：不播受擊（gethit3）、不滑動、不震退，怪物就留在碰到的位置
        float mx = transform.position.x;
        transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // 面向右邊（她要走去的觀景台方向），倒下途中不再轉向
        _hitAnimLockUntil = 0f;                                // 前一根的受擊鎖若還在，別擋住倒下
        if (_hitShrinkCoroutine != null) { StopCoroutine(_hitShrinkCoroutine); _hitShrinkCoroutine = null; }
        _isHitShrinking = false;
        _finaleCamMonsterX = mx;
        FinaleCamOn();
        Debug.Log($"【影子怪物】★1119 結局倒下演出開始：怪物 x={mx:F1} 當場倒下（不播受擊），她走到 x={watchX:F1}（燭火 x={candleX:F1}）");

        // 3. 同一時間：怪物直接在 Generic 骨架上播 death1（★1118c 你做的兩隻手版本 Mixamo 動畫），她自動走上觀景台
        float deathLen = 0f;
        float deathEndAt = float.PositiveInfinity;   // 倒下動畫＋停留結束的時間（量到長度後才定）
        if (_animator != null && !string.IsNullOrEmpty(deathAnimationName)
            && _animator.runtimeAnimatorController != null && _animator.HasState(0, Animator.StringToHash(deathAnimationName)))
        {
            _animator.CrossFadeInFixedTime(deathAnimationName, 0.12f, 0, 0f);   // 短交叉淡入：從現在的姿勢接過去，不跳
            _currentAnimName = deathAnimationName;
            {   // 量站著時的高度，倒下鏡頭用
                Bounds bb = new Bounds(transform.position, Vector3.zero); bool first = true;
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
                { if (first) { bb = smr.bounds; first = false; } else bb.Encapsulate(smr.bounds); }
                _finaleBodyH = bb.size.y;
                _finaleCamMonsterX = transform.position.x;
                _finaleDeathFrame = deathFrameCamera;
                _finaleAnchorInit = false; _finaleWarnedClamp = false;
            }
        }
        else
        {
            PlayAnimationByName(hitAnimationName, true);
            deathLen = 1.2f;
            deathEndAt = Time.time + deathLen + Mathf.Max(0f, deathHoldSeconds);
            Debug.LogWarning("【影子怪物】★1118 找不到死亡動畫（death1 狀態），改播受擊後淡出");
        }
        float deathStart = Time.time;
        bool deathMeasured = !float.IsPositiveInfinity(deathEndAt);

        if (_pm != null) { _pm.scriptedMove = true; _pm.scriptedMoveInput = 0f; }
        float t = 0f, stuckT = 0f, lastPx = player != null ? player.position.x : 0f;
        bool arrived = false; float arriveAt = 0f;
        // 「她走到、轉身看怪物」＋「倒下動畫＋停留」兩件都做完才往下（淡黑）；保底 12 秒
        while (t < 12f)
        {
            t += Time.deltaTime;
            if (!deathMeasured && t > 0.03f)
            {   // 等交叉淡入走完（轉場中 IsName 對不上），再讀 death1 的實際長度；0.6 秒還讀不到就用 3 秒保底
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                bool onDeath = !_animator.IsInTransition(0) && info.IsName(deathAnimationName);
                if (onDeath || t > 0.6f)
                {
                    deathLen = onDeath ? info.length : 3f;
                    float left = onDeath ? deathLen * Mathf.Clamp01(1f - info.normalizedTime) : deathLen - t;
                    // ★1119b 讓倒下動畫剛好和最後一句語音一起結束：調 Animator 速度（夾在 finaleDeathSpeedRange），1× ＝ 沒有語音可對齊
                    float speed = FinaleDeathSpeed(left);
                    if (!Mathf.Approximately(speed, 1f))
                    {
                        _deathAnimSpeedSaved = _animator.speed;
                        _animator.speed = speed;
                    }
                    float realLeft = left / speed;   // 實際還要幾秒（Animator 速度已套用）
                    deathEndAt = Time.time + realLeft + Mathf.Max(0f, deathHoldSeconds);
                    deathMeasured = true;
                    StopChaseBGM(false, Mathf.Max(finaleBgmFadeMinSeconds, realLeft));   // 追逐音樂：從現在淡到倒下動畫結束（最短 finaleBgmFadeMinSeconds）
                    Debug.Log($"【影子怪物】★1119 怪物當場播 {deathAnimationName}（{deathLen:F2} 秒，Generic 骨架，無受擊），速度 {speed:F2}×，動畫＋停留預計 +{deathEndAt - deathStart:F2}s 結束；追逐音樂 {Mathf.Max(finaleBgmFadeMinSeconds, realLeft):F2}s 淡出");
                }
            }
            if (_pm != null && player != null)
            {
                // 保險：她被卡住（碰撞接縫、擋路物）超過 1.2 秒沒前進，直接把她放到觀景台，不讓結局卡死
                if (!arrived)
                {
                    stuckT = player.position.x > lastPx + 0.02f ? 0f : stuckT + Time.deltaTime;
                    lastPx = player.position.x;
                    if (stuckT > 1.2f && Mathf.Abs(watchX - player.position.x) > 0.5f)
                    {
                        Debug.LogWarning($"【影子怪物】★1118 她走到 x={player.position.x:F1} 被卡住，直接放到觀景台 x={watchX:F1}");
                        _pm.WarpTo(new Vector3(watchX, player.position.y + 0.1f, player.position.z));
                        _pm.scriptedMove = true; _pm.scriptedMoveInput = 0f;
                        stuckT = 0f; lastPx = watchX;
                    }
                    float dx = watchX - player.position.x;
                    if (Mathf.Abs(dx) < 0.25f)
                    {
                        arrived = true; arriveAt = t;
                        _pm.scriptedMoveInput = 0f;
                        _pm.SetFacingScripted(-1f);   // 她停下、轉身看向怪物
                        Debug.Log($"【影子怪物】★1119 她走到觀景台（{t:F2}s）並轉身；倒下動畫＋停留預計 {Mathf.Max(0f, deathEndAt - Time.time):F2}s 後結束");
                    }
                    else _pm.scriptedMoveInput = Mathf.Sign(dx);
                }
            }
            else arrived = true;   // 沒有玩家＝不用等
            if (arrived && t >= arriveAt + 0.7f && deathMeasured && Time.time >= deathEndAt) break;
            yield return null;
        }
        if (!arrived) Debug.LogWarning($"【影子怪物】★1119 保底時間到她還沒走到觀景台（x={(player != null ? player.position.x : 0f):F1}/{watchX:F1}），直接往下");
        if (_pm != null) { _pm.scriptedMoveInput = 0f; if (!arrived) _pm.SetFacingScripted(-1f); }

        // ★1118d 接結局漫畫時：倒下的怪物和框好的鏡頭就留在畫面上，由 PlayEndingRoutine 把整個畫面淡成全黑（不淡出怪物、不收鏡頭）
        if (playEndingAfterAllCandles && !string.IsNullOrEmpty(endingBookScene))
        {
            if (_pm != null) _pm.scriptedMoveInput = 0f;
            RestoreDeathAnimSpeed();
            if (crumbleToOrbFinale && string.IsNullOrEmpty(endingCardId))
            {
                // ★1119c 碎裂 → 白色光球 → 飛向鏡頭全白；白幕（EndingFade）由碎裂演出自己開始，PlayEndingRoutine 看到就不再淡黑
                EndCredits.EndingMode = true;
                // ★1119e 先看結局漫畫，翻完回到這個畫面（倒下的怪物、她站在觀景台）才碎裂
                if (comicBeforeCrumble) yield return EndingComicInterlude();
                var crumble = GetComponent<MonsterDisintegrateFinale>();
                if (crumble == null) crumble = gameObject.AddComponent<MonsterDisintegrateFinale>();
                EndCredits.CreditsOnly = comicBeforeCrumble;   // 光球全白後進 Book：漫畫看過了，直接捲名單
                yield return crumble.Play(SetVisualAlpha, endingBookScene, _mainCam != null ? _mainCam : Camera.main);
            }
            currentState = MonsterState.Dormant;
            _vanishDone = true;
            Debug.Log("【影子怪物】★1118d 怪物倒下停在畫面上，接結局淡黑");
            yield break;
        }
        RestoreDeathAnimSpeed();

        // 6. 淡出
        float f = 0f, fade = Mathf.Max(0.1f, deathFadeSeconds);
        while (f < fade)
        {
            f += Time.deltaTime;
            SetVisualAlpha(1f - Mathf.Clamp01(f / fade));
            yield return null;
        }
        SetVisualAlpha(0f);
        FinaleCamOff();   // 鏡頭交還給她（慢慢回到跟著她）
        if (_pm != null) _pm.scriptedMoveInput = 0f;   // 她維持站著看（scriptedMove 保持開著，結局漫畫前不會自己亂走）
        currentState = MonsterState.Dormant;
        _vanishDone = true;
        Debug.Log("【影子怪物】★1118 怪物倒下、消散，接結局");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ★1119e 結局漫畫插在「倒下」和「碎裂」中間：Book 場景疊加載入，玻璃館留著（怪物倒下的姿勢、她的位置、鏡頭都不動），
    //   漫畫期間把玻璃館的鏡頭、聲音接收、UI、EventSystem 關掉、正在播的聲音暫停；翻完卸載 Book、全部還原。
    // ──────────────────────────────────────────────────────────────────────────
    private readonly List<Behaviour> _glassHidden = new List<Behaviour>();
    private readonly List<AudioSource> _glassPaused = new List<AudioSource>();
    private Scene _glassScene;

    private IEnumerator EndingComicInterlude()
    {
        bool finished = false;
        _glassScene = gameObject.scene;
        EndCredits.EndingMode = true;
        EndCredits.ComicFinishedHook = () => finished = true;
        while (EndingFade.Instance != null) yield return null;
        EndingFade.BeginCustom(LoadEndingComicAdditive, "疊加載入結局漫畫 " + endingBookScene,
            endingFadeOutSeconds, endingBlackHoldSeconds, endingFadeInSeconds, Color.black);
        Debug.Log("【影子怪物】★1119e 倒下演完 → 結局漫畫（翻完回玻璃館碎裂成光球）");
        while (!finished) yield return null;

        while (EndingFade.Instance != null) yield return null;
        EndingFade.BeginCustom(UnloadEndingComic, "卸載結局漫畫、回玻璃館", 1.2f, 0.4f, 1.2f, Color.black);
        while (EndingFade.Instance != null) yield return null;   // 等畫面完全淡回玻璃館
        yield return new WaitForSeconds(0.5f);                    // 先讓玩家看到倒下的怪物，再開始碎
        Debug.Log("【影子怪物】★1119e 回到玻璃館，開始碎裂");
    }

    private IEnumerator LoadEndingComicAdditive()
    {
        // 玻璃館鏡頭要在載入前關掉：PageBook 會拿現成的 Camera.main 來用，沒有才自己建一台。
        // 但 Book 載入要好幾幀，這中間沒有任何鏡頭，畫面會閃一下「Display 1 No cameras rendering」，
        // 所以載入期間放一台只畫黑色的暫代鏡頭（不標 MainCamera，PageBook 不會拿它），漫畫的鏡頭出現後再拆掉。
        SetGlassSceneHidden(true, false);
        SetGlassSceneHidden(true, true);
        var placeholder = new GameObject("結局漫畫載入中（暫代黑色鏡頭）");
        var phCam = placeholder.AddComponent<Camera>();
        phCam.clearFlags = CameraClearFlags.SolidColor;
        phCam.backgroundColor = Color.black;
        phCam.cullingMask = 0;
        phCam.depth = -100f;
        AsyncOperation op = null;
        try { op = SceneManager.LoadSceneAsync(endingBookScene, LoadSceneMode.Additive); }
        catch (System.Exception e) { Debug.LogError($"【影子怪物】★1119e 疊加載入 {endingBookScene} 失敗：{e.Message}"); }
        if (op != null) while (!op.isDone) yield return null;
        for (int i = 0; i < 30 && Camera.allCamerasCount < 2; i++) yield return null;   // 等 PageBook 建好自己的鏡頭
        Destroy(placeholder);
        Scene book = SceneManager.GetSceneByName(endingBookScene);
        if (book.IsValid() && book.isLoaded) SceneManager.SetActiveScene(book);
        else
        {
            // 載不到漫畫：不要卡死，直接當作看完
            Debug.LogError($"【影子怪物】★1119e 找不到 {endingBookScene} 場景，略過漫畫");
            var hook = EndCredits.ComicFinishedHook; EndCredits.ComicFinishedHook = null;
            hook?.Invoke();
        }
    }

    private IEnumerator UnloadEndingComic()
    {
        if (_glassScene.IsValid() && _glassScene.isLoaded) SceneManager.SetActiveScene(_glassScene);
        SetGlassSceneHidden(false, true);   // 先開回玻璃館的鏡頭再卸載 Book，中間才不會有沒鏡頭的幀
        Scene book = SceneManager.GetSceneByName(endingBookScene);
        if (book.IsValid() && book.isLoaded)
        {
            AsyncOperation op = SceneManager.UnloadSceneAsync(book);
            if (op != null) while (!op.isDone) yield return null;
        }
        SetGlassSceneHidden(false, false);
        yield return null;
    }

    private readonly List<Camera> _glassCams = new List<Camera>();

    /// <summary>cameras＝true 只處理鏡頭；false 處理聲音接收、UI、EventSystem 和聲音（兩者分開，換場時隨時都有一台鏡頭在畫）。</summary>
    private void SetGlassSceneHidden(bool hide, bool cameras)
    {
        if (hide)
        {
            if (cameras) _glassCams.Clear(); else { _glassHidden.Clear(); _glassPaused.Clear(); }
            if (!_glassScene.IsValid()) return;
            foreach (GameObject root in _glassScene.GetRootGameObjects())
            {
                if (cameras)
                {
                    foreach (var c in root.GetComponentsInChildren<Camera>()) if (c.enabled) { c.enabled = false; _glassCams.Add(c); }
                    continue;
                }
                foreach (var l in root.GetComponentsInChildren<AudioListener>()) if (l.enabled) { l.enabled = false; _glassHidden.Add(l); }
                foreach (var cv in root.GetComponentsInChildren<Canvas>()) if (cv.enabled && cv.isRootCanvas) { cv.enabled = false; _glassHidden.Add(cv); }
                foreach (var es in root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>()) if (es.enabled) { es.enabled = false; _glassHidden.Add(es); }
                foreach (var a in root.GetComponentsInChildren<AudioSource>()) if (a.isPlaying) { a.Pause(); _glassPaused.Add(a); }
            }
            Debug.Log(cameras
                ? $"【影子怪物】★1119e 漫畫載好，關掉玻璃館 {_glassCams.Count} 台鏡頭"
                : $"【影子怪物】★1119e 玻璃館暫時隱藏：關掉 {_glassHidden.Count} 個聲音接收／UI，暫停 {_glassPaused.Count} 個聲音");
        }
        else if (cameras)
        {
            foreach (var c in _glassCams) if (c != null) c.enabled = true;
            _glassCams.Clear();
        }
        else
        {
            foreach (var b in _glassHidden) if (b != null) b.enabled = true;
            foreach (var a in _glassPaused) if (a != null) a.UnPause();
            _glassHidden.Clear(); _glassPaused.Clear();
        }
    }

    private IEnumerator VanishSequence()
    {
        currentState = MonsterState.Vanishing;

        // 停止追逐背景音樂 (平滑淡出)
        StopChaseBGM(false);

        if (victoryReliefSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(victoryReliefSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(victoryReliefSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }
        else if (vanishSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(vanishSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(vanishSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 恢復玩家的移動速度
        if (_pm != null) _pm.currentSpeed = _pm.baseSpeed;
        RemoveFear();
        UnlockCamera();

        float t = 0f;
        float startMult = _currentScaleMultiplier;

        while (t < vanishDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(1f, 0f, t / vanishDuration);

            SetVisualAlpha(progress);
            _currentScaleMultiplier = startMult * progress;
            ApplyCurrentScale();

            yield return null;
        }

        SetVisualAlpha(0f);
        _currentScaleMultiplier = minScaleMultiplier;
        ApplyCurrentScale();
        currentState = MonsterState.Dormant;
        _vanishDone = true;
        Debug.Log("【影子怪物】玩家收集所有燭火，怪物消散！");
    }

    private void TriggerDevour()
    {
        if (currentState == MonsterState.Devouring) return;
        if (_lungePhase != 0)
        {
            float cl = player != null ? Mathf.Abs(player.position.x - transform.position.x) : -1f;
            Debug.Log($"【追逐節奏】★1118e 撲擊（{_lungeTag}）階段 {_lungePhase} 被抓到：離她 {cl:F1}m、怪物速度 {_lungeV:F1} m/s、規劃終點 X={_lungeXEnd:F1}、怪物 X={transform.position.x:F1}、她 X={(player != null ? player.position.x : 0f):F1}、撲擊中最近 {_lungeMinLag:F1}m");
            _lungePhase = 0;
        }
        currentState = MonsterState.Devouring;
        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(ClawAttackSequence());
    }

    /// <summary> 真實巨爪揮擊判定演出：抬手前搖 ➔ 巨爪揮下碰撞檢測 ➔ 命中定身與震動 ➔ 轉場重生 </summary>
    private IEnumerator ClawAttackSequence()
    {
        Debug.Log($"【影子怪物】進入揮爪距離，開始抬手準備揮擊 (attack2)...（怪物 x={transform.position.x:F1}、主角 x={(player != null ? player.position.x : 0f):F1}）");

        // 1. 播放 attack2 揮爪動畫 (開始抬手前搖)
        PlayAnimationByName(attackAnimationName);

        if (attackSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(attackSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(attackSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 2. 等待巨爪向下揮擊的命中點瞬間 (約 0.55 秒)
        yield return new WaitForSeconds(0.55f);

        // 3. 巨爪揮至最低點：檢測主角是否在爪擊命中範圍內 (純水平 X 軸判定，不受巨怪腳底 Y 軸高度差影響)
        bool isHit = false;
        if (player != null)
        {
            float xDist = Mathf.Abs(transform.position.x - player.position.x);
            if (xDist <= clawHitDistance)
            {
                isHit = true;
            }
        }

        if (isHit)
        {
            Debug.Log("💥【影子怪物】巨爪命中主角！定身並觸發重擊震動反饋！");

            // 命中主角時立即中斷追逐音樂
            StopChaseBGM(true);

            if (catchPlayerSFX != null)
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(catchPlayerSFX, player.position, sfxVolume);
                else AudioSource.PlayClipAtPoint(catchPlayerSFX, player.position, AudioManager.ScaleSfx(sfxVolume));
            }

            // 定身主角
            if (_pm != null)
            {
                _pm.isCutsceneFrozen = true;
                Rigidbody prb = _pm.GetComponent<Rigidbody>();
                if (prb != null) prb.linearVelocity = Vector3.zero;
            }

            // 觸發螢幕受傷震動與紅光閃爍
            if (ScreenFeedbackManager.Instance != null)
            {
                ScreenFeedbackManager.Instance.TriggerHitFeedback();
            }

            // 等待完整揮爪後續動態結束 (約 0.85 秒)
            yield return new WaitForSeconds(0.85f);

            // 觸發重生機制
            if (respawnSystem != null)
            {
                respawnSystem.TriggerRespawn();
            }

            if (_pm != null)
            {
                _pm.isCutsceneFrozen = false;
            }

            yield return new WaitForSecondsRealtime(1.5f);
            ResetToInitialState();
        }
        else
        {
            Debug.Log("💨【影子怪物】巨爪揮空 (主角已及時逃出範圍)，攻擊結束後繼續追擊！");
            yield return new WaitForSeconds(0.85f);
            currentState = _doomed ? MonsterState.Punishing : MonsterState.Chasing;   // ★1021 注定失敗的，抓空後維持懲罰追擊
        }
    }

    private void TriggerPunishment()
    {
        if (currentState == MonsterState.Punishing) return;
        currentState = MonsterState.Punishing;

        if (punishTeleportSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(punishTeleportSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(punishTeleportSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        _currentScaleMultiplier = 1f;
        ApplyCurrentScale();

        ApplyFear();
        LockCamera();

        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(GrowHaloRoutine(
            _currentScaleMultiplier,
            punishHaloMultiplier,
            punishHaloGrowDuration
        ));

        Debug.Log("【影子怪物】懲罰狀態：鏡頭鎖定、玩家減速、怪物恢復大小！");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：移動與動畫切換邏輯
    // ──────────────────────────────────────────────────────────────────────────

    private void EnsurePlayerReference()
    {
        if (player != null)
        {
            // ★1016 場景裡 player 欄位已經拖好時，原本直接 return、_pm 永遠是 null：
            //   怪物用的主角速度變成預設 5（實際 6）、結局時主角沒被定住。這裡補抓一次。
            if (_pm == null)
            {
                _pm = player.GetComponent<PlayerMovement>();
                if (_pm == null) _pm = player.GetComponentInChildren<PlayerMovement>();
                if (_pm == null) _pm = player.GetComponentInParent<PlayerMovement>();
            }
            return;
        }

        GameObject p = GameObject.FindWithTag("Player");
        if (p == null)
        {
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) p = pm.gameObject;
        }
        if (p == null) p = GameObject.Find("Player");

        if (p != null)
        {
            player = p.transform;
            _pm = player.GetComponent<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInChildren<PlayerMovement>();
            if (_pm == null) _pm = player.GetComponentInParent<PlayerMovement>();
            Debug.Log($"【影子怪物】成功連結玩家物件：{player.name}");
        }
    }

    /// <summary>
    /// 依落後距離決定速度，把怪物維持在 idealLagMin ~ idealLagMax 的距離帶內。
    ///
    /// 為什麼要做這個：原本最快只有 chaseSpeed(3.2) × runSpeedMultiplier(1.35) ＝ 4.32，
    /// 而玩家是 5，所以怪物永遠追不上，也永遠走不到後面那幾根燭火——
    /// 追逐沒有壓迫感，最後一根燭火也收不到。
    ///
    /// 落後太多 → 允許超過玩家速度追回來；貼太近 → 放慢，給玩家喘息，
    /// 在距離帶內 → 大致跟著玩家的速度走。
    /// </summary>
    private float SmartSpeed(float baseChaseSpeed, float lagDistance)
    {
        float playerSpeed = (_pm != null && _pm.baseSpeed > 0.1f) ? _pm.baseSpeed : 5f;
        float lo = Mathf.Min(idealLagMin, idealLagMax);
        float hi = Mathf.Max(idealLagMin, idealLagMax);

        float target;
        if (lagDistance > hi)
        {
            // 落後了：越遠追越快，最多到 catchUpMaxSpeed
            float t = Mathf.InverseLerp(hi, hi + 45f, lagDistance);
            target = Mathf.Lerp(playerSpeed * 1.05f, Mathf.Max(catchUpMaxSpeed, playerSpeed * 1.05f), t);
        }
        else if (lagDistance < lo)
        {
            // 太近了（含跑到玩家前面去的情況）：放慢
            float t = Mathf.InverseLerp(lo, 0f, lagDistance);
            target = Mathf.Lerp(playerSpeed * 0.92f, Mathf.Max(0.5f, easeOffSpeed), t);
        }
        else
        {
            // 在距離帶內：跟著玩家走，讓距離慢慢收斂
            target = playerSpeed * 0.98f;
        }

        // 別低於原本設定的基礎速度太多，也別瞬間變速
        target = Mathf.Max(target, Mathf.Min(baseChaseSpeed, easeOffSpeed));
        if (_smoothedChaseSpeed < 0f) _smoothedChaseSpeed = target;
        _smoothedChaseSpeed = Mathf.MoveTowards(_smoothedChaseSpeed, target, Mathf.Max(0.1f, speedSmooth) * Time.deltaTime);
        return _smoothedChaseSpeed;
    }

    /// <summary>只剩最後一根燭火、怪物快到它時，進入衝刺嚇人模式。</summary>
    private void TryStartFinaleDash()
    {
        if (_revealActive || player == null || candles == null || _totalCandles <= 0) return;
        _candlesCollected = RecountCollected();   // ★1102 以燭火實際狀態為準
        if (_candlesCollected != _totalCandles - 1) return;
        if (_doomed || MissedCount() > 0) return;   // ★1021 有漏點的燭火：不進「只嚇人不攻擊」的最後衝刺

        CandleCollectible remaining = null;
        foreach (var c in candles)
        {
            if (c != null && !c.isCollected) { remaining = c; break; }
        }
        if (remaining == null) return;

        // 用「主角離最後一根燭火多遠」判斷：玩家走到最後一段，怪物才把距離追回來。
        // 不用「怪物離燭火多遠」，因為剛吃到倒數第二根時怪物就在附近，會一吃完立刻暴衝。
        float mx = transform.position.x;
        float cx = remaining.transform.position.x;
        if (Mathf.Abs(player.position.x - cx) > finaleDashTriggerDistance) return;
        if (player.position.x < mx) return;

        _finaleDash = true;
        _finaleSpeed = -1f;
        _finalePrevPx = float.NaN; _finaleApproach = -1f;
        _finaleStartTime = Time.time;
        if (_pm != null) _pm.currentSpeed = _pm.baseSpeed;   // 別讓主角停在被減速的狀態
        Debug.Log($"【影子怪物】主角接近最後一根燭火，怪物開始追回距離到主角身後 {finaleStopDistance} 公尺（不攻擊）。（收集 {_candlesCollected}/{_totalCandles}、漏 {MissedCount()}、剩下 {remaining.name} X={remaining.transform.position.x:F1}、怪物 X={mx:F1}）");
    }

    /// <summary>★1102 最後衝刺時還沒定案的那根燭火（沒被收、沒漏掉）。</summary>
    private CandleCollectible FinaleRemainingCandle()
    {
        if (candles == null) return null;
        foreach (var c in candles) if (c != null && !c.isCollected && !c.missed) return c;
        return null;
    }

    private void FinaleDashMove()
    {
        CandleCollectible rest = FinaleRemainingCandle();

        // ★1102 保險：衝刺拖太久還沒結束（燭火卡在「沒收也沒漏」之類），強制定案，怪物才不會永遠跟著不攻擊
        if (rest != null && Time.time - _finaleStartTime > Mathf.Max(5f, finaleMaxSeconds))
        {
            Debug.LogWarning($"【影子怪物】★1102 最後衝刺超過 {finaleMaxSeconds:F0} 秒，強制定案 {rest.name}（已點亮={rest.IsLitByPlayer}）");
            _finaleStartTime = Time.time;
            rest.ForceResolve();
            if (!_finaleDash) return;   // 定案後衝刺已被關掉（收完或進懲罰）
            rest = FinaleRemainingCandle();
        }

        float dir = player.position.x >= transform.position.x ? 1f : -1f;
        float targetX = player.position.x - dir * finaleStopDistance;

        // ★1102 最後一根還沒點亮：怪物停在燭火前面等玩家點完，不然點到一半怪物碰到、燭火被打碎，就再也定不了案。
        //   玩家已經走過燭火還沒點，就放行讓怪物衝過去，照漏點處理。
        if (rest != null && dir > 0f && !rest.IsLitByPlayer && player.position.x < rest.transform.position.x + finalePassMargin)
        {
            // ★1119 燭台改成「手碰到就算」之後，要用手的位置來停，不然手會伸到燭台上方等她點
            float reach = candleHitByFrontEdge ? Mathf.Max(candleReachX, FrontReachForHold(rest.transform.position) + candleFrontTouchMargin) : candleReachX;
            float holdX = rest.transform.position.x - reach - finaleWaitMargin;
            targetX = transform.position.x >= holdX ? transform.position.x : Mathf.Min(targetX, holdX);
        }
        float gap = Mathf.Abs(targetX - transform.position.x);
        float playerSpeed = (_pm != null && _pm.baseSpeed > 0.1f) ? _pm.baseSpeed : 5f;
        // 落後越多越快，越接近越緩：從「比主角快一點」平滑加到 finaleDashSpeed，不會突然暴衝
        float k = Mathf.Clamp01(gap / Mathf.Max(1f, finaleEaseDistance));
        float want = gap > 0.3f ? Mathf.Lerp(playerSpeed * 1.0f, Mathf.Max(finaleDashSpeed * difficultyScale, playerSpeed * 1.05f), k) : playerSpeed;
        // ★1118e 她停下來點最後一根燭火時：原本怪物用「追趕速度」衝到她身後 finaleStopDistance 就一幀硬停——
        //   現在跟一般燭火一樣，開始點的那一刻算一個固定逼近速度，剛好在放開那一刻走到目標，沒有「衝過去再煞住」
        bool finaleLocked = IsPlayerLightingCandle() || CandleAuraGlow.LightingReleaseAt + postLightGrace > Time.time;
        if (finaleLocked)
        {
            if (_finaleApproach < 0f)
            {
                float lockSec = Mathf.Max(0.5f, CandleAuraGlow.LightingReleaseAt + postLightGrace - Time.time);
                _finaleApproach = Mathf.Clamp(Mathf.Max(0f, gap) / lockSec, 0.8f, Mathf.Max(0.8f, finaleDashSpeed));
                Debug.Log($"【追逐節奏】★1118e 最後一根點燭火：離目標 {gap:F1}m、鎖定 {lockSec:F2}s → 固定逼近速度 {_finaleApproach:F2}m/s");
            }
            want = _finaleApproach;
        }
        else _finaleApproach = -1f;
        if (_finaleSpeed < 0f) _finaleSpeed = _smoothedChaseSpeed > 0f ? _smoothedChaseSpeed : playerSpeed * 0.98f;
        _finaleSpeed = Mathf.MoveTowards(_finaleSpeed, want, Mathf.Max(0.1f, finaleAccel) * Time.deltaTime);   // 速度只能慢慢升降
        float spd = _finaleSpeed;
        // ★1118e 她站住（點燭火）、怪物抵達「身後 finaleStopDistance」時原本是從全速一幀硬停（實測 6 m/s → 0）。
        //   這一關最後一根只嚇人不攻擊，怪物終究要停在她身後，但至少是減速進站：速度上限 ＝ √(2·a·剩餘距離)，a ＝ finaleStopDecel
        float pdt = Mathf.Max(0.0001f, Time.deltaTime);
        float pv = float.IsNaN(_finalePrevPx) ? playerSpeed : Mathf.Abs(player.position.x - _finalePrevPx) / pdt;
        _finalePrevPx = player.position.x;
        _finalePlayerV = Mathf.Lerp(_finalePlayerV, pv, 1f - Mathf.Exp(-8f * pdt));
        if (_finalePlayerV < 1.5f)
            spd = Mathf.Min(spd, Mathf.Sqrt(2f * Mathf.Max(1f, finaleStopDecel) * gap) + 0.15f);
        PlayAnimationByName(runAnimationName);

        float newX = Mathf.MoveTowards(transform.position.x, targetX, spd * Time.deltaTime);
        transform.position = new Vector3(newX, transform.position.y, transform.position.z);

        if (player.position.x > transform.position.x + 0.2f) transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        else if (player.position.x < transform.position.x - 0.2f) transform.rotation = Quaternion.Euler(0f, -90f, 0f);
    }

    /// <summary>★1104 實際安全距離：至少要比揮爪起手距離（場景裡實際用的怪物是 10 公尺）再多 6 公尺，不然「放開時還有 9 公尺」其實已經在爪子範圍內。</summary>
    private float SafeLag() { return Mathf.Max(safeLagAtRelease, attackTriggerDistance + 6f); }

    /// <summary>
    /// ★1102 點燭火的安全網（不改 ★1031 的「點燭火時怪物照樣逼近」）：
    /// 預估「她被放開那一刻」怪物離她多遠，夠遠（≥ SafeLag()）就完全不干預；
    /// 會太近才把速度壓到「剛好落在 SafeLag()」，而且有加速度限制、不會降到 0，看起來是怪物自己的步調。
    /// 沒有在點燭火時原樣放行速度（不影響 ★1102 不停下的其他邏輯）。
    /// </summary>
    private float PaceGovernor(float speed, float lag)
    {
        float dt = Time.deltaTime;
        float playerSpeed = (_pm != null && _pm.baseSpeed > 0.1f) ? _pm.baseSpeed : 5f;
        float dist = Mathf.Abs(lag);
        float cap = float.PositiveInfinity;
        string why = "-";

        float busy = CandleAuraGlow.LightingReleaseAt + postLightGrace - Time.time;
        bool busyNow = lag > 0f && busy > 0f;
        if (busyNow)
        {
            float allowed = (dist - SafeLag()) / busy;
            cap = Mathf.Max(playerSpeed * paceMinSpeedFactor, allowed);
            if (cap < speed) { why = "點燭安全網"; _paceLastCapAt = Time.time; }
            else why = "點燭";
        }

        if (!busyNow && anticipateDistance > 0f && lag > 0f && candles != null)
        {
            // ★1104 她正走向下一根還沒點的燭火：提前很緩地把距離拉開，點燭火那一刻就不用突然掉速
            float px2 = player.position.x, nx = float.MaxValue;
            for (int i = 0; i < candles.Length; i++)
            {
                CandleCollectible c = candles[i];
                if (c == null || c.isCollected || c.missed || c.IsLitByPlayer) continue;
                float cx = c.transform.position.x;
                if (cx >= px2 - 1f && cx < nx) nx = cx;
            }
            float toCandle = nx - px2;
            if (nx < float.MaxValue && toCandle > 0.5f && toCandle <= anticipateDistance)
            {
                float t = toCandle / playerSpeed;
                float need = SafeLag() + speed * anticipateLockSeconds - dist;   // 到燭火前還要多拉開幾公尺
                if (need > 0f)
                {
                    float slow = Mathf.Min(need / Mathf.Max(0.5f, t), anticipateMaxSlow);
                    cap = Mathf.Max(playerSpeed * paceMinSpeedFactor, playerSpeed - slow);
                    if (cap < speed) { why = "預先拉開"; _paceLastCapAt = Time.time; }
                }
            }
        }

        float target = Mathf.Min(speed, cap);
        if (_paceCap < 0f) _paceCap = speed;
        if (cap < speed) _paceCap = Mathf.MoveTowards(_paceCap, target, Mathf.Max(0.1f, paceAccel) * dt);
        else if (_paceCap < speed - 0.01f && Time.time < _paceLastCapAt + 2f) _paceCap = Mathf.MoveTowards(_paceCap, speed, Mathf.Max(0.1f, paceAccel) * dt);   // 剛解除安全網：速度緩緩回升
        else _paceCap = speed;

        // 事件紀錄：點燭火開始／結束時的距離，探針才抓得到「放開時到底剩幾公尺」
        if (busyNow && !_paceBusyPrev) { _paceBusyStartLag = dist; _paceBusyStartAt = Time.time; _paceMinLag = dist; }
        if (busyNow) _paceMinLag = Mathf.Min(_paceMinLag, dist);
        if (!busyNow && _paceBusyPrev)
        {
            string msg = $"【怪物節奏】點燭結束：開始時落後 {_paceBusyStartLag:F1}m → 放開時 {dist:F1}m（目標≥{SafeLag():F0}m、歷時 {Time.time - _paceBusyStartAt:F1}s、調節後速度 {_paceCap:F1}）";
            if (dist < SafeLag()) Debug.LogWarning(msg + " ★太近，節奏調節沒顧到");
            else Debug.Log(msg);
        }
        _paceBusyPrev = busyNow;

        DebugLag = lag; DebugSpeed = _paceCap; DebugPace = why;
        return _paceCap;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ★1116 追逐節奏導演
    // ══════════════════════════════════════════════════════════════════════════
    private float _nextDirectorLog;

    /// <summary>★1116 重生／重置：節奏導演的狀態全部歸零（險些被抓的橋段重新可以觸發）。</summary>
    private void ResetDirectorState()
    {
        _dirSpeed = -1f;
        _playerPrevX = float.NaN;
        _playerFwdSmooth = 0f;
        _reliefStart = _surgeStart = _knockStart = -999f;
        _reliefAmount = 0f;
        _lungePhase = 0;
        _beatDone = null;
        _beatTriedIdx = -1;
        _lungeLast = Time.time;          // ★1118e 重生／重置後從頭算隨機撲擊的冷卻
        _lungeRandomWait = -1f;
        _approachSpeed = -1f;
    }

    private void SetMonsterX(float x)
    {
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
    }

    private void FaceTowardPlayer()
    {
        if (player.position.x < transform.position.x - 0.2f) transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        else if (player.position.x > transform.position.x + 0.2f) transform.rotation = Quaternion.Euler(0f, 90f, 0f);
    }

    /// <summary>原本的「怪物貼很近時她變慢」（照舊）。</summary>
    private void ApplyPlayerSlowdown(float dist)
    {
        if (_pm == null) return;
        if (dist < slowdownDistance) _pm.currentSpeed = _pm.baseSpeed * slowdownFactor;
        else if (_pm.currentSpeed < _pm.baseSpeed) _pm.currentSpeed = _pm.baseSpeed;
    }

    /// <summary>★1116 吃到燭火：被光震退＋喘息。</summary>
    private void StartCandleRecoil()
    {
        StartKnockback(candleKnockback);
        StartRelief(1f);
    }

    private void StartKnockback(float distance)
    {
        _knockStart = Time.time;
        _knockDone = 0f;
        _knockDist = Mathf.Max(0f, distance);
        _knockDir = player != null && player.position.x >= transform.position.x ? -1f : 1f;   // 往遠離她的方向退
    }

    private void StartRelief(float amount)
    {
        // 正在喘息又來一次：取比較大的那個，不會把剛開始的喘息縮短
        if (Relief01 > amount) return;
        _reliefStart = Time.time;
        _reliefAmount = amount;
    }

    private void PlayMonsterRoar(float volume)
    {
        AudioClip clip = roarSFX;
        if (clip == null)
        {
            if (_roarClip == null) _roarClip = Resources.Load<AudioClip>("Sfx/Glass_MonsterRoar");
            clip = _roarClip;
        }
        if (clip == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(clip, volume);
        else AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : transform.position, AudioManager.ScaleSfx(volume));
    }

    private void PlayLungeSting()
    {
        if (_lungeClip == null) _lungeClip = Resources.Load<AudioClip>("Sfx/Glass_ChaseLunge");
        if (_lungeClip == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(_lungeClip, 0.8f);
        else AudioSource.PlayClipAtPoint(_lungeClip, Camera.main != null ? Camera.main.transform.position : transform.position, AudioManager.ScaleSfx(0.8f));
    }

    private static void Shake(float seconds, float power)
    {
        if (ScreenFeedbackManager.Instance != null) ScreenFeedbackManager.Instance.TriggerShake(seconds, power);
    }

    /// <summary>★1116 燭火被吃的那一刻爆一下光（暖色點光源，1 秒內退掉，自己銷毀）。</summary>
    private void SpawnLightBurst(Vector3 pos)
    {
        if (candleBurstIntensity <= 0f) return;
        GameObject go = new GameObject("CandleBurstLight");
        go.transform.position = pos + new Vector3(0f, 1.5f, -3f);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.82f, 0.5f);
        l.range = Mathf.Max(1f, candleBurstRange);
        l.intensity = candleBurstIntensity;
        go.AddComponent<FadeAndDestroyLight>().duration = 1f;
    }

    /// <summary>★1116 縮放倍率：吃一根縮一格，漏一根長回 missedRegrow。</summary>
    private float TargetScaleMult()
    {
        if (shedArmsInsteadOfShrink) return 1f;   // ★1118b 不縮小也不因漏點長大：大小固定，變的是手臂數量
        float m = 1f - shrinkPerCandle * _candlesCollected;
        if (usePacingDirector) m += Mathf.Max(0f, missedRegrow) * MissedCount();
        return Mathf.Clamp(m, minScaleMultiplier, 1f);
    }

    /// <summary>★1118b 吃到 count 根燭火時的大小倍率：掉手臂模式維持原大小，舊模式照 1 − 每根縮小量 × 根數。</summary>
    private float CandleScaleMult(int count)
    {
        if (shedArmsInsteadOfShrink) return 1f;
        return Mathf.Max(minScaleMultiplier, 1f - shrinkPerCandle * count);
    }

    /// <summary>★1118b 手臂數量對齊「已吃燭火數」（最多掉 4 隻）。animate＝真的演一次手臂掉下去；不然直接換網格。</summary>
    private void SyncArms(bool animate)
    {
        MonsterArmShedding sh = GetComponent<MonsterArmShedding>();
        if (sh == null) return;
        sh.SetShedCount(shedArmsInsteadOfShrink ? Mathf.Min(MonsterArmShedding.ArmCount, _candlesCollected) : 0, animate);
    }

    private IEnumerator GrowToRoutine(float targetMult, float seconds)
    {
        float start = _currentScaleMultiplier, t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            _currentScaleMultiplier = Mathf.Lerp(start, targetMult, Mathf.SmoothStep(0f, 1f, t / seconds));
            ApplyCurrentScale();
            yield return null;
        }
        _currentScaleMultiplier = targetMult;
        ApplyCurrentScale();
        _hitShrinkCoroutine = null;
    }

    /// <summary>★1116 漏點燭火的當下：吼、長大、逼近幾秒、畫面震一下。</summary>
    private void ReactToMissedCandle(CandleCollectible candle)
    {
        if (!usePacingDirector) return;
        PlayMonsterRoar(1f);
        Shake(0.5f, 0.35f);
        _surgeStart = Time.time;
        if (_hitShrinkCoroutine != null) StopCoroutine(_hitShrinkCoroutine);
        _isHitShrinking = false;
        _hitShrinkCoroutine = StartCoroutine(GrowToRoutine(TargetScaleMult(), 0.8f));
        Debug.Log($"【追逐節奏】★1116 漏點 {candle.gameObject.name}：怪物吼、長大到 ×{TargetScaleMult():F2}、{missedSurgeSeconds:F0} 秒內逼近 {missedSurgeLag:F0} 公尺");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ★1118e 追逐撲擊
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>下一根還沒點的燭火離她多遠（她前方，公尺）。沒有＝無限大。</summary>
    private float NextUnlitCandleAhead(float px)
    {
        float best = float.PositiveInfinity;
        if (candles == null) return best;
        foreach (var c in candles)
        {
            if (c == null || c.isCollected || c.missed || c.IsLitByPlayer) continue;
            float d = c.transform.position.x - px;
            if (d > -1f && d < best) best = d;
        }
        return best;
    }

    /// <summary>撲擊的規劃（純算術）：給起跑速度 v0、她的速度 vp、現在的落後 lag0，回傳各段時間／距離。tC 小於 0＝距離太近，撲不起來。</summary>
    private void PlanLungeMath(float v0, float vp, float lag0, out float endLag, out float vEnd,
                               out float tT, out float dT, out float tA, out float dA, out float tC, out float dC, out float tB, out float dB)
    {
        endLag = attackTriggerDistance + Mathf.Max(0f, lungeEndExtraLag);
        float vPk = Mathf.Max(vp + 1f, lungePeakSpeed);
        vEnd = Mathf.Clamp(vp * lungeEndSpeedFactor, 0.5f, vPk - 0.5f);
        float aA = Mathf.Max(1f, lungeAccel), aB = Mathf.Max(1f, lungeBrakeDecel);
        tT = Mathf.Max(0f, lungeTelegraphSeconds);
        dT = v0 * tT;
        tA = Mathf.Max(0f, (vPk - v0) / aA);
        dA = (vPk * vPk - v0 * v0) / (2f * aA);
        tB = (vPk - vEnd) / aB;
        dB = (vPk * vPk - vEnd * vEnd) / (2f * aB);
        // 相對閉合量 ＝ 怪物走的距離 − 她同時間走的距離；要閉合 lag0 − endLag
        float closing = (dT - vp * tT) + (dA - vp * tA) + (dB - vp * tB);   // 預告＋加速＋煞車這三段本來就會多靠近她幾公尺
        float need = (lag0 - endLag) - closing;
        // ★1119 終點離她 14 公尺比導演後半段的距離（13.5～15）還遠：後半段的固定橋段會因為「距離太近」撲不出去。
        //   太近時把終點縮短到剛好不用巡航（縮短量＝不夠的那幾公尺），但至少留 揮爪距離+1.5 公尺（絕不直接貼到她）
        if (need < 0f)
        {
            float shorter = Mathf.Max(attackTriggerDistance + 1.5f, endLag + need);
            endLag = Mathf.Min(endLag, shorter);
            need = (lag0 - endLag) - closing;
        }
        tC = need / Mathf.Max(0.5f, vPk - vp);
        dC = vPk * tC;
    }

    private string _lungeWhy = "";
    private float _lungeEndLagPlanned;

    /// <summary>★1118e 在「現在」規劃一次撲擊。成功＝開始撲，回傳 true；條件不合或距離太近回傳 false。</summary>
    private bool TryStartLunge(string tag, bool checkConditions)
    {
        if (player == null) return false;
        float px = player.position.x, mx = transform.position.x;
        float side = px >= mx ? 1f : -1f;
        float lag0 = Mathf.Abs(px - mx);
        float vp = _playerFwdSmooth;
        string why = null;
        if (side < 0f) why = "她在怪物左邊";
        else if (currentState != MonsterState.Chasing) why = "不是 Chasing";
        else if (_revealActive || IsRevealRunning) why = "登場運鏡中";
        else if (_finaleDash) why = "最後衝刺";
        else if (PlayerRespawnSystem.IsAnyRespawning) why = "重生中";
        else if (lag0 > lungeMaxLag) why = "太遠";
        if (checkConditions && why == null)
        {
            if (IsPlayerLightingCandle() || CandleAuraGlow.LightingReleaseAt + postLightGrace > Time.time) why = "點燭火／放開寬限";
            else if (vp < lungeMinPlayerSpeed) why = $"她沒在跑（{vp:F1}m/s）";
            else if (Relief01 > 0.001f) why = "喘息中";
            else if (Surge01 > 0.001f) why = "漏點逼近中";
            else if (Time.time - _knockStart < candleKnockbackSeconds + 1f) why = "震退中";
            else if (NextUnlitCandleAhead(px) < lungeNoCandleAhead) why = "前方 " + lungeNoCandleAhead.ToString("F0") + "m 內有沒點的燭火";
        }
        if (why != null) { _lungeWhy = why; return false; }

        float v0 = Mathf.Max(_dirSpeed, Mathf.Max(1f, directorMinSpeed));
        vp = Mathf.Max(vp, 0.5f);
        PlanLungeMath(v0, vp, lag0, out float endLag, out float vEnd, out float tT, out float dT, out float tA, out float dA,
                      out float tC, out float dC, out float tB, out float dB);
        if (tC < 0f) { _lungeWhy = $"距離太近（lag {lag0:F1}m，需要 cruise {tC:F2}s）"; return false; }

        float tToBrakeEnd = tT + tA + tC + tB;
        _lungeSide = side;
        _lungePlayerV = vp;
        _lungeV0 = v0;
        _lungeVEnd = vEnd;
        _lungeXStart = mx;
        _lungeEndLagPlanned = endLag;
        _lungeXEnd = px + side * (vp * tToBrakeEnd - endLag);
        _lungeXBrake = _lungeXEnd - side * dB;
        _lungeLag0 = lag0;
        _lungeMinLag = lag0;
        _lungePlanLen = dT + dA + dC + dB;
        _lungeTag = tag;
        _lungeV = v0;
        _lungePhase = 1;
        _lungePhaseStart = Time.time;
        _lungeLast = Time.time;
        _lungeRandomWait = -1f;
        _dirSpeed = v0;
        PlayAnimationByName(runAnimationName);
        PlayMonsterRoar(0.9f);
        PlayLungeSting();
        Debug.Log($"【追逐節奏】★1118e 撲擊（{tag}）規劃：lag0={lag0:F1}m 她 {vp:F1}m/s 怪物起跑 {v0:F1}m/s → 總路徑 {_lungePlanLen:F1}m（預告 {dT:F1}m/{tT:F2}s、加速 {dA:F1}m/{tA:F2}s、巡航 {dC:F1}m/{tC:F2}s@{Mathf.Max(vp + 1f, lungePeakSpeed):F0}m/s、煞車 {dB:F1}m/{tB:F2}s→{vEnd:F1}m/s、收尾 {vEnd * lungeRecoverySeconds:F1}m/{lungeRecoverySeconds:F1}s）" +
                  $"｜怪物 X {mx:F1} → 煞車起點 {_lungeXBrake:F1} → 終點 X_end {_lungeXEnd:F1}（她預測 {px + side * vp * tToBrakeEnd:F1}，endLag {endLag:F1}m）");
        return true;
    }

    private void EndLunge(string reason, bool relief)
    {
        if (_lungePhase == 0) return;
        float lag = player != null ? Mathf.Abs(player.position.x - transform.position.x) : -1f;
        Debug.Log($"【追逐節奏】★1118e 撲擊（{_lungeTag}）結束（{reason}）：離她 {lag:F1}m、撲擊中最近 {_lungeMinLag:F1}m、怪物實際走 {Mathf.Abs(transform.position.x - _lungeXStart):F1}m（規劃到煞車終點 {_lungePlanLen:F1}m）、沒被抓");
        _lungePhase = 0;
        if (relief)
        {
            StartRelief(Mathf.Clamp01(nearMissRecoverLag / Mathf.Max(0.1f, candleReliefLag)));
            _dirSpeed = _lungeVEnd;
        }
    }

    /// <summary>★1118e 測試鍵／測試腳本：不看冷卻與觸發位置，立刻照現況規劃撲擊（checkConditions=false 連安全條件也略過）。</summary>
    public bool TesterTriggerLunge(bool checkConditions = true)
    {
        bool ok = TryStartLunge("測試", checkConditions);
        if (!ok) Debug.Log($"【追逐節奏】★1118e 測試撲擊沒開始：{_lungeWhy}");
        return ok;
    }

    /// <summary>★1118e 追逐撲擊：固定位置＋隨機。回傳 true＝這一幀由撲擊接管。</summary>
    private bool RunLunge(float px, float lag, float side, float dt)
    {
        if (_lungePhase == 0)
        {
            if (!useNearMissBeats && !useRandomLunges) return false;
            if (currentState != MonsterState.Chasing || _revealActive || side < 0f) return false;
            if (AssistLevel >= 2) return false;   // ★1125 在同一個存檔點連死 3 次以上：先不撲擊，讓她有機會過去

            // 固定位置：到了就試，條件不合（停、點燭火…）在 12 公尺內每幀再試，過了就放棄這一次
            if (useNearMissBeats && nearMissBeatsX != null && nearMissBeatsX.Length > 0)
            {
                if (_beatDone == null || _beatDone.Length != nearMissBeatsX.Length) _beatDone = new bool[nearMissBeatsX.Length];
                for (int i = 0; i < nearMissBeatsX.Length; i++)
                {
                    if (_beatDone[i] || px < nearMissBeatsX[i]) continue;
                    if (px - nearMissBeatsX[i] > 12f)
                    {
                        _beatDone[i] = true;
                        Debug.Log($"【追逐節奏】★1118e 固定撲擊 #{i + 1}（X={nearMissBeatsX[i]:F0}）放棄：{_lungeWhy}");
                        continue;
                    }
                    if (TryStartLunge($"固定 #{i + 1} X={nearMissBeatsX[i]:F0}", true)) { _beatDone[i] = true; return RunLungeStep(px, dt); }
                    break;   // 先到的這個還在等條件，後面的不用看
                }
            }
            // 隨機額外撲擊
            if (useRandomLunges)
            {
                if (_lungeRandomWait < 0f) _lungeRandomWait = Random.Range(Mathf.Min(randomLungeCooldownMin, randomLungeCooldownMax), Mathf.Max(randomLungeCooldownMin, randomLungeCooldownMax));
                if (Time.time - _lungeLast >= _lungeRandomWait && TryStartLunge("隨機", true)) return RunLungeStep(px, dt);
            }
            return false;
        }
        return RunLungeStep(px, dt);
    }

    private bool RunLungeStep(float px, float dt)
    {
        // 被別的流程打斷（登場、重生、狀態改變）：直接放棄
        if (currentState != MonsterState.Chasing || _revealActive || PlayerRespawnSystem.IsAnyRespawning) { EndLunge("被打斷", false); return false; }
        float s = _lungeSide;
        float mx = transform.position.x;
        float vPk = Mathf.Max(_lungePlayerV + 1f, lungePeakSpeed);
        float aA = Mathf.Max(1f, lungeAccel), aB = Mathf.Max(1f, lungeBrakeDecel);
        float elapsed = Time.time - _lungePhaseStart;
        FaceTowardPlayer();

        if (_lungePhase == 1)   // 預告：吼，速度不變
        {
            _lungeV = _lungeV0;
            if (elapsed >= Mathf.Max(0f, lungeTelegraphSeconds)) { _lungePhase = 2; _lungePhaseStart = Time.time; }
        }
        else if (_lungePhase == 2)   // 加速
        {
            _lungeV = Mathf.MoveTowards(_lungeV, vPk, aA * dt);
            if (_lungeV >= vPk - 0.001f) { _lungePhase = 3; _lungePhaseStart = Time.time; }
        }
        if (_lungePhase == 3)   // 巡航：到煞車起點為止
        {
            _lungeV = vPk;
            if ((_lungeXBrake - mx) * s <= 0f) EnterLungeBrake();
        }
        else if (_lungePhase == 2 && (_lungeXBrake - mx) * s <= 0f) EnterLungeBrake();   // 加速段還沒到頂就到煞車點（極短路徑）

        if (_lungePhase == 4)   // 煞車：速度只看「離終點多遠」，v = sqrt(vEnd² + 2·a·剩餘距離)，平滑降到 vEnd
        {
            float rem = (_lungeXEnd - mx) * s;
            _lungeV = Mathf.Min(_lungeV, Mathf.Sqrt(_lungeVEnd * _lungeVEnd + 2f * aB * Mathf.Max(0f, rem)));
            _lungeV = Mathf.Max(_lungeV, _lungeVEnd);
            float step = _lungeV * dt;
            if (rem - step <= 0f)
            {
                SetMonsterX(_lungeXEnd);
                _lungeV = _lungeVEnd;
                _lungePhase = 5;
                _lungePhaseStart = Time.time;
                float lagEnd = Mathf.Abs(px - _lungeXEnd);
                _lungeMinLag = Mathf.Min(_lungeMinLag, lagEnd);
                Debug.Log($"【追逐節奏】★1118e 撲擊（{_lungeTag}）煞車結束：離她 {lagEnd:F1}m（預期約 {_lungeEndLagPlanned:F1}m）、怪物實際走 {Mathf.Abs(_lungeXEnd - _lungeXStart):F1}m（規劃 {_lungePlanLen:F1}m）、速度 {_lungeV:F1}m/s、撲擊中最近 {_lungeMinLag:F1}m");
                _dirSpeed = _lungeV;
                DebugLag = lagEnd; DebugSpeed = _lungeV; DebugPace = "撲擊4";
                return true;
            }
            SetMonsterX(mx + s * step);
        }
        else if (_lungePhase == 5)   // 收尾：以煞車結束速度繼續往前，不停
        {
            _lungeV = _lungeVEnd;
            SetMonsterX(mx + s * _lungeV * dt);
            if (!_isHitShrinking && elapsed > 0.45f) PlayAnimationByName(_lungeV > Mathf.Max(0.01f, chaseSpeed) * 1.05f ? runAnimationName : walkAnimationName);
            if (elapsed >= Mathf.Max(0f, lungeRecoverySeconds)) EndLunge("收尾完", true);
        }
        else if (_lungePhase != 0) SetMonsterX(mx + s * _lungeV * dt);

        if (_lungePhase != 0)
        {
            float lagNow = Mathf.Abs(player.position.x - transform.position.x);
            _lungeMinLag = Mathf.Min(_lungeMinLag, lagNow);
            _dirSpeed = _lungeV;
            DebugLag = lagNow; DebugSpeed = _lungeV; DebugPace = "撲擊" + _lungePhase;
        }
        return true;
    }

    private void EnterLungeBrake()
    {
        _lungePhase = 4;
        _lungePhaseStart = Time.time;
        _lungeV = Mathf.Max(_lungeV, _lungeVEnd);
        PlayAnimationByName(attackAnimationName, true);   // 煞車一開始就揮爪：減速看起來是為了踩穩出爪
        if (attackSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(attackSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(attackSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }
        Shake(0.45f, 0.4f);
    }

    /// <summary>★1116 照節奏曲線追：她跑就跟在曲線的距離，停下就逼近；吃燭火震退、漏點逼近、固定橋段險些被抓。</summary>
    private void DirectorMove()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (_animator != null && _animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        float px = player.position.x;
        float mx = transform.position.x;
        float side = px >= mx ? 1f : -1f;
        float lag = Mathf.Abs(px - mx);

        // 她往前（遠離怪物）走多快，平滑過（重生瞬移那一幀不算）
        float vx = float.IsNaN(_playerPrevX) ? 0f : (px - _playerPrevX) / dt * side;
        _playerPrevX = px;
        _playerForwardSpeed = Mathf.Clamp(vx, 0f, 15f);
        _playerFwdSmooth = Mathf.Lerp(_playerFwdSmooth, _playerForwardSpeed, 1f - Mathf.Exp(-6f * dt));

        FaceTowardPlayer();
        ApplyPlayerSlowdown(lag);

        // 停著多久：撲擊、重生、登場運鏡都不算她停著。
        // ★1119 點燭火（她被鎖住）不再豁免——怪物不會因為她在點燭火就放過她，停著的壓力照算（只是點燭火當下的速度由下面固定住）
        bool busy = IsPlayerLightingCandle() || CandleAuraGlow.LightingReleaseAt + postLightGrace > Time.time;
        UpdatePlayerProgress();
        if (_lungePhase != 0 || PlayerRespawnSystem.IsAnyRespawning || _revealActive) _lastProgressTime = Time.time;

        // 1. 被燭火的光震退
        float kt = (Time.time - _knockStart) / Mathf.Max(0.05f, candleKnockbackSeconds);
        if (kt >= 0f && kt < 1f)
        {
            if (_lungePhase != 0) EndLunge("被燭火震退打斷", false);
            float want = _knockDist * (1f - (1f - kt) * (1f - kt));
            SetMonsterX(mx + _knockDir * (want - _knockDone));
            _knockDone = want;
            _dirSpeed = 0f;
            return;
        }

        // 2. ★1118e 追逐撲擊（固定位置＋隨機）：路徑在觸發那一刻定好，不看她之後在哪、也沒有免疫
        if (RunLunge(px, lag, side, dt)) return;

        // 3. 節奏曲線：該離她多遠
        float p = (float.IsNaN(_firstCandleX) || _lastCandleByX == null) ? 0f
                : Mathf.InverseLerp(_firstCandleX, _lastCandleByX.transform.position.x, px);
        float movingFloor = attackTriggerDistance + Mathf.Max(0.5f, directorMinMovingLag);
        float target = Mathf.Max(Mathf.Lerp(directorLagStart, directorLagEnd, p), movingFloor);
        target += candleReliefLag * Relief01;
        target = Mathf.Max(target - missedSurgeLag * Surge01, movingFloor);
        float stall = Time.time - _lastProgressTime - Mathf.Max(0f, stallGraceSeconds);
        if (stall > 0f) target -= stall * Mathf.Max(0f, stallCloseRate);   // 她停著：一路縮到揮爪距離以內

        float desired;
        if (busy)
        {
            // ★1119 點燭火時她動不了：怪物照樣追，速度＝開始點的那一刻的速度（至少 lightingPressureSpeed），整段鎖定期間不放慢、不盤旋、不等她。
            //   ＝取消 ★1118e「算好剛好在放開時離她 揮爪距離+2.5 公尺」的做法：她離怪物近時那會變成約 1 m/s，看起來像怪物在等她點完。
            //   點燭火離怪物太近就是會被抓（CheckCatch 照常運作）。
            if (_approachSpeed < 0f)
            {
                _approachSpeed = Mathf.Max(_dirSpeed, lightingPressureSpeed);
                Debug.Log($"【追逐節奏】★1119 點燭火：離她 {lag:F1}m、怪物維持 {_approachSpeed:F2}m/s 照樣追（開始時 {_dirSpeed:F1}m/s、下限 {lightingPressureSpeed:F1}m/s）");
            }
            desired = _approachSpeed;
        }
        else desired = Mathf.Clamp(_playerFwdSmooth + directorGain * (lag - target), directorMinSpeed, EffectiveMaxSpeed);
        // ★1119 她停著：不再照「離目標多遠」比例爬行（越靠近越慢＝像在放水），停越久衝越快，一路衝到抓到為止（最後一刻交給子彈時間）
        if (!busy && stall > 0f && _lungePhase == 0)
            desired = Mathf.Max(desired, Mathf.Min(Mathf.Max(0f, stallChaseMaxSpeed), stall * Mathf.Max(0f, stallChaseAccel)));
        // ★1119 她站著不動（不在喘息、不在點燭火）：怪物不再為了「回到跟隨距離」減速，原本多快就照樣衝過來
        if (!busy && _lungePhase == 0 && Relief01 <= 0f && _playerFwdSmooth < 0.5f && _dirSpeed > 0f)
            desired = Mathf.Max(desired, Mathf.Min(_dirSpeed, EffectiveMaxSpeed));
        if (!busy) _approachSpeed = -1f;
        if (_dirSpeed < 0f) _dirSpeed = desired;
        _dirSpeed = Mathf.MoveTowards(_dirSpeed, desired, Mathf.Max(0.1f, directorAccel) * dt);

        if (!_isHitShrinking)
            PlayAnimationByName(_dirSpeed > Mathf.Max(0.01f, chaseSpeed) * 1.05f ? runAnimationName : walkAnimationName);

        DebugLag = lag; DebugSpeed = _dirSpeed; DebugPace = busy ? "點燭" : (stall > 0f ? "她停著" : (Relief01 > 0f ? "喘息" : (Surge01 > 0f ? "漏點逼近" : "導演")));
        if ((Application.isEditor || TesterMode.Enabled) && Time.time >= _nextDirectorLog)
        {
            _nextDirectorLog = Time.time + 2f;
            Debug.Log($"【追逐節奏】進度 {p * 100f:F0}% | 離她 {lag:F1}m 目標 {target:F1}m | 怪物 {_dirSpeed:F1} m/s 她 {_playerFwdSmooth:F1} m/s | {DebugPace}" +
                      (stall > 0f ? $"（停了 {stall + stallGraceSeconds:F1}s）" : ""));
        }

        if (lag <= attackTriggerDistance) return;   // 進到揮爪距離：Update 的 CheckCatch 出爪
        SetMonsterX(Mathf.MoveTowards(mx, px, _dirSpeed * dt));
    }

    private void MoveTowardPlayer(float speed)
    {
        if (player == null) return;
        if (_finaleDash) { if (_lungePhase != 0) EndLunge("最後衝刺接管", false); FinaleDashMove(); return; }
        if (usePacingDirector && (currentState == MonsterState.Chasing || currentState == MonsterState.Appearing)) { DirectorMove(); return; }   // ★1116

        if (_animator != null && _animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 計算怪物落後玩家的距離（玩家在前面時為正值）
        float lagDistance = player.position.x - transform.position.x;
        float absoluteDist = Mathf.Abs(lagDistance);

        float currentSpeed = speed;
        if (!_isHitShrinking)
        {
            if (smartChaseSpeed)
            {
                currentSpeed = SmartSpeed(speed, lagDistance);
                // 用速度決定動畫，不再用固定距離門檻
                float walkTop = Mathf.Max(0.01f, speed);
                PlayAnimationByName(currentSpeed > walkTop * 1.05f ? runAnimationName : walkAnimationName);
            }
            else
            {
                // 原本的二段式：落後超過門檻才切跑步
                if (lagDistance > runDistanceThreshold)
                {
                    currentSpeed = speed * runSpeedMultiplier;
                    PlayAnimationByName(runAnimationName);
                }
                else
                {
                    PlayAnimationByName(walkAnimationName);
                }
            }
        }

        // ★1031 她在點燭火：怪物不停下，用固定的 lightingPressureSpeed 逼近（不跟遠近加速疊加，避免一點燭火就被抓）
        bool lightingPressure = keepChasingWhileLighting && IsPlayerLightingCandle();
        if (lightingPressure && !_isHitShrinking)
            currentSpeed = Mathf.Max(currentSpeed, lightingPressureSpeed);   // ★1104 點燭火時只保底、不再壓低：原本一點燭火就從 5.5 掉到 3.2，看起來像在等她
        // ★1102 難度倍率與上限、節奏調節（懲罰階段不動）
        if (!_isHitShrinking && (currentState == MonsterState.Chasing || currentState == MonsterState.Appearing))
        {
            if (!lightingPressure) currentSpeed = Mathf.Min(currentSpeed * difficultyScale, maxChaseSpeed);   // 點燭火時用 ★1031 的固定逼近速度，不再打折
            if (smartPacing) currentSpeed = PaceGovernor(currentSpeed, lagDistance);
        }

        // 玩家減速：距離小於 slowdownDistance 時降低玩家移動速度
        if (_pm != null)
        {
            if (absoluteDist < slowdownDistance)
            {
                _pm.currentSpeed = _pm.baseSpeed * slowdownFactor;
            }
            else
            {
                // 离開減速範圍後恢復玩家完整速度
                if (_pm.currentSpeed < _pm.baseSpeed)
                    _pm.currentSpeed = _pm.baseSpeed;
            }
        }

        // 面向玩家 (3D Euler 旋轉)
        if (player.position.x < transform.position.x - 0.2f)
        {
            transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        }
        else if (player.position.x > transform.position.x + 0.2f)
        {
            transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        }

        float targetX = player.position.x;
        float xDist = Mathf.Abs(transform.position.x - targetX);

        // ★ 當接近至揮爪起手距離 (約 6.0 單位) 時，就地停步並開始發動揮爪攻擊
        if (xDist <= attackTriggerDistance)
        {
            CheckCatch();
            return;
        }

        // ★1102 量她這一幀往遠離怪物方向走多快（往回走＝0）
        {
            float dtp = Mathf.Max(Time.deltaTime, 0.0001f);
            float sideNow = player.position.x >= transform.position.x ? 1f : -1f;
            float vx = float.IsNaN(_playerPrevX) ? 0f : (player.position.x - _playerPrevX) / dtp * sideNow;
            _playerForwardSpeed = Mathf.Max(0f, vx);
            _playerPrevX = player.position.x;
        }

        // ★1016 耐心：她最近有往前進，就只跟到她身後 hoverLag 公尺（不後退，只是不再逼近）
        // ★1102 不停：到了 hoverLag 線不再站著等——她在走就跟著走，她停了就以 creepSpeed 慢慢逼近
        if (holdBackWhilePlayerProgressing && !lightingPressure && (currentState == MonsterState.Chasing || currentState == MonsterState.Appearing))
        {
            UpdatePlayerProgress();
            if (Time.time - _lastProgressTime < chasePatienceSeconds)
            {
                float side = player.position.x >= transform.position.x ? 1f : -1f;
                float keep = Mathf.Max(hoverLag, attackTriggerDistance + 1f);
                float hoverX = player.position.x - side * keep;
                bool insideHover = side > 0f ? transform.position.x >= hoverX - 0.05f : transform.position.x <= hoverX + 0.05f;
                if (!neverStopNearPlayer || !insideHover)
                {
                    // 還在 hoverLag 線外面：照 1016 走到線上（她在走，線也跟著走，所以怪物不會停）
                    targetX = side > 0f
                        ? Mathf.Max(transform.position.x, Mathf.Min(targetX, hoverX))
                        : Mathf.Min(transform.position.x, Mathf.Max(targetX, hoverX));
                }
                else if (_playerForwardSpeed > 0.3f)
                {
                    // ★1102 線內、她在往前走：不定住，照 SmartSpeed「太近就放慢」的速度跟著走（距離會慢慢拉回 hoverLag），目標仍是她
                    //   SmartSpeed 在 idealLagMin 以內最低是 easeOffSpeed，不會停
                }
                else
                {
                    // ★1102 線內、她停著／等平台／往回走：不再站著等，慢慢逼近
                    if (!_isHitShrinking) currentSpeed = Mathf.Min(currentSpeed, Mathf.Max(0.1f, creepSpeed));
                    PlayAnimationByName(walkAnimationName);
                }
            }
        }

        float newX = Mathf.MoveTowards(transform.position.x, targetX, currentSpeed * Time.deltaTime);
        float newY = transform.position.y;

        // ★ 自動貼地 / 隱形地板吸附邏輯
        //   keepVisualCenterHeight 開著時 Y 由 ApplyCurrentScale 決定，這裡不要搶。
        if (enableGroundSnap && !keepVisualCenterHeight)
        {
            Vector3 rayOrigin = new Vector3(newX, transform.position.y + 5.0f, transform.position.z);
            RaycastHit hit;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, groundRaycastDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                float targetY = hit.point.y + feetOffsetY;
                newY = Mathf.Lerp(transform.position.y, targetY, Time.deltaTime * 12f);
            }
        }

        transform.position = new Vector3(newX, newY, transform.position.z);
    }

    private string _currentAnimName = "";

    private void PlayAnimationByName(string animName, bool restart = false)
    {
        if (_animator == null || string.IsNullOrEmpty(animName)) return;

        if (_animator.runtimeAnimatorController == null)
        {
#if UNITY_EDITOR
            var defaultCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/MonsterMutant 7/MonsterMutant7 Animator Controller.controller");
            if (defaultCtrl != null) _animator.runtimeAnimatorController = defaultCtrl;
            else return;
#else
            return;
#endif
        }

        // 用字串比對防止每幀重複呼叫
        if (_currentAnimName == animName && !restart) return;

        bool leavingHit = _currentAnimName == hitAnimationName && animName != hitAnimationName;
        float fade = leavingHit ? hitExitCrossFade : animCrossFade;

        // ★受擊動畫播完之前，走路／跑步／待機都不能打斷它（揮爪攻擊可以）。
        //   提前 fade 秒放行，讓混合跟受擊最後幾格疊在一起，不會停在最後一格再跳到移動動作
        if (leavingHit && animName != attackAnimationName && Time.time < _hitAnimLockUntil - fade) return;

        string prev = _currentAnimName;
        _currentAnimName = animName;

        // 第一次播（剛出場／重置）、或重播同一個動作：直接從頭播
        if (string.IsNullOrEmpty(prev) || prev == animName || fade <= 0f)
        {
            _animator.Play(animName, 0, 0f);
            return;
        }

        // 走路↔跑步：接續目前的步伐相位，腳步才不會亂跳
        AnimatorStateInfo cur = _animator.GetCurrentAnimatorStateInfo(0);
        float startNorm = 0f;
        bool locomotionSwap = (prev == walkAnimationName && animName == runAnimationName)
                           || (prev == runAnimationName && animName == walkAnimationName);
        if (locomotionSwap) startNorm = Mathf.Repeat(cur.normalizedTime, 1f);

        // CrossFade 的混合時間是以「目前動作長度」為單位，換算成秒數
        float normFade = Mathf.Clamp01(fade / Mathf.Max(0.01f, cur.length));
        _animator.CrossFade(animName, normFade, 0, startNorm);
    }


    private void CheckCatch()
    {
        if (player == null || currentState == MonsterState.Devouring) return;
        if (_finaleDash) return;   // ★最後衝刺只嚇人，不攻擊

        float xDist = Mathf.Abs(transform.position.x - player.position.x);

        // ★ 核心修復：純粹以水平 X 軸距離為準 (不受怪物腳底 Pivot 與主角走道 Y 軸高差影響)！
        if (xDist <= attackTriggerDistance)
        {
            if (CandleAuraGlow.LightingReleaseAt + postLightGrace > Time.time)
                Debug.LogWarning($"【怪物節奏】★點燭火期間被抓：距離 {xDist:F1}m、節奏調節={smartPacing}、調節後速度 {DebugSpeed:F1}、狀態 {DebugPace}");
            TriggerDevour();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：相機與恐懼機制
    // ──────────────────────────────────────────────────────────────────────────

    private void LockCamera()
    {
        if (_camLocked) return;
        _camLocked = true;

        if (_camLockDummy == null)
            _camLockDummy = new GameObject("ShadowMonster_CamLockDummy").transform;
        _camLockDummy.position = player != null ? player.position : Vector3.zero;

        _vcams3.Clear();
        _vcamsLegacy.Clear();
        _origFollow.Clear();

        foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            _vcams3.Add(v);
            _origFollow.Add(v.Follow);
            v.Follow = _camLockDummy;
        }
        foreach (var v in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None))
        {
            _vcamsLegacy.Add(v);
            _origFollow.Add(v.Follow);
            v.Follow = _camLockDummy;
        }

        Debug.Log("【影子怪物】相機已鎖定。");
    }

    private void UnlockCamera()
    {
        if (!_camLocked) return;
        _camLocked = false;

        int idx = 0;
        foreach (var v in _vcams3)
        {
            if (v != null && idx < _origFollow.Count)
                v.Follow = _origFollow[idx];
            idx++;
        }
        foreach (var v in _vcamsLegacy)
        {
            if (v != null && idx < _origFollow.Count)
                v.Follow = _origFollow[idx];
            idx++;
        }

        Debug.Log("【影子怪物】相機已解鎖。");
    }

    private void ClampPlayerToCameraView()
    {
        if (_mainCam == null || player == null) return;

        float halfW;
        if (_mainCam.orthographic)
        {
            halfW = _mainCam.orthographicSize * _mainCam.aspect;
        }
        else
        {
            float dist = Mathf.Abs(_mainCam.transform.position.z - player.position.z);
            halfW = dist * Mathf.Tan(_mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _mainCam.aspect;
        }

        float margin = 0.5f;
        float minX = _mainCam.transform.position.x - halfW + margin;
        float maxX = _mainCam.transform.position.x + halfW - margin;

        Vector3 pos = player.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        player.position = pos;
    }

    private void ApplyFear()
    {
        if (_fearActive || _pm == null) return;
        _fearActive = true;
        _origSpeed = _pm.baseSpeed;
        _origJumpForce = _pm.jumpForce;
        _pm.baseSpeed *= fearSpeedMultiplier;
        _pm.jumpForce *= fearJumpMultiplier;
        Debug.Log($"【影子怪物】恐懼效果：速度 {_origSpeed} → {_pm.baseSpeed}，跳躍力 {_origJumpForce} → {_pm.jumpForce}");
    }

    private void RemoveFear()
    {
        if (!_fearActive || _pm == null) return;
        _fearActive = false;
        _pm.baseSpeed = _origSpeed;
        _pm.jumpForce = _origJumpForce;
        Debug.Log("【影子怪物】恐懼效果已解除。");
    }

    private void SetupCandles()
    {
        if (candles == null || candles.Length == 0)
        {
            Debug.LogWarning("【影子怪物】⚠ 燭火清單為空！請在 Inspector 手動拖曳所有 CandleCollectible 物件至 candles[]。");
            return;
        }

        // ★只算真的接上的燭火。用 candles.Length 會把空格算進去，
        //   收永遠收不滿，玩家會打不倒怪物也看不到結局，而且完全沒有錯誤訊息。
        int nullSlots = 0;
        _totalCandles = 0;
        _lastCandleByX = null;
        foreach (var c in candles)
        {
            if (c == null) { nullSlots++; continue; }
            _totalCandles++;
            if (float.IsNaN(_firstCandleX) || c.transform.position.x < _firstCandleX) _firstCandleX = c.transform.position.x;   // ★1116 節奏曲線的起點
            if (_lastCandleByX == null || c.transform.position.x > _lastCandleByX.transform.position.x)
                _lastCandleByX = c;
        }
        if (nullSlots > 0)
            Debug.LogWarning($"【影子怪物】⚠ candles[] 有 {nullSlots} 個空格沒接東西，已忽略。" +
                             $"實際有效燭火 {_totalCandles} 根——請到 Inspector 補齊或縮短陣列。", this);
        if (_totalCandles == 0)
        {
            Debug.LogError("【影子怪物】⚠⚠ 一根有效燭火都沒有！怪物將無法被打倒，結局也不會觸發。", this);
            return;
        }

        Debug.Log($"【影子怪物】共 {_totalCandles} 根燭火。" +
                  $"X 最大（懲罰判定）：{_lastCandleByX?.gameObject.name} " +
                  $"(X = {_lastCandleByX?.transform.position.x:F1})");
    }

    private void ApplyCurrentScale()
    {
        // ★ 關鍵架構解耦：MonsterRoot 的 Scale 保持固定不變，永不影響 Movement、GroundSnapping 與 Gameplay Colliders！
        transform.localScale = _baseScale;

        // ★ 獨立 Visual 縮放與數學地表錨點 Y 軸向上補償 (Mathematical Surface Anchor)
        if (_visualContainer != null)
        {
            _visualContainer.localScale = Vector3.one * _currentScaleMultiplier;

            // 1. 計算世界空間所需的 Y 軸補償量 (以走道地表錨點為基準)
            float worldYOffset = _submergedDepth * (1.0f - _currentScaleMultiplier);

            // 2. 嚴格 Clamp 防護：限制在 [0, maxWorldVisualYOffset]，徹底杜絕怪物浮空/飛天
            worldYOffset = Mathf.Clamp(worldYOffset, 0f, maxWorldVisualYOffset);

            // 3. 轉換為 _visualContainer 的 Local 座標 (除以 Root 的 lossyScale.y，避免被父節點 Scale 放大數十倍)
            float parentScaleY = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.y));
            float localYOffset = worldYOffset / parentScaleY;

            _visualContainer.localPosition = new Vector3(0f, localYOffset, 0f);
        }

        UpdateHaloRadius(_currentScaleMultiplier);
    }

    private IEnumerator GrowHaloRoutine(float fromMult, float toMult, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float m = Mathf.SmoothStep(fromMult, toMult, t / duration);
            UpdateHaloRadius(m);
            yield return null;
        }
        UpdateHaloRadius(toMult);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：強效光暈（Sprite 貼圖 + 粒子 + 光源）
    // ──────────────────────────────────────────────────────────────────────────

    private void CreateHaloEffect()
    {
        GameObject glowObj = new GameObject("Shadow_HaloGlowSprite");
        glowObj.transform.SetParent(transform);
        glowObj.transform.localPosition = new Vector3(0f, 1.2f, -0.2f);
        glowObj.transform.localScale = Vector3.one * haloBaseRadius * 1.8f;

        _haloSpriteRenderer = glowObj.AddComponent<SpriteRenderer>();
        _haloSpriteRenderer.sprite = GenerateSoftGlowSprite();
        _haloSpriteRenderer.color = haloColor;
        _haloSpriteRenderer.sortingOrder = 999;

        GameObject lightObj = new GameObject("Shadow_HaloLight");
        lightObj.transform.SetParent(transform);
        lightObj.transform.localPosition = new Vector3(0f, 1.2f, -1.2f);

        _haloLight = lightObj.AddComponent<Light>();
        _haloLight.type = LightType.Point;
        _haloLight.color = lightColor;
        _haloLight.intensity = lightBaseIntensity;
        _haloLight.range = lightRange;
        _haloLight.shadows = LightShadows.None;

        GameObject psObj = new GameObject("Shadow_HaloParticles");
        psObj.transform.SetParent(transform);
        psObj.transform.localPosition = new Vector3(0f, 1.0f, -0.5f);
        psObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _haloPs = psObj.AddComponent<ParticleSystem>();

        var main = _haloPs.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(haloColor.r, haloColor.g, haloColor.b, 0.8f),
            new Color(haloColor.r * 0.7f, 0.05f, 0.05f, 0.9f)
        );

        var emission = _haloPs.emission;
        emission.rateOverTime = 120f;

        var shape = _haloPs.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = haloBaseRadius;

        var psRend = _haloPs.GetComponent<ParticleSystemRenderer>();
        psRend.renderMode = ParticleSystemRenderMode.Billboard;
        psRend.sortingOrder = 1000;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Transparent");
        if (sh != null)
        {
            Material mat = new Material(sh);
            mat.color = haloColor;
            psRend.material = mat;
        }

        _haloPs.Play();
        // ★1119 紅光錨在身體中心骨上（不跟物件原點、粒子不留在世界座標裡拖尾巴）
        if (GetComponent<MonsterGlowAnchor>() == null) gameObject.AddComponent<MonsterGlowAnchor>();
        Debug.Log("【影子怪物】強效光暈（2D貼圖 + 粒子 + 光源）建立完成。");
    }

    private Sprite GenerateSoftGlowSprite()
    {
        int res = 128;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
        float radius = res * 0.48f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float normDist = Mathf.Clamp01(dist / radius);
                float alpha = Mathf.Pow(1f - normDist, 2.0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
    }

    private void UpdateHaloRadius(float multiplier)
    {
        float m = Mathf.Max(0.1f, multiplier);
        if (_haloSpriteRenderer != null)
        {
            _haloSpriteRenderer.transform.localScale = Vector3.one * haloBaseRadius * 1.8f * m;
        }
        if (_haloPs != null)
        {
            var shape = _haloPs.shape;
            shape.radius = haloBaseRadius * m;
        }
        if (_haloLight != null)
        {
            _haloLight.range = lightRange * m;
            _haloLight.intensity = lightBaseIntensity * m;
        }
    }

    /// <summary>★1116 目前顯示的透明度（0＝看不見）：眼罩紅光跟著它淡入淡出。</summary>
    public float VisualAlpha { get; private set; }

    /// <summary>★1116 最後一根燭火（X 最大的那根）。</summary>
    public CandleCollectible LastCandle => _lastCandleByX;

    /// <summary>★1116 外部壓低追逐音樂（1＝不壓）：熄燈時音樂退後，只剩腳步和呼吸。</summary>
    [System.NonSerialized] public float externalBgmDuck = 1f;

    private void SetVisualAlpha(float alpha)
    {
        EnsureInitialized();
        VisualAlpha = Mathf.Max(0f, alpha);

        bool showVisuals = alpha > 0.001f;

        if (_renderers != null && _originalColors != null)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                _renderers[i].enabled = showVisuals;

                if (showVisuals)
                {
                    Color c = (i < _originalColors.Length) ? _originalColors[i] : Color.white;
                    c.a = c.a * alpha;

                    _renderers[i].GetPropertyBlock(_mpb);
                    if (_renderers[i].sharedMaterial != null && _renderers[i].sharedMaterial.HasProperty("_BaseColor"))
                        _mpb.SetColor("_BaseColor", c);
                    else
                        _mpb.SetColor("_Color", c);

                    _renderers[i].SetPropertyBlock(_mpb);

                    if (_renderers[i] is SpriteRenderer sr)
                        sr.color = c;
                }
            }
        }

        if (_haloSpriteRenderer != null)
        {
            _haloSpriteRenderer.enabled = showVisuals;
            Color sc = haloColor;
            sc.a = haloColor.a * alpha;
            _haloSpriteRenderer.color = sc;
        }

        if (_haloPs != null)
        {
            if (!showVisuals)
            {
                _haloPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            else
            {
                if (!_haloPs.isPlaying) _haloPs.Play();
            }
        }

        if (_haloLight != null)
        {
            _haloLight.enabled = showVisuals;
            _haloLight.intensity = lightBaseIntensity * alpha;
        }
    }

    public void ResetToInitialState()
    {
        EnsureInitialized();

        StopAllCoroutines();
        _stateCoroutine = null;
        _hitShrinkCoroutine = null;
        _deferCoroutine = null;
        _activationPending = false;   // ★1016 延後出場的等待也一起取消（重生流程會再安排）
        _isHitShrinking = false;
        _hitAnimLockUntil = 0f;
        _finaleDash = false;
        _currentAnimName = "";
        RestoreDeathAnimSpeed();   // ★1119b 倒下對齊語音時改過的 Animator 速度
        _chaseStepRamp = false;    // ★1119b 追逐音樂的燭火階梯：重置後依存檔點保留的燭火數直接對齊，不緩降
        _chaseTimer = 0f;  // 重置追逐計時，確保下次出場先播 walk4

        // 立即停止追逐背景音樂
        StopChaseBGM(true);

        currentState = MonsterState.Dormant;
        _candlesCollected = 0;
        _doomed = false;          // ★1021 倒數與注定失敗的標記一起清掉（漏掉的燭火在下面 ResetToInitialState 時刷新，數量自動重算）
        _doomRoutine = null;
        _currentScaleMultiplier = 1f;
        { var shd = GetComponent<MonsterArmShedding>(); if (shd != null) shd.SetShedCount(0, false); }   // ★1118b 手臂先全部長回來，下面依存檔點前的燭火再掉
        _smoothedChaseSpeed = -1f; _paceCap = -1f; _paceBusyPrev = false;
        ResetDirectorState();

        RemoveFear();
        UnlockCamera();
        FinaleCamOff(true);            // ★1118 結局倒下演出：鏡頭、她的自動走路一律還原
        if (_pm != null) { _pm.scriptedMove = false; _pm.scriptedMoveInput = 0f; }
        RevealCancel();   // ★StopAllCoroutines 會把演出砍在半路，這裡要把鏡頭與控制權還回去

        transform.position = _initialPosition;
        _currentScaleMultiplier = 1f;
        ApplyCurrentScale();
        UpdateGameplayCollidersPosition();

        UpdateHaloRadius(1f);
        PlayAnimationByName(idleAnimationName);
        SetVisualAlpha(0f);

        if (candles != null)
        {
            foreach (var c in candles)
            {
                if (c == null) continue;
                // ★1024 存檔點前面的燭火玩家已經過關：不刷新、直接算已收集；存檔點之後的才刷新（不算數）
                if (IsCandleKeptByCheckpoint(c)) { if (!c.isCollected) c.MarkCollectedSilently(); }
                else c.ResetToInitialState();
            }

            _candlesCollected = 0;
            foreach (var c in candles) if (c != null && c.isCollected) _candlesCollected++;
            _currentScaleMultiplier = CandleScaleMult(_candlesCollected);   // ★1118b
            ApplyCurrentScale();
            SyncArms(false);   // ★1118b 存檔點前保留的燭火＝那幾隻手臂已經掉了
        }

        Debug.Log($"【影子怪物】已完整重置至初始狀態（存檔點前 {_candlesCollected} 根燭火保留為已完成，其餘刷新）。");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 🎵 追逐專屬背景音樂核心管理 (Chase BGM Management)
    // ──────────────────────────────────────────────────────────────────────────

    private void SetupChaseAudioSource()
    {
        if (_chaseAudioSource == null)
        {
            _chaseAudioSource = gameObject.GetComponent<AudioSource>();
            if (_chaseAudioSource == null) _chaseAudioSource = gameObject.AddComponent<AudioSource>();
            _chaseAudioSource.loop = true;
            _chaseAudioSource.playOnAwake = false;
            _chaseAudioSource.spatialBlend = spatialBlend;
            _chaseAudioSource.volume = 0f;
            _chaseAudioSource.rolloffMode = AudioRolloffMode.Linear;
            _chaseAudioSource.minDistance = 5f;
            _chaseAudioSource.maxDistance = 60f;
        }
        if (chaseBGM == null)
        {
#if UNITY_EDITOR
            chaseBGM = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/玻璃館/玻璃館_追逐_loop.wav");
#endif
        }

        if (victoryReliefSFX == null)
        {
#if UNITY_EDITOR
            victoryReliefSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/玻璃館/玻璃館_追逐_脫離.wav");
#endif
        }

        if (_chaseAudioSource != null && chaseBGM != null)
        {
            _chaseAudioSource.clip = chaseBGM;
        }
    }

    public void StartChaseBGM()
    {
        if (_chaseAudioSource == null) SetupChaseAudioSource();
        if (_chaseAudioSource == null || _chaseAudioSource.clip == null) return;

        if (_chaseBgmFadeCoroutine != null) StopCoroutine(_chaseBgmFadeCoroutine);
        _chaseBgmFadeCoroutine = StartCoroutine(FadeChaseBgmRoutine(chaseBGMVolume, chaseBGMFadeInDuration, true));
        Debug.Log($"🎵【影子怪物】追逐背景音樂啟動淡入播放（{_chaseAudioSource.clip.name}，音量 {chaseBGMVolume}）");
    }

    /// <param name="fadeSeconds">淡出秒數；小於 0 ＝ 用 chaseBGMFadeOutDuration</param>
    public void StopChaseBGM(bool immediate = false, float fadeSeconds = -1f)
    {
        if (_chaseAudioSource == null) return;

        if (_chaseBgmFadeCoroutine != null) StopCoroutine(_chaseBgmFadeCoroutine);

        if (immediate)
        {
            _chaseAudioSource.Stop();
            SetChaseBase(0f);
            _chaseAudioSource.pitch = 1.0f;
            _chaseBgmFadeCoroutine = null;
        }
        else
        {
            _chaseBgmFadeCoroutine = StartCoroutine(FadeChaseBgmRoutine(0f, fadeSeconds >= 0f ? fadeSeconds : chaseBGMFadeOutDuration, false));
        }
    }

    // ★1119b 追逐音樂音量的唯一出口：所有地方（淡入淡出、逼近緊張度、燭火喘息、熄燈）都只算「邏輯音量」_chaseBase，
    //   真正寫進 AudioSource 的是 _chaseBase × 燭火階梯倍率（_chaseStepMul），所以幾個倍率是相乘、不會互相打架。
    private float _chaseBase;            // 邏輯音量（不含燭火階梯）
    private float _chaseStepMul = 1f;    // 燭火階梯倍率：1 − chaseBgmStepPerCandle × 已吃燭火數（最多 總數−1 根）
    private bool _chaseStepRamp;         // true＝剛撞到燭火，平滑降到新音量；false＝直接對齊（重生、存檔點、測試鍵）

    /// <summary>目前已吃的燭火數換算出的階梯倍率目標（最後一根不算，它改走淡出）。</summary>
    private float ChaseStepTarget()
    {
        int cap = Mathf.Max(0, _totalCandles - 1);
        int n = Mathf.Clamp(_candlesCollected, 0, cap);
        return Mathf.Clamp01(1f - Mathf.Max(0f, chaseBgmStepPerCandle) * n);
    }

    /// <summary>測試／驗證用：追逐音樂的邏輯音量、燭火階梯倍率、實際 AudioSource 音量。</summary>
    public float DebugChaseBase { get { return _chaseBase; } }
    public float DebugChaseStepMul { get { return _chaseStepMul; } }
    public float DebugChaseSourceVolume { get { return _chaseAudioSource != null ? _chaseAudioSource.volume : -1f; } }
    public bool DebugChasePlaying { get { return _chaseAudioSource != null && _chaseAudioSource.isPlaying; } }

    private void SetChaseBase(float v)
    {
        _chaseBase = Mathf.Max(0f, v);
        ApplyChaseVolume();
    }

    private void ApplyChaseVolume()
    {
        if (_chaseAudioSource != null) _chaseAudioSource.volume = _chaseBase * _chaseStepMul;
    }

    void LateUpdate()
    {
        if (_chaseAudioSource == null) return;
        float target = ChaseStepTarget();
        if (_chaseStepRamp && !Mathf.Approximately(_chaseStepMul, target))
        {
            // 撞到燭火：用 chaseBgmStepRampSeconds 秒平滑降到新的階梯（降一階的速度；一次降多階就照比例）
            float rate = Mathf.Max(0.0001f, chaseBgmStepPerCandle) / Mathf.Max(0.05f, chaseBgmStepRampSeconds);
            _chaseStepMul = Mathf.MoveTowards(_chaseStepMul, target, rate * Time.deltaTime);
        }
        else
        {
            _chaseStepMul = target;   // 重生／存檔點／測試鍵跳關：直接對齊
            _chaseStepRamp = false;
        }
        ApplyChaseVolume();
    }

    private IEnumerator FadeChaseBgmRoutine(float targetVolume, float duration, bool playIfStarting)
    {
        if (_chaseAudioSource == null) yield break;

        if (playIfStarting && !_chaseAudioSource.isPlaying)
        {
            SetChaseBase(0f);
            _chaseAudioSource.pitch = 1.0f;
            _chaseAudioSource.Play();
        }

        float startVol = _chaseBase;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scaledTargetVolume = targetVolume * AudioManager.BgmVolume;
            SetChaseBase(Mathf.Lerp(startVol, scaledTargetVolume, t));
            yield return null;
        }

        SetChaseBase(targetVolume * AudioManager.BgmVolume);

        if (!playIfStarting && targetVolume <= 0.001f)
        {
            _chaseAudioSource.Stop();
            _chaseAudioSource.pitch = 1.0f;
        }

        _chaseBgmFadeCoroutine = null;
    }

    private void UpdateProximityAudioTension()
    {
        if (_chaseAudioSource == null || !_chaseAudioSource.isPlaying || player == null || _chaseBgmFadeCoroutine != null) return;

        float bgmVolume = AudioManager.BgmVolume;
        float dist = Mathf.Abs(player.position.x - transform.position.x);
        // ★1116 吃到燭火後的喘息：追逐音樂降下來幾秒（漏點逼近時不降）
        float duck = usePacingDirector ? 1f - candleReliefBgmDuck * Relief01 : 1f;
        duck *= Mathf.Clamp01(externalBgmDuck);
        if (dist < 8.5f)
        {
            // 怪物越靠近，追逐音樂音量與緊張度稍微提高
            float t = Mathf.InverseLerp(8.5f, 2.5f, dist);
            SetChaseBase(Mathf.Lerp(chaseBGMVolume, Mathf.Min(1f, chaseBGMVolume * 1.25f), t) * bgmVolume * duck);
            _chaseAudioSource.pitch = Mathf.Lerp(1.0f, 1.05f, t);
        }
        else
        {
            SetChaseBase(Mathf.MoveTowards(_chaseBase, chaseBGMVolume * bgmVolume * duck, Time.unscaledDeltaTime * (usePacingDirector ? 1.5f : 0.5f)));
            _chaseAudioSource.pitch = Mathf.MoveTowards(_chaseAudioSource.pitch, 1.0f, Time.deltaTime * 0.5f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 1. 繪製黃色線框：起手揮爪距離 (Attack Trigger Distance)，涵蓋怪物全身高度
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.7f);
        Vector3 forwardDir = transform.rotation * Vector3.forward;
        Vector3 triggerCenter = transform.position + forwardDir * (attackTriggerDistance * 0.5f) + Vector3.up * 15f;
        Gizmos.DrawWireCube(triggerCenter, new Vector3(attackTriggerDistance, 35f, 4f));

        // 2. 繪製紅色線框：巨爪命中傷害範圍 (Claw Hit Distance)，涵蓋怪物全身高度
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.85f);
        Vector3 hitCenter = transform.position + forwardDir * (clawHitDistance * 0.5f) + Vector3.up * 15f;
        Gizmos.DrawWireCube(hitCenter, new Vector3(clawHitDistance, 35f, 4f));
    }
}

/// <summary>
/// 專門負責燭火收集的觸發代理元件 (Gameplay Trigger Proxy)
/// 掛載於怪物獨立的 CandleCollectionTrigger 上，精準判定路面走道上的燭火物件
/// </summary>
public class MonsterCandleTrigger : MonoBehaviour
{
    public ShadowMonsterController controller;

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        CandleCollectible candle = other.GetComponent<CandleCollectible>();
        if (candle == null) candle = other.GetComponentInParent<CandleCollectible>();
        if (candle == null) candle = other.GetComponentInChildren<CandleCollectible>();

        if (candle != null)
        {
            candle.TryCollect(gameObject);
        }
    }
}

