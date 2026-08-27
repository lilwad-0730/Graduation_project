using UnityEditor;
using UnityEngine;

/// <summary>
/// Resources/Pages/ 底下那 74 張頁面的 import 設定自動套好 —— 不用手選 Inspector。
/// 有新圖丟進去也會自動照這組設定進來。
/// </summary>
public class BookAssetSettings : AssetPostprocessor
{
    const string PagesPath = "Assets/Resources/Pages/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(PagesPath)) return;

        var ti = assetImporter as TextureImporter;
        if (ti == null) return;

        ti.textureType        = TextureImporterType.Default;
        ti.wrapMode           = TextureWrapMode.Clamp;   // 邊緣不吃到對面那一邊
        ti.maxTextureSize     = 2048;                    // 星點不糊
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.mipmapEnabled      = false;                   // 滿版 2D，用不到 mipmap
    }

    [MenuItem("我你他/重新套用 74 張圖的 Import 設定")]
    static void Reapply()
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Pages" });
        foreach (var g in guids)
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
        Debug.Log("[我你他] 重新 import 了 " + guids.Length + " 張頁面");
    }
}
