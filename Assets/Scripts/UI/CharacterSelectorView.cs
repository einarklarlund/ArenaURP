using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The UI that lets the local player choose which character to play as.
/// Responsibilities:
/// - Forwards the chosen character (a Pawn prefab) to the local Player script.
/// </summary>
public sealed class CharacterSelectorView : View
{
    [Header("First Character")]
    [SerializeField] private Button firstCharacterButton;
    [SerializeField] private Pawn firstCharacter;

    [Header("Second Character")]
    [SerializeField] private Button secondCharacterButton;
    [SerializeField] private Pawn secondCharacter;

    [Header("Controls")]
    [SerializeField] private Button closeButton;

    public override void Show()
    {
        firstCharacterButton.onClick.AddListener(OnFirstCharacterClicked);
        secondCharacterButton.onClick.AddListener(OnSecondCharacterClicked);
        closeButton.onClick.AddListener(OnCloseButtonClicked);

        base.Show();
    }

    public override void Hide()
    {
        firstCharacterButton.onClick.RemoveListener(OnFirstCharacterClicked);
        secondCharacterButton.onClick.RemoveListener(OnSecondCharacterClicked);
        closeButton.onClick.RemoveListener(OnCloseButtonClicked);

        base.Hide();
    }

    private void OnFirstCharacterClicked() => SelectCharacter(firstCharacter);

    private void OnSecondCharacterClicked() => SelectCharacter(secondCharacter);

    private void SelectCharacter(Pawn character)
    {
        // The local player doesn't exist until we've connected and spawned.
        if (NetworkPlayer.LocalInstance == null) return;

        NetworkPlayer.LocalInstance.ServerSetCharacter(character);
    }

    private void OnCloseButtonClicked()
    {
        LocalUIEvents.OnCharacterSelectClosed?.Invoke();
    }
}