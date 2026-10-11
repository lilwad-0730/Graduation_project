using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 主角水下游動氣泡 (Player Swim Bubbles)。
/// 主角在水下時，每隔一段時間在主角位置冒出 2~3 顆大小不等的氣泡 (外觀沿用 BubbleCluster 的泡泡材質)。
/// 氣泡生成後留在世界空間、不跟著主角移動，一邊左右輕微飄動一邊上升，
/// 升到比「生成當下主角的 Y 座標」高出指定高度後消失。
/// 會自動掛到場景中的主角身上，不需要手動加到 Player。
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerSwimBubbles : MonoBehaviour
{
    [Header("生成 (Spawn)")]
    [Tooltip("每隔幾秒冒一次氣泡")]
    public float spawnInterval = 2f;
    [Tooltip("每次冒出的氣泡數量 (最小; 最大，皆含)")]
    public Vector2Int bubblesPerSpawn = new Vector2Int(2, 3);
    [Tooltip("氣泡大小 (最小; 最大，世界單位直徑)")]
    public Vector2 bubbleSize = new Vector2(0.12f, 0.4f);
    [Tooltip("以主角中心為準，生成位置的左右/上下隨機散佈範圍")]
    public Vector2 spawnSpread = new Vector2(0.35f, 0.3f);
    [Tooltip("生成位置的前後 (Z) 隨機範圍 (負值 = 朝鏡頭)")]
    public Vector2 depthRange = new Vector2(-0.4f, 0.2f);

    [Header("上升與飄動 (Rise & Sway)")]
    [Tooltip("氣泡升到比生成當下主角 Y 座標高出這麼多單位後消失")]
    public float riseHeight = 6f;
    [Tooltip("上升速度 (最小; 最大，單位/秒)")]
    public Vector2 riseSpeed = new Vector2(1.2f, 1.8f);
    [Tooltip("左右飄動幅度 (最小; 最大)")]
    public Vector2 swayAmplitude = new Vector2(0.12f, 0.3f);
    [Tooltip("左右飄動頻率 (最小; 最大，每秒來回次數)")]
    public Vector2 swayFrequency = new Vector2(0.5f, 1f);

    [Header("外觀 (Look)")]
    [Tooltip("泡泡材質。留空時自動沿用場景中 BubbleCluster 的泡泡材質")]
    public Material bubbleMaterial;
    [Tooltip("泡泡透明度")]
    [Range(0f, 1f)]
    public float bubbleAlpha = 0.3f;
    [Tooltip("剛生成時由小變大所花的時間 (秒)")]
    public float growTime = 0.25f;
    [Tooltip("接近消失高度時開始縮小淡出的比例 (0.2 = 最後 20% 高度)")]
    [Range(0.01f, 1f)]
    public float topShrinkMargin = 0.2f;

    private class Bubble
    {
        public Transform transform;
        public Material material;
        public Vector3 origin;
        public float endY;
        public float speed;
        public float size;
        public float swayAmp;
        public float swayFreq;
        public float swayPhase;
        public float age;
        public float height;
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private PlayerMovement _player;
    private Collider _col;
    private Mesh _bubbleMesh;
    private Transform _bubbleRoot;
    private readonly List<Bubble> _bubbles = new List<Bubble>();
    private float _spawnTimer;

    // ── 自動掛載 ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AutoAttach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AutoAttach();
    }

    static void AutoAttach()
    {
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        if (pm == null) return;
        if (pm.GetComponent<PlayerSwimBubbles>() != null) return;
        pm.gameObject.AddComponent<PlayerSwimBubbles>();
    }

    // ── 本體 ─────────────────────────────────────────────────
    private void Awake()
    {
        _player = GetComponent<PlayerMovement>();
        _col = GetComponent<Collider>();
    }

    private void OnDestroy()
    {
        if (_bubbleRoot != null) Destroy(_bubbleRoot.gameObject);
        foreach (Bubble b in _bubbles)
        {
            if (b.material != null) Destroy(b.material);
        }
        _bubbles.Clear();
    }

    private void Update()
    {
        if (_player != null && _player.isUnderwater)
        {
            _spawnTimer += Time.deltaTime;
            if (_spawnTimer >= spawnInterval)
            {
                _spawnTimer = 0f;
                SpawnBubbles();
            }
        }
        else
        {
            // 離開水面後重新計時，下次入水不會立刻冒泡
            _spawnTimer = 0f;
        }

        UpdateBubbles();
    }

    private void SpawnBubbles()
    {
        if (!EnsureResources()) return;

        Vector3 center = (_col != null) ? _col.bounds.center : transform.position;
        float endY = transform.position.y + riseHeight;
        int count = Random.Range(bubblesPerSpawn.x, bubblesPerSpawn.y + 1);

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject("SwimBubble");
            // 掛在世界空間的根物件下，不跟著主角移動
            go.transform.SetParent(_bubbleRoot, false);

            go.AddComponent<MeshFilter>().sharedMesh = _bubbleMesh;
            Material mat = new Material(bubbleMaterial);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            Bubble b = new Bubble
            {
                transform = go.transform,
                material = mat,
                origin = center + new Vector3(
                    Random.Range(-spawnSpread.x, spawnSpread.x),
                    Random.Range(-spawnSpread.y, spawnSpread.y),
                    Random.Range(depthRange.x, depthRange.y)),
                endY = endY,
                speed = Random.Range(riseSpeed.x, riseSpeed.y),
                size = Random.Range(bubbleSize.x, bubbleSize.y),
                swayAmp = Random.Range(swayAmplitude.x, swayAmplitude.y),
                swayFreq = Random.Range(swayFrequency.x, swayFrequency.y),
                swayPhase = Random.Range(0f, Mathf.PI * 2f)
            };

            go.transform.position = b.origin;
            go.transform.localScale = Vector3.zero;
            _bubbles.Add(b);
        }
    }

    private void UpdateBubbles()
    {
        float dt = Time.deltaTime;

        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            Bubble b = _bubbles[i];
            if (b.transform == null)
            {
                if (b.material != null) Destroy(b.material);
                _bubbles.RemoveAt(i);
                continue;
            }

            b.age += dt;
            b.height += b.speed * dt;

            float y = b.origin.y + b.height;
            if (y >= b.endY)
            {
                Destroy(b.transform.gameObject);
                Destroy(b.material);
                _bubbles.RemoveAt(i);
                continue;
            }

            // 左右飄動：以生成點為中心的正弦擺動 (從 0 起擺，避免生成瞬間位置跳動)
            float sway = (Mathf.Sin(b.age * b.swayFreq * Mathf.PI * 2f + b.swayPhase) - Mathf.Sin(b.swayPhase)) * b.swayAmp;
            b.transform.position = new Vector3(b.origin.x + sway, y, b.origin.z);

            // 剛生成時漸大，接近消失高度時縮小淡出
            float grow = (growTime > 0f) ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.age / growTime)) : 1f;
            float total = Mathf.Max(0.01f, b.endY - b.origin.y);
            float progress = Mathf.Clamp01(b.height / total);
            float shrink = 1f;
            if (progress > 1f - topShrinkMargin)
            {
                shrink = Mathf.SmoothStep(1f, 0f, (progress - (1f - topShrinkMargin)) / topShrinkMargin);
            }

            b.transform.localScale = Vector3.one * (b.size * grow * shrink);

            if (b.material.HasProperty(BaseColorId))
            {
                Color col = b.material.GetColor(BaseColorId);
                col.a = bubbleAlpha * grow * shrink;
                b.material.SetColor(BaseColorId, col);
            }
        }
    }

    private bool EnsureResources()
    {
        if (_bubbleRoot == null)
        {
            _bubbleRoot = new GameObject("PlayerSwimBubbles_Root").transform;
        }

        // 沿用場景中 BubbleCluster 的泡泡材質與 Mesh
        if (bubbleMaterial == null || _bubbleMesh == null)
        {
            BubbleClusterFx cluster = FindAnyObjectByType<BubbleClusterFx>();
            if (cluster != null)
            {
                if (bubbleMaterial == null) bubbleMaterial = cluster.bubbleMaterial;
                if (_bubbleMesh == null) _bubbleMesh = cluster.bubbleMesh;
            }
        }

#if UNITY_EDITOR
        if (bubbleMaterial == null)
        {
            bubbleMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/BubbleR/Example scene/Materials/Bubbles/Bubble_blue.mat");
        }
#endif

        if (_bubbleMesh == null)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _bubbleMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
        }

        return bubbleMaterial != null;
    }
}
