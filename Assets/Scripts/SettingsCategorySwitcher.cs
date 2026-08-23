using System;
using UnityEngine;

public sealed class SettingsCategorySwitcher : MonoBehaviour
{
    public enum Category
    {
        Volume = 0,
        Windows = 1,
        Guide = 2
    }

    [SerializeField] private Category defaultCategory = Category.Volume;
    [SerializeField] private Transform volumeRoot;
    [SerializeField] private Transform windowsRoot;
    [SerializeField] private Transform guideRoot;

    public Category CurrentCategory { get; private set; }

private void Awake()
    {
        ResolveRoots();
    }

private void Start()
    {
        ShowCategory(defaultCategory);
    }


public void ShowCategory(Category category)
    {
        ResolveRoots();
        CurrentCategory = category;

        SetCategoryContents(volumeRoot, category == Category.Volume);
        SetCategoryContents(windowsRoot, category == Category.Windows);
        SetCategoryContents(guideRoot, category == Category.Guide);
        SetCategoryCanvasText(category);
        SetSelectedTab(category);
    }

private void SetSelectedTab(Category category)
    {
        SettingsCategoryTab[] tabs = GetComponentsInChildren<SettingsCategoryTab>(true);

        for (int i = 0; i < tabs.Length; i++)
            tabs[i].SetSelected(tabs[i].Category == category);
    }


    private void ResolveRoots()
    {
        if (volumeRoot == null)
            volumeRoot = transform.Find("Volume");

        if (windowsRoot == null)
            windowsRoot = transform.Find("Windows");

        if (guideRoot == null)
            guideRoot = transform.Find("Guide");
    }

    private static void SetCategoryContents(Transform categoryRoot, bool visible)
    {
        if (categoryRoot == null)
            return;

        categoryRoot.gameObject.SetActive(true);

        for (int i = 0; i < categoryRoot.childCount; i++)
        {
            Transform child = categoryRoot.GetChild(i);
            bool isTabButton =
                child.GetComponent<SettingsCategoryTab>() != null ||
                child.name.IndexOf("_graybox", StringComparison.OrdinalIgnoreCase) >= 0;

            child.gameObject.SetActive(isTabButton || visible);
        }
    }

    private void SetCategoryCanvasText(Category category)
    {
        Transform canvasRoot = transform.Find("SettingsTextCanvas");
        if (canvasRoot == null)
            return;

        for (int i = 0; i < canvasRoot.childCount; i++)
        {
            GameObject child = canvasRoot.GetChild(i).gameObject;
            string objectName = child.name.ToLowerInvariant();

            if (objectName.Contains("master_volume") || objectName.Contains("bgm_volume"))
            {
                child.SetActive(category == Category.Volume);
            }
            else if (objectName.Contains("windows_content"))
            {
                child.SetActive(category == Category.Windows);
            }
            else if (objectName.Contains("guide_content"))
            {
                child.SetActive(category == Category.Guide);
            }
        }
    }
}
