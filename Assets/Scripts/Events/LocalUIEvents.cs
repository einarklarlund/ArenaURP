using System;

public static class LocalUIEvents
{
    // Pause events
    public static Action OnPause;
    public static Action OnUnpause;

    // Settings events
    public static Action OnSettingsOpened;
    public static Action OnSettingsClosed;
    public static Action OnSettingsSaved;

    // Room browser events
    public static Action OnRoomBrowserOpened;
    public static Action OnRoomBrowserClosed;

    // Host options events
    public static Action OnHostOptionsOpened;
    public static Action OnHostOptionsClosed;

    // Room actions
    public static Action OnHostInitiated;
    public static Action OnJoinInitiated;

    // Debug events
    public static Action OnDebugViewOpened;
    public static Action OnDebugViewClosed;

    // In-game HUD events
    public static Action<float> OnTemperatureChanged;

    public static void Reset()
    {
        OnPause = null;
        OnUnpause = null;
        OnSettingsOpened = null;
        OnSettingsClosed = null;
        OnSettingsSaved = null;
        OnRoomBrowserOpened = null;
        OnRoomBrowserClosed = null;
        OnHostOptionsOpened = null;
        OnHostOptionsClosed = null;
        OnHostInitiated = null;
        OnJoinInitiated = null;
        OnDebugViewOpened = null;
        OnDebugViewClosed = null;
        OnTemperatureChanged = null;
    }
}