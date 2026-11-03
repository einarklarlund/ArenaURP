using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The join/host loading screen. Shows which phase the local join attempt is in and offers a way
/// out of one that is going nowhere (ADR 0012). While a level loads (<see cref="LevelSync.Loading"/>)
/// it shows the generation stage running instead, and the cancel button is hidden.
/// </summary>
public class LoadingView : View
{
    [SerializeField] private TMP_Text mainText;
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private Button cancelButton;

    /// <summary>The LevelSync whose progress this view is listening to, if any.</summary>
    private LevelSync levelSync;

    private void OnEnable()
    {
        bool hostingRatherThanJoining = RoomManager.CurrentRoom == "";

        mainText.text = hostingRatherThanJoining
            ? "Creating a room..."
            : $"Joining room {RoomManager.CurrentRoom}...";

        SignalManager.OnRoomCreated += HandleRoomCreated;
        LocalUIEvents.OnJoinPhaseChanged += HandleJoinPhaseChanged;
        LocalUIEvents.OnLevelLoadStarted += HandleLevelLoadStarted;

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(HandleCancelClicked);
            cancelButton.gameObject.SetActive(true);
        }
        else
            Debug.LogWarning("[LoadingView] No cancel button assigned; a stuck join cannot be cancelled from this screen.");

        infoText.color = Color.white;
        infoText.text = "";

        if (LevelSync.Instance != null && LevelSync.Instance.Loading)
            HandleLevelLoadStarted();
    }

    private void OnDisable()
    {
        SignalManager.OnRoomCreated -= HandleRoomCreated;
        LocalUIEvents.OnJoinPhaseChanged -= HandleJoinPhaseChanged;
        LocalUIEvents.OnLevelLoadStarted -= HandleLevelLoadStarted;
        ListenTo(null);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(HandleCancelClicked);
    }

    private void HandleRoomCreated(string roomName)
    {
        mainText.text = $"Created room {roomName}, joining...";
    }

    private void HandleJoinPhaseChanged(JoinPhase phase)
    {
        // Once a level is loading, its stages say more than "Connected." does.
        if (levelSync != null && phase != JoinPhase.Failed)
            return;

        infoText.color = phase == JoinPhase.Failed ? Color.red : Color.white;
        infoText.text = PhaseText(phase);
    }

    private void HandleLevelLoadStarted()
    {
        ListenTo(LevelSync.Instance);
        if (levelSync == null)
            return;

        mainText.text = "Loading level...";
        if (cancelButton != null)
            cancelButton.gameObject.SetActive(false);
        HandleLoadProgress(levelSync.LoadStatus);
    }

    private void HandleLoadProgress(LevelLoadStatus status)
    {
        infoText.color = Color.white;
        infoText.text = ProgressText(status);
    }

    private void ListenTo(LevelSync sync)
    {
        if (levelSync != null)
            levelSync.OnLoadProgress -= HandleLoadProgress;
        levelSync = sync;
        if (levelSync != null)
            levelSync.OnLoadProgress += HandleLoadProgress;
    }

    private void HandleCancelClicked()
    {
        if (RoomManager.Instance == null)
            return;

        RoomManager.Instance.CancelJoin();
    }

    private static string ProgressText(LevelLoadStatus status) => status.Step switch
    {
        LevelLoadStatus.Phase.Generating => status.Stage == null
            ? "Generating the level..."
            : $"Generating the level: {status.Stage}" +
              (status.Attempt > 1 ? $" (attempt {status.Attempt} of {status.Attempts})" : ""),
        LevelLoadStatus.Phase.Downloading => status.ChunkCount > 0
            ? $"Downloading the level from the host ({status.Chunks} of {status.ChunkCount})"
            : "Downloading the level from the host...",
        LevelLoadStatus.Phase.Building => "Building the level...",
    };

    // Deliberately a switch expression with NO default arm, matching UIManager: a new JoinPhase
    // then emits CS8509 and throws at runtime rather than silently showing nothing.
    private static string PhaseText(JoinPhase phase) => phase switch
    {
        JoinPhase.ContactingSignal       => "Contacting the signal server...",
        JoinPhase.Negotiating            => "Negotiating with the host...",
        JoinPhase.EstablishingConnection => "Establishing the connection - this can take up to 40 seconds on a slow network.",
        JoinPhase.Connected              => "Connected.",
        JoinPhase.Failed                 => "Could not connect. Cancel to return to the menu.",
    };
}
