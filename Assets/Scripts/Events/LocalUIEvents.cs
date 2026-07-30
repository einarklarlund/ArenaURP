using System;

/// <summary>
/// The phase this peer's own join attempt is in. Local, not networked: it describes
/// what the local player is waiting on, never the state of another peer's join.
/// Ordered as the join progresses; <see cref="Failed"/> is terminal (ADR 0012).
/// </summary>
public enum JoinPhase
{
    /// Join sent to the signal server, waiting for it to accept the room code.
    ContactingSignal,
    /// Room code accepted; SDP offer/answer in flight with the host.
    Negotiating,
    /// Offer and answer exchanged; ICE is establishing the peer connection. This is the
    /// phase that can legitimately last the full 39.5s PAC timer.
    EstablishingConnection,
    /// The local client connection reported Started.
    Connected,
    /// The attempt timed out or failed and the client was torn down.
    Failed,
}

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
    public static Action<JoinPhase> OnJoinPhaseChanged;
    public static Action OnJoinCancelled;

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
        OnJoinPhaseChanged = null;
        OnJoinCancelled = null;
        OnDebugViewOpened = null;
        OnDebugViewClosed = null;
        OnTemperatureChanged = null;
    }
}