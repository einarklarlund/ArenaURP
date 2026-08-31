using System;

/// <summary>
/// The phase this peer's own join attempt is in — never another peer's. Ordered as the join
/// progresses; <see cref="Failed"/> is terminal (ADR 0012).
/// </summary>
public enum JoinPhase
{
    ContactingSignal,
    Negotiating,
    /// This is the phase that can legitimately last the full 39.5s signal timeout.
    EstablishingConnection,
    Connected,
    Failed,
}

public static class LocalUIEvents
{
    public static Action OnPause;
    public static Action OnUnpause;

    public static Action OnSettingsOpened;
    public static Action OnSettingsClosed;
    public static Action OnSettingsSaved;

    public static Action OnRoomBrowserOpened;
    public static Action OnRoomBrowserClosed;

    public static Action OnHostOptionsOpened;
    public static Action OnHostOptionsClosed;

    public static Action OnHostInitiated;
    public static Action OnJoinInitiated;
    public static Action<JoinPhase> OnJoinPhaseChanged;
    public static Action OnJoinCancelled;

    public static Action OnDebugViewOpened;
    public static Action OnDebugViewClosed;

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
        OnJoinPhaseChanged = null;
        OnJoinCancelled = null;
        OnDebugViewOpened = null;
        OnDebugViewClosed = null;
    }
}