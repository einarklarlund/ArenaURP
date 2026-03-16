using System;
using UnityEngine;

/// <summary>
/// Singleton that holds the current host options for the next match.
/// </summary>
public class HostOptionsManager : MonoBehaviour
{
    public static HostOptionsManager Instance { get; private set; }

    [SerializeField] private HostOptionsData currentOptions = new();

    public HostOptionsData Options => currentOptions;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Apply a modification to the current host options.
    /// </summary>
    public void UpdateOptions(Action<HostOptionsData> modification)
    {
        modification(currentOptions);
    }

    /// <summary>
    /// Resets all host options back to their defaults.
    /// </summary>
    public void ResetToDefaults()
    {
        currentOptions = new HostOptionsData();
    }
}