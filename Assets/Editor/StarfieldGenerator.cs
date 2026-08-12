using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class StarfieldGenerator : EditorWindow
{
    [MenuItem("Tools/Generate Far Starfield (Tiny Stars)")]
    public static void GenerateFarStarfield()
    {
        int width = 1024;
        int height = 576; // 16:9 ratio
        
        // 1. Create a transparent texture
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        
        // Fill with clear transparent pixels
        Color[] clearColors = new Color[width * height];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = Color.clear;
        }
        texture.SetPixels(clearColors);
        
        // 2. Generate random tiny stars
        int starCount = 500;
        Random.InitState(42); // Seed for reproducible clean layout
        
        for (int i = 0; i < starCount; i++)
        {
            int x = Random.Range(0, width);
            int y = Random.Range(0, height);
            
            // Random soft alpha (increased brightness)
            float alpha = Random.Range(0.3f, 0.8f);
            
            // White with very slight cool blue tint
            Color starColor = new Color(0.85f, 0.92f, 1.0f, alpha);
            
            // Star size: mostly 2px, some 3x3 soft (slightly scaled up)
            float sizeRoll = Random.value;
            if (sizeRoll < 0.7f)
            {
                // 2px soft star
                DrawPixelSeamless(texture, x, y, width, height, starColor);
                DrawPixelSeamless(texture, x + 1, y, width, height, starColor * 0.7f);
                DrawPixelSeamless(texture, x, y + 1, width, height, starColor * 0.7f);
            }
            else
            {
                // 3x3 soft circular star
                DrawPixelSeamless(texture, x, y, width, height, starColor);
                DrawPixelSeamless(texture, x + 1, y, width, height, starColor * 0.5f);
                DrawPixelSeamless(texture, x - 1, y, width, height, starColor * 0.5f);
                DrawPixelSeamless(texture, x, y + 1, width, height, starColor * 0.5f);
                DrawPixelSeamless(texture, x, y - 1, width, height, starColor * 0.5f);
            }
        }
        
        texture.Apply();
        
        // 3. Save as transparent PNG
        string relativePath = "Assets/FreeParallax/Images/starfield_far.png";
        SaveTextureToPNG(texture, relativePath);
        
        // 4. Configure Import Settings
        ConfigureSpriteImporter(relativePath);
        
        // 5. Add to scene 'dark glasses'
        SetupSceneLayer(relativePath, "Parallax_StarfieldFar", 20f, -100, 0.05f);
    }

    [MenuItem("Tools/Generate Mid Starfield (Medium Stars)")]
    public static void GenerateMidStarfield()
    {
        int width = 1024;
        int height = 576; // 16:9 ratio
        
        // 1. Create a transparent texture
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        
        // Fill with clear transparent pixels
        Color[] clearColors = new Color[width * height];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = Color.clear;
        }
        texture.SetPixels(clearColors);
        
        // 2. Generate random medium stars
        int starCount = 120; // Moderate number of stars
        Random.InitState(100); // Unique seed for different distribution
        
        for (int i = 0; i < starCount; i++)
        {
            int x = Random.Range(0, width);
            int y = Random.Range(0, height);
            
            // Random bright alpha (0.45 to 0.9)
            float alpha = Random.Range(0.45f, 0.9f);
            
            // Mostly white, some slightly warm, some cool blue-tinted
            float tintRoll = Random.value;
            Color starColor;
            if (tintRoll < 0.2f)
            {
                // Soft warm/yellow star
                starColor = new Color(1.0f, 0.95f, 0.85f, alpha);
            }
            else if (tintRoll < 0.4f)
            {
                // Cool blue-tinted star
                starColor = new Color(0.8f, 0.9f, 1.0f, alpha);
            }
            else
            {
                // Clean white star
                starColor = new Color(1.0f, 1.0f, 1.0f, alpha);
            }
            
            // Select star shape/size:
            // 60% simple 3x3 circular glow (type 0)
            // 30% soft 5x5 glowing star (type 1)
            // 10% sparkling cross-flare star (type 2)
            float shapeRoll = Random.value;
            int shapeType = 0;
            if (shapeRoll >= 0.6f && shapeRoll < 0.9f)
            {
                shapeType = 1;
            }
            else if (shapeRoll >= 0.9f)
            {
                shapeType = 2;
            }
            
            DrawMidStar(texture, x, y, width, height, starColor, shapeType);
        }
        
        texture.Apply();
        
        // 3. Save as transparent PNG
        string relativePath = "Assets/FreeParallax/Images/starfield_mid.png";
        SaveTextureToPNG(texture, relativePath);
        
        // 4. Configure Import Settings (PPU = 48f to enlarge mid stars close to near stars)
        ConfigureSpriteImporter(relativePath, 48f);
        
        // 5. Add to scene 'dark glasses'
        SetupSceneLayer(relativePath, "Parallax_StarfieldMid", 15f, -90, 0.15f);
    }

    [MenuItem("Tools/Generate Near Starfield (Large Particles)")]
    public static void GenerateNearStarfield()
    {
        int width = 1024;
        int height = 576; // 16:9 ratio
        
        // 1. Create a transparent texture
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        
        // Fill with clear transparent pixels
        Color[] clearColors = new Color[width * height];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = Color.clear;
        }
        texture.SetPixels(clearColors);
        
        // 2. Generate random large particles and star glints (150 stars & 100% max brightness)
        int starCount = 150;
        Random.InitState(2026);
        
        for (int i = 0; i < starCount; i++)
        {
            int x = Random.Range(0, width);
            int y = Random.Range(0, height);
            
            // 90% Solid Alpha Brightness
            float alpha = 0.9f;
            
            float colorRoll = Random.value;
            Color starColor;
            if (colorRoll < 0.35f)
            {
                // Vivid Ultra Electric Cyan/Teal glow
                starColor = new Color(0.8f, 1.0f, 1.0f, alpha);
            }
            else if (colorRoll < 0.7f)
            {
                // Radiant Warm Gold Diamond
                starColor = new Color(1.0f, 0.96f, 0.8f, alpha);
            }
            else
            {
                // Pure Brilliant White Flare
                starColor = new Color(1.0f, 1.0f, 1.0f, alpha);
            }
            
            float shapeRoll = Random.value;
            int shapeType = shapeRoll < 0.5f ? 0 : 1;
            
            DrawNearStar(texture, x, y, width, height, starColor, shapeType);
        }
        
        texture.Apply();
        
        // 3. Save as transparent PNG
        string relativePath = "Assets/FreeParallax/Images/starfield_near.png";
        SaveTextureToPNG(texture, relativePath);
        
        // 4. Configure Import Settings (PPU = 42 for half-sized dazzling stars)
        ConfigureSpriteImporter(relativePath, 42f);
        
        // 5. Add to scene 'dark glasses' with 0.5x speed ratio
        SetupSceneLayer(relativePath, "Parallax_StarfieldNear", 10f, -80, 0.5f);
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
        Debug.Log("Starfield texture saved successfully to: " + relativePath);
    }
    
    private static void ConfigureSpriteImporter(string relativePath, float pixelsPerUnit = 100f)
    {
        AssetDatabase.ImportAsset(relativePath);
        TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            
            // Disable mipmaps to prevent blur and trails when moving
            importer.mipmapEnabled = false;
            
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            
            importer.SaveAndReimport();
        }
    }
    
    private static void DrawPixelSeamless(Texture2D tex, int x, int y, int w, int h, Color color)
    {
        int wrappedX = (x + w) % w;
        int boundedY = Mathf.Clamp(y, 0, h - 1);
        
        Color existing = tex.GetPixel(wrappedX, boundedY);
        Color blended = Color.Lerp(existing, color, color.a);
        blended.a = Mathf.Max(existing.a, color.a);
        
        tex.SetPixel(wrappedX, boundedY, blended);
    }

    private static void DrawMidStar(Texture2D tex, int x, int y, int w, int h, Color color, int type)
    {
        if (type == 0)
        {
            // 5x5 soft circular glow (larger than previous 3x3)
            DrawPixelSeamless(tex, x, y, w, h, color);
            
            float inner = 0.6f;
            DrawPixelSeamless(tex, x + 1, y, w, h, color * inner);
            DrawPixelSeamless(tex, x - 1, y, w, h, color * inner);
            DrawPixelSeamless(tex, x, y + 1, w, h, color * inner);
            DrawPixelSeamless(tex, x, y - 1, w, h, color * inner);
            
            float diag = 0.3f;
            DrawPixelSeamless(tex, x + 1, y + 1, w, h, color * diag);
            DrawPixelSeamless(tex, x - 1, y + 1, w, h, color * diag);
            DrawPixelSeamless(tex, x + 1, y - 1, w, h, color * diag);
            DrawPixelSeamless(tex, x - 1, y - 1, w, h, color * diag);
            
            float outer = 0.15f;
            DrawPixelSeamless(tex, x + 2, y, w, h, color * outer);
            DrawPixelSeamless(tex, x - 2, y, w, h, color * outer);
            DrawPixelSeamless(tex, x, y + 2, w, h, color * outer);
            DrawPixelSeamless(tex, x, y - 2, w, h, color * outer);
        }
        else if (type == 1)
        {
            // 7x7 soft glowing star
            DrawPixelSeamless(tex, x, y, w, h, color);
            
            float inner = 0.7f;
            DrawPixelSeamless(tex, x + 1, y, w, h, color * inner);
            DrawPixelSeamless(tex, x - 1, y, w, h, color * inner);
            DrawPixelSeamless(tex, x, y + 1, w, h, color * inner);
            DrawPixelSeamless(tex, x, y - 1, w, h, color * inner);
            
            float diag1 = 0.4f;
            DrawPixelSeamless(tex, x + 1, y + 1, w, h, color * diag1);
            DrawPixelSeamless(tex, x - 1, y + 1, w, h, color * diag1);
            DrawPixelSeamless(tex, x + 1, y - 1, w, h, color * diag1);
            DrawPixelSeamless(tex, x - 1, y - 1, w, h, color * diag1);
            
            float outer1 = 0.3f;
            DrawPixelSeamless(tex, x + 2, y, w, h, color * outer1);
            DrawPixelSeamless(tex, x - 2, y, w, h, color * outer1);
            DrawPixelSeamless(tex, x, y + 2, w, h, color * outer1);
            DrawPixelSeamless(tex, x, y - 2, w, h, color * outer1);
            
            float outerDiag = 0.15f;
            DrawPixelSeamless(tex, x + 2, y + 2, w, h, color * outerDiag);
            DrawPixelSeamless(tex, x - 2, y + 2, w, h, color * outerDiag);
            DrawPixelSeamless(tex, x + 2, y - 2, w, h, color * outerDiag);
            DrawPixelSeamless(tex, x - 2, y - 2, w, h, color * outerDiag);
            
            float extreme = 0.1f;
            DrawPixelSeamless(tex, x + 3, y, w, h, color * extreme);
            DrawPixelSeamless(tex, x - 3, y, w, h, color * extreme);
            DrawPixelSeamless(tex, x, y + 3, w, h, color * extreme);
            DrawPixelSeamless(tex, x, y - 3, w, h, color * extreme);
        }
        else // type == 2
        {
            // 7x7 compact sparkling star with slight cross flare
            DrawPixelSeamless(tex, x, y, w, h, color);
            
            float core = 0.8f;
            DrawPixelSeamless(tex, x + 1, y, w, h, color * core);
            DrawPixelSeamless(tex, x - 1, y, w, h, color * core);
            DrawPixelSeamless(tex, x, y + 1, w, h, color * core);
            DrawPixelSeamless(tex, x, y - 1, w, h, color * core);
            
            float diag = 0.35f;
            DrawPixelSeamless(tex, x + 1, y + 1, w, h, color * diag);
            DrawPixelSeamless(tex, x - 1, y + 1, w, h, color * diag);
            DrawPixelSeamless(tex, x + 1, y - 1, w, h, color * diag);
            DrawPixelSeamless(tex, x - 1, y - 1, w, h, color * diag);
            
            float flare = 0.4f;
            DrawPixelSeamless(tex, x + 2, y, w, h, color * flare);
            DrawPixelSeamless(tex, x - 2, y, w, h, color * flare);
            DrawPixelSeamless(tex, x, y + 2, w, h, color * flare);
            DrawPixelSeamless(tex, x, y - 2, w, h, color * flare);
            
            float flareOuter = 0.15f;
            DrawPixelSeamless(tex, x + 3, y, w, h, color * flareOuter);
            DrawPixelSeamless(tex, x - 3, y, w, h, color * flareOuter);
            DrawPixelSeamless(tex, x, y + 3, w, h, color * flareOuter);
            DrawPixelSeamless(tex, x, y - 3, w, h, color * flareOuter);
        }
    }

    private static void DrawNearStar(Texture2D tex, int x, int y, int w, int h, Color color, int type)
    {
        if (type == 0)
        {
            // 9x9 compact glowing particle (half size of previous 17x17)
            int radius = 4;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= radius)
                    {
                        float factor = Mathf.Clamp01(1f - (dist / radius));
                        float alphaFactor = factor * factor;
                        
                        if (alphaFactor > 0.01f)
                        {
                            DrawPixelSeamless(tex, x + dx, y + dy, w, h, color * alphaFactor);
                        }
                    }
                }
            }
        }
        else // type == 1
        {
            // 7x7 compact sparkling star glint (half size of previous 13x13)
            DrawPixelSeamless(tex, x, y, w, h, color);
            
            float core = 0.9f;
            DrawPixelSeamless(tex, x + 1, y, w, h, color * core);
            DrawPixelSeamless(tex, x - 1, y, w, h, color * core);
            DrawPixelSeamless(tex, x, y + 1, w, h, color * core);
            DrawPixelSeamless(tex, x, y - 1, w, h, color * core);
            
            float diag = 0.55f;
            DrawPixelSeamless(tex, x + 1, y + 1, w, h, color * diag);
            DrawPixelSeamless(tex, x - 1, y + 1, w, h, color * diag);
            DrawPixelSeamless(tex, x + 1, y - 1, w, h, color * diag);
            DrawPixelSeamless(tex, x - 1, y - 1, w, h, color * diag);
            
            float flare1 = 0.6f;
            DrawPixelSeamless(tex, x + 2, y, w, h, color * flare1);
            DrawPixelSeamless(tex, x - 2, y, w, h, color * flare1);
            DrawPixelSeamless(tex, x, y + 2, w, h, color * flare1);
            DrawPixelSeamless(tex, x, y - 2, w, h, color * flare1);
            
            float flare2 = 0.3f;
            DrawPixelSeamless(tex, x + 3, y, w, h, color * flare2);
            DrawPixelSeamless(tex, x - 3, y, w, h, color * flare2);
            DrawPixelSeamless(tex, x, y + 3, w, h, color * flare2);
            DrawPixelSeamless(tex, x, y - 3, w, h, color * flare2);
        }
    }
    
    private static void SetupSceneLayer(string spritePath, string goName, float zDepth, int sortingOrder, float speedRatio)
    {
        // Find or create FreeParallaxManager
        FreeParallax parallaxManager = Object.FindFirstObjectByType<FreeParallax>();
        if (parallaxManager == null)
        {
            GameObject go = new GameObject("FreeParallaxManager");
            parallaxManager = go.AddComponent<FreeParallax>();
            parallaxManager.Speed = 2.0f;
            parallaxManager.IsHorizontal = true;
            parallaxManager.parallaxCamera = Camera.main;
        }
        
        // Find or create layer GameObject
        GameObject layerGo = GameObject.Find(goName);
        if (layerGo == null)
        {
            layerGo = new GameObject(goName);
        }
        
        // Match size and Y position of dark bluebg_0
        float targetWidth = 226.4f;
        float targetHeight = 98.48f;
        float posY = -9.4f;
        float posX = -92.2f;
        
        GameObject bg = GameObject.Find("dark bluebg_0");
        if (bg != null)
        {
            posX = bg.transform.position.x;
            posY = bg.transform.position.y;
            SpriteRenderer bgSr = bg.GetComponent<SpriteRenderer>();
            if (bgSr != null)
            {
                targetWidth = bgSr.bounds.size.x;
                targetHeight = bgSr.bounds.size.y;
            }
        }
        
        // Clean up any stray edit-mode clones
        string cloneName = goName + "(Clone)";
        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in allObjects)
        {
            if (go.name == cloneName)
            {
                Object.DestroyImmediate(go);
            }
        }
        
        // Setup SpriteRenderer
        SpriteRenderer sr = layerGo.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            sr = layerGo.AddComponent<SpriteRenderer>();
        }
        
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder;

        // Enable Tiled mode to repeat texture without stretching the stars
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.size = new Vector2(targetWidth, targetHeight);
        
        // Reset scale and position in Edit Mode (keep scale at (1,1,1) so tiling is 1:1 pixel size)
        layerGo.transform.localScale = Vector3.one;
        layerGo.transform.position = new Vector3(posX, posY, zDepth);
        
        // Setup FreeParallax Element
        if (parallaxManager.Elements == null)
        {
            parallaxManager.Elements = new List<FreeParallaxElement>();
        }
        
        // Check if the element is already registered
        bool alreadyAdded = false;
        FreeParallaxElement existingElement = null;
        foreach (var element in parallaxManager.Elements)
        {
            if (element.GameObjects != null && element.GameObjects.Contains(layerGo))
            {
                element.GameObjects.Clear();
                element.GameObjects.Add(layerGo);
                existingElement = element;
                alreadyAdded = true;
                break;
            }
        }
        
        if (!alreadyAdded)
        {
            FreeParallaxElement newElement = new FreeParallaxElement();
            newElement.GameObjects = new List<GameObject> { layerGo };
            newElement.SpeedRatio = speedRatio;
            
            newElement.RepositionLogic = new FreeParallaxElementRepositionLogic();
            newElement.RepositionLogic.PositionMode = FreeParallaxPositionMode.WrapAnchorNone;
            newElement.RepositionLogic.ScaleHeight = 0.0f; // 0 means do not override transform scale!
            newElement.RepositionLogic.SortingOrder = sortingOrder;
            
            // Insert logically: backgrounds first, ordering by sortingOrder ascending
            int insertIndex = 0;
            for (int i = 0; i < parallaxManager.Elements.Count; i++)
            {
                if (parallaxManager.Elements[i].RepositionLogic.SortingOrder > sortingOrder)
                {
                    insertIndex = i;
                    break;
                }
                insertIndex = i + 1;
            }
            parallaxManager.Elements.Insert(insertIndex, newElement);
        }
        else if (existingElement != null)
        {
            // Update properties if already registered
            existingElement.SpeedRatio = speedRatio;
            existingElement.RepositionLogic.SortingOrder = sortingOrder;
            existingElement.RepositionLogic.ScaleHeight = 0.0f; // Ensure it's 0.0f
        }
        
        // Mark scene dirty and save
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        
        Debug.Log($"Scene configured with parallax layer '{goName}' at depth {zDepth} with SortingOrder {sortingOrder} matching background size!");
    }
}
