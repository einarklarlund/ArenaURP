using TMPro;
using UnityEngine;
using System.Text.RegularExpressions;

public class WaitScreenInfoDisplay : MonoBehaviour
{
    public TMP_Text infoText;

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
        infoText.text = "";
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
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