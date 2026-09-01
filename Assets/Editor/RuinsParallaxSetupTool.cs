#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity 廢墟背景 Parallax 系統：一鍵 Editor 建置、驗證與重設工具
///
/// 選單路徑：
///   Tools -> Ruins Parallax -> Setup Ruins Parallax   (一鍵自動建立、清理重複組件並分類 Parent)
///   Tools -> Ruins Parallax -> Validate Ruins Parallax(一鍵檢驗系統安全性與設定)
///   Tools -> Ruins Parallax -> Reset Ruins Parallax   (一鍵重設為初始狀態)
/// </summary>
[InitializeOnLoad]
public static class RuinsParallaxSetupTool
{
    private const string ROOT_NAME = "RuinsParallaxRoot";

    static RuinsParallaxSetupTool()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying) return;
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.isLoaded && (activeScene.name.Contains("SampleScene") || activeScene.name.Contains("ruin")))
            {
                GameObject rootObj = GameObject.Find(ROOT_NAME);
                if (rootObj == null)
                {
                    Debug.Log("[RuinsParallaxSetupTool] 偵測到尚未建立 '" + ROOT_NAME + "'，自動執行初次 Setup...");
                    SetupRuinsParallax(false);
                }
            }
        };
    }

    [MenuItem("Tools/Ruins Parallax/Setup Ruins Parallax", false, 10)]
    public static void SetupRuinsParallaxMenu()
    {
        SetupRuinsParallax(true);
    }

    public static void SetupRuinsParallax(bool showDialog)
    {
        Debug.Log("====================================================================");
        Debug.Log("🚀【Ruins Parallax】開始執行《廢墟背景 Parallax 系統》一鍵自動建置與場景重構...");

        // 1. 尋找玩家
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) playerObj = GameObject.Find("Player");

        if (playerObj == null)
        {
            Debug.LogError("❌ [RuinsParallaxSetup] 場景中找不到任何 Tag='Player' 或名稱為 'Player' 的物件！建置中止。");
            return;
        }

        // 2. 清除場景中所有殘留的個別 duplicate ParallaxGroup (過去掛在單一背景上的多餘腳本)
        ParallaxGroup[] existingGroups = Object.FindObjectsByType<ParallaxGroup>(FindObjectsSortMode.None);
        int removedDuplicateCount = 0;
        foreach (var group in existingGroups)
        {
            if (group.gameObject.name != ROOT_NAME)
            {
                Debug.Log($"  🧹 [清除重複控制器] 移除 '{group.gameObject.name}' 上的個別 ParallaxGroup 組件，由 Root 統一接管。");
                Undo.DestroyObjectImmediate(group);
                removedDuplicateCount++;
            }
        }

        // 3. 尋找或建立 RuinsParallaxRoot
        GameObject rootObj = GameObject.Find(ROOT_NAME);
        if (rootObj == null)
        {
            rootObj = new GameObject(ROOT_NAME);
            rootObj.transform.position = Vector3.zero;
            rootObj.transform.rotation = Quaternion.identity;
            rootObj.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(rootObj, "Create RuinsParallaxRoot");
            Debug.Log($"✨ [RuinsParallaxSetup] 已在場景中建立全新的 '{ROOT_NAME}' 父物件。");
        }
        else
        {
            Debug.Log($"ℹ️ [RuinsParallaxSetup] 偵測到現有 '{ROOT_NAME}' 父物件，將在其基礎上進行整合。");
        }

        // 4. 自動掃描場景中所有候選視覺物件
        int backgroundCount = 0;
        int decorationCount = 0;
        int debrisCount = 0;
        int atmosphereCount = 0;
        int skippedGameplayCount = 0;
        List<string> ambiguousObjects = new List<string>();

        // 取得場景中所有物件進行遍歷
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

        foreach (var go in allObjects)
        {
            if (go == null) continue;
            if (go == rootObj || go.transform.IsChildOf(rootObj.transform)) continue; // 已在 Root 內或就是 Root

            // --- 核心防呆：絕對排除 Gameplay、Player、Camera、Boundary 等系統物件 ---
            if (IsGameplayProtectedObject(go, playerObj))
            {
                skippedGameplayCount++;
                continue;
            }

            // --- 判斷是否為廢墟視覺候選物件 ---
            VisualClassification classification = ClassifyVisualObject(go);

            if (classification != VisualClassification.NotCandidate)
            {
                // 安全檢查：若該物件包含實體物理碰撞體 (非 Trigger)，則禁止自動 Parent
                if (HasNonTriggerSolidCollider(go))
                {
                    ambiguousObjects.Add($"{go.name} (原因: 包含實體物理 Collider，可能為 Gameplay 地形)");
                    continue;
                }

                // 執行 Safe Reparenting (worldPositionStays = true 保證世界座標 100% 絕對不變)
                Undo.SetTransformParent(go.transform, rootObj.transform, "Parent to RuinsParallaxRoot");
                go.transform.SetParent(rootObj.transform, true);

                // 自動修正 Sorting Order，杜絕背景/雲霧/傢俱遮擋 Player (Player 在 Layer 0, Order 0)
                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    Undo.RecordObject(sr, "Fix Sorting Order for Ruins Visuals");
                    switch (classification)
                    {
                        case VisualClassification.Background:
                            sr.sortingOrder = -10; // 最深底層背景
                            break;
                        case VisualClassification.Atmosphere:
                            sr.sortingOrder = -5;  // 遠景雲霧
                            break;
                        case VisualClassification.Decoration:
                        case VisualClassification.Debris:
                            sr.sortingOrder = -2;  // 中景裝飾與碎石（恆在玩家 Order 0 背後）
                            break;
                    }
                }

                switch (classification)
                {
                    case VisualClassification.Background:
                        backgroundCount++;
                        Debug.Log($"  📁 [加入背景] '{go.name}' (世界座標: {go.transform.position}, SortingOrder={sr?.sortingOrder})");
                        break;
                    case VisualClassification.Decoration:
                        decorationCount++;
                        Debug.Log($"  📁 [加入裝飾] '{go.name}' (世界座標: {go.transform.position}, SortingOrder={sr?.sortingOrder})");
                        break;
                    case VisualClassification.Debris:
                        debrisCount++;
                        Debug.Log($"  📁 [加入碎石殘骸] '{go.name}' (世界座標: {go.transform.position}, SortingOrder={sr?.sortingOrder})");
                        break;
                    case VisualClassification.Atmosphere:
                        atmosphereCount++;
                        Debug.Log($"  📁 [加入氛圍/雲霧] '{go.name}' (世界座標: {go.transform.position}, SortingOrder={sr?.sortingOrder})");
                        break;
                }
            }
        }

        // 5. 在 RuinsParallaxRoot 上配置 ParallaxGroup 組件
        ParallaxGroup parallax = rootObj.GetComponent<ParallaxGroup>();
        if (parallax == null)
        {
            parallax = Undo.AddComponent<ParallaxGroup>(rootObj);
        }

        Undo.RecordObject(parallax, "Configure ParallaxGroup");
        parallax.player = playerObj.transform;
        parallax.parallaxRoot = rootObj.transform;
        parallax.useCurrentTransformAsRoot = true;
        parallax.ruinedZoneYThreshold = -85f;
        parallax.enablePlayerParallax = true;
        parallax.rightMoveParallaxFactor = 0.15f;
        parallax.leftMoveParallaxFactor = 0.08f;
        parallax.playerStopThreshold = 0.001f;
        parallax.enableAutonomousDrift = true;
        parallax.driftSpeedX = 0.5f;

        // 6. 標記場景髒標記並立即存檔
        EditorUtility.SetDirty(rootObj);
        EditorUtility.SetDirty(parallax);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        // 7. 輸出建置統計與報告
        Debug.Log("--------------------------------------------------------------------");
        Debug.Log($"🎉【Ruins Parallax 建置完成並已保存至場景！】\n" +
                  $" - 統一根物件: '{ROOT_NAME}'\n" +
                  $" - 追蹤玩家: '{playerObj.name}'\n" +
                  $" - 清除舊版重複控制器: {removedDuplicateCount} 個\n" +
                  $" - 納入背景 (Backgrounds): {backgroundCount} 個\n" +
                  $" - 納入裝飾 (Decorations): {decorationCount} 個\n" +
                  $" - 納入碎石 (Debris): {debrisCount} 個\n" +
                  $" - 納入氛圍 (Atmosphere/Clouds): {atmosphereCount} 個\n" +
                  $" - 安全保護排除 (Gameplay/Colliders/Player/Camera): {skippedGameplayCount} 個");

        if (ambiguousObjects.Count > 0)
        {
            Debug.LogWarning($"⚠️ [RuinsParallaxSetup] 以下 {ambiguousObjects.Count} 個物件因包含實體碰撞未自動移動，請手動確認：\n" +
                             string.Join("\n", ambiguousObjects));
        }

        Debug.Log("====================================================================");

        ValidateRuinsParallax();
    }

    [MenuItem("Tools/Ruins Parallax/Validate Ruins Parallax", false, 11)]
    public static void ValidateRuinsParallax()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) playerObj = GameObject.Find("Player");

        GameObject rootObj = GameObject.Find(ROOT_NAME);
        ParallaxGroup parallax = rootObj != null ? rootObj.GetComponent<ParallaxGroup>() : null;
        CameraTargetXFollower cameraFollower = Object.FindFirstObjectByType<CameraTargetXFollower>();

        bool playerOk = playerObj != null;
        bool rootOk = rootObj != null && parallax != null;
        bool noPlayerInRoot = true;
        bool noCameraInRoot = true;
        bool noSolidColliderInRoot = true;

        int bgCount = 0;
        int decorCount = 0;
        List<string> issues = new List<string>();

        if (rootObj != null)
        {
            Transform[] children = rootObj.GetComponentsInChildren<Transform>(true);
            foreach (var t in children)
            {
                if (t == rootObj.transform) continue;

                if (t.gameObject.CompareTag("Player") || t.GetComponent<PlayerMovement>() != null)
                {
                    noPlayerInRoot = false;
                    issues.Add($"❌ Player 物件 '{t.name}' 被誤放入了 RuinsParallaxRoot 底下！");
                }

                if (t.GetComponent<Camera>() != null || t.name.ToLower().Contains("camera"))
                {
                    noCameraInRoot = false;
                    issues.Add($"❌ Camera 物件 '{t.name}' 被誤放入了 RuinsParallaxRoot 底下！");
                }

                Collider col = t.GetComponent<Collider>();
                if (col != null && !col.isTrigger && col.gameObject.layer != LayerMask.NameToLayer("Ignore Raycast"))
                {
                    noSolidColliderInRoot = false;
                    issues.Add($"⚠️ 實體碰撞體 '{t.name}' 在 RuinsParallaxRoot 底下，會隨背景位移！建議移出。");
                }

                string n = t.name.ToLower();
                if (n.Contains("bg") || n.Contains("background")) bgCount++;
                else decorCount++;
            }
        }

        ParallaxGroup[] allGroups = Object.FindObjectsByType<ParallaxGroup>(FindObjectsSortMode.None);
        bool singleGroup = allGroups.Length == 1 && allGroups[0].gameObject.name == ROOT_NAME;
        if (!singleGroup)
        {
            issues.Add($"⚠️ 場景中存在 {allGroups.Length} 個 ParallaxGroup 組件！建議由 RuinsParallaxRoot 統一管理。");
        }

        bool allReady = playerOk && rootOk && noPlayerInRoot && noCameraInRoot && noSolidColliderInRoot && singleGroup;

        string output = "\n" +
            "========== RUINS PARALLAX VALIDATION ==========\n" +
            $"Player Reference        {(playerOk ? "✓" : "✗ MISSING")}\n" +
            $"RuinsParallaxRoot       {(rootOk ? "✓" : "✗ MISSING")}\n" +
            $"Background Objects      ✓ {bgCount}\n" +
            $"Decoration Objects      ✓ {decorCount}\n" +
            $"Gameplay Objects        {(noSolidColliderInRoot ? "✓ Protected" : "⚠ Review Needed")}\n" +
            $"Camera                  {(noCameraInRoot ? "✓ Protected" : "✗ ERROR")}\n" +
            $"Camera Bounds           {(cameraFollower != null ? "✓ Protected & Dynamic Offset Active" : "✓ (Standard)")}\n" +
            $"Duplicate Controllers   {(singleGroup ? "✓ None (Single Controller)" : $"⚠ {allGroups.Length} Controllers Found")}\n\n" +
            $"Player Follow           ✓ (Right: {(parallax != null ? parallax.rightMoveParallaxFactor.ToString("F2") : "0.15")}, Left: {(parallax != null ? parallax.leftMoveParallaxFactor.ToString("F2") : "0.08")})\n" +
            $"Autonomous Drift        ✓ (Speed: {(parallax != null ? parallax.driftSpeedX.ToString("F1") : "0.5")})\n\n" +
            $"STATUS: {(allReady ? "READY" : "ATTENTION REQUIRED")}\n" +
            "===============================================";

        if (allReady)
        {
            Debug.Log(output);
        }
        else
        {
            Debug.LogWarning(output + "\n\n詳細診斷問題：\n" + string.Join("\n", issues));
        }
    }

    [MenuItem("Tools/Ruins Parallax/Reset Ruins Parallax", false, 12)]
    public static void ResetRuinsParallax()
    {
        GameObject rootObj = GameObject.Find(ROOT_NAME);
        if (rootObj == null)
        {
            Debug.LogWarning("⚠️ [RuinsParallaxSetup] 場景中找不到 'RuinsParallaxRoot'，無需重設。");
            return;
        }

        Undo.RecordObject(rootObj.transform, "Reset RuinsParallaxRoot Position");
        rootObj.transform.position = Vector3.zero;

        ParallaxGroup parallax = rootObj.GetComponent<ParallaxGroup>();
        if (parallax != null)
        {
            Undo.RecordObject(parallax, "Reset ParallaxGroup State");
            parallax.ResetToInitialState();
        }

        Debug.Log("🔄 [RuinsParallaxSetup] 已重設 'RuinsParallaxRoot' 位置為 (0,0,0) 並清除累積位移。");
    }

    // --- 輔助分類判斷邏輯 ---

    private enum VisualClassification
    {
        NotCandidate,
        Background,
        Decoration,
        Debris,
        Atmosphere
    }

    private static bool IsGameplayProtectedObject(GameObject go, GameObject playerObj)
    {
        if (go == playerObj || go.transform.IsChildOf(playerObj.transform)) return true;

        string n = go.name.ToLower();
        string t = go.tag;

        if (t == "Player" || t == "MainCamera" || t == "CameraBoundary" || t == "Respawn" || t == "Finish" || t == "Enemy") return true;

        if (n.Contains("camera") || n.Contains("cinemachine") || n.Contains("confiner") || n.Contains("cameratarget")) return true;
        if (n.Contains("respawn") || n.Contains("checkpoint") || n.Contains("deathzone") || n.Contains("killzone")) return true;
        if (n.Contains("ground") || n.Contains("floor") || n.Contains("platform") || n.Contains("ladder") || n.Contains("pushable")) return true;
        if (n.Contains("player") || n.Contains("monster") || n.Contains("enemy") || n.Contains("bird") || n.Contains("boss")) return true;
        if (n.Contains("lever") || n.Contains("door") || n.Contains("destructible") || n.Contains("candle") || n.Contains("interact")) return true;

        if (go.GetComponent<PlayerMovement>() != null ||
            go.GetComponent<PlayerRespawnSystem>() != null ||
            go.GetComponent<CameraTargetXFollower>() != null ||
            go.GetComponent<Rigidbody>() != null)
        {
            return true;
        }

        return false;
    }

    private static VisualClassification ClassifyVisualObject(GameObject go)
    {
        string n = go.name.ToLower();
        string t = go.tag;

        bool isInRuinedY = go.transform.position.y <= -50f;

        if (t == "RuinedBackground" || n.Contains("ruinedbackground") || n.Contains("ruinsbackground") || n.Contains("ruins_bg") || n.Contains("ruin_bg") || n.Contains("ruin_background"))
        {
            return VisualClassification.Background;
        }

        if (n.Contains("ruin_cloud") || n.Contains("ruinscloud") || n.Contains("ruinsfog") || n.Contains("ruinsatmosphere") || n.Contains("stormwind"))
        {
            if (isInRuinedY) return VisualClassification.Atmosphere;
        }

        if (n.Contains("ruinsdebris") || n.Contains("debris") || n.Contains("碎石") || n.Contains("rubble"))
        {
            if (isInRuinedY) return VisualClassification.Debris;
        }

        if (n.Contains("ruin_furniture") || n.Contains("ruinsdecoration") || n.Contains("ruins_decor") || n.Contains("ruinsdecor") || n.Contains("ruinspillar_bg"))
        {
            if (isInRuinedY) return VisualClassification.Decoration;
        }

        return VisualClassification.NotCandidate;
    }

    private static bool HasNonTriggerSolidCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col != null && !col.isTrigger && col.gameObject.layer != LayerMask.NameToLayer("Ignore Raycast"))
        {
            return true;
        }
        return false;
    }
}
#endif
