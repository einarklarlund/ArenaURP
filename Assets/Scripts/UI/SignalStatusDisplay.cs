using JamesFrowen.SimpleWeb;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SignalStatusDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button retryButton;

    public void Initialize(ClientState state)
    {
        HandleClientStateChanged(state);
    }

    private void Awake()
    {
        SignalManager.OnClientStateChanged += HandleClientStateChanged;
        retryButton.onClick.AddListener(HandleRetryClicked);
        HideRetryButton();
    }

    private void OnDestroy()
    {
        SignalManager.OnClientStateChanged -= HandleClientStateChanged;
        retryButton.onClick.RemoveListener(HandleRetryClicked);
    }

    private void HandleClientStateChanged(ClientState state)
    {
        statusText.text = state.ToString();
        if (state == ClientState.NotConnected)
        {
            ShowRetryButton();
        }
        else
        {
            HideRetryButton();
        }
    }

    private void HandleRetryClicked()
    {
        SignalManager.Instance.StartSignalClient();
    }

    private void ShowRetryButton()
    {
        retryButton.gameObject.SetActive(true);
    }

    private void HideRetryButton()
    {
        retryButton.gameObject.SetActive(false);        
    }
}