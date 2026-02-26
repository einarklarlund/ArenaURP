using TMPro;
using UnityEngine;
using System.Text.RegularExpressions;
public class LoadingView : View
{
    [SerializeField] private TMP_Text mainText;
    [SerializeField] private TMP_Text infoText;

    private void OnEnable()
    {
        // Hacky way to see if we're hosting or joining a room
        if (RoomManager.CurrentRoom == "")
        {
            mainText.text = "Creating a room...";
        }
        else
        {
            mainText.text = $"Joining room {RoomManager.CurrentRoom}...";
        }

        SignalManager.OnRoomCreated += HandleRoomCreated;
        Application.logMessageReceived += HandleLog;
        mainText.text = $"Connecting to {RoomManager.CurrentRoom}";
        infoText.text = "";
    }

    private void OnDisable()
    {
        SignalManager.OnRoomCreated -= HandleRoomCreated;
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleRoomCreated(string roomName)
    {
        mainText.text = $"Created room {roomName}, joining...";
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
        {
            infoText.color = Color.red;
            infoText.text = logString;
        }
        else if (logString.Contains("[Signal]"))
        {
            infoText.color = Color.white;
            var logLine = logString.Replace("[Signal]", "");
    		string pattern = "<.*>";
            logLine = Regex.Replace(logLine, pattern, "");
            logLine.Trim();
            logLine += "\n";
            infoText.text = logLine + infoText.text;
        }
    }
}