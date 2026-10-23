using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// ★1019 M：教學字、按鍵提示改成修毅那種線條畫，放到她後面；玩家經過亮起，離開後黯淡消失。
///
/// 換場景時自己裝上（場景檔、組員的程式都不動）：
///   ・棉花堡的世界教學字（TextMeshPro 3D：TutorialText_1～5、NewTutorialText）
///       → 字藏起來，原來的位置放一張線條畫（LineHintSign）。
///       A、D 左右移動／跟著光球前進　　→ Hints/Tut_Move
///       空白鍵 跳躍／試著越過障礙物　　→ Hints/Tut_Jump
///       按住 SHIFT 抓取物體／可避免危險 → Hints/Tut_Grab
///       過程中不會死亡／…會重新挑戰　　→ Hints/Tut_Retry
///       接著，有趣的冒險即將展開　　　　→ Hints/Tut_Adventure
///   ・荒原「起風了　按住 S 硬撐／風停了　放開 S 繼續走」（畫面下方的字）
///       → 字關掉（DesertBraceHint.Enabled＝false），修毅的說明圖右邊接一張 Hints/Desert_Brace。
///   ・水下「W／S　上浮與下潛」（畫面下方的字）
///       → 字關掉（InputHintSwimProbe.Enabled＝false），出生點右邊放一張 Hints/Swim_WS_light（深色水裡用淺色線）。
///   ・拉桿「按下 E　拉動拉桿」：拉桿現在被光球取代、藏起來，這行字本來就不會出現；一樣關掉（InputHintLeverProbe.Enabled＝false）。
///
/// ★1020 M：「後面的關卡需要的地方都幫我補上類似的教學」→ 再補五張（同樣的線色、她的造型、比例）：
///   ・廢墟　Hints/Ruins_Push：按住 D，把巨石推上坡、推到光那裡（修毅「回頭看狼」圖的右邊、同一條地線）。
///   ・荒原　Hints/Desert_Bird：鳥抖一下就要衝下來；按住 S 硬撐，鳥啄不動、彈開（掩體帶結束、開始有鳥的空地前）。
///   ・水下　Hints/Swim_Air_light：光圈縮小＝氣快沒了；碰到光球、撿到日誌，光圈撐開（W／S 圖下面）。
///   ・水下　Hints/Swim_Notes_light：撿到三張日誌，擋路的大石頭才會散開（大石頭左邊；石頭散掉後收起來）。
///   ・玻璃館 Hints/Glass_Candles_light：黑影追來，一路碰到每一根燭火，它就變小、消失（鏡牆後、第一根燭火前）。
/// ★1020 出現方式：測試檔寫一行 hints=page → 全部改成「翻頁」（LineHintSign.Style.PageTurn）；沒寫就是原本的淡入。
///
/// 開關：LineHintDirector.Enabled＝false 就全部照舊（字回來、不放圖）。
///       只關 1020 新補的五張：LineHintDirector.LaterLevels＝false。
/// </summary>
public class LineHintDirector : MonoBehaviour
{
    public static bool Enabled = true;
    /// <summary>★1020 廢墟、荒原鳥、水下兩張、玻璃館（後面關卡新補的五張）。</summary>
    public static bool LaterLevels = true;

    // 圖的寬度照修毅回頭圖示的比例：820px＝8.5 公尺（約 96.5 px／公尺），她在圖裡跟她本人差不多高。
    private const float PxPerMeter = 820f / 8.5f;

    private struct TutMap
    {
        public string objectName;   // 場景裡的物件名
        public string contains;     // 名字對不上時，用字的內容找
        public string res;
        public Vector2 offset;      // 圖中心相對原本字的中心（公尺）
        public TutMap(string n, string c, string r, Vector2 o) { objectName = n; contains = c; res = r; offset = o; }
    }

    private static readonly TutMap[] Tutorials =
    {
        new TutMap("TutorialText_1", "左右移動", "Hints/Tut_Move", new Vector2(0f, 0.4f)),
        new TutMap("TutorialText_2", "跳躍", "Hints/Tut_Jump", new Vector2(0f, 0.6f)),
        new TutMap("TutorialText_3", "SHIFT", "Hints/Tut_Grab", new Vector2(0f, 0.4f)),
        // ★1019 實測：原位置在兩朵雲台階中間，畫裡的台階跟真的雲台階疊在一起、看起來像能踩
        //   → 往左上移到第一朵雲台階上方的空中（她站上去時頭剛好碰到圖的地線；掉到下面也看得到）
        new TutMap("TutorialText_4", "不會死亡", "Hints/Tut_Retry", new Vector2(-5.8f, 3.1f)),
        new TutMap("TutorialText_5", "冒險", "Hints/Tut_Adventure", new Vector2(0f, 0.6f)),
        new TutMap("NewTutorialText", "冒險", "Hints/Tut_Adventure", new Vector2(0f, 0.6f)),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // ★1020 測試檔 hints=page：提示改成翻頁出現（給 M 比較用；展場沒有測試檔＝原本的淡入）
        string mode = TesterMode.Option("hints");
        if (!string.IsNullOrEmpty(mode) && mode.Trim().ToLower().StartsWith("page"))
        {
            LineHintSign.DefaultStyle = LineHintSign.Style.PageTurn;
            Debug.Log("【線條畫提示】★1020 測試檔 hints=page：提示改用翻頁出現");
        }
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!Enabled || !scene.IsValid()) return;
        // 畫面下方的按鍵字：有線條畫就關。一定要在這裡關（場景剛載入、第一幀 Update 之前）——
        //   ★1019 實測：水下一進場她就在水裡，InputHintSwimProbe 第一幀就亮字；等協程下一幀才關會慢一步。
        InputHintLeverProbe.Enabled = false;   // 拉桿現在藏起來，本來就不會出現；跟其他按鍵提示一樣不要字
        if (Resources.Load<Texture2D>("Hints/Swim_WS_light") != null) InputHintSwimProbe.Enabled = false;
        if (Resources.Load<Texture2D>("Hints/Desert_Brace") != null) DesertBraceHint.Enabled = false;

        foreach (LineHintDirector d in FindObjectsByType<LineHintDirector>(FindObjectsInactive.Include))
            if (d.gameObject.scene == scene) return;          // 這個場景裝過了
        GameObject host = new GameObject("[LineHintDirector]");
        SceneManager.MoveGameObjectToScene(host, scene);
        LineHintDirector dir = host.AddComponent<LineHintDirector>();
        dir.StartCoroutine(dir.InstallRoutine(scene));
    }

    private IEnumerator InstallRoutine(Scene scene)
    {
        // 等一幀：場景裡別人的 AfterSceneLoad／sceneLoaded（修毅的說明圖、玩家）先生好
        yield return null;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        float behindZ = pm != null ? pm.transform.position.z + 1f : 0f;   // 她後面 1 公尺

        // ── 1　世界教學字（棉花堡）──
        int replaced = 0;
        TextMeshPro[] texts = FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include);
        foreach (TextMeshPro t in texts)
        {
            if (t == null || t.gameObject.scene != scene) continue;
            int k = Match(t);
            if (k < 0) continue;
            TutMap m = Tutorials[k];
            Texture2D tex = Resources.Load<Texture2D>(m.res);
            if (tex == null) { Debug.LogWarning("【線條畫提示】找不到 Resources/" + m.res + "，這句字照舊"); continue; }
            Vector3 c = t.transform.position;
            Renderer r = t.GetComponent<Renderer>();
            if (r != null && r.bounds.size.sqrMagnitude > 0.01f) c = new Vector3(r.bounds.center.x, r.bounds.center.y, c.z);
            Vector3 pos = new Vector3(c.x + m.offset.x, c.y + m.offset.y, Mathf.Max(c.z, behindZ));
            LineHintSign.Create("LineHint_" + t.gameObject.name, m.res, pos, tex.width / PxPerMeter);
            // 字藏起來（不關物件：物件身上的觸發框、TutorialZone 照常運作，只是看不到字）
            t.enabled = false;
            if (r != null) r.enabled = false;
            replaced++;
        }
        if (replaced > 0) Debug.Log("【線條畫提示】★1019 " + scene.name + "：" + replaced + " 句教學字換成線條畫（字藏起來，場景沒改）");

        // ── 2　荒原：修毅的說明圖右邊接「按住 S／放開 S」──
        if (FindAnyObjectByType<WindGustSystem>() != null || FindAnyObjectByType<WindShelter>() != null)
        {
            // 畫面下方的字已在 Install() 關掉
            DesertGuideHint guide = null;
            float wait = 0f;
            while (guide == null && wait < 4f)
            {
                guide = FindAnyObjectByType<DesertGuideHint>();
                if (guide == null) { wait += Time.unscaledDeltaTime; yield return null; }
            }
            Texture2D mine = Resources.Load<Texture2D>("Hints/Desert_Brace");
            Texture2D his = Resources.Load<Texture2D>("Hints/DesertGuide");
            // ★1023 有光球門檻時（DesertOrbDirector）：修毅的說明圖、按住 S、鳥的圖全部放到光球之後——
            //   拿到光球才有「按住 S」，提示在那之前亮沒有意義（M：吃到光球才有提示、才有石化的功能）。
            //   等光球把位置算好（它會避開岩柱）。
            float orbWait = 0f;
            while (DesertOrbDirector.GateActive && float.IsNegativeInfinity(DesertOrbDirector.GuideLeftX) && orbWait < 3f)
            { orbWait += Time.unscaledDeltaTime; yield return null; }
            float braceRight = float.NegativeInfinity;
            if (guide != null && DesertOrbDirector.GateActive && !float.IsNegativeInfinity(DesertOrbDirector.GuideLeftX))
            {
                Vector3 gp = guide.transform.position;
                float newX = DesertOrbDirector.GuideLeftX + guide.worldWidth * 0.5f;
                guide.transform.position = new Vector3(newX, gp.y, gp.z);
                Debug.Log($"【線條畫提示】★1023 修毅的說明圖從 x={gp.x:F1} 搬到光球之後 x={newX:F1}");
            }
            if (guide != null && mine != null && his != null)
            {
                float w = guide.worldWidth * mine.width / his.width;     // 同一個比例：她一樣大、地線對齊
                Vector3 g = guide.transform.position;
                Vector3 pos = new Vector3(g.x + guide.worldWidth * 0.5f + 1.2f + w * 0.5f, g.y, Mathf.Max(g.z, behindZ));
                LineHintSign s = LineHintSign.Create("LineHint_荒原按住S", "Hints/Desert_Brace", pos, w);
                s.bobAmplitude = 0f;          // 跟修毅的圖並排，不各自漂
                s.verticalRange = 14f;
                braceRight = pos.x + w * 0.5f;
            }
            else Debug.LogWarning("【線條畫提示】荒原：找不到修毅的說明圖或 Hints/Desert_Brace，按住 S 的圖沒放");

            // ★1020 荒原的鳥：掩體帶結束、開始有鳥的空地前（★1023 有光球門檻時接在按住 S 的圖右邊）
            if (LaterLevels) PlaceDesertBird(guide, his, behindZ, DesertOrbDirector.GateActive ? braceRight : float.NegativeInfinity);
        }

        // ── 3　水下：出生點右邊放「W／S 上浮與下潛」──
        if (scene.name.ToLower().Contains("underwater") && pm != null)
        {
            // 畫面下方的字已在 Install() 關掉
            Texture2D tex = Resources.Load<Texture2D>("Hints/Swim_WS_light");
            Vector3 p = pm.transform.position;
            float swimH = 0f;
            if (tex != null)
            {
                LineHintSign s = LineHintSign.Create("LineHint_水下WS", "Hints/Swim_WS_light", new Vector3(p.x + 6.5f, p.y + 1.2f, behindZ), tex.width / PxPerMeter);
                SetWaterGlow(s);
                swimH = tex.height / PxPerMeter;
            }
            if (LaterLevels)
            {
                // ★1020 光圈（氣）：W／S 圖的正下方
                Texture2D air = Resources.Load<Texture2D>("Hints/Swim_Air_light");
                if (air != null)
                {
                    float h = air.height / PxPerMeter;
                    Vector3 pos = new Vector3(p.x + 6.5f, p.y + 1.2f - swimH * 0.5f - 0.6f - h * 0.5f, behindZ);
                    LineHintSign s = LineHintSign.Create("LineHint_水下光圈", "Hints/Swim_Air_light", pos, air.width / PxPerMeter);
                    SetWaterGlow(s);
                }
                // ★1020 三張日誌開路：通關巨石的左邊；巨石散掉（物件關掉）就收起來
                PlaceNotesHint(behindZ);
            }
        }

        // ── 4　★1020 廢墟：修毅「回頭看狼」右邊接「按住 D 把巨石推上坡」──
        if (LaterLevels && FindAnyObjectByType<BoulderChallengeController>() != null)
        {
            RuinsLookBackHint lb = null;
            float wait = 0f;
            while (lb == null && wait < 4f)
            {
                lb = FindAnyObjectByType<RuinsLookBackHint>();
                if (lb == null) { wait += Time.unscaledDeltaTime; yield return null; }
            }
            PlaceRuinsPush(lb, behindZ);
        }

        // ── 5　★1020 玻璃館：鏡牆後、第一根燭火前「碰到每一根燭火，黑影變小、消失」──
        if (LaterLevels && FindAnyObjectByType<ShadowMonsterTriggerZone>() != null)
            PlaceGlassCandles(behindZ);
    }

    private static void SetWaterGlow(LineHintSign s)
    {
        s.glowColor = new Color(0.75f, 0.85f, 1f, 1f);   // 水裡用冷一點的柔光，淺色線才看得清楚
        s.glowIdle = 0.12f;
        s.glowLit = 0.35f;
        s.appearFar = 20f;
        s.appearFull = 10f;
    }

    // ★1020 廢墟推巨石
    private static void PlaceRuinsPush(RuinsLookBackHint lb, float behindZ)
    {
        Texture2D mine = Resources.Load<Texture2D>("Hints/Ruins_Push");
        if (mine == null) { Debug.LogWarning("【線條畫提示】★1020 找不到 Hints/Ruins_Push，廢墟推巨石的圖沒放"); return; }
        float w = mine.width / PxPerMeter, h = mine.height / PxPerMeter;
        Vector3 anchor; float hisW, hisH;
        Texture2D his = Resources.Load<Texture2D>("Hints/LookBackKey");
        if (lb != null)
        {
            anchor = lb.transform.position;
            hisW = lb.worldWidth;
        }
        else if (RuinsLookBackHint.TryGetAutoPosition(out anchor)) hisW = 8.5f;
        else { Debug.LogWarning("【線條畫提示】★1020 廢墟：找不到回頭看狼的圖示位置，推巨石的圖沒放"); return; }
        hisH = his != null ? hisW * his.height / his.width : hisW * 310f / 820f;
        // 兩張的底邊對齊（修毅的圖底下就是地線），放在他右邊
        float bottom = anchor.y - hisH * 0.5f;
        Vector3 pos = new Vector3(anchor.x + hisW * 0.5f + 1.2f + w * 0.5f, bottom + h * 0.5f, Mathf.Max(anchor.z, behindZ));
        LineHintSign s = LineHintSign.Create("LineHint_廢墟推巨石", "Hints/Ruins_Push", pos, w);
        s.bobAmplitude = 0f;            // 跟修毅的圖並排
        s.verticalRange = 14f;
        s.appearFar = 22f;
        s.appearFull = 12f;
        s.fadeOutStart = 9f;
        s.fadeOutEnd = 20f;
    }

    // ★1020 荒原的鳥
    private static void PlaceDesertBird(DesertGuideHint guide, Texture2D guideTex, float behindZ, float afterX = float.NegativeInfinity)
    {
        Texture2D mine = Resources.Load<Texture2D>("Hints/Desert_Bird");
        if (mine == null) { Debug.LogWarning("【線條畫提示】★1020 找不到 Hints/Desert_Bird，鳥的圖沒放"); return; }
        float w = mine.width / PxPerMeter, h = mine.height / PxPerMeter;
        // x：最後一根掩體（掩體帶結束、後面是鳥多的空地）往左 4 米；★1023 有光球門檻時接在「按住 S」右邊 1.5 米
        float lastX = float.NegativeInfinity;
        foreach (WindShelter ws in FindObjectsByType<WindShelter>(FindObjectsInactive.Exclude))
            if (ws != null) lastX = Mathf.Max(lastX, ws.transform.position.x);
        if (float.IsNegativeInfinity(lastX) && float.IsNegativeInfinity(afterX)) { Debug.LogWarning("【線條畫提示】★1020 荒原：沒有掩體，鳥的圖沒放"); return; }
        // y：跟修毅說明圖同一條地線（他的圖地線在 480/580 高的地方；我的在 452/470）
        float y;
        if (guide != null)
        {
            float hisH = guideTex != null ? guide.worldWidth * guideTex.height / guideTex.width : guide.worldWidth * 580f / 2100f;
            float groundY = guide.transform.position.y + hisH * 0.5f - hisH * (480f / 580f);
            y = groundY - h * 0.5f + h * (452f / 470f);
        }
        else y = 3.6f;
        float bx = float.IsNegativeInfinity(afterX) ? lastX - 4f : Mathf.Max(afterX + 1.5f + w * 0.5f, lastX - 4f);
        Vector3 pos = new Vector3(bx, y, behindZ);
        LineHintSign s = LineHintSign.Create("LineHint_荒原鳥", "Hints/Desert_Bird", pos, w);
        s.bobAmplitude = 0f;
        s.verticalRange = 14f;
        s.appearFar = 22f;
        s.appearFull = 12f;
    }

    // ★1020 水下：三張日誌開路
    private static void PlaceNotesHint(float behindZ)
    {
        Texture2D mine = Resources.Load<Texture2D>("Hints/Swim_Notes_light");
        QuestClearBarrierRock rock = FindAnyObjectByType<QuestClearBarrierRock>();
        if (mine == null || rock == null) { Debug.LogWarning("【線條畫提示】★1020 水下：找不到 Hints/Swim_Notes_light 或通關巨石，日誌的圖沒放"); return; }
        float w = mine.width / PxPerMeter, h = mine.height / PxPerMeter;
        Bounds b = new Bounds(rock.transform.position, Vector3.one * 4f);
        bool any = false;
        foreach (Renderer r in rock.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (b.size.x > 40f) b = new Bounds(rock.transform.position, Vector3.one * 6f);   // 太大就當作沒量到
        Vector3 pos = new Vector3(b.min.x - 1.5f - w * 0.5f, b.center.y + 1.0f, behindZ);
        LineHintSign s = LineHintSign.Create("LineHint_水下日誌開路", "Hints/Swim_Notes_light", pos, w);
        SetWaterGlow(s);
        s.radial = true;
        s.appearFar = 24f;
        s.appearFull = 14f;
        s.litRange = 9f;
        s.litFalloff = 8f;
        s.hideWhen = () => rock == null || !rock.gameObject.activeInHierarchy;
    }

    // ★1020 玻璃館：燭火與黑影
    private static void PlaceGlassCandles(float behindZ)
    {
        Texture2D mine = Resources.Load<Texture2D>("Hints/Glass_Candles_light");
        ShadowMonsterTriggerZone trig = FindAnyObjectByType<ShadowMonsterTriggerZone>();
        if (mine == null || trig == null) return;
        float w = mine.width / PxPerMeter, h = mine.height / PxPerMeter;
        float tx = trig.transform.position.x;
        Collider tc = trig.GetComponent<Collider>();
        if (tc != null) tx = tc.bounds.center.x;
        CandleCollectible first = null;
        foreach (CandleCollectible c in FindObjectsByType<CandleCollectible>(FindObjectsInactive.Exclude))
        {
            if (c == null || c.transform.position.x < tx) continue;
            if (first == null || c.transform.position.x < first.transform.position.x) first = c;
        }
        if (first == null) { Debug.LogWarning("【線條畫提示】★1020 玻璃館：找不到黑影觸發區右邊的燭火，燭火的圖沒放"); return; }
        Vector3 fc = first.transform.position;
        // x：觸發區和第一根燭火的中間；y：圖的地線在燭火底座上方 1.5 米
        float groundY = fc.y + 1.5f;
        Vector3 pos = new Vector3((tx + fc.x) * 0.5f, groundY - h * 0.5f + h * (452f / 470f), behindZ);
        LineHintSign s = LineHintSign.Create("LineHint_玻璃館燭火", "Hints/Glass_Candles_light", pos, w);
        s.glowColor = new Color(1f, 0.86f, 0.62f, 1f);   // 燭火的暖光；深色的星空裡淺色線
        s.glowIdle = 0.12f;
        s.glowLit = 0.38f;
        s.bobAmplitude = 0.08f;
        s.appearFar = 20f;
        s.appearFull = 10f;
        s.fadeOutStart = 8f;
        s.fadeOutEnd = 18f;
    }

    private static int Match(TextMeshPro t)
    {
        string n = t.gameObject.name;
        for (int i = 0; i < Tutorials.Length; i++)
            if (n == Tutorials[i].objectName) return i;
        string s = t.text ?? "";
        for (int i = 0; i < Tutorials.Length; i++)
            if (!string.IsNullOrEmpty(Tutorials[i].contains) && s.Contains(Tutorials[i].contains)) return i;
        return -1;
    }
}
