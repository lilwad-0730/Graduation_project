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
/// 開關：LineHintDirector.Enabled＝false 就全部照舊（字回來、不放圖）。
/// </summary>
public class LineHintDirector : MonoBehaviour
{
    public static bool Enabled = true;

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

        foreach (LineHintDirector d in FindObjectsByType<LineHintDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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
        TextMeshPro[] texts = FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
            if (guide != null && mine != null && his != null)
            {
                float w = guide.worldWidth * mine.width / his.width;     // 同一個比例：她一樣大、地線對齊
                Vector3 g = guide.transform.position;
                Vector3 pos = new Vector3(g.x + guide.worldWidth * 0.5f + 1.2f + w * 0.5f, g.y, Mathf.Max(g.z, behindZ));
                LineHintSign s = LineHintSign.Create("LineHint_荒原按住S", "Hints/Desert_Brace", pos, w);
                s.bobAmplitude = 0f;          // 跟修毅的圖並排，不各自漂
                s.verticalRange = 14f;
            }
            else Debug.LogWarning("【線條畫提示】荒原：找不到修毅的說明圖或 Hints/Desert_Brace，按住 S 的圖沒放");
        }

        // ── 3　水下：出生點右邊放「W／S 上浮與下潛」──
        if (scene.name.ToLower().Contains("underwater") && pm != null)
        {
            // 畫面下方的字已在 Install() 關掉
            Texture2D tex = Resources.Load<Texture2D>("Hints/Swim_WS_light");
            if (tex != null)
            {
                Vector3 p = pm.transform.position;
                LineHintSign s = LineHintSign.Create("LineHint_水下WS", "Hints/Swim_WS_light", new Vector3(p.x + 6.5f, p.y + 1.2f, behindZ), tex.width / PxPerMeter);
                s.glowColor = new Color(0.75f, 0.85f, 1f, 1f);   // 水裡用冷一點的柔光，淺色線才看得清楚
                s.glowIdle = 0.12f;
                s.glowLit = 0.35f;
                s.appearFar = 20f;
                s.appearFull = 10f;
            }
        }
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
