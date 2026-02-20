using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization; // Assuming you use TextMeshPro for the value label

public class SettingsView : View
{
    [Header("Value Components")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_InputField sensitivityInputField;

    [Header("Navigation Components")]
    [SerializeField] private Button saveButton;
    [SerializeField] private Button cancelButton;

    public override void Show()
    {
        var current = SettingsManager.Instance.Settings;
        UpdateUI(current);

        sensitivitySlider.onValueChanged.AddListener(OnSensitivitySliderChanged);
        sensitivityInputField.onEndEdit.AddListener(OnSensitivityInputChanged);
        
        saveButton.onClick.AddListener(OnSaveClicked);
        cancelButton.onClick.AddListener(OnCancelClicked);

        base.Show();
    }

    public override void Hide()
    {
        sensitivitySlider.onValueChanged.RemoveListener(OnSensitivitySliderChanged);
        sensitivityInputField.onEndEdit.RemoveListener(OnSensitivityInputChanged);
        
        saveButton.onClick.RemoveListener(OnSaveClicked);
        cancelButton.onClick.RemoveListener(OnCancelClicked);

        base.Hide();
    }

    private void OnSaveClicked()
    {
        SettingsManager.Instance.SaveSettings();

        LocalUIEvents.OnSettingsSaved?.Invoke();
        LocalUIEvents.OnSettingsClosed?.Invoke();
    }

    private void OnCancelClicked()
    {
        SettingsManager.Instance.LoadSettings(); // undoes unsaved changes
        LocalUIEvents.OnSettingsClosed?.Invoke();
    }

    private void OnSensitivitySliderChanged(float value)
    {
        SettingsManager.Instance.UpdateSettings(s => s.MouseSensitivity = value);
        sensitivityInputField.text = value.ToString("F2"); // Format to 2 decimal places
    }
    
    private void OnSensitivityInputChanged(string text)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
        {
            result = Mathf.Clamp(result, sensitivitySlider.minValue, sensitivitySlider.maxValue);
            
            SettingsManager.Instance.UpdateSettings(s => s.MouseSensitivity = result);
            sensitivitySlider.value = result;
            sensitivityInputField.text = result.ToString("F2");
        }
        else
        {
            sensitivityInputField.text = SettingsManager.Instance.Settings.MouseSensitivity.ToString("F2");
        }
    }

    private void UpdateUI(PlayerSettingsData data)
    {
        sensitivitySlider.value = data.MouseSensitivity;
        sensitivityInputField.text = data.MouseSensitivity.ToString("F2");
    }
}