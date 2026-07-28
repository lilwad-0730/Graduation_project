using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class GlassShatterRuntimeGenerator : MonoBehaviour
{
    void Awake()
    {
        ApplyShatterEffectToMirrorWall();
    }

    void Start()
    {
        ApplyShatterEffectToMirrorWall();
    }

    void OnEnable()
    {
        ApplyShatterEffectToMirrorWall();
    }

    public void ApplyShatterEffectToMirrorWall()
    {
        // 尋找目標物件 "mirror wall_001"
        GameObject mirrorWall = GameObject.Find("mirror wall_001");
        if (mirrorWall == null)
        {
            Debug.LogWarning("[GlassShatterRuntimeGenerator] 未找到 'mirror wall_001' 物件");
            return;
        }

        // 清除舊的獨立測試物件 "Mirror Wall"
        GameObject oldWall = GameObject.Find("Mirror Wall");
        if (oldWall != null && oldWall != mirrorWall)
        {
            DestroyImmediate(oldWall);
        }

        // 確保具有碰撞體
        BoxCollider col = mirrorWall.GetComponent<BoxCollider>();
        if (col == null) col = mirrorWall.AddComponent<BoxCollider>();

        SpriteRenderer sr = mirrorWall.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            Bounds b = sr.bounds;
            col.center = mirrorWall.transform.InverseTransformPoint(b.center);
            col.size = mirrorWall.transform.InverseTransformVector(b.size);
        }

        // 掛載破壞組件 Destructible
        Destructible destructible = mirrorWall.GetComponent<Destructible>();
        if (destructible == null) destructible = mirrorWall.AddComponent<Destructible>();
        destructible.shatterOnCollision = true;
        destructible.disappearDelay = 2.5f;

        // 生成不規則尖銳玻璃碎片 (Sharp Irregular Glass Shards)
        List<Sprite> shardSprites = CreateShardSprites();

        // 在 mirror wall_001 下建立/更新 ParticleSystem 碎裂特效
        Transform vfxTransform = mirrorWall.transform.Find("MirrorGlass_ShatterVFX");
        GameObject particleGo;
        if (vfxTransform != null)
        {
            particleGo = vfxTransform.gameObject;
        }
        else
        {
            particleGo = new GameObject("MirrorGlass_ShatterVFX");
            particleGo.transform.SetParent(mirrorWall.transform, false);
            particleGo.transform.localPosition = Vector3.zero;
        }

        ParticleSystem ps = particleGo.GetComponent<ParticleSystem>();
        if (ps == null) ps = particleGo.AddComponent<ParticleSystem>();

        ConfigureParticleSystem(ps, shardSprites[0], mirrorWall);

        Debug.Log("[GlassShatterRuntimeGenerator] 已成功將 2D 鏡牆碎裂崩塌特效綁定至 'mirror wall_001'！");
    }

    private void ConfigureParticleSystem(ParticleSystem ps, Sprite shardSprite, GameObject targetWall)
    {
        var main = ps.main;
        main.duration = 3.0f;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 2.5f; // 重力驅動下墜
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 35;
        var burst = new ParticleSystem.Burst(0.0f, 50, 80);
        emission.SetBursts(new ParticleSystem.Burst[] { burst });

        SpriteRenderer sr = targetWall.GetComponent<SpriteRenderer>();
        Vector3 wallSize = sr != null ? sr.bounds.size : new Vector3(2f, 4f, 1f);

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = wallSize;

        var rotationOverLifetime = ps.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-240f * Mathf.Deg2Rad, 240f * Mathf.Deg2Rad);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(new Color(0.75f, 0.92f, 1f), 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colorOverLifetime.color = grad;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = (sr != null ? sr.sortingOrder : 10) + 5;
            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = shardSprite.texture;
            renderer.material = mat;
        }
    }

    private List<Sprite> CreateShardSprites()
    {
        List<Sprite> list = new List<Sprite>();
        int w = 256, h = 256;

        list.Add(CreateSingleShardSprite(w, h, new Vector2[] { new Vector2(30, 30), new Vector2(220, 70), new Vector2(110, 220) }));
        list.Add(CreateSingleShardSprite(w, h, new Vector2[] { new Vector2(20, 200), new Vector2(230, 230), new Vector2(190, 30), new Vector2(90, 80) }));
        list.Add(CreateSingleShardSprite(w, h, new Vector2[] { new Vector2(40, 130), new Vector2(130, 220), new Vector2(220, 140), new Vector2(150, 30) }));

        return list;
    }

    private Sprite CreateSingleShardSprite(int w, int h, Vector2[] poly)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color transparent = new Color(0, 0, 0, 0);
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;
        tex.SetPixels(pixels);

        Color baseGlass = new Color(0.55f, 0.85f, 0.98f, 0.85f);
        Color highlight = new Color(0.98f, 1.0f, 1.0f, 0.98f);
        Color reflection = new Color(1.0f, 1.0f, 1.0f, 0.9f);

        float minX = w, maxX = 0, minY = h, maxY = 0;
        foreach (var v in poly) {
            if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
        }

        for (int y = (int)minY; y <= (int)maxY; y++)
        {
            for (int x = (int)minX; x <= (int)maxX; x++)
            {
                Vector2 p = new Vector2(x, y);
                if (IsPointInPolygon(p, poly))
                {
                    Color c = baseGlass;
                    float diag = (x * 0.7f + y * 0.7f);
                    if (Mathf.Abs((diag % 50) - 25) < 5f)
                    {
                        c = Color.Lerp(c, reflection, 0.5f);
                    }
                    float dist = GetDistanceToEdge(p, poly);
                    if (dist < 4f)
                    {
                        c = Color.Lerp(c, highlight, 1f - dist / 4f);
                    }
                    tex.SetPixel(x, y, c);
                }
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    private bool IsPointInPolygon(Vector2 p, Vector2[] poly)
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

    private float GetDistanceToEdge(Vector2 p, Vector2[] poly)
    {
        float minDist = float.MaxValue;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[j], b = poly[i];
            Vector2 l2 = b - a;
            float lengthSq = l2.sqrMagnitude;
            float t = lengthSq == 0 ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, b - a) / lengthSq);
            Vector2 projection = a + t * (b - a);
            float d = Vector2.Distance(p, projection);
            if (d < minDist) minDist = d;
            j = i;
        }
        return minDist;
    }
}
