using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Unity 頂部選單一鍵工具：
/// 點擊【Tools > 自動為場景中所有鳥敵人掛載程式與音效 (Setup All Birds)】
/// 即可一鍵自動搜尋場景所有烏鴉/鳥類物件，自動掛載 IndividualBirdEnemy、設定音效、動畫與剛體！
/// </summary>
public static class BirdEnemySetupTool
{
    [MenuItem("Tools/自動為場景中所有鳥敵人掛載程式與音效 (Setup All Birds) %#b")]
    public static void SetupAllBirdsInScene()
    {
        // 載入標準音效資源
        AudioClip warningClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/living birds/sounds/crow1.wav");
        AudioClip flapClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/鳥振翅1.mp3");

        // 搜尋場景中所有可能的烏鴉物件
        Transform[] allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        List<GameObject> birdObjects = new List<GameObject>();

        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            string name = t.name.ToLower();

            // 包含 crow / bird / living bird 或已掛載 IndividualBirdEnemy 的物件
            if (name.Contains("crow") || name.Contains("bird") || t.GetComponent<IndividualBirdEnemy>() != null)
            {
                // 排除父層群組容器 (只抓取有 Animator 或 MeshRenderer 的實體鳥)
                if (t.GetComponent<Animator>() != null || t.GetComponentInChildren<SkinnedMeshRenderer>() != null || t.GetComponent<MeshRenderer>() != null || t.GetComponent<IndividualBirdEnemy>() != null)
                {
                    if (!birdObjects.Contains(t.gameObject))
                    {
                        birdObjects.Add(t.gameObject);
                    }
                }
            }
        }

        if (birdObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "場景中未找到名稱包含 crow 或 bird 的鳥類物件！", "確定");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Setup All Bird Enemies");
        int undoGroup = Undo.GetCurrentGroup();

        int setupCount = 0;
        foreach (var go in birdObjects)
        {
            Undo.RegisterCompleteObjectUndo(go, "Setup Bird");

            // 1. 若誤掛了 GroundCollisionNotifier 則自動移除 (防止空中自爆)
            GroundCollisionNotifier gcn = go.GetComponent<GroundCollisionNotifier>();
            if (gcn != null)
            {
                Undo.DestroyObjectImmediate(gcn);
            }

            // 2. 掛載或取得 IndividualBirdEnemy
            IndividualBirdEnemy bird = go.GetComponent<IndividualBirdEnemy>();
            if (bird == null)
            {
                bird = Undo.AddComponent<IndividualBirdEnemy>(go);
            }

            // 3. 配置標準設定
            bird.autoDetectPlayer = true;
            bird.detectionRange = 10f;
            bird.behaviorType = BirdBehavior.DirectPlayer; // 直撲玩家位置 (可走位閃避)
            bird.diveSpeed = 12f;
            bird.warningDuration = 1.2f;
            bird.stuckDuration = 4f;
            bird.fadeDuration = 1f;

            // 4. 配置音效
            if (bird.warningClip == null) bird.warningClip = warningClip;
            if (bird.flapClip == null) bird.flapClip = flapClip;

            // 5. 配置 AudioSource
            AudioSource audio = go.GetComponent<AudioSource>();
            if (audio == null)
            {
                audio = Undo.AddComponent<AudioSource>(go);
            }
            audio.playOnAwake = false;
            audio.spatialBlend = 0.5f;

            // 6. 配置 Rigidbody
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(go);
            }
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezePositionZ;

            EditorUtility.SetDirty(go);
            setupCount++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"✅【鳥群工具】已成功為場景中 {setupCount} 隻鳥敵人自動掛載 IndividualBirdEnemy 與配置完整音效！");
        EditorUtility.DisplayDialog("完成", $"已成功為場景中 {setupCount} 隻鳥敵人自動掛載程式與音效！\n（包含 crow1.wav 鳴叫、振翅聲、直撲走位閃避機制）", "確定");
    }

    [MenuItem("Tools/仙人掌高度調整/一鍵對齊沙漠地表 (Y = 2.45)")]
    public static void AlignCactiToGround()
    {
        SetCactusY(2.45f);
    }

    [MenuItem("Tools/仙人掌高度調整/稍微往下移 0.5 米 (Down 0.5m)")]
    public static void MoveCactiDown()
    {
        GameObject cactus = GameObject.Find("cactus");
        if (cactus != null)
        {
            SetCactusY(cactus.transform.position.y - 0.5f);
        }
    }

    [MenuItem("Tools/仙人掌高度調整/稍微往上移 0.5 米 (Up 0.5m)")]
    public static void MoveCactiUp()
    {
        GameObject cactus = GameObject.Find("cactus");
        if (cactus != null)
        {
            SetCactusY(cactus.transform.position.y + 0.5f);
        }
    }

    private static void SetCactusY(float newY)
    {
        GameObject cactus = GameObject.Find("cactus");
        if (cactus == null)
        {
            EditorUtility.DisplayDialog("提示", "場景中未找到名稱為 'cactus' 的物件！", "確定");
            return;
        }

        Undo.RecordObject(cactus.transform, "Adjust Cactus Height");
        
        Vector3 pos = cactus.transform.position;
        pos.y = newY;
        cactus.transform.position = pos;

        EditorUtility.SetDirty(cactus);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"🌵【仙人掌工具】已將 cactus 群組高度調整為 Y = {newY:F2}f！");
    }
}
