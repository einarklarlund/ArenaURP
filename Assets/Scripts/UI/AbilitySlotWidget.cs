using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One slot in the active-ability HUD bar: an icon, a remaining-uses count, and a selection highlight.
/// Pure view — <see cref="AbilityBar"/> drives it from the local pawn's synced ability state.
/// </summary>
public sealed class AbilitySlotWidget : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text usesLabel;
    [SerializeField] private GameObject highlight;

    /// <summary>Show an occupied slot (null <paramref name="type"/> renders as empty).</summary>
    public void SetData(AbilityType type, int usesRemaining, bool selected)
    {
        bool filled = type != null;
        if (iconImage != null)
        {
            iconImage.sprite = filled ? type.Icon : null;
            iconImage.enabled = filled && type.Icon != null;
        }
        if (usesLabel != null)
            usesLabel.text = filled ? usesRemaining.ToString() : string.Empty;
        if (highlight != null)
            highlight.SetActive(selected);
    }
}
