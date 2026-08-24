using UnityEngine;

[DisallowMultipleComponent]
public sealed class DisplaySettingsController : MonoBehaviour
{
    private const string FullScreenKey = "DisplaySettings.FullScreen";
    private const string WidthKey = "DisplaySettings.Width";
    private const string HeightKey = "DisplaySettings.Height";

    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;

    public bool IsFullScreen { get; private set; }
    public int SelectedWidth { get; private set; }
    public int SelectedHeight { get; private set; }

    private void Start()
    {
        IsFullScreen = PlayerPrefs.GetInt(FullScreenKey, 1) == 1;
        SelectedWidth = PlayerPrefs.GetInt(WidthKey, DefaultWidth);
        SelectedHeight = PlayerPrefs.GetInt(HeightKey, DefaultHeight);

        if (!IsSupportedResolution(SelectedWidth, SelectedHeight))
        {
            SelectedWidth = DefaultWidth;
            SelectedHeight = DefaultHeight;
        }

        ApplyCurrentSettings(false);
    }

    public void SelectOption(DisplaySettingsOption option)
    {
        if (option == null)
            return;

        switch (option.Kind)
        {
            case DisplaySettingsOption.OptionKind.FullScreen:
                IsFullScreen = true;
                break;
            case DisplaySettingsOption.OptionKind.Windowed:
                IsFullScreen = false;
                break;
            case DisplaySettingsOption.OptionKind.Resolution:
                if (!IsSupportedResolution(option.Width, option.Height))
                    return;
                SelectedWidth = option.Width;
                SelectedHeight = option.Height;
                break;
        }

        ApplyCurrentSettings(true);
    }

    private void ApplyCurrentSettings(bool save)
    {
        FullScreenMode mode = IsFullScreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;

        Screen.SetResolution(SelectedWidth, SelectedHeight, mode);
        RefreshSelectionSprites(false);

        if (save)
        {
            PlayerPrefs.SetInt(FullScreenKey, IsFullScreen ? 1 : 0);
            PlayerPrefs.SetInt(WidthKey, SelectedWidth);
            PlayerPrefs.SetInt(HeightKey, SelectedHeight);
            PlayerPrefs.Save();
        }
    }

    private void RefreshSelectionSprites(bool unused)
    {
        DisplaySettingsOption[] options =
            GetComponentsInChildren<DisplaySettingsOption>(true);

        for (int i = 0; i < options.Length; i++)
        {
            DisplaySettingsOption option = options[i];
            bool selected;

            if (option.Kind == DisplaySettingsOption.OptionKind.FullScreen)
                selected = IsFullScreen;
            else if (option.Kind == DisplaySettingsOption.OptionKind.Windowed)
                selected = !IsFullScreen;
            else
                selected =
                    option.Width == SelectedWidth &&
                    option.Height == SelectedHeight;

            option.SetSelected(selected);
        }
    }

    private static bool IsSupportedResolution(int width, int height)
    {
        return
            (width == 1920 && height == 1080) ||
            (width == 1280 && height == 720) ||
            (width == 960 && height == 540);
    }
}
