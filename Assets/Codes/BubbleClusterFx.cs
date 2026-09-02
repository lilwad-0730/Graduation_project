using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class BubbleClusterFx : MonoBehaviour
{
    [Header("編輯與控制")]
    [Tooltip("勾選即可暫停泡泡動畫 (方便編輯其他物件)")]
    public bool pauseAnimation = false;

    [Header("泡泡集群設定")]
    [Tooltip("生成的泡泡數量")]
    public int bubbleCount = 14;

    [Tooltip("無縫循環週期的秒數")]
    public float cycleDuration = 8.0f;

    [Tooltip("泡泡生成的水平寬度範圍")]
    public float areaWidth = 6.0f;

    [Tooltip("泡泡上升的高度範圍")]
    public float floatHeight = 10.0f;

    [Tooltip("頂部縮小消失的比例範圍 (0.25 表示最後 25% 高度開始縮小)")]
    public float topShrinkMargin = 0.25f;

    [Tooltip("底部出現膨脹的比例範圍 (0.15 表示前 15% 高度開始出現)")]
    public float bottomGrowMargin = 0.15f;

    [Tooltip("泡泡材質球 (使用專案中相容 URP 的 Bubble_blue)")]
    public Material bubbleMaterial;

    [Tooltip("泡泡 Mesh (預設使用 Sphere)")]
    public Mesh bubbleMesh;

    [Header("🎵 泡泡音效 (Bubble SFX)")]
    [Tooltip("靠近泡泡群時播放的循環環境音效 (例如 水下_氣泡_loop.wav)")]
    public AudioClip bubbleLoopSFX;

    [Tooltip("玩家靠近泡泡群的感應距離 (公尺)")]
    public float hearDistance = 8.0f;

    [Tooltip("穿過/碰撞泡泡時播放的單發氣泡音效 (例如 水下_氣泡單發_02.wav)")]
    public AudioClip bubbleBurstSFX;

    [Tooltip("單發氣泡音效冷卻時間 (秒，避免連續觸發重疊炸音)")]
    public float burstCooldown = 0.4f;

    [Range(0f, 1f)] public float sfxVolume = 0.85f;

    private List<BubbleInstanceData> _bubbles = new List<BubbleInstanceData>();
    private AudioSource loopAudioSource;
    private AudioSource burstAudioSource;
    private Transform playerTransform;
    private float lastBurstTime = -999f;
    private BoxCollider triggerCollider;

    private class BubbleInstanceData
    {
        public GameObject gameObject;
        public Transform transform;
        public Material materialInstance;

        public Vector3 basePosition;
        public float baseScale;
        public float baseAlpha;

        public int verticalSpeedCycles; // 週期內完成幾次完整上升
        public int swayFrequency;       // 左右擺動的整數頻率 (確保首尾無縫)
        public float swayAmplitude;     // 擺動幅度
        public float phaseOffset;       // 初始相位 (0~1)
        public int pulseFrequency;      // 輕微變形/脈動頻率
    }

    void OnEnable()
    {
        GenerateCluster();
    }

    void OnValidate()
    {
        // 避免在 OnValidate 中直接引發 DestroyImmediate 導致 Unity 崩潰與 Console 狂刷錯誤
    }

    [ContextMenu("切換暫停/播放動畫")]
    public void TogglePause()
    {
        pauseAnimation = !pauseAnimation;
    }

    [ContextMenu("重新生成泡泡集群")]
    public void GenerateCluster()
    {
        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in transform)
        {
            children.Add(child.gameObject);
        }
        foreach (var child in children)
        {
            if (child == null) continue;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (child != null) DestroyImmediate(child);
                };
                #else
                DestroyImmediate(child);
                #endif
            }
        }
        _bubbles.Clear();

        if (bubbleMesh == null)
        {
            GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bubbleMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying)
            {
                Destroy(tempCube);
            }
            else
            {
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (tempCube != null) DestroyImmediate(tempCube);
                };
                #else
                DestroyImmediate(tempCube);
                #endif
            }
        }

        if (bubbleMaterial == null)
        {
            #if UNITY_EDITOR
            bubbleMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/BubbleR/Example scene/Materials/Bubbles/Bubble_blue.mat");
            #endif
        }

        Random.InitState(42);

        for (int i = 0; i < bubbleCount; i++)
        {
            GameObject bubbleGO = new GameObject($"SubBubble_{i}");
            bubbleGO.transform.SetParent(transform);

            MeshFilter mf = bubbleGO.AddComponent<MeshFilter>();
            mf.sharedMesh = bubbleMesh;

            MeshRenderer mr = bubbleGO.AddComponent<MeshRenderer>();
            Material matInst = new Material(bubbleMaterial);
            mr.sharedMaterial = matInst;

            int depthLayer = i % 3; 
            float scale, alpha, zOffset, swayAmp;
            int vCycles, swayFreq;

            if (depthLayer == 0) // 前景 (較大、稍快、清晰)
            {
                scale = Random.Range(0.7f, 1.1f);
                alpha = 0.35f;
                zOffset = -0.4f;
                vCycles = Random.Range(2, 3);
                swayFreq = Random.Range(1, 2);
                swayAmp = Random.Range(0.25f, 0.45f);
            }
            else if (depthLayer == 1) // 中景 (標準大小)
            {
                scale = Random.Range(0.4f, 0.65f);
                alpha = 0.25f;
                zOffset = 0.0f;
                vCycles = Random.Range(1, 2);
                swayFreq = Random.Range(2, 3);
                swayAmp = Random.Range(0.15f, 0.3f);
            }
            else // 背景 (小、淡、移動慢)
            {
                scale = Random.Range(0.18f, 0.35f);
                alpha = 0.15f;
                zOffset = 0.5f;
                vCycles = 1;
                swayFreq = Random.Range(1, 3);
                swayAmp = Random.Range(0.08f, 0.2f);
            }

            float startX = Random.Range(-areaWidth * 0.5f, areaWidth * 0.5f);
            float startY = Random.Range(-floatHeight * 0.5f, floatHeight * 0.5f);

            bubbleGO.transform.localPosition = new Vector3(startX, startY, zOffset);
            bubbleGO.transform.localScale = Vector3.one * scale;

            BubbleInstanceData data = new BubbleInstanceData
            {
                gameObject = bubbleGO,
                transform = bubbleGO.transform,
                materialInstance = matInst,
                basePosition = new Vector3(startX, -floatHeight * 0.5f, zOffset),
                baseScale = scale,
                baseAlpha = alpha,
                verticalSpeedCycles = vCycles,
                swayFrequency = swayFreq,
                swayAmplitude = swayAmp,
                phaseOffset = Random.Range(0f, 1f),
                pulseFrequency = Random.Range(1, 3)
            };

            _bubbles.Add(data);
        }
    }

    void Update()
    {
        if (pauseAnimation) return;

        if (_bubbles == null || _bubbles.Count == 0) return;

#if UNITY_EDITOR
        float currentTime = Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
        float currentTime = Time.time;
#endif
        float normalizedTime = (currentTime % cycleDuration) / cycleDuration; // 0 ~ 1

        float halfH = floatHeight * 0.5f;

        foreach (var b in _bubbles)
        {
            if (b.transform == null) continue;

            // 1. 垂直高度無縫計算 (0 ~ 1)
            float yProgress = (normalizedTime * b.verticalSpeedCycles + b.phaseOffset) % 1.0f;
            float currentY = Mathf.Lerp(-halfH, halfH, yProgress);

            // 2. 水平擺動無縫計算 (正弦波整數倍數)
            float swayAngle = (normalizedTime + b.phaseOffset) * Mathf.PI * 2.0f * b.swayFrequency;
            float currentX = b.basePosition.x + Mathf.Sin(swayAngle) * b.swayAmplitude;

            // 3. 輕微尺寸脈動
            float pulseAngle = (normalizedTime + b.phaseOffset) * Mathf.PI * 2.0f * b.pulseFrequency;
            float pulseFactor = 1.0f + Mathf.Sin(pulseAngle) * 0.06f;

            // 4. 計算頂部縮小消失 (Top Shrink) 與底部生成出現 (Bottom Grow) 比例
            float scaleMultiplier = 1.0f;
            float fadeAlpha = 1.0f;

            if (yProgress < bottomGrowMargin) // 底部 0 ~ bottomGrowMargin 漸大出現
            {
                float growProgress = Mathf.Clamp01(yProgress / bottomGrowMargin);
                scaleMultiplier = Mathf.SmoothStep(0.0f, 1.0f, growProgress);
                fadeAlpha = growProgress;
            }
            else if (yProgress > (1.0f - topShrinkMargin)) // 頂部 (1 - topShrinkMargin) ~ 1 漸小並消失
            {
                float shrinkProgress = Mathf.Clamp01((yProgress - (1.0f - topShrinkMargin)) / topShrinkMargin);
                scaleMultiplier = Mathf.SmoothStep(1.0f, 0.0f, shrinkProgress);
                fadeAlpha = 1.0f - shrinkProgress;
            }

            Vector3 currentScale = Vector3.one * (b.baseScale * pulseFactor * scaleMultiplier);

            b.transform.localPosition = new Vector3(currentX, currentY, b.basePosition.z);
            b.transform.localScale = currentScale;

            if (b.materialInstance != null && b.materialInstance.HasProperty("_BaseColor"))
            {
                Color col = b.materialInstance.GetColor("_BaseColor");
                col.a = b.baseAlpha * fadeAlpha;
                b.materialInstance.SetColor("_BaseColor", col);
            }
        }

        UpdateBubbleAudio();
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            SetupAudioTriggerCollider();
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }
    }

    private void SetupAudioTriggerCollider()
    {
        triggerCollider = GetComponent<BoxCollider>();
        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<BoxCollider>();
        }
        triggerCollider.isTrigger = true;
        triggerCollider.size = new Vector3(areaWidth * 0.9f, floatHeight * 0.85f, 4.0f);
        triggerCollider.center = Vector3.zero;
    }

    private void UpdateBubbleAudio()
    {
        if (!Application.isPlaying) return;

        if (bubbleLoopSFX != null)
        {
            if (playerTransform == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerTransform = p.transform;
            }

            if (playerTransform != null)
            {
                float dist = Vector3.Distance(transform.position, playerTransform.position);
                if (dist <= hearDistance)
                {
                    if (loopAudioSource == null)
                    {
                        loopAudioSource = gameObject.AddComponent<AudioSource>();
                        loopAudioSource.clip = bubbleLoopSFX;
                        loopAudioSource.loop = true;
                        loopAudioSource.playOnAwake = false;
                        loopAudioSource.spatialBlend = 0.5f; // 3D 空間感
                        loopAudioSource.minDistance = 2f;
                        loopAudioSource.maxDistance = hearDistance * 1.5f;
                    }

                    float volumeFactor = Mathf.Clamp01(1f - (dist / hearDistance));
                    loopAudioSource.volume = AudioManager.ScaleSfx(sfxVolume * Mathf.SmoothStep(0.2f, 1f, volumeFactor));

                    if (!loopAudioSource.isPlaying)
                    {
                        loopAudioSource.Play();
                    }
                }
                else
                {
                    if (loopAudioSource != null && loopAudioSource.isPlaying)
                    {
                        loopAudioSource.Stop();
                    }
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckAndPlayBurstSFX(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        CheckAndPlayBurstSFX(other.gameObject);
    }

    private void CheckAndPlayBurstSFX(GameObject go)
    {
        if (bubbleBurstSFX == null) return;
        if (!go.CompareTag("Player") && go.GetComponentInParent<PlayerMovement>() == null) return;

        if (Time.time - lastBurstTime >= burstCooldown)
        {
            lastBurstTime = Time.time;
            if (burstAudioSource == null)
            {
                burstAudioSource = gameObject.AddComponent<AudioSource>();
                burstAudioSource.playOnAwake = false;
                burstAudioSource.spatialBlend = 0.3f;
            }
            burstAudioSource.PlayOneShot(bubbleBurstSFX, AudioManager.ScaleSfx(sfxVolume));
        }
    }
}
