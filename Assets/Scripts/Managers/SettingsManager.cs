using System;
using UnityEngine;

[System.Serializable]
public class PlayerSettingsData
{
    // Default values for the first time a user plays
    public float MouseSensitivity = 1f;
    public float FieldOfView = 90f;
}

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [SerializeField] private PlayerSettingsData currentSettings = new();
    
    public PlayerSettingsData Settings => currentSettings;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSettings();
    }
    
    public void UpdateSettings(Action<PlayerSettingsData> modification)
    {
        modification(currentSettings);
    }

    public void SaveSettings()
    {
        Debug.Log("Settings Saved locally.");
    }

    public void LoadSettings()
    {
        Debug.Log("Settings Loaded.");
    }
}