using UnityEditor;
using UnityEngine;

/// <summary>
/// ★1004 重生漫畫 D 的完整頁面圖（Resources/RespawnStory/D_freeze/page.png，建議 3840×2160）的匯入設定。
/// Unity 預設會把貼圖縮到 2048、把非 2 的次方補成 2 的次方，鏡頭推近時整頁會糊；這裡只針對這一張改成：
/// 原尺寸（上限 8192）、不補次方、不做 mipmap、邊緣 Clamp、高品質壓縮、不用 alpha。其他貼圖不受影響。
/// ★1016 命運之書的書頁（Resources/FateBook/&lt;頁&gt;/page.png、sketch.png）同樣處理。
/// </summary>
public class RespawnStoryImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        bool dPage = p.EndsWith("/Resources/RespawnStory/D_freeze/page.png");
        bool fateBook = p.Contains("/Resources/FateBook/") && (p.EndsWith("/page.png") || p.EndsWith("/sketch.png"));
        if (!dPage && !fateBook) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = false;
        ti.maxTextureSize = 8192;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.alphaSource = TextureImporterAlphaSource.None;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
