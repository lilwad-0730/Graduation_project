using UnityEngine;

/// <summary>
/// 自動為場景創建並確保 MonsterMutant7_Run1 存在於 Hierarchy 與場景中。
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
        if (existing != null) return;

#if UNITY_EDITOR
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
            RuntimeAnimatorController controller = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/MonsterMutant 7/MonsterMutant7 Animator Controller.controller");
            if (controller != null) animator.runtimeAnimatorController = controller;

            UnityEditor.Selection.activeGameObject = monsterGo;
            UnityEditor.EditorGUIUtility.PingObject(monsterGo);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("[MonsterMutantRun1AutoSpawner] 成功在 Hierarchy 中建立並選取 MonsterMutant7_Run1！");
        }
#endif
    }
}
