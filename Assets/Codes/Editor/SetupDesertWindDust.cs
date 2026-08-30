using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class SetupDesertWindDust
{
    static SetupDesertWindDust()
    {
        EditorApplication.delayCall += AutoSetupInOpenScene;
    }

    [MenuItem("Tools/Setup Desert Wind Dust VFX")]
    public static void ExecuteMenuItem()
    {
        string scenePath = "Assets/Scenes/desert.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        string log = ApplyWindDustToScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log(log);
    }

    private static void AutoSetupInOpenScene()
    {
        var activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.isLoaded && activeScene.name.ToLower().Contains("desert"))
        {
            ApplyWindDustToScene();
        }
    }

    public static string ApplyWindDustToScene()
    {
        GameObject windObj = GameObject.Find("WindParticles");
        if (windObj == null)
        {
            windObj = new GameObject("WindParticles");
            windObj.AddComponent<ParticleSystem>();
        }

        // 強制重置旋轉為 0，杜絕原本 (180, 90, -90) 的歪斜發射角度
        windObj.transform.localRotation = Quaternion.identity;
        windObj.transform.localScale = Vector3.one;

        // 徹底刪除巨大方塊 DustHaze 物件
        Transform oldHaze = windObj.transform.Find("DustHaze");
        if (oldHaze != null)
        {
            Object.DestroyImmediate(oldHaze.gameObject);
        }

        DesertWindDustFX fx = windObj.GetComponent<DesertWindDustFX>();
        if (fx == null)
        {
            fx = windObj.AddComponent<DesertWindDustFX>();
        }

        fx.followCamera = false;
        fx.InitializeComponents();
        fx.ApplyVFXSettings();

        // 連接或建立 WindGustSystem
        WindGustSystem windSystem = Object.FindFirstObjectByType<WindGustSystem>();
        if (windSystem == null)
        {
            GameObject windSysObj = new GameObject("WindGustSystem");
            windSystem = windSysObj.AddComponent<WindGustSystem>();
        }

        windSystem.windParticles = windObj.GetComponent<ParticleSystem>();
        
        AudioSource audioSource = windSystem.GetComponent<AudioSource>();
        if (audioSource == null) audioSource = windSystem.gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;

        AudioClip windClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/強風3.mp3");
        if (windClip == null) windClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/風聲2.mp3");
        if (windClip != null) audioSource.clip = windClip;
        windSystem.windAudioSource = audioSource;

        // 確保玩家身上掛載 PlayerPetrification 並配置音效
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
            if (pm != null) playerObj = pm.gameObject;
        }

        if (playerObj != null)
        {
            PlayerPetrification petrify = playerObj.GetComponent<PlayerPetrification>();
            if (petrify == null) petrify = playerObj.AddComponent<PlayerPetrification>();

            AudioClip pClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/石化.mp3");
            AudioClip unpClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/解除石化.mp3");
            if (pClip != null) petrify.petrifySFX = pClip;
            if (unpClip != null) petrify.unpetrifySFX = unpClip;

            EditorUtility.SetDirty(petrify);
            EditorUtility.SetDirty(playerObj);
        }

        // 配置綠洲 3D 水流環境音效
        GameObject oasisObj = GameObject.Find("Transition_To_Underwater");
        if (oasisObj != null)
        {
            OasisWaterAmbient waterAmbient = oasisObj.GetComponent<OasisWaterAmbient>();
            if (waterAmbient == null) waterAmbient = oasisObj.AddComponent<OasisWaterAmbient>();

            AudioClip waterClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/水聲2（小）.mp3");
            if (waterClip != null) waterAmbient.waterClip = waterClip;
            EditorUtility.SetDirty(oasisObj);
        }

        // 配置石柱崩塌與延續流沙音效
        Destructible[] destructibles = Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None);
        AudioClip shatterClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/石柱崩解.mp3");
        AudioClip sand2Clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/沙聲2.mp3");
        foreach (var d in destructibles)
        {
            if (d != null)
            {
                if (shatterClip != null && d.shatterSFX == null) d.shatterSFX = shatterClip;
                if (sand2Clip != null && d.followUpSandSFX == null) d.followUpSandSFX = sand2Clip;
                EditorUtility.SetDirty(d);
            }
        }

        EditorUtility.SetDirty(windSystem);
        EditorUtility.SetDirty(windObj);

        return $"【風暴與全方位荒漠音效配置成功】已為場景完整配置 WindGustSystem、PlayerPetrification、綠洲水聲與石柱崩塌流沙音效！";
    }
}