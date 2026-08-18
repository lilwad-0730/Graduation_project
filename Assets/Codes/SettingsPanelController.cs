using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 設定面板控制器
/// 包含主音量、音樂音量、音效音量、解析度選單、全螢幕切換與返回按鈕
/// 資料透身 PlayerPrefs 自動儲存與載入，整合 AudioManager
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
    [Header("UI 控制項綁定")]
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public TMP_Dropdown resolutionDropdown;
    public Toggle fullscreenToggle;
    public Button backButton;

    [Header("與 MainMenu 的關聯")]
    public MainMenuController mainMenuController;
    public CanvasGroup panelCanvasGroup;

    private void Awake()
    {
        if (panelCanvasGroup == null) panelCanvasGroup = GetComponent<CanvasGroup>();
    }

    private void Start()
    {
        // 初始化 Slider 監聽
        if (masterVolumeSlider != null) masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (musicVolumeSlider != null) musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null) sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);

        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            var options = new System.Collections.Generic.List<string>
            {
                "1920 x 1080 (16:9)",
                "1600 x 900 (16:9)",
                "1280 x 720 (16:9)",
                "2560 x 1440 (21:9)"
            };
            resolutionDropdown.AddOptions(options);
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        }

        if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggled);
        if (backButton != null) backButton.onClick.AddListener(ClosePanel);

        LoadSavedSettings();
    }

    public void OpenPanel()
    {
        gameObject.SetActive(true);
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        if (backButton != null)
        {
            backButton.Select();
        }
    }

    public void ClosePanel()
    {
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);

        if (mainMenuController != null)
        {
            mainMenuController.OnSubPanelClosed(1); // 1 代表「設定」按鈕索引
        }
    }

    private void LoadSavedSettings()
    {
        float master = PlayerPrefs.GetFloat("MasterVolume", 1.0f);
        float music = PlayerPrefs.GetFloat("MusicVolume", 0.8f);
        float sfx = PlayerPrefs.GetFloat("SFXVolume", 1.0f);
        int resIndex = PlayerPrefs.GetInt("ResolutionIndex", 0);
        bool fullscreen = PlayerPrefs.GetInt("Fullscreen", 1) == 1;

        if (masterVolumeSlider != null) masterVolumeSlider.value = master;
        if (musicVolumeSlider != null) musicVolumeSlider.value = music;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = sfx;
        if (resolutionDropdown != null) resolutionDropdown.value = resIndex;
        if (fullscreenToggle != null) fullscreenToggle.isOn = fullscreen;

        AudioListener.volume = master;
    }

    private void OnMasterVolumeChanged(float val)
    {
        AudioListener.volume = val;
        PlayerPrefs.SetFloat("MasterVolume", val);
    }

    private void OnMusicVolumeChanged(float val)
    {
        PlayerPrefs.SetFloat("MusicVolume", val);
    }

    private void OnSfxVolumeChanged(float val)
    {
        PlayerPrefs.SetFloat("SFXVolume", val);
    }

    private void OnResolutionChanged(int index)
    {
        PlayerPrefs.SetInt("ResolutionIndex", index);
        switch (index)
        {
            case 0: Screen.SetResolution(1920, 1080, Screen.fullScreen); break;
            case 1: Screen.SetResolution(1600, 900, Screen.fullScreen); break;
            case 2: Screen.SetResolution(1280, 720, Screen.fullScreen); break;
            case 3: Screen.SetResolution(2560, 1440, Screen.fullScreen); break;
        }
    }

    private void OnFullscreenToggled(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
        PlayerPrefs.SetInt("Fullscreen", isFullscreen ? 1 : 0);
    }
}
