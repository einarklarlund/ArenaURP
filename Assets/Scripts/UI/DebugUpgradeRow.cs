using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One upgrade in the debug menu's list: its name, description and stacks, and a button that grants it.</summary>
public sealed class DebugUpgradeRow : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button grantButton;

    private Upgrade _upgrade;

    public void Bind(int id, Upgrade upgrade, Action<int> onGrant)
    {
        _upgrade = upgrade;
        grantButton.onClick.AddListener(() => onGrant(id));
    }

    public void Refresh(int stacks)
    {
        int max = _upgrade.MaxStacks;
        label.text = $"<b>{_upgrade.DisplayName}</b>  x{stacks}/{(max > 0 ? max.ToString() : "∞")}\n" +
            $"<size=75%>{_upgrade.Description}</size>";
        grantButton.interactable = max <= 0 || stacks < max;
    }
}
