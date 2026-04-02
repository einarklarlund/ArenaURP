using UnityEngine;
using UnityEngine.UI;

public sealed class DebugView : View
{
    [SerializeField] private Button spawnBotButton;
    [SerializeField] private Button backButton;

    public override void Show()
    {
        var botSpawner = FindAnyObjectByType<BotSpawner>();
        bool isDuringMatch = MatchFlowManager.Instance != null
            && MatchFlowManager.Instance.State.Value == MatchState.During;

        spawnBotButton.gameObject.SetActive(isDuringMatch && botSpawner != null);
        spawnBotButton.onClick.AddListener(HandleSpawnBotClicked);
        backButton.onClick.AddListener(HandleBackClicked);

        base.Show();
    }

    public override void Hide()
    {
        spawnBotButton.onClick.RemoveListener(HandleSpawnBotClicked);
        backButton.onClick.RemoveListener(HandleBackClicked);

        base.Hide();
    }

    private void HandleSpawnBotClicked()
    {
        var botSpawner = FindAnyObjectByType<BotSpawner>();
        if (botSpawner != null)
            botSpawner.SpawnBot();
    }

    private void HandleBackClicked()
    {
        LocalUIEvents.OnDebugViewClosed?.Invoke();
    }
}
