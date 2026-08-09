using UnityEngine;
using UnityEditor;

/// <summary>
/// 自動修正場景中所有 ParticleSystem 的 VelocityOverLifetime 模組中 X/Y/Z 軸曲線 Mode 混用導致的報錯
/// （'Particle Velocity curves must all be in the same mode'）
/// </summary>
[InitializeOnLoad]
public class FixParticleSystemVelocityCurves
{
    static FixParticleSystemVelocityCurves()
    {
        FixAllParticleSystemsInScene();
        FixAllParticleSystemPrefabs();
    }

    [MenuItem("Tools/Fix Particle System Velocity Curves")]
    public static void FixAllParticleSystemsInScene()
    {
        ParticleSystem[] systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int fixedCount = 0;

        foreach (var ps in systems)
        {
            if (FixParticleSystem(ps)) fixedCount++;
        }

        if (fixedCount > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[FixParticleSystemVelocityCurves] ✅ 已成功為 {fixedCount} 個場景 ParticleSystem 修正 Velocity 軸向 Mode 並存檔！");
        }
    }

    public static void FixAllParticleSystemPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        int fixedPrefabCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            bool prefabModified = false;

            foreach (var ps in systems)
            {
                if (FixParticleSystem(ps))
                {
                    prefabModified = true;
                }
            }

            if (prefabModified)
            {
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                fixedPrefabCount++;
            }
        }

        if (fixedPrefabCount > 0)
        {
            Debug.Log($"[FixParticleSystemVelocityCurves] ✅ 已成功為 {fixedPrefabCount} 個 Prefab 檔案（如 featherEmitter）修正 Velocity 軸向 Mode 並存檔！");
        }
    }

    private static bool FixParticleSystem(ParticleSystem ps)
    {
        if (ps == null) return false;
        var velocityModule = ps.velocityOverLifetime;
        if (!velocityModule.enabled) return false;

        ParticleSystemCurveMode modeX = velocityModule.x.mode;
        ParticleSystemCurveMode modeY = velocityModule.y.mode;
        ParticleSystemCurveMode modeZ = velocityModule.z.mode;

        bool modified = false;

        if (modeX != modeY || modeX != modeZ)
        {
            var yCurve = velocityModule.y;
            var zCurve = velocityModule.z;

            yCurve.mode = modeX;
            zCurve.mode = modeX;

            velocityModule.y = yCurve;
            velocityModule.z = zCurve;
            modified = true;
        }

        // 特別修復 TwoCurves (mode 2) 與 TwoConstants 之間的內部混用報錯
        if (modeX == ParticleSystemCurveMode.TwoCurves || modeY == ParticleSystemCurveMode.TwoCurves || modeZ == ParticleSystemCurveMode.TwoCurves)
        {
            var xCurve = velocityModule.x;
            var yCurve = velocityModule.y;
            var zCurve = velocityModule.z;

            if (xCurve.mode != ParticleSystemCurveMode.TwoConstants || yCurve.mode != ParticleSystemCurveMode.TwoConstants || zCurve.mode != ParticleSystemCurveMode.TwoConstants)
            {
                xCurve.mode = ParticleSystemCurveMode.TwoConstants;
                yCurve.mode = ParticleSystemCurveMode.TwoConstants;
                zCurve.mode = ParticleSystemCurveMode.TwoConstants;

                velocityModule.x = xCurve;
                velocityModule.y = yCurve;
                velocityModule.z = zCurve;
                modified = true;
            }
        }

        if (modified)
        {
            EditorUtility.SetDirty(ps);
        }

        return modified;
    }


}
