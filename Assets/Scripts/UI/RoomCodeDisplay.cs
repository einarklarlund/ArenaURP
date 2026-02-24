using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomCodeDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text roomText;
    [SerializeField] private Button copyToClipboardButton;

    private void OnEnable()
    {
        roomText.text = RoomManager.CurrentRoom;
        copyToClipboardButton.onClick.AddListener(OnCopyButtonClicked);
    }

    private void OnDisable()
    {
        copyToClipboardButton.onClick.RemoveListener(OnCopyButtonClicked);
    }

    private void OnCopyButtonClicked()
    {
        GUIUtility.systemCopyBuffer = RoomManager.CurrentRoom;
    }
}