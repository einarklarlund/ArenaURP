using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RoomEntryUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI roomCodeText;
    [SerializeField] private Button joinButton;
    
    private string roomCode;

    /// <summary>
    /// Called by RoomBrowserManager when the list is populated.
    /// </summary>
    public void Setup(RoomEntry room)
    {
        roomCode = room.id;
        
        // UI Display - Upper case usually looks better for room codes
        if (roomCodeText != null)
            roomCodeText.text = $"Room: {room.id.ToUpper()}";

        // Clean up any old listeners to prevent double-joining or memory leaks
        joinButton.onClick.RemoveAllListeners();
        
        // Hook up the button to your existing RoomManager logic
        joinButton.onClick.AddListener(HandleJoinRequest);
    }

    private void HandleJoinRequest()
    {
        if (string.IsNullOrEmpty(roomCode)) return;

        joinButton.interactable = false;
        RoomManager.Instance.JoinRoom(roomCode);
    }
}