using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization;
using Arena.LevelGen;

/// <summary>
/// UI screen displayed after the player clicks "Host", allowing them to
/// configure match options (bot count, etc.) before starting the server.
/// Reads/writes through HostOptionsManager.
/// </summary>
public sealed class HostOptionsView : View
{
    [Header("Bot Settings")]
    [SerializeField] private Slider botCountSlider;
    [SerializeField] private TMP_InputField botCountInputField;

    [Header("Level")]
    [SerializeField] private LevelArtRegistry levelRegistry;
    [SerializeField] private TMP_InputField seedInputField;
    [SerializeField] private Button randomizeSeedButton;
    [SerializeField] private TMP_Dropdown presetDropdown;
    [SerializeField] private TMP_Dropdown themeDropdown;
    [Tooltip("Shows the code for the chosen seed and preset; pasting another code takes its seed and config.")]
    [SerializeField] private TMP_InputField shareCodeInputField;
    [SerializeField] private TMP_Text shareCodeStatusText;

    [Header("Navigation")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button backButton;

    public override void Show()
    {
        var current = HostOptionsManager.Instance.Options;
        UpdateUI(current);

        botCountSlider.onValueChanged.AddListener(OnBotCountSliderChanged);
        botCountInputField.onEndEdit.AddListener(OnBotCountInputChanged);
        ShowLevel(current);

        startButton.onClick.AddListener(OnStartClicked);
        backButton.onClick.AddListener(OnBackClicked);

        base.Show();
    }

    public override void Hide()
    {
        botCountSlider.onValueChanged.RemoveListener(OnBotCountSliderChanged);
        botCountInputField.onEndEdit.RemoveListener(OnBotCountInputChanged);
        HideLevel();

        startButton.onClick.RemoveListener(OnStartClicked);
        backButton.onClick.RemoveListener(OnBackClicked);

        base.Hide();
    }

    private void OnStartClicked()
    {
        RoomManager.Instance.CreateRoom();
    }

    private void OnBackClicked()
    {
        HostOptionsManager.Instance.ResetToDefaults();
        LocalUIEvents.OnHostOptionsClosed?.Invoke();
    }

    private void OnBotCountSliderChanged(float value)
    {
        int intValue = Mathf.RoundToInt(value);
        HostOptionsManager.Instance.UpdateOptions(o => o.BotCount = intValue);
        botCountInputField.text = intValue.ToString();
    }

    private void OnBotCountInputChanged(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            result = Mathf.Clamp(result, (int)botCountSlider.minValue, (int)botCountSlider.maxValue);

            HostOptionsManager.Instance.UpdateOptions(o => o.BotCount = result);
            botCountSlider.value = result;
            botCountInputField.text = result.ToString();
        }
        else
        {
            botCountInputField.text = HostOptionsManager.Instance.Options.BotCount.ToString();
        }
    }

    // ── Level ────────────────────────────────────────────────────────────

    /// <summary>Fills the Level section. A host who hasn't picked a seed gets a fresh random one each time.</summary>
    private void ShowLevel(HostOptionsData data)
    {
        if (seedInputField == null) return;

        if (data.RandomLevelSeed)
            HostOptionsManager.Instance.UpdateOptions(o =>
            {
                o.LevelSeed = RandomSeed();
                o.RandomLevelSeed = false;
            });

        FillDropdown(presetDropdown, levelRegistry != null ? levelRegistry.presets : null, data.LevelPreset);
        FillDropdown(themeDropdown, levelRegistry != null ? levelRegistry.themes : null, data.LevelTheme);

        seedInputField.onEndEdit.AddListener(OnSeedChanged);
        randomizeSeedButton.onClick.AddListener(OnRandomizeSeedClicked);
        presetDropdown.onValueChanged.AddListener(OnPresetChanged);
        themeDropdown.onValueChanged.AddListener(OnThemeChanged);
        shareCodeInputField.onValueChanged.AddListener(OnShareCodeChanged);
        RefreshLevel();
    }

    private void HideLevel()
    {
        if (seedInputField == null) return;

        seedInputField.onEndEdit.RemoveListener(OnSeedChanged);
        randomizeSeedButton.onClick.RemoveListener(OnRandomizeSeedClicked);
        presetDropdown.onValueChanged.RemoveListener(OnPresetChanged);
        themeDropdown.onValueChanged.RemoveListener(OnThemeChanged);
        shareCodeInputField.onValueChanged.RemoveListener(OnShareCodeChanged);
    }

    private void OnSeedChanged(string text)
    {
        if (ulong.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed))
            HostOptionsManager.Instance.UpdateOptions(o =>
            {
                o.LevelSeed = seed;
                o.LevelShareCode = "";
            });
        RefreshLevel();
    }

    private void OnRandomizeSeedClicked()
    {
        HostOptionsManager.Instance.UpdateOptions(o =>
        {
            o.LevelSeed = RandomSeed();
            o.LevelShareCode = "";
        });
        RefreshLevel();
    }

    private void OnPresetChanged(int index)
    {
        HostOptionsManager.Instance.UpdateOptions(o =>
        {
            o.LevelPreset = index;
            o.LevelShareCode = "";
        });
        RefreshLevel();
    }

    private void OnThemeChanged(int index)
    {
        HostOptionsManager.Instance.UpdateOptions(o => o.LevelTheme = index);
    }

    /// <summary>A pasted code takes over the seed and config; anything else that isn't the shown code is reported.</summary>
    private void OnShareCodeChanged(string text)
    {
        if (text == CurrentShareCode()) return;
        if (ShareCode.TryDecode(text, out ulong seed, out _, out string error))
        {
            HostOptionsManager.Instance.UpdateOptions(o =>
            {
                o.LevelShareCode = text.Trim();
                o.LevelSeed = seed;
            });
            RefreshLevel();
            return;
        }
        HostOptionsManager.Instance.UpdateOptions(o => o.LevelShareCode = "");
        SetShareCodeStatus(error, true);
    }

    /// <summary>Shows the options: the seed, whether the config comes from a pasted code, and the share code.</summary>
    private void RefreshLevel()
    {
        var data = HostOptionsManager.Instance.Options;
        bool fromCode = !string.IsNullOrEmpty(data.LevelShareCode);
        seedInputField.SetTextWithoutNotify(data.LevelSeed.ToString(CultureInfo.InvariantCulture));
        presetDropdown.interactable = !fromCode;
        shareCodeInputField.SetTextWithoutNotify(fromCode ? data.LevelShareCode : CurrentShareCode());
        SetShareCodeStatus(fromCode ? "Using the pasted code's seed and config." : "Copy this code to share the level.", false);
    }

    /// <summary>The code for the chosen seed and preset (or the pasted one).</summary>
    private string CurrentShareCode()
    {
        var data = HostOptionsManager.Instance.Options;
        if (!string.IsNullOrEmpty(data.LevelShareCode)) return data.LevelShareCode;
        var preset = levelRegistry != null ? levelRegistry.Preset(data.LevelPreset) : null;
        var settings = preset != null ? preset.ToSettings() : LevelGenSettings.Default.Sanitized();
        return ShareCode.Encode(LevelGenerator.GeneratorVersion, data.LevelSeed, settings);
    }

    private void SetShareCodeStatus(string message, bool error)
    {
        if (shareCodeStatusText == null) return;
        shareCodeStatusText.text = message;
        shareCodeStatusText.color = error ? new Color(1f, 0.45f, 0.4f) : Color.white;
    }

    private static void FillDropdown<T>(TMP_Dropdown dropdown, List<T> items, int selected) where T : UnityEngine.Object
    {
        var options = new List<TMP_Dropdown.OptionData>();
        if (items != null)
            foreach (var item in items) options.Add(new TMP_Dropdown.OptionData(item != null ? item.name : "(missing)"));
        if (options.Count == 0) options.Add(new TMP_Dropdown.OptionData("Default"));
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, options.Count - 1));
    }

    private static ulong RandomSeed() => BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);

    private void UpdateUI(HostOptionsData data)
    {
        botCountSlider.value = data.BotCount;
        botCountInputField.text = data.BotCount.ToString();
    }
}