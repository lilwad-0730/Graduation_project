using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SequentialRevealTrigger : MonoBehaviour
{
    public enum RevealType
    {
        ScaleUp,    // 縮放顯現 (適用於所有3D物件，最推薦，不需特殊材質)
        AlphaFade,   // 透明度漸變 (適用於 2D Sprite 或 URP 半透明材質)
        Instant     // 瞬間出現
    }

    [System.Serializable]
    public class CustomRevealTarget
    {
        [Tooltip("要被顯現的目標物件 (如平台、橋樑、樓梯)")]
        public GameObject targetObject;
        [Tooltip("此物件專用的額外延遲時間 (若為 0 則使用全局預設延遲)")]
        public float delayOverride = 0f;
    }

    [Header("觸發條件")]
    [Tooltip("是否允許主角踩上去來觸發顯現平台？(勾選後，主角踩上去就會觸發，不需要拉任何物件入 Specific Stone)")]
    public bool triggerByPlayer = false;

    [Tooltip("指定觸發此機關的特定巨石。若留空且 triggerByPlayer 為 false，則任何帶有 UnlockableMovableObject 組件的巨石壓上都能觸發。")]
    public UnlockableMovableObject specificStone;

    [Header("顯示物件清單 (依排序順序顯現)")]
    [Tooltip("將所有需要顯現的物件拉入此清單，拖曳順序即為顯現的先後順序。")]
    public List<CustomRevealTarget> objectsToReveal = new List<CustomRevealTarget>();

    [Header("顯現動畫設定")]
    [Tooltip("顯現效果的類型")]
    public RevealType revealType = RevealType.ScaleUp;
    [Tooltip("每個物件完成顯現動畫所需的秒數")]
    public float revealDuration = 0.5f;
    [Tooltip("每個物件開始顯現之間的間隔延遲時間")]
    public float delayBetweenObjects = 0.3f;

    [Header("機制行為")]
    [Tooltip("如果打勾，一旦觸發顯現後，即使巨石移開，平台也會永久保持顯示。如果不打勾，巨石移開後會隱藏。")]
    public bool keepRevealed = false;

    // 內部快取狀態
    private class TargetCachedState
    {
        public GameObject obj;
        public Vector3 originalScale;
        public Renderer meshRenderer;
        public SpriteRenderer spriteRenderer;
        public Collider collider;
        public List<Color> originalMaterialColors = new List<Color>();
        public Color originalSpriteColor;
        public bool isRevealed = false;
    }

    private List<TargetCachedState> _cachedStates = new List<TargetCachedState>();
    private HashSet<Collider> _activeStones = new HashSet<Collider>();
    private Coroutine _activeSequenceCoroutine;
    private bool _isTriggered = false;

    private void Start()
    {
        // 確保自身的 Collider 是 Trigger
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        // 快取並隱藏所有目標物件
        CacheAndHideAllTargets();
    }

    private void CacheAndHideAllTargets()
    {
        _cachedStates.Clear();

        foreach (var target in objectsToReveal)
        {
            if (target == null || target.targetObject == null) continue;

            GameObject obj = target.targetObject;
            TargetCachedState state = new TargetCachedState();
            state.obj = obj;
            state.originalScale = obj.transform.localScale;
            state.meshRenderer = obj.GetComponent<Renderer>();
            state.spriteRenderer = obj.GetComponent<SpriteRenderer>();
            state.collider = obj.GetComponent<Collider>();

            // 快取 3D MeshRenderer 的材質顏色
            if (state.meshRenderer != null)
            {
                foreach (var mat in state.meshRenderer.materials)
                {
                    if (mat.HasProperty("_BaseColor"))
                    {
                        state.originalMaterialColors.Add(mat.GetColor("_BaseColor"));
                    }
                    else if (mat.HasProperty("_Color"))
                    {
                        state.originalMaterialColors.Add(mat.GetColor("_Color"));
                    }
                    else
                    {
                        state.originalMaterialColors.Add(Color.white);
                    }
                }
            }

            // 快取 2D SpriteRenderer 的顏色
            if (state.spriteRenderer != null)
            {
                state.originalSpriteColor = state.spriteRenderer.color;
            }

            _cachedStates.Add(state);

            // 遊戲開始時，無條件初始隱藏
            SetTargetVisibility(state, false, 0f);
        }
    }

    // 設定單一物件的隱藏/顯示狀態
    private void SetTargetVisibility(TargetCachedState state, bool visible, float progress)
    {
        if (state.obj == null) return;

        state.isRevealed = visible;

        // 1. 控制碰撞器 (隱藏時不能踩，顯示時在動畫開始時或完成後開啟)
        if (state.collider != null)
        {
            state.collider.enabled = visible;
        }

        // 2. 根據不同類型控制渲染器與動畫效果
        if (revealType == RevealType.Instant)
        {
            if (state.meshRenderer != null) state.meshRenderer.enabled = visible;
            if (state.spriteRenderer != null) state.spriteRenderer.enabled = visible;
            state.obj.transform.localScale = state.originalScale;
        }
        else if (revealType == RevealType.ScaleUp)
        {
            if (state.meshRenderer != null) state.meshRenderer.enabled = visible;
            if (state.spriteRenderer != null) state.spriteRenderer.enabled = visible;

            // 縮放插值
            state.obj.transform.localScale = visible ? Vector3.Lerp(Vector3.zero, state.originalScale, progress) : Vector3.zero;
        }
        else if (revealType == RevealType.AlphaFade)
        {
            if (state.meshRenderer != null) state.meshRenderer.enabled = visible;
            if (state.spriteRenderer != null) state.spriteRenderer.enabled = visible;

            float alpha = visible ? progress : 0f;

            // 3D 材質透明度
            if (state.meshRenderer != null && state.originalMaterialColors.Count > 0)
            {
                for (int i = 0; i < state.meshRenderer.materials.Length; i++)
                {
                    if (i >= state.originalMaterialColors.Count) break;
                    Material mat = state.meshRenderer.materials[i];
                    Color origCol = state.originalMaterialColors[i];
                    Color targetCol = new Color(origCol.r, origCol.g, origCol.b, origCol.a * alpha);
                    
                    if (mat.HasProperty("_BaseColor"))
                    {
                        mat.SetColor("_BaseColor", targetCol);
                    }
                    else if (mat.HasProperty("_Color"))
                    {
                        mat.SetColor("_Color", targetCol);
                    }
                }
            }

            // 2D Sprite 透明度
            if (state.spriteRenderer != null)
            {
                Color origCol = state.originalSpriteColor;
                state.spriteRenderer.color = new Color(origCol.r, origCol.g, origCol.b, origCol.a * alpha);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 1. 偵測是否為主角觸發
        if (triggerByPlayer)
        {
            bool isPlayer = other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>() != null || other.GetComponentInChildren<PlayerMovement>() != null;
            if (isPlayer)
            {
                if (!_activeStones.Contains(other))
                {
                    _activeStones.Add(other);
                }

                if (!_isTriggered)
                {
                    _isTriggered = true;
                    Debug.Log($"【機關觸發】主角已踏上 {gameObject.name}。開始順序顯現物件。");
                    StartRevealSequence();
                }
                return; // 優先處理主角，直接結束
            }
        }

        // 2. 偵測是否為巨石組件
        UnlockableMovableObject stone = other.GetComponentInParent<UnlockableMovableObject>();
        if (stone == null)
        {
            stone = other.GetComponent<UnlockableMovableObject>();
        }

        if (stone != null)
        {
            // 如果指定了特定巨石，且踏入的不是該巨石，則無視
            if (specificStone != null && stone != specificStone) return;

            if (!_activeStones.Contains(other))
            {
                _activeStones.Add(other);
            }

            if (!_isTriggered)
            {
                _isTriggered = true;
                Debug.Log($"【機關觸發】巨石 {stone.gameObject.name} 已壓下 {gameObject.name}。開始順序顯現物件。");
                StartRevealSequence();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (_activeStones.Contains(other))
        {
            _activeStones.Remove(other);

            // 當沒有任何觸發源在上面，且設定為移開後隱藏 (keepRevealed = false)
            if (_activeStones.Count == 0 && _isTriggered)
            {
                _isTriggered = false;
                Debug.Log($"【機關重置】踏板 {gameObject.name} 上已無觸發源。");
                
                if (!keepRevealed)
                {
                    StartHideSequence();
                }
            }
        }
    }

    private void StartRevealSequence()
    {
        if (_activeSequenceCoroutine != null)
        {
            StopCoroutine(_activeSequenceCoroutine);
        }
        _activeSequenceCoroutine = StartCoroutine(RevealSequenceRoutine());
    }

    private void StartHideSequence()
    {
        if (_activeSequenceCoroutine != null)
        {
            StopCoroutine(_activeSequenceCoroutine);
        }
        _activeSequenceCoroutine = StartCoroutine(HideSequenceRoutine());
    }

    // 順序漸層顯現協程
    private IEnumerator RevealSequenceRoutine()
    {
        for (int i = 0; i < _cachedStates.Count; i++)
        {
            var state = _cachedStates[i];
            if (state == null || state.obj == null) continue;

            // 取得對應的延遲設定
            float delay = objectsToReveal[i].delayOverride > 0f ? objectsToReveal[i].delayOverride : delayBetweenObjects;
            
            // 執行當前物件的漸變動畫
            if (revealType != RevealType.Instant)
            {
                float elapsed = 0f;
                SetTargetVisibility(state, true, 0f);

                while (elapsed < revealDuration)
                {
                    elapsed += Time.deltaTime;
                    float progress = Mathf.Clamp01(elapsed / revealDuration);
                    SetTargetVisibility(state, true, progress);
                    yield return null;
                }
            }
            
            // 確保設回最終的完全顯示狀態
            SetTargetVisibility(state, true, 1f);

            // 等待間隔時間，再處理下一個物件
            if (i < _cachedStates.Count - 1)
            {
                yield return new WaitForSeconds(delay);
            }
        }
        _activeSequenceCoroutine = null;
    }

    // 順序隱藏協程 (當巨石移開且 keepRevealed = false 時觸發，採用反方向漸隱)
    private IEnumerator HideSequenceRoutine()
    {
        for (int i = _cachedStates.Count - 1; i >= 0; i--)
        {
            var state = _cachedStates[i];
            if (state == null || state.obj == null) continue;

            float delay = objectsToReveal[i].delayOverride > 0f ? objectsToReveal[i].delayOverride : delayBetweenObjects;

            if (revealType != RevealType.Instant)
            {
                float elapsed = 0f;
                while (elapsed < revealDuration)
                {
                    elapsed += Time.deltaTime;
                    float progress = 1f - Mathf.Clamp01(elapsed / revealDuration);
                    SetTargetVisibility(state, true, progress);
                    yield return null;
                }
            }

            SetTargetVisibility(state, false, 0f);

            if (i > 0)
            {
                yield return new WaitForSeconds(delay);
            }
        }
        _activeSequenceCoroutine = null;
    }
}
