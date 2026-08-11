using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The join/host loading screen. Shows which phase the local join attempt is in and offers a way
/// out of one that is going nowhere (ADR 0012).
/// </summary>
public class LoadingView : View
{
    [SerializeField] private TMP_Text mainText;
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private Button cancelButton;

    private void OnEnable()
    {
        bool hostingRatherThanJoining = RoomManager.CurrentRoom == "";

        mainText.text = hostingRatherThanJoining
            ? "Creating a room..."
            : $"Joining room {RoomManager.CurrentRoom}...";

        SignalManager.OnRoomCreated += HandleRoomCreated;
        LocalUIEvents.OnJoinPhaseChanged += HandleJoinPhaseChanged;

        if (cancelButton != null)
            cancelButton.onClick.AddListener(HandleCancelClicked);
        else
            Debug.LogWarning("[LoadingView] No cancel button assigned; a stuck join cannot be cancelled from this screen.");

        infoText.color = Color.white;
        infoText.text = "";
    }

    private void OnDisable()
    {
        SignalManager.OnRoomCreated -= HandleRoomCreated;
        LocalUIEvents.OnJoinPhaseChanged -= HandleJoinPhaseChanged;

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(HandleCancelClicked);
    }

    private void HandleRoomCreated(string roomName)
    {
        mainText.text = $"Created room {roomName}, joining...";
    }

    private void HandleJoinPhaseChanged(JoinPhase phase)
    {
        infoText.color = phase == JoinPhase.Failed ? Color.red : Color.white;
        infoText.text = PhaseText(phase);
    }

    private void HandleCancelClicked()
    {
        if (RoomManager.Instance == null)
            return;

        RoomManager.Instance.CancelJoin();
    }

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
