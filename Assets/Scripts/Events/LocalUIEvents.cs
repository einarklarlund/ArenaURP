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

    // Room actions
    public static Action OnHostInitiated;
    public static Action OnJoinInitiated;
}