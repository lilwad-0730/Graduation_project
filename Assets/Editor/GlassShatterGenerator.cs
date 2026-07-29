using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class GlassShatterGenerator
{
    [MenuItem("Tools/Generate Mirror Wall Shatter Effect")]
    public static void GenerateMirrorWallAndShatterEffect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[GlassShatterGenerator] 遊戲正在運行中 (Play Mode)，無法在 Play Mode 執行 Editor 場景生成工具。");
            return;
        }
        // 1. 生成 procédural 鏡面玻璃碎片與鏡牆貼圖
        Texture2D shardsTexture = GenerateGlassShardsTexture();
        Texture2D wallTexture = GenerateMirrorWallTexture();

        string folderPath = "Assets/dark glass";
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string shardsPath = folderPath + "/mirror_glass_shards.png";
        string wallPath = folderPath + "/mirror_wall_tile.png";

        File.WriteAllBytes(shardsPath, shardsTexture.EncodeToPNG());
        File.WriteAllBytes(wallPath, wallTexture.EncodeToPNG());
        AssetDatabase.Refresh();

        ConfigureSpriteImporter(shardsPath, true);
        ConfigureSpriteImporter(wallPath, false);

        Sprite shardsSprite = AssetDatabase.LoadAssetAtPath<Sprite>(shardsPath);
        Sprite wallSprite = AssetDatabase.LoadAssetAtPath<Sprite>(wallPath);

        // 2. 確保 dark glasses 場景已開啟
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!activeScene.name.Equals("dark glasses", System.StringComparison.OrdinalIgnoreCase))
        {
            var scenes = AssetDatabase.FindAssets("t:Scene");
            foreach (var guid in scenes)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).Equals("dark glasses", System.StringComparison.OrdinalIgnoreCase))
                {
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                    break;
                }
            }
        }

        // 3. 在場景中建立 "Mirror Wall" 物件
        GameObject mirrorWall = GameObject.Find("Mirror Wall");
        if (mirrorWall == null)
        {
            mirrorWall = new GameObject("Mirror Wall");
            Undo.RegisterCreatedObjectUndo(mirrorWall, "Create Mirror Wall");
        }

        mirrorWall.transform.position = new Vector3(0f, 2.5f, 0f);
        mirrorWall.transform.localScale = new Vector3(1.57f, 1.57f, 1.57f);

        SpriteRenderer sr = mirrorWall.GetComponent<SpriteRenderer>();
        if (sr == null) sr = mirrorWall.AddComponent<SpriteRenderer>();
        sr.sprite = wallSprite;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(3.2f, 6.4f);
        sr.sortingOrder = -10;

        BoxCollider col = mirrorWall.GetComponent<BoxCollider>();
        if (col == null) col = mirrorWall.AddComponent<BoxCollider>();
        col.center = Vector3.zero;
        col.size = new Vector3(3.2f, 6.4f, 2f);

        // 4. 掛載或更新 Destructible 組件
        Destructible destructible = mirrorWall.GetComponent<Destructible>();
        if (destructible == null) destructible = mirrorWall.AddComponent<Destructible>();
        destructible.shatterOnCollision = true;
        destructible.disappearDelay = 2.5f;

        // 5. 建立 ParticleSystem 2D Shatter VFX 預製特效
        GameObject particleGo = GameObject.Find("MirrorWall_ShatterVFX");
        if (particleGo == null)
        {
            particleGo = new GameObject("MirrorWall_ShatterVFX");
            Undo.RegisterCreatedObjectUndo(particleGo, "Create MirrorWall_ShatterVFX");
        }
        particleGo.transform.SetParent(mirrorWall.transform, false);
        particleGo.transform.localPosition = Vector3.zero;

        ParticleSystem ps = particleGo.GetComponent<ParticleSystem>();
        if (ps == null) ps = particleGo.AddComponent<ParticleSystem>();

        ConfigureParticleSystem(ps, shardsSprite);

        EditorUtility.SetDirty(mirrorWall);
        EditorUtility.SetDirty(particleGo);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        Debug.Log("[GlassShatterGenerator] 成功生成鏡牆與 2D 破裂碎片特效 (Mirror Wall & Shatter VFX)！");
    }

    private static void ConfigureSpriteImporter(string assetPath, bool isMultiple)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = isMultiple ? SpriteImportMode.Single : SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureParticleSystem(ParticleSystem ps, Sprite shardSprite)
    {
        var main = ps.main;
        main.duration = 2.0f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 2.0f; // 自然下墜力重力加速度
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0;
        var burst = new ParticleSystem.Burst(0.0f, 60, 90);
        emission.SetBursts(new ParticleSystem.Burst[] { burst });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(3.0f, 6.0f, 0.5f);

        var velocity = ps.inheritVelocity;

        var rotationOverLifetime = ps.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-180f * Mathf.Deg2Rad, 180f * Mathf.Deg2Rad);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(new Color(0.8f, 0.95f, 1f), 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colorOverLifetime.color = grad;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            // 建立標準 2D Quad 網格給粒子使用
            Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (quadMesh == null)
            {
                quadMesh = CreateQuadMesh();
            }
            renderer.mesh = quadMesh;

            Material mat = new Material(Shader.Find("Sprites/Default"));
            if (shardSprite != null) mat.mainTexture = shardSprite.texture;
            renderer.material = mat;
        }
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[] {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0),
            new Vector3(0.5f, 0.5f, 0)
        };
        mesh.uv = new Vector2[] {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Texture2D GenerateGlassShardsTexture()
    {
        int width = 512;
        int height = 512;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        Color transparent = new Color(0, 0, 0, 0);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;
        tex.SetPixels(pixels);

        // 繪製數個形狀各異、邊緣銳利的不規則鏡面碎片
        // 碎片 1: 大形不規則多邊形 (左上)
        DrawShardPolygon(tex, new Vector2[] {
            new Vector2(60, 420), new Vector2(220, 480), new Vector2(240, 360),
            new Vector2(180, 280), new Vector2(80, 310)
        });

        // 碎片 2: 細長尖銳三角玻璃 (右上)
        DrawShardPolygon(tex, new Vector2[] {
            new Vector2(290, 490), new Vector2(460, 440), new Vector2(340, 290)
        });

        // 碎片 3: 菱形不規則中型碎片 (左下)
        DrawShardPolygon(tex, new Vector2[] {
            new Vector2(50, 240), new Vector2(170, 250), new Vector2(220, 110),
            new Vector2(120, 50), new Vector2(40, 130)
        });

        // 碎片 4: 尖銳斜長碎片 (右下)
        DrawShardPolygon(tex, new Vector2[] {
            new Vector2(260, 260), new Vector2(470, 220), new Vector2(490, 70),
            new Vector2(350, 100), new Vector2(280, 150)
        });

        // 碎片 5: 中央小型針狀與碎石
        DrawShardPolygon(tex, new Vector2[] { new Vector2(210, 220), new Vector2(260, 240), new Vector2(240, 190) });
        DrawShardPolygon(tex, new Vector2[] { new Vector2(150, 290), new Vector2(190, 320), new Vector2(180, 270) });

        tex.Apply();
        return tex;
    }

    private static void DrawShardPolygon(Texture2D tex, Vector2[] vertices)
    {
        int w = tex.width;
        int h = tex.height;

        // 計算邊界
        float minX = w, maxX = 0, minY = h, maxY = 0;
        foreach (var v in vertices)
        {
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.y > maxY) maxY = v.y;
        }

        Color baseGlassColor = new Color(0.6f, 0.85f, 0.98f, 0.65f); // 冰藍透光玻璃主體
        Color highlightEdge = new Color(0.95f, 1.0f, 1.0f, 0.95f);    // 亮白銳利邊框高光
        Color reflectionLine = new Color(1.0f, 1.0f, 1.0f, 0.85f);    // 鏡面斜向反光線

        for (int y = (int)minY; y <= (int)maxY; y++)
        {
            for (int x = (int)minX; x <= (int)maxX; x++)
            {
                Vector2 p = new Vector2(x, y);
                if (IsPointInPolygon(p, vertices))
                {
                    // 點到最近邊緣距離 (計算高光 bevel)
                    float distToEdge = GetDistanceToPolygonEdge(p, vertices);

                    Color pixelColor = baseGlassColor;

                    // 內部斜向鏡面反光條紋
                    float diag = (x * 0.7f + y * 0.7f);
                    if (Mathf.Abs((diag % 80) - 40) < 5f)
                    {
                        pixelColor = Color.Lerp(pixelColor, reflectionLine, 0.45f);
                    }

                    // 銳利外邊框高光 (Dist < 4px)
                    if (distToEdge < 4f)
                    {
                        float t = 1f - (distToEdge / 4f);
                        pixelColor = Color.Lerp(pixelColor, highlightEdge, t);
                    }

                    tex.SetPixel(x, y, pixelColor);
                }
            }
        }
    }

    private static bool IsPointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
            {
                inside = !inside;
            }
            j = i;
        }
        return inside;
    }

    private static float GetDistanceToPolygonEdge(Vector2 p, Vector2[] poly)
    {
        float minDist = float.MaxValue;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            float d = DistanceToLineSegment(p, poly[j], poly[i]);
            if (d < minDist) minDist = d;
            j = i;
        }
        return minDist;
    }

    private static float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 l2 = b - a;
        float lengthSq = l2.sqrMagnitude;
        if (lengthSq == 0) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / lengthSq);
        Vector2 projection = a + t * (b - a);
        return Vector2.Distance(p, projection);
    }

    private static Texture2D GenerateMirrorWallTexture()
    {
        int width = 256;
        int height = 512;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        Color glassBase = new Color(0.4f, 0.7f, 0.9f, 0.7f);
        Color frameBorder = new Color(0.2f, 0.35f, 0.5f, 0.9f);
        Color specularLine = new Color(1.0f, 1.0f, 1.0f, 0.8f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color c = glassBase;

                // 邊框
                if (x < 8 || x > width - 9 || y < 8 || y > height - 9)
                {
                    c = frameBorder;
                }
                else
                {
                    // 頂部高光
                    if (y > height - 20)
                    {
                        float t = (y - (height - 20)) / 12f;
                        c = Color.Lerp(c, Color.white, t * 0.7f);
                    }

                    // 斜向大範圍鏡面光亮
                    float diag = (x + y * 0.5f);
                    if (Mathf.Abs((diag % 150) - 75) < 12f)
                    {
                        c = Color.Lerp(c, specularLine, 0.4f);
                    }
                }

                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }
}
