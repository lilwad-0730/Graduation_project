using UnityEngine;
using UnityEngine.UI;
using System.Collections; 

public class TutorialZone : MonoBehaviour
{
    [Header("要把哪個教學文字打開？")]
    public GameObject tutorialText; 

    [Header("淡化效果設定")]
    [Tooltip("花費幾秒完成淡入或淡出？")]
    public float fadeDuration = 0.5f;

    private Coroutine _fadeCoroutine;

    // 自動蒐集文字物件身上所有的視覺元件，確保不管你是 2D還是 3D 文字都能完美淡入淡出！
    private Graphic[] _uiElements;
    private SpriteRenderer[] _sprites;
    private TextMesh[] _textMeshes;
    // (如果對方用的是 TMPro 的文字，因為安全防呆，會透過 GetComponent 抓到底層的 Renderer)
    private Renderer[] _allRenderers;
    private CanvasGroup[] _canvasGroups;

    private void Start()
    {
        if (tutorialText != null)
        {
            // 防呆絕招：抓取底下所有的視覺組件
            _uiElements = tutorialText.GetComponentsInChildren<Graphic>(true);
            _sprites = tutorialText.GetComponentsInChildren<SpriteRenderer>(true);
            _textMeshes = tutorialText.GetComponentsInChildren<TextMesh>(true);
            _allRenderers = tutorialText.GetComponentsInChildren<Renderer>(true);
            _canvasGroups = tutorialText.GetComponentsInChildren<CanvasGroup>(true);

            // 防呆絕招 2：如果玩家在 Inspector 把文字物件關掉了，這邊強制打開，否則改透明度也看不到！
            tutorialText.SetActive(true);

            // 防呆絕招 3：確保身上的碰撞器有打勾 IsTrigger，否則 OnTriggerEnter 永遠不會觸發！
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            // 神級修改：不要使用 SetActive(false) 來關閉物件！
            // 為什麼？因為很多新手會把「教學文字」跟「碰撞隱形方塊」當成同一個物件，
            // 如果你把自己關掉了，你的碰撞器跟腳本就通通死掉了，以後再走進去也沒反應了！
            // 所以我們改用「隱形法」：把身上的顏色透明度強制變成 0。
            SetVisualAlpha(0f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && tutorialText != null)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(1f));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && tutorialText != null)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(0f));
        }
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        // 算出現在的透明度
        float startAlpha = GetCurrentAlpha();
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            // 計算此刻剛好的透明度
            float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            
            // 一口氣把所有元件的透明度更新
            SetVisualAlpha(currentAlpha);
            
            yield return null; 
        }

        // 確保終點數值無誤
        SetVisualAlpha(targetAlpha);
    }

    // 將所有元件都塗上我們指定的透明度
    private void SetVisualAlpha(float alpha)
    {
        // 一般 UI
        foreach (var ui in _uiElements) {
            if (ui != null) { Color c = ui.color; c.a = alpha; ui.color = c; }
        }
        // 純圖片
        foreach (var sp in _sprites) {
            if (sp != null) { Color c = sp.color; c.a = alpha; sp.color = c; }
        }
        // 舊版 3D 文字
        foreach (var tm in _textMeshes) {
            if (tm != null) { Color c = tm.color; c.a = alpha; tm.color = c; }
        }
        // 任何被抓到的世界模型 (含 TextMeshPro 3D 版)
        foreach (var r in _allRenderers) {
            if (r != null && r.material != null && r.material.HasProperty("_Color")) {
                Color c = r.material.color; c.a = alpha; r.material.color = c;
            } else if (r != null && r.material != null && r.material.HasProperty("_BaseColor")) { // URP/HDRP
                Color c = r.material.GetColor("_BaseColor"); c.a = alpha; r.material.SetColor("_BaseColor", c);
            }
        }
        // CanvasGroup 支援
        foreach (var cg in _canvasGroups) {
            if (cg != null) cg.alpha = alpha;
        }
    }

    private float GetCurrentAlpha()
    {
        // 隨便抓一個人身上的透明度來當作起始值，大家通常都會一起同步
        if (_canvasGroups != null && _canvasGroups.Length > 0 && _canvasGroups[0] != null) return _canvasGroups[0].alpha;
        if (_uiElements != null && _uiElements.Length > 0 && _uiElements[0] != null) return _uiElements[0].color.a;
        if (_sprites != null && _sprites.Length > 0 && _sprites[0] != null) return _sprites[0].color.a;
        if (_textMeshes != null && _textMeshes.Length > 0 && _textMeshes[0] != null) return _textMeshes[0].color.a;
        return 0f; 
    }
}