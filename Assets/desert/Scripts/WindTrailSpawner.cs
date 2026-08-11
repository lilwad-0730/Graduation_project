using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 高雅 S 曲線風痕氣流發射器 (Slender Wind Trail Spawner)。
/// 依據用戶提供之原圖標籤 (Wind Trails) 精確打造：
/// 在空中與沙丘上方持續密集發射多層輕盈、纖細、波浪起伏的純白天藍色弧線風痕。
/// </summary>
public class WindTrailSpawner : MonoBehaviour
{
    [Header("風痕貼圖列表 (WindAlpha Textures)")]
    public List<Sprite> windSprites = new List<Sprite>();

    [Header("發射時間間隔 (秒)")]
    public float minSpawnInterval = 0.18f;
    public float maxSpawnInterval = 0.42f;

    [Header("飛行速度與高度範圍")]
    public float minSpeed = 15.0f;
    public float maxSpeed = 24.0f;
    public float minYOffset = -3.0f;
    public float maxYOffset = 14.0f;

    [Header("追蹤攝影機")]
    public Transform targetCamera;

    private float timer = 0f;
    private float nextSpawnInterval = 0.3f;

    private void Start()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        // 開局預先填滿畫面，提供高雅密集流線
        if (targetCamera != null)
        {
            float camX = targetCamera.position.x;
            SpawnWindTrailAt(camX - 14.0f, 7.0f);
            SpawnWindTrailAt(camX - 6.0f, 12.0f);
            SpawnWindTrailAt(camX + 1.0f, 2.0f);
            SpawnWindTrailAt(camX + 8.0f, 9.0f);
            SpawnWindTrailAt(camX + 15.0f, 13.0f);
            SpawnWindTrailAt(camX + 22.0f, 5.0f);
        }

        nextSpawnInterval = Random.Range(minSpawnInterval, maxSpawnInterval);
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            if (Camera.main != null) targetCamera = Camera.main.transform;
            else return;
        }

        timer += Time.deltaTime;
        if (timer >= nextSpawnInterval)
        {
            timer = 0f;
            nextSpawnInterval = Random.Range(minSpawnInterval, maxSpawnInterval);
            float spawnY = targetCamera.position.y + Random.Range(minYOffset, maxYOffset);
            SpawnWindTrailAt(targetCamera.position.x + 16.0f, spawnY);
        }
    }

    /// <summary>
    /// 在指定 X 座標與 Y 座標生成纖細飄逸風痕
    /// </summary>
    public void SpawnWindTrailAt(float customX, float customY)
    {
        if (windSprites == null || windSprites.Count == 0) return;

        Sprite chosenSprite = windSprites[Random.Range(0, windSprites.Count)];

        GameObject trailGo = new GameObject("WindTrail_Instance");
        WindTrailSpriteAnimator animator = trailGo.AddComponent<WindTrailSpriteAnimator>();

        float speed = Random.Range(minSpeed, maxSpeed);
        float sizeMult = Random.Range(0.85f, 1.3f);
        float angle = Random.Range(-6f, 6f); // 微幅自然弧向角度

        animator.Init(new Vector3(customX, customY, -0.8f), chosenSprite, speed, sizeMult, angle);
    }
}
