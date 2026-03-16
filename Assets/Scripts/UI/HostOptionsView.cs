using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization;

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

    [Header("Navigation")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button backButton;

    public override void Show()
    {
        var current = HostOptionsManager.Instance.Options;
        UpdateUI(current);

        botCountSlider.onValueChanged.AddListener(OnBotCountSliderChanged);
        botCountInputField.onEndEdit.AddListener(OnBotCountInputChanged);

        startButton.onClick.AddListener(OnStartClicked);
        backButton.onClick.AddListener(OnBackClicked);

        base.Show();
    }

    public override void Hide()
    {
        botCountSlider.onValueChanged.RemoveListener(OnBotCountSliderChanged);
        botCountInputField.onEndEdit.RemoveListener(OnBotCountInputChanged);

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

    private void UpdateUI(HostOptionsData data)
    {
        botCountSlider.value = data.BotCount;
        botCountInputField.text = data.BotCount.ToString();
    }
}