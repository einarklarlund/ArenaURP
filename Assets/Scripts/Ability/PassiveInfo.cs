using UnityEngine;

/// <summary>
/// Display identity of a passive KIND, authored on its child under the shared passive registry: the name and icon
/// a HUD shows for it (including a FOREIGN player's loadout). Gameplay config lives on the sibling
/// <see cref="Passive"/> applicator components; this only names the kind for the UI.
/// </summary>
public sealed class PassiveInfo : MonoBehaviour
{
    [Tooltip("Name shown in a loadout HUD (own or foreign).")]
    public string DisplayName = "Passive";

    [Tooltip("Icon shown in a loadout HUD.")]
    public Sprite Icon;
}
