using UnityEngine;

/// <summary>
/// 自動為場景創建並確保 MonsterMutant7_Run1 存在於 Hierarchy 與場景中。
/// 並提供自動清空與重建乾淨 Animator Controller 之功能。
/// </summary>
[ExecuteAlways]
public class MonsterMutantRun1AutoSpawner : MonoBehaviour
{
    private void OnEnable()
    {
        EnsureMonsterInScene();
    }

    private void Start()
    {
        EnsureMonsterInScene();
    }

    public static void EnsureMonsterInScene()
    {
        GameObject existing = GameObject.Find("MonsterMutant7_Run1");
        
#if UNITY_EDITOR
        RuntimeAnimatorController controller = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/MonsterMutant 7/MonsterMutant7 Animator Controller.controller");

        if (existing != null)
        {
            Animator anim = existing.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController == null && controller != null)
            {
                anim.runtimeAnimatorController = controller;
            }
            return;
        }

        string fbxPath = "Assets/MonsterMutant 7/Animations/MutantMonster2@run1.fbx";
        GameObject fbxAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

        if (fbxAsset != null)
        {
            GameObject monsterGo = UnityEditor.PrefabUtility.InstantiatePrefab(fbxAsset) as GameObject;
            if (monsterGo == null) monsterGo = Instantiate(fbxAsset);
            monsterGo.name = "MonsterMutant7_Run1";

            monsterGo.transform.position = new Vector3(-78.5f, -35.61f, 0f);
            monsterGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            monsterGo.transform.localScale = Vector3.one;

            Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/MonsterMutant 7/Materials/Mat_MonsterMutant7_Skin1.mat");
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                UnityEditor.AssetDatabase.CreateAsset(mat, "Assets/MonsterMutant 7/Materials/Mat_MonsterMutant7_Skin1.mat");
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat.shader = shader;
            }

            Texture2D albedo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MonsterMutant 7/Texture/textures skin1 body v2/1_Albedo.tga");
            Texture2D normal = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MonsterMutant 7/Texture/textures skin1 body v2/1_Normal.tga");
            Texture2D emission = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MonsterMutant 7/Texture/textures skin1 body v2/1_Emission.tga");

            if (albedo != null) { mat.SetTexture("_BaseMap", albedo); mat.SetTexture("_MainTex", albedo); }
            if (normal != null) { mat.SetTexture("_BumpMap", normal); }
            if (emission != null) { mat.SetTexture("_EmissionMap", emission); mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Color.white); }

            UnityEditor.EditorUtility.SetDirty(mat);

            Renderer[] renderers = monsterGo.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                r.sharedMaterial = mat;
                UnityEditor.EditorUtility.SetDirty(r);
            }

            Animator animator = monsterGo.GetComponent<Animator>();
            if (animator == null) animator = monsterGo.AddComponent<Animator>();
            if (controller != null) animator.runtimeAnimatorController = controller;

            ShadowMonsterController shadowCtrl = monsterGo.GetComponent<ShadowMonsterController>();
            if (shadowCtrl == null) shadowCtrl = monsterGo.AddComponent<ShadowMonsterController>();

            UnityEditor.Selection.activeGameObject = monsterGo;
            UnityEditor.EditorGUIUtility.PingObject(monsterGo);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("[MonsterMutantRun1AutoSpawner] 成功在 Hierarchy 中建立並選取 MonsterMutant7_Run1！");
        }
#endif
    }

#if UNITY_EDITOR
    // ──────────────────────────────────────────────
    // ★ 重要：第一次設置時請先點這個！
    // Tools > Fix MonsterMutant7 Avatar + Controller
    // ──────────────────────────────────────────────
    [UnityEditor.MenuItem("Tools/Fix MonsterMutant7 Avatar + Controller")]
    public static void FixMonsterAvatar()
    {
        // 1. 載入正確的 Avatar（來自 Base mesh MonsterMutant7.fbx）
        string avatarSourcePath = "Assets/MonsterMutant 7/Base mesh/Base mesh MonsterMutant7.fbx";
        Object[] avatarAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(avatarSourcePath);
        Avatar correctAvatar = null;
        foreach (Object obj in avatarAssets)
        {
            if (obj is Avatar av) { correctAvatar = av; break; }
        }

        // 2. 重新建立乾淨且設好 loop 的 Animator Controller
        Debug.Log("[Fix] 強制重建 Controller 以確保 walk4 與 run2 具備循環 (Loop) 屬性...");
        UnityEditor.Animations.AnimatorController cleanController =
            (UnityEditor.Animations.AnimatorController)CreateCleanAnimatorController();

        // 3. 在場景中找到 MonsterMutant7_Run1
        GameObject monster = GameObject.Find("MonsterMutant7_Run1");
        if (monster == null)
        {
            Debug.LogError("[Fix] 場景中找不到 MonsterMutant7_Run1！請確認 Hierarchy 中有這個物件。");
            return;
        }

        Animator anim = monster.GetComponent<Animator>();
        if (anim == null) anim = monster.GetComponentInChildren<Animator>();
        if (anim == null)
        {
            Debug.LogError("[Fix] MonsterMutant7_Run1 上找不到 Animator 組件！");
            return;
        }

        // 4. 套用 Avatar
        if (correctAvatar != null)
        {
            anim.avatar = correctAvatar;
            Debug.Log($"[Fix] ✅ Avatar [{correctAvatar.name}] 套用完成");
        }
        else
        {
            Debug.LogWarning("[Fix] ⚠️ Base mesh 裡找不到 Avatar，Generic rig 可能不需要，繼續執行...");
        }

        // 5. 套用我們的乾淨 Controller（這是最關鍵的！）
        anim.runtimeAnimatorController = cleanController;
        Debug.Log($"[Fix] ✅ Controller [{cleanController.name}] 套用完成");

        // 6. 印出診斷資訊
        Debug.Log($"[Fix] 診斷 - GameObject: {monster.name}");
        Debug.Log($"[Fix] 診斷 - Animator Controller: {anim.runtimeAnimatorController?.name ?? "NULL"}");
        Debug.Log($"[Fix] 診斷 - Avatar: {anim.avatar?.name ?? "NULL (Generic rig OK)"}");

        // 7. 儲存場景
        UnityEditor.EditorUtility.SetDirty(anim);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Fix] ✅✅✅ 全部修復完成！請直接按 Play 測試動畫。");
    }

    [UnityEditor.InitializeOnLoadMethod]
    [UnityEditor.MenuItem("Tools/Rebuild MonsterMutant7 Animator Controller")]
    public static void RebuildCleanAnimatorController()
    {
        CreateCleanAnimatorController();
    }

    public static RuntimeAnimatorController CreateCleanAnimatorController()
    {
        string controllerPath = "Assets/MonsterMutant 7/MonsterMutant7 Animator Controller.controller";

        // 先刪掉舊的，再重建（確保乾淨）
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(controllerPath) != null)
            UnityEditor.AssetDatabase.DeleteAsset(controllerPath);

        UnityEditor.Animations.AnimatorController controller =
            UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        UnityEditor.Animations.AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // 載入原始 FBX clip（FBX clip 是唯讀，不能直接設 loop，必須複製）
        AnimationClip walk4Src   = GetClipFromAssetPath("Assets/MonsterMutant 7/Animations/MutantMonster2@walk4.fbx");
        AnimationClip run2Src    = GetClipFromAssetPath("Assets/MonsterMutant 7/Animations/MutantMonster2@run2.fbx");
        AnimationClip gethit3Src = GetClipFromAssetPath("Assets/MonsterMutant 7/Animations/MutantMonster2@gethit3.fbx");
        AnimationClip attack2Src = GetClipFromAssetPath("Assets/MonsterMutant 7/Animations/MutantMonster2@attack2.fbx");

        Debug.Log($"[Rebuild] walk4={walk4Src?.name ?? "NULL"}, run2={run2Src?.name ?? "NULL"}, hit={gethit3Src?.name ?? "NULL"}, atk={attack2Src?.name ?? "NULL"}");

        // ── 建立 loop 副本的 helper ──
        // FBX clip 唯讀：SetAnimationClipSettings 對它沒效果 → 播完凍住
        // 必須 Instantiate 複製後才能設 loopTime
        AnimationClip MakeLoopClip(AnimationClip src, string clipName)
        {
            if (src == null) return null;
            AnimationClip copy = Object.Instantiate(src);
            copy.name = clipName;
            copy.wrapMode = WrapMode.Loop;
            var s = UnityEditor.AnimationUtility.GetAnimationClipSettings(copy);
            s.loopTime = true;
            s.loopBlend = true;
            UnityEditor.AnimationUtility.SetAnimationClipSettings(copy, s);
            UnityEditor.AssetDatabase.AddObjectToAsset(copy, controller);
            return copy;
        }

        // 為每個 State 建立專屬的 loop 動畫副本（避免 idle1 與 walk4 共享同個 Clip 導致 CrossFade 卡住）
        AnimationClip idle1Loop = MakeLoopClip(walk4Src, "idle1_loop");
        AnimationClip walk4Loop = MakeLoopClip(walk4Src, "walk4_loop");
        AnimationClip run2Loop  = MakeLoopClip(run2Src,  "run2_loop");

        // ── idle1（Dormant 待機，用獨立的 idle1_loop） ──
        var sIdle = sm.AddState("idle1");
        if (idle1Loop != null) sIdle.motion = idle1Loop;

        // ── walk4（追逐時走路，用獨立的 walk4_loop） ──
        var sWalk = sm.AddState("walk4");
        if (walk4Loop != null) sWalk.motion = walk4Loop;

        // ── run2（追逐時奔跑，loop clip） ──
        var sRun = sm.AddState("run2");
        if (run2Loop != null) sRun.motion = run2Loop;

        // ── gethit3（播一次，不需要 loop） ──
        var sHit = sm.AddState("gethit3");
        if (gethit3Src != null) sHit.motion = gethit3Src;

        // ── attack2（播一次，不需要 loop） ──
        var sAttack = sm.AddState("attack2");
        if (attack2Src != null) sAttack.motion = attack2Src;

        sm.defaultState = sIdle;

        UnityEditor.EditorUtility.SetDirty(controller);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
        Debug.Log("[Rebuild] ✅ 完成！walk4/run2 皆用 loop 副本，不會再凍住。");
        return controller;
    }

    private static AnimationClip GetClipFromAssetPath(string path)
    {
        Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets == null) return null;
        foreach (Object obj in assets)
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        foreach (Object obj in assets)
            if (obj is AnimationClip clip)
                return clip;
        return null;
    }
#endif
}
