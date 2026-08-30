using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

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
    private TMP_Text[] _tmpTexts;
    private Renderer[] _allRenderers;
    private CanvasGroup[] _canvasGroups;

    private void Start()
    {
        if (tutorialText != null)
        {
            // 抓取底下所有的視覺組件
            _uiElements = tutorialText.GetComponentsInChildren<Graphic>(true);
            _sprites = tutorialText.GetComponentsInChildren<SpriteRenderer>(true);
            _textMeshes = tutorialText.GetComponentsInChildren<TextMesh>(true);
            _tmpTexts = tutorialText.GetComponentsInChildren<TMP_Text>(true);
            _canvasGroups = tutorialText.GetComponentsInChildren<CanvasGroup>(true);

            // ★ 排除 TextMeshPro 3D 的 MeshRenderer，防止存取 r.material 破壞 SDF 動態貼圖導致黑塊！
            List<Renderer> validRenderers = new List<Renderer>();
            Renderer[] allRends = tutorialText.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRends)
            {
                if (r != null && r.GetComponent<TMP_Text>() == null && r.GetComponentInParent<TMP_Text>() == null)
                {
                    validRenderers.Add(r);
                }
            }
            _allRenderers = validRenderers.ToArray();

            // 防呆絕招 2：如果玩家在 Inspector 把文字物件關掉了，這邊強制打開
            tutorialText.SetActive(true);

            // 防呆絕招 3：確保身上的碰撞器有打勾 IsTrigger
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            // 初始透明度設為 0 (隱形)
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
        float startAlpha = GetCurrentAlpha();
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            SetVisualAlpha(currentAlpha);
            yield return null; 
        }

        SetVisualAlpha(targetAlpha);
    }

    // 將所有元件都塗上我們指定的透明度
    private void SetVisualAlpha(float alpha)
    {
        // 1. TextMeshPro 文字 (安全透過 .color 與 .alpha 調整透明度，絕不觸摸 material 造成黑塊)
        if (_tmpTexts != null)
        {
            foreach (var tmp in _tmpTexts)
            {
                if (tmp != null)
                {
                    Color c = tmp.color;
                    c.a = alpha;
                    tmp.color = c;
                }
            }
        }

        // 2. 一般 UI
        if (_uiElements != null)
        {
            foreach (var ui in _uiElements)
            {
                if (ui != null)
                {
                    Color c = ui.color;
                    c.a = alpha;
                    ui.color = c;
                }
            }
        }

        // 3. 純圖片
        if (_sprites != null)
        {
            foreach (var sp in _sprites)
            {
                if (sp != null)
                {
                    Color c = sp.color;
                    c.a = alpha;
                    sp.color = c;
                }
            }
        }

        // 4. 舊版 3D 文字
        if (_textMeshes != null)
        {
            foreach (var tm in _textMeshes)
            {
                if (tm != null)
                {
                    Color c = tm.color;
                    c.a = alpha;
                    tm.color = c;
                }
            }
        }

        // 5. 非 TMP 的世界模型
        if (_allRenderers != null)
        {
            foreach (var r in _allRenderers)
            {
                if (r != null && r.material != null && r.material.HasProperty("_Color"))
                {
                    Color c = r.material.color;
                    c.a = alpha;
                    r.material.color = c;
                }
                else if (r != null && r.material != null && r.material.HasProperty("_BaseColor"))
                {
                    Color c = r.material.GetColor("_BaseColor");
                    c.a = alpha;
                    r.material.SetColor("_BaseColor", c);
                }
            }
        }

        // 6. CanvasGroup
        if (_canvasGroups != null)
        {
            foreach (var cg in _canvasGroups)
            {
                if (cg != null) cg.alpha = alpha;
            }
        }
    }

    private float GetCurrentAlpha()
    {
        if (_tmpTexts != null && _tmpTexts.Length > 0 && _tmpTexts[0] != null) return _tmpTexts[0].color.a;
        if (_canvasGroups != null && _canvasGroups.Length > 0 && _canvasGroups[0] != null) return _canvasGroups[0].alpha;
        if (_uiElements != null && _uiElements.Length > 0 && _uiElements[0] != null) return _uiElements[0].color.a;
        if (_sprites != null && _sprites.Length > 0 && _sprites[0] != null) return _sprites[0].color.a;
        if (_textMeshes != null && _textMeshes.Length > 0 && _textMeshes[0] != null) return _textMeshes[0].color.a;
        return 0f; 
    }
}