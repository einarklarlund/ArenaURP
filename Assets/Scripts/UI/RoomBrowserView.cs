using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class RoomBrowserView : View
{
    [Header("Server Settings")]
    [SerializeField] private string mainServerUrl = "http://localhost:3000/Servers";
    
    [Header("UI References")]
    [SerializeField] private RoomEntryUI roomEntryPrefab;
    [Tooltip("This should be the 'Content' object inside your ScrollView Viewport")]
    [SerializeField] private Transform scrollContentContainer;
    [SerializeField] private Button closeBrowserButton;
    [SerializeField] private TMP_Text errorText;

    private void Start()
    {
        closeBrowserButton.onClick.AddListener(OnCloseClicked);
    }

    private void OnDestroy()
    {
        closeBrowserButton.onClick.RemoveListener(OnCloseClicked);
    }

    private void OnCloseClicked()
    {
        LocalUIEvents.OnRoomBrowserClosed?.Invoke();
    }

    public override void Show()
    {
        base.Show();
        RefreshRooms();
    }

    private void RefreshRooms()
    {
        errorText.text = "";
        StopAllCoroutines();
        StartCoroutine(GetRoomsRoutine());
    }

    private IEnumerator GetRoomsRoutine()
    {
        if (Debug.isDebugBuild || Application.isEditor)
        {
            mainServerUrl = "http://localhost:3000/Servers";
        }
        using (UnityWebRequest webRequest = UnityWebRequest.Get(mainServerUrl))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string json = webRequest.downloadHandler.text;
                    RoomListResponse response = JsonUtility.FromJson<RoomListResponse>(json);
                    PopulateList(response.rooms);
                }
                catch(Exception e)
                {
                    errorText.text = e.Message;
                }                
            }
            else
            {
                errorText.text = $"Fetch failed: {webRequest.error}";
            }
        }
    }

    private void PopulateList(RoomEntry[] rooms)
    {
        // Clear existing entries
        foreach (Transform child in scrollContentContainer) {
            Destroy(child.gameObject);
        }

        if (rooms == null || rooms.Length == 0)
        {
            errorText.text = "No active rooms found.";
            return;
        }

        foreach (var room in rooms)
        {
            RoomEntryUI entry = Instantiate(roomEntryPrefab, scrollContentContainer);
            entry.Setup(room);
        }
        
        // Helps if the ScrollRect doesn't realize the content size changed immediately
        Canvas.ForceUpdateCanvases();
    }
}