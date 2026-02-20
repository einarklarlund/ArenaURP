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
}