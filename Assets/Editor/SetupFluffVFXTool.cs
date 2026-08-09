using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// 為 SampleScene 配置 Stylized Environment VFX 資產包中的 Fluff 特效。
/// 包含實例化 Fluff.prefab 與建立專屬 Fluff.tif 高效能蒲公英棉絮粒子系統。
/// </summary>
// Force run SetupFluffVFX
[InitializeOnLoad]
public class SetupFluffVFXTool
{
    private static bool executed = false;

    static SetupFluffVFXTool()
    {
        EditorApplication.update += RunOnce;
    }

    private static void RunOnce()
    {
        if (executed) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
        executed = true;
        EditorApplication.update -= RunOnce;
        SetupFluffVFX();
    }

    [MenuItem("Tools/Setup Fluff VFX in SampleScene Now")]
    public static void SetupFluffVFX()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);


        string prefabPath = "Assets/TheLazzyKnight/Stylized Environment VFX/Prefabs/Fluff.prefab";
        string texPath = "Assets/TheLazzyKnight/Stylized Environment VFX/Textures/Fluff.tif";
        string matPath = "Assets/CloudSea/Materials/Mat_FluffParticle.mat";

        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Texture2D fluffTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        // 1. 確保 Fluff.tif 為 Texture/Sprite
        if (fluffTex != null)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null && !importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }

        // 2. 創建通用高質感 Fluff 材質
        Material fluffMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (fluffMat == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Mobile/Particles/Additive");
            fluffMat = new Material(shader);
            AssetDatabase.CreateAsset(fluffMat, matPath);
        }

        if (fluffTex != null)
        {
            fluffMat.mainTexture = fluffTex;
            fluffMat.SetTexture("_BaseMap", fluffTex);
            fluffMat.SetTexture("_MainTex", fluffTex);
            EditorUtility.SetDirty(fluffMat);
        }

        // 3. 實例化官方 Fluff.prefab (VisualEffect Graph)
        GameObject existingFluffVfx = GameObject.Find("Fluff_StylizedVFX");
        if (existingFluffVfx != null) Undo.DestroyObjectImmediate(existingFluffVfx);

        if (prefabAsset != null)
        {
            GameObject vfxInstance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (vfxInstance == null) vfxInstance = Object.Instantiate(prefabAsset);
            vfxInstance.name = "Fluff_StylizedVFX";
            vfxInstance.transform.position = new Vector3(0f, -1.5f, 0f);
            vfxInstance.transform.localScale = new Vector3(3f, 3f, 3f);
            Undo.RegisterCreatedObjectUndo(vfxInstance, "Instantiate Fluff Prefab");
        }

        // 4. 建立通用全相容 Fluff 蒲公英棉絮粒子系統 (Fluff2D_ParticleEmitter)
        GameObject existingEmitter = GameObject.Find("Fluff_ParticleEmitter");
        if (existingEmitter != null) Undo.DestroyObjectImmediate(existingEmitter);

        GameObject emitterGo = new GameObject("Fluff_ParticleEmitter");
        emitterGo.transform.position = new Vector3(0f, -1.0f, -0.5f);
        Undo.RegisterCreatedObjectUndo(emitterGo, "Create Fluff Particle Emitter");

        ParticleSystem ps = emitterGo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6.0f, 12.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = new Color(1.0f, 0.99f, 0.95f, 0.85f);
        main.gravityModifier = -0.015f; // 微幅向上漂浮

        var emission = ps.emission;
        emission.rateOverTime = 18f; // 適中飄散數量

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(150f, 4f, 1f); // 覆蓋整個雲海關卡寬度

        var velocityOverLife = ps.velocityOverLifetime;
        velocityOverLife.enabled = true;
        velocityOverLife.x = new ParticleSystem.MinMaxCurve(0.3f, 0.9f); // 微風向右飄動
        velocityOverLife.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.3f);
        velocityOverLife.z = new ParticleSystem.MinMaxCurve(0f, 0f);


        var rotOverLife = ps.rotationOverLifetime;
        rotOverLife.enabled = true;
        rotOverLife.z = new ParticleSystem.MinMaxCurve(-45f * Mathf.Deg2Rad, 45f * Mathf.Deg2Rad);

        var colOverLife = ps.colorOverLifetime;
        colOverLife.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.98f, 0.92f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.2f), new GradientAlphaKey(0.85f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        colOverLife.color = grad;

        var psRenderer = emitterGo.GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.material = fluffMat;
            psRenderer.sortingLayerName = "Default";
            psRenderer.sortingOrder = 20; // 繪製於雲海前方
        }

        EditorUtility.SetDirty(emitterGo);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[SetupFluffVFXTool] 成功為 SampleScene.unity 配置 Stylized Environment VFX 檔名 Fluff 特效與粒子發射器並存檔！");
    }
}
