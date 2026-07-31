using UnityEngine;

/// <summary>
/// 自動將場景中所有名稱開頭為 'glass floor' 的物件造型，替換為 'glass floor transparent_001' Sprite。
/// 無論是在 Edit Mode 還是 Play Mode 均會自動執行。
/// </summary>
[ExecuteAlways]
public class ReplaceGlassFloorSpriteRuntime : MonoBehaviour
{
    private static bool executed = false;

    private void Awake()
    {
        ApplySprite();
    }

    private void OnEnable()
    {
        ApplySprite();
    }

    public static void ApplySprite()
    {
        if (executed) return;

        Sprite targetSprite = null;
        var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in allSprites)
        {
            if (s.name.Equals("glass floor transparent_001", System.StringComparison.OrdinalIgnoreCase))
            {
                targetSprite = s;
                break;
            }
        }

        if (targetSprite == null)
        {
            foreach (var s in allSprites)
            {
                if (s.name.ToLower().Contains("transparent"))
                {
                    targetSprite = s;
                    break;
                }
            }
        }

        if (targetSprite == null) return;

        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int replacedCount = 0;

        foreach (var go in all)
        {
            if (go.name.ToLower().StartsWith("glass floor"))
            {
                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null && sr.sprite != targetSprite)
                {
                    sr.sprite = targetSprite;
                    replacedCount++;
                }
            }
        }

        if (replacedCount > 0)
        {
            executed = true;
            Debug.Log($"[ReplaceGlassFloorSpriteRuntime] 成功將 {replacedCount} 個 'glass floor' 物件造型替換為 '{targetSprite.name}'！");
        }
    }
}
