using UnityEngine;
using UnityEditor;
using System.IO;

public class GlassPlatformGenerator : EditorWindow
{
    [MenuItem("Tools/Generate Glass Platform Tile")]
    public static void GenerateGlassPlatformTile()
    {
        int width = 512;
        int height = 128;
        
        // 1. Create a transparent texture
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        
        // Fill with clear transparent pixels
        Color[] clearColors = new Color[width * height];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = Color.clear;
        }
        texture.SetPixels(clearColors);
        
        // 2. Draw the glass platform slab
        DrawGlassPlatform(texture, width, height);
        
        texture.Apply();
        
        // 3. Save as transparent PNG
        string relativePath = "Assets/dark glass/glass_platform_tile.png";
        SaveTextureToPNG(texture, relativePath);
        
        // 4. Configure Import Settings
        ConfigureSpriteImporter(relativePath);
        
        Debug.Log("Glass Platform Tile generated successfully at: " + relativePath);
    }
    
    private static void SaveTextureToPNG(Texture2D texture, string relativePath)
    {
        string fullPath = Application.dataPath + relativePath.Substring("Assets".Length);
        string dirPath = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
        
        byte[] bytes = texture.EncodeToPNG();
        File.WriteAllBytes(fullPath, bytes);
        DestroyImmediate(texture);
    }
    
    private static void ConfigureSpriteImporter(string relativePath)
    {
        AssetDatabase.ImportAsset(relativePath);
        TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            
            importer.SaveAndReimport();
        }
    }
    
    private static void BlendPixel(Texture2D tex, int x, int y, Color color)
    {
        int wrappedX = (x + tex.width) % tex.width;
        if (y < 0 || y >= tex.height) return;
        
        Color existing = tex.GetPixel(wrappedX, y);
        Color blended = Color.Lerp(existing, color, color.a);
        blended.a = Mathf.Max(existing.a, color.a);
        tex.SetPixel(wrappedX, y, blended);
    }
    
    private static void DrawGlassPlatform(Texture2D tex, int w, int h)
    {
        int topY = 108;
        int botY = 20;
        
        // 1. Fill the glass body with a highly visible gradient and frosted transparency
        for (int x = 0; x < w; x++)
        {
            for (int y = botY; y <= topY; y++)
            {
                float t = (float)(y - botY) / (topY - botY);
                // Vibrant ice blue to rich deep cyan-blue gradient with high opacity (60% to 80%)
                Color bodyColor = Color.Lerp(
                    new Color(0.10f, 0.28f, 0.48f, 0.80f), // Bottom (darker solid support)
                    new Color(0.20f, 0.52f, 0.76f, 0.55f), // Top (brighter translucent face)
                    t
                );
                tex.SetPixel(x, y, bodyColor);
                
                // Add a soft frosted glass white glare near the top walkable surface (glossy sheen)
                if (y >= topY - 12 && y < topY)
                {
                    float glareFactor = (float)(y - (topY - 12)) / 12f; // Fade up to top
                    float glareAlpha = glareFactor * glareFactor * 0.25f; // Soft curve, max 0.25 alpha
                    Color existing = tex.GetPixel(x, y);
                    Color glareColor = new Color(0.85f, 0.95f, 1.0f, glareAlpha);
                    Color blended = Color.Lerp(existing, glareColor, glareColor.a);
                    blended.a = Mathf.Max(existing.a, glareAlpha);
                    tex.SetPixel(x, y, blended);
                }
            }
        }
        
        // 2. Inner horizontal glow core (seamless) - represents refraction/thickness core
        for (int x = 0; x < w; x++)
        {
            float coreGlow = Mathf.Sin((float)x / w * Mathf.PI * 4) * 0.12f + 0.18f;
            BlendPixel(tex, x, 64, new Color(0.45f, 0.85f, 1.0f, coreGlow));
            BlendPixel(tex, x, 63, new Color(0.45f, 0.85f, 1.0f, coreGlow * 0.8f));
            BlendPixel(tex, x, 65, new Color(0.45f, 0.85f, 1.0f, coreGlow * 0.8f));
            
            BlendPixel(tex, x, 62, new Color(0.35f, 0.75f, 1.0f, coreGlow * 0.4f));
            BlendPixel(tex, x, 66, new Color(0.35f, 0.75f, 1.0f, coreGlow * 0.4f));
        }
        
        // 3. Bright diagonal specular reflections (wrapped for seamless tiling)
        // High visibility cyan-white glares to convey glossy transparency
        DrawDiagonalReflection(tex, w, h, 30, 35, 120, 95, new Color(0.70f, 0.92f, 1.0f, 0.42f));
        DrawDiagonalReflection(tex, w, h, 200, 30, 290, 90, new Color(0.70f, 0.92f, 1.0f, 0.38f));
        DrawDiagonalReflection(tex, w, h, 370, 45, 460, 100, new Color(0.70f, 0.92f, 1.0f, 0.40f));
        
        // 4. Stylized internal cracks (ice blue cracks inside the glass)
        DrawCrack(tex, w, h, 140, 72, 18, new Color(0.85f, 0.96f, 1.0f, 0.55f));
        DrawCrack(tex, w, h, 330, 52, 16, new Color(0.85f, 0.96f, 1.0f, 0.50f));
        DrawCrack(tex, w, h, 470, 78, 17, new Color(0.85f, 0.96f, 1.0f, 0.52f));
        
        // 5. Walkable top edge highlights (very bright and sharp for clean gameplay alignment)
        for (int x = 0; x < w; x++)
        {
            // Sharp brilliant top border (white-cyan)
            BlendPixel(tex, x, topY, new Color(0.96f, 0.99f, 1.0f, 1.0f));
            // Multi-tier glowing bevel
            BlendPixel(tex, x, topY - 1, new Color(0.75f, 0.93f, 1.0f, 0.90f));
            BlendPixel(tex, x, topY - 2, new Color(0.55f, 0.85f, 1.0f, 0.65f));
            BlendPixel(tex, x, topY - 3, new Color(0.40f, 0.78f, 1.0f, 0.35f));
            BlendPixel(tex, x, topY - 4, new Color(0.30f, 0.70f, 1.0f, 0.15f));
        }
        
        // 6. Bottom edge highlights (rich cyan bevel to anchor the base)
        for (int x = 0; x < w; x++)
        {
            // Sharp bottom border
            BlendPixel(tex, x, botY, new Color(0.55f, 0.85f, 1.0f, 0.85f));
            // Bevel glow
            BlendPixel(tex, x, botY + 1, new Color(0.35f, 0.68f, 0.90f, 0.55f));
            BlendPixel(tex, x, botY + 2, new Color(0.20f, 0.52f, 0.80f, 0.25f));
        }
    }
    
    private static void DrawDiagonalReflection(Texture2D tex, int w, int h, int startX, int startY, int endX, int endY, Color color)
    {
        int steps = Mathf.Max(Mathf.Abs(endX - startX), Mathf.Abs(endY - startY));
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(startX, endX, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(startY, endY, t));
            
            float edgeFade = Mathf.Sin(t * Mathf.PI);
            Color fadedColor = color;
            fadedColor.a *= edgeFade;
            
            BlendPixel(tex, x, y, fadedColor);
            BlendPixel(tex, x + 1, y, fadedColor * 0.4f);
            BlendPixel(tex, x - 1, y, fadedColor * 0.4f);
        }
    }
    
    private static void DrawCrack(Texture2D tex, int w, int h, int cx, int cy, int size, Color color)
    {
        int x = cx;
        int y = cy;
        Random.InitState(cx * cy);
        
        for (int i = 0; i < size; i++)
        {
            BlendPixel(tex, x, y, color);
            
            if (Random.value < 0.6f)
            {
                y += Random.value < 0.5f ? 1 : -1;
            }
            else
            {
                x += Random.value < 0.5f ? 1 : -1;
            }
        }
    }
}
