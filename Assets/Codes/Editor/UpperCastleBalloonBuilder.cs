#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ★1012 棉花堡上層氣球 一鍵建立工具。
///
/// 選單：Tools ▸ 棉花堡上層 ▸ 建立氣球（可吸收＋後景）
///   1. 把 Assets/Sprites/UpperCastleBalloons 裡的貼圖設成 Sprite（含字卡底圖的 9-slice 邊界）
///   2. 在目前開著的場景（SampleScene）建立 UpperCastle_Balloons：
///        ・10 顆可吸收的氣球：沿著上層由左到右分布，高度跟著地面（有的走過去就碰到、有的要跳一下），每顆一張字卡
///        ・後景鋪底氣球 33 顆：遠／中／近三層，越遠越小越淡越慢
///   3. 接好字型（Msjh_SDF）、笑聲、字卡底圖、火花，存檔。
/// 重複按會先刪掉舊的再重建（位置用固定亂數種子，結果每次都一樣）。
/// 不想要了：Tools ▸ 棉花堡上層 ▸ 移除氣球。或直接刪掉整個 feature/upper-castle-balloons 分支。
/// </summary>
public static class UpperCastleBalloonBuilder
{
    const string RootName = "UpperCastle_Balloons";
    const string SpriteDir = "Assets/Sprites/UpperCastleBalloons";
    const string LaughPath = "Assets/Music/棉花堡/嬰兒笑聲_吸收.wav";   // 由「嬰兒笑聲.mp3」裁 1.3~2.1 秒＋整理＋淡淡殘響而來
    const string FontPath = "Assets/TextMesh Pro/Msjh_SDF.asset";

    static readonly string[] ColorNames = { "peach", "rose", "butter", "sky", "lavender", "mint" };
    // ★1015 配合棉花堡的珍珠白灰藍雲（平均色約 226,231,235）：氣球貼圖已降飽和，這裡的色調跟貼圖實際色一致
    static readonly Color[] Tints =
    {
        new Color(0.87f, 0.78f, 0.75f), new Color(0.84f, 0.73f, 0.77f), new Color(0.89f, 0.87f, 0.76f),
        new Color(0.74f, 0.80f, 0.84f), new Color(0.78f, 0.76f, 0.83f), new Color(0.77f, 0.84f, 0.81f),
    };

    // 產婦對生產的美好幻想（草稿，文字請撰稿者過目後再調整；換行是手排的）
    static readonly string[] Cards =
    {
        "想像抱起她的那一刻，\n所有的痛，都會變得很輕",
        "她的小手會握住我的手指，\n像是在說：我在這裡",
        "我想，她第一次睜開眼睛，\n一定會先看見我",
        "窗外的陽光剛好落下來，\n落在她小小的臉上",
        "我會把唱了好幾個月的歌，\n輕輕唱給她聽",
        "她的呼吸那麼輕，\n像一朵剛剛飄來的雲",
        "我已經想好她的名字了，\n要一個字一個字，慢慢告訴她",
        "春天的時候，\n我們會一起認識這個世界",
        "等她長大一點，\n我要帶她來看這片雲海",
        "她笑的時候，\n整個房間都亮了起來",
    };

    // 可吸收氣球的高度（離地面多高）：循環使用，有的走過去就碰到、有的要跳一下
    static readonly float[] HeightPattern = { 2.4f, 3.6f, 4.8f, 3.0f, 5.6f, 2.8f, 4.2f, 5.2f, 3.4f, 4.6f };

    [MenuItem("Tools/棉花堡上層/建立氣球（可吸收＋後景）")]
    public static void Build()
    {
        if (!EditorUtility.DisplayDialog("建立棉花堡上層氣球",
                "會在目前開著的場景建立 " + RootName + "（可吸收氣球 10 顆＋後景氣球），然後存檔。\n如果已經有，會先刪掉舊的再重建。\n\n要繼續嗎？",
                "建立並存檔", "取消"))
            return;

        ConfigureImporters();

        Sprite[] balloonSprites = new Sprite[ColorNames.Length];
        for (int i = 0; i < ColorNames.Length; i++)
            balloonSprites[i] = LoadSprite("balloon_" + ColorNames[i] + ".png");
        Sprite stringSprite = LoadSprite("balloon_string.png");
        Sprite glowSprite = LoadSprite("balloon_glow.png");
        Sprite sparkSprite = LoadSprite("balloon_spark.png");
        Sprite cardSprite = LoadSprite("balloon_card.png");
        AudioClip laugh = AssetDatabase.LoadAssetAtPath<AudioClip>(LaughPath);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        foreach (Sprite s in balloonSprites)
            if (s == null) { EditorUtility.DisplayDialog("缺貼圖", "找不到 " + SpriteDir + " 裡的氣球貼圖，請確認分支內容完整。", "好"); return; }
        if (laugh == null) Debug.LogWarning("[棉花堡氣球] 找不到笑聲音檔 " + LaughPath + "，吸收時不會有聲音（之後可在 Field 的 Laugh Clip 補上）。");

        RemoveExisting();

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "建立棉花堡氣球");
        UpperCastleBalloonField field = root.AddComponent<UpperCastleBalloonField>();
        field.fontAsset = font;
        field.cardSprite = cardSprite;
        field.sparkSprite = sparkSprite;
        field.laughClip = laugh;

        UpperCastleFloorController floor = Object.FindFirstObjectByType<UpperCastleFloorController>();
        System.Random rng = new System.Random(20261012);

        // ── 可吸收的氣球 ──
        GameObject collectRoot = new GameObject("Collectible");
        collectRoot.transform.SetParent(root.transform, false);
        var collectPositions = new List<Vector2>();
        int count = Cards.Length;
        for (int i = 0; i < count; i++)
        {
            float x = Mathf.Lerp(-56f, 118f, i / (float)(count - 1)) + (float)(rng.NextDouble() * 5.0 - 2.5);
            float ground = floor != null ? floor.GetExpectedGroundY(x) : 40f;
            float y = ground + HeightPattern[i % HeightPattern.Length];
            collectPositions.Add(new Vector2(x, y));

            int ci = i % ColorNames.Length;
            float scale = 0.76f + (float)rng.NextDouble() * 0.1f;
            GameObject b = new GameObject("Balloon_" + (i + 1).ToString("00"));
            b.transform.SetParent(collectRoot.transform, false);
            b.transform.position = new Vector3(x, y, -0.5f);
            b.transform.localScale = new Vector3(scale, scale, 1f);

            SpriteRenderer halo, glow;
            AddGlows(b.transform, glowSprite, Tints[ci], scale, out halo, out glow);
            SpriteRenderer str = AddSprite(b.transform, "String", stringSprite, new Vector3(0f, -0.69f, 0f), 6, Color.white);
            SpriteRenderer body = AddSprite(b.transform, "Body", balloonSprites[ci], Vector3.zero, 7, Color.white);

            UpperCastleBalloon comp = b.AddComponent<UpperCastleBalloon>();
            comp.cardText = Cards[i];
            comp.tint = Tints[ci];
            comp.body = body;
            comp.stringRenderer = str;
            comp.glow = glow;
            comp.halo = halo;
            comp.bobAmplitude = 0.28f + (float)rng.NextDouble() * 0.2f;
            comp.bobSpeed = 0.75f + (float)rng.NextDouble() * 0.35f;
            comp.driftAmplitude = 0.8f + (float)rng.NextDouble() * 0.8f;
            comp.driftSpeed = 0.16f + (float)rng.NextDouble() * 0.12f;
        }

        // ── 後景鋪底氣球（遠／中／近三層）──
        GameObject backRoot = new GameObject("Backdrop");
        backRoot.transform.SetParent(root.transform, false);
        int made = 0;
        made += BuildBackdropBand(backRoot.transform, "Far", rng, balloonSprites, stringSprite, collectPositions, 14, -90f, 175f, 52f, 62f, 9f, 2, 0.42f, 0.58f, 0.50f, 0.35f, 0.22f, 1.1f, 0.6f);
        made += BuildBackdropBand(backRoot.transform, "Mid", rng, balloonSprites, stringSprite, collectPositions, 11, -85f, 170f, 45f, 56f, 5f, 3, 0.65f, 0.90f, 0.62f, 0.20f, 0.35f, 1.8f, 0.8f);
        made += BuildBackdropBand(backRoot.transform, "Near", rng, balloonSprites, stringSprite, collectPositions, 8, -75f, 160f, 43f, 51f, 2.5f, 4, 0.95f, 1.25f, 0.78f, 0.10f, 0.45f, 2.4f, 1.0f);

        // ── 棉花堡下層：原本的教學文字改成氣球，碰到才浮出同一段字 ──
        int lowerMade = BuildLowerTutorial(root.transform, balloonSprites, stringSprite, glowSprite);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log("[棉花堡氣球] 建立完成：上層可吸收氣球 " + count + " 顆、下層教學氣球 " + lowerMade + " 顆、後景氣球 " + made + " 顆，已存檔 " + root.scene.name + "。\n"
                  + "測試：從棉花堡進階梯到上層，碰氣球看字卡與笑聲。不要了就選 Tools ▸ 棉花堡上層 ▸ 移除氣球，或刪掉 feature/upper-castle-balloons 分支。");
    }

    // 棉花堡下層原本的教學文字物件（位置固定在場景裡，文字內容直接從物件上讀，不寫死）
    static readonly string[] LowerTextNames = { "TutorialText_1", "TutorialText_2", "TutorialText_3", "TutorialText_4", "TutorialText_5" };
    // 氣球離地高度：碰得到（走過去就碰到，不用跳），不會比原本文字位置高
    const float LowerBalloonHeight = 2.3f;

    static GameObject FindSceneObjectByName(string name)
    {
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            if (go != null && go.name == name && go.scene.IsValid() && go.transform.parent == null) return go;
        return null;
    }

    static int BuildLowerTutorial(Transform root, Sprite[] balloonSprites, Sprite stringSprite, Sprite glowSprite)
    {
        GameObject group = new GameObject("LowerTutorial");
        group.transform.SetParent(root, false);
        System.Random rng = new System.Random(20261015);   // 獨立的亂數：不影響上層與後景氣球原本的位置
        int made = 0;
        for (int i = 0; i < LowerTextNames.Length; i++)
        {
            GameObject src = FindSceneObjectByName(LowerTextNames[i]);
            if (src == null) { Debug.LogWarning("[棉花堡氣球] 找不到下層教學文字 " + LowerTextNames[i] + "，略過。"); continue; }
            TMP_Text tmp = src.GetComponent<TMP_Text>();
            string card = tmp != null ? tmp.text.Trim() : "";
            if (string.IsNullOrEmpty(card)) { Debug.LogWarning("[棉花堡氣球] " + LowerTextNames[i] + " 沒有文字，略過。"); continue; }

            // 氣球放在原文字的 X；高度＝腳下地面＋一點點（從文字上方往下打射線找地面），不超過原文字高度
            Vector3 tp = src.transform.position;
            float y = tp.y - 2f;
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(tp.x, tp.y + 12f, 0f), Vector3.down, out hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                y = Mathf.Clamp(hit.point.y + LowerBalloonHeight, tp.y - 3.5f, tp.y);

            int ci = (i + 2) % ColorNames.Length;
            float scale = 0.9f;
            GameObject go = new GameObject("TutorialBalloon_" + (i + 1));
            go.transform.SetParent(group.transform, false);
            go.transform.position = new Vector3(tp.x, y, -0.5f);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            SpriteRenderer halo, glow;
            AddGlows(go.transform, glowSprite, Tints[ci], scale, out halo, out glow);
            SpriteRenderer str = AddSprite(go.transform, "String", stringSprite, new Vector3(0f, -0.69f, 0f), 6, Color.white);
            SpriteRenderer body = AddSprite(go.transform, "Body", balloonSprites[ci], Vector3.zero, 7, Color.white);

            UpperCastleBalloon comp = go.AddComponent<UpperCastleBalloon>();
            comp.cardText = card;
            comp.tint = Tints[ci];
            comp.body = body;
            comp.stringRenderer = str;
            comp.glow = glow;
            comp.halo = halo;
            comp.bobAmplitude = 0.28f + (float)rng.NextDouble() * 0.15f;
            comp.bobSpeed = 0.75f + (float)rng.NextDouble() * 0.3f;
            comp.driftAmplitude = 0.5f + (float)rng.NextDouble() * 0.4f;   // 教學氣球漂得小一點，位置才穩定
            comp.driftSpeed = 0.16f + (float)rng.NextDouble() * 0.1f;
            comp.touchRadius = 1.4f;                                        // 教學提示判定寬鬆一點，不要錯過

            src.SetActive(false);   // 原本的靜態教學文字收起來（Tools ▸ 移除氣球 會還原）
            made++;
            Debug.Log("[棉花堡氣球] 下層教學氣球 " + (i + 1) + "：「" + card.Replace("\n", " ") + "」→ 位置 (" + tp.x.ToString("F1") + ", " + y.ToString("F1") + ")");
        }
        return made;
    }

    /// <summary>★1015 柔光：內圈 Glow（亮、呼吸）＋外圈 Halo（大而淡），顏色從氣球色往棉花堡的奶油暖白靠，跟場景搭。</summary>
    static void AddGlows(Transform parent, Sprite glowSprite, Color tint, float scale, out SpriteRenderer halo, out SpriteRenderer glow)
    {
        // ★1015 背景是淺色的珍珠白，白色／淡色的光暈看不出來。光暈改用比背景「深一階、暖一點」的香檳金，
        //   跟光球（奶油金）同一個色系，在淺色雲海上才看得到，也不會跟珍珠色氣球打架。
        Color gold = new Color(1f, 0.80f, 0.50f);
        Color c = Color.Lerp(gold, tint, 0.18f);
        halo = AddSprite(parent, "Halo", glowSprite, new Vector3(0f, 0.1f, 0f), 4, new Color(c.r, c.g, c.b, 0.42f));
        halo.transform.localScale = Vector3.one * (10.5f / (2.56f * scale));
        glow = AddSprite(parent, "Glow", glowSprite, new Vector3(0f, 0.1f, 0f), 5, new Color(c.r, c.g, c.b, 0.95f));
        glow.transform.localScale = Vector3.one * (6.4f / (2.56f * scale));
    }

    [MenuItem("Tools/棉花堡上層/移除氣球")]
    public static void Remove()
    {
        // 還原下層被收起來的教學文字
        foreach (string nm in LowerTextNames)
        {
            GameObject go = FindSceneObjectByName(nm);
            if (go != null && !go.activeSelf) go.SetActive(true);
        }
        int n2 = RemoveExisting();
        int n = n2;
        if (n > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[棉花堡氣球] 已移除 " + RootName + "（記得存檔）。");
        }
        else Debug.Log("[棉花堡氣球] 場景裡沒有 " + RootName + "。");
    }

    static int RemoveExisting()
    {
        int n = 0;
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
        {
            if (go != null && go.name == RootName && go.transform.parent == null)
            {
                Undo.DestroyObjectImmediate(go);
                n++;
            }
        }
        return n;
    }

    static int BuildBackdropBand(Transform parent, string bandName, System.Random rng, Sprite[] balloons, Sprite stringSprite, List<Vector2> avoid,
                                 int count, float xMin, float xMax, float yMin, float yMax, float z, int order,
                                 float scaleMin, float scaleMax, float alpha, float paleness, float stringAlpha, float driftAmp, float speed)
    {
        GameObject band = new GameObject(bandName);
        band.transform.SetParent(parent, false);
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            Vector2 p = Vector2.zero;
            for (int tries = 0; tries < 12; tries++)
            {
                p = new Vector2(Mathf.Lerp(xMin, xMax, (i + (float)rng.NextDouble() * 0.9f) / count), Mathf.Lerp(yMin, yMax, (float)rng.NextDouble()));
                bool clear = true;
                foreach (Vector2 a in avoid)
                    if (Mathf.Abs(a.x - p.x) < 4f && Mathf.Abs(a.y - p.y) < 3.5f) { clear = false; break; }
                if (clear) break;
            }

            int ci = rng.Next(ColorNames.Length);
            float scale = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            GameObject b = new GameObject("Backdrop_" + bandName + "_" + (i + 1).ToString("00"));
            b.transform.SetParent(band.transform, false);
            b.transform.position = new Vector3(p.x, p.y, z);
            b.transform.localScale = new Vector3(scale, scale, 1f);

            // 越遠越淡：顏色往天空的奶油白靠，再降透明度
            Color pale = Color.Lerp(Color.white, new Color(1f, 0.93f, 0.90f), paleness);
            Color bodyColor = new Color(pale.r, pale.g, pale.b, alpha);
            SpriteRenderer str = AddSprite(b.transform, "String", stringSprite, new Vector3(0f, -0.69f, 0f), order - 1 + 0, new Color(1f, 1f, 1f, stringAlpha));
            SpriteRenderer body = AddSprite(b.transform, "Body", balloons[ci], Vector3.zero, order, bodyColor);

            UpperCastleBackdropBalloon comp = b.AddComponent<UpperCastleBackdropBalloon>();
            comp.stringRenderer = str;
            comp.bobAmplitude = (0.25f + (float)rng.NextDouble() * 0.35f) * (0.7f + speed * 0.3f);
            comp.bobSpeed = (0.35f + (float)rng.NextDouble() * 0.3f) * speed;
            comp.driftAmplitude = driftAmp * (0.6f + (float)rng.NextDouble() * 0.8f);
            comp.driftSpeed = (0.08f + (float)rng.NextDouble() * 0.08f) * speed;
            made++;
        }
        return made;
    }

    static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, Vector3 localPos, int order, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.color = color;
        return sr;
    }

    static Sprite LoadSprite(string file)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "/" + file);
    }

    /// <summary>把氣球貼圖設成 Sprite：100 PPU、無 mipmap、不壓縮；繩子的軸心在上緣中央；字卡底圖設 9-slice。</summary>
    static void ConfigureImporters()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteDir });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;

            string file = System.IO.Path.GetFileName(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;

            TextureImporterSettings ts = new TextureImporterSettings();
            ti.ReadTextureSettings(ts);
            if (file == "balloon_string.png")
            {
                ts.spriteAlignment = (int)SpriteAlignment.Custom;
                ti.SetTextureSettings(ts);
                ti.spritePivot = new Vector2(0.5f, 1f);
            }
            else
            {
                ts.spriteAlignment = (int)SpriteAlignment.Center;
                ti.SetTextureSettings(ts);
            }
            if (file == "balloon_card.png") ti.spriteBorder = new Vector4(56f, 46f, 56f, 46f);

            ti.SaveAndReimport();
        }
    }
}
#endif
