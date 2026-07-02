using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HUD widget showing the local player's 5 active-ability slots, mirroring the
/// <see cref="DamageIndicator"/> pattern (subscribe to synced pawn state, refresh on change). Binds to the
/// local pawn's <see cref="PawnActiveAbilities"/> when it spawns (via
/// <see cref="NetworkUIEvents.OnLocalPawnSpawned"/>) and reflects the replicated ability instances: the
/// facade raises <see cref="PawnActiveAbilities.InventoryChanged"/> whenever child instances (or their
/// synced uses) change, and <see cref="PawnActiveAbilities.SelectedIndex"/> drives the highlight.
/// </summary>
public sealed class AbilityBar : MonoBehaviour
{
    [SerializeField] private Transform slotsParent;
    [SerializeField] private AbilitySlotWidget slotPrefab;

    private readonly List<AbilitySlotWidget> _slots = new();
    private readonly List<ActiveAbility> _activesBuf = new();
    private PawnActiveAbilities _abilities;

    private void Awake()
    {
        if (slotPrefab != null && slotsParent != null)
        {
            for (int i = 0; i < PawnActiveAbilities.MaxActives; i++)
                _slots.Add(Instantiate(slotPrefab, slotsParent));
        }
        Refresh();
    }

    private void OnEnable()
    {
        NetworkUIEvents.OnLocalPawnSpawned += HandleLocalPawnSpawned;
        NetworkUIEvents.OnLocalDeath += HandleLocalDeath;
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnLocalPawnSpawned -= HandleLocalPawnSpawned;
        NetworkUIEvents.OnLocalDeath -= HandleLocalDeath;
        Unbind();
    }

    private void HandleLocalPawnSpawned(Pawn pawn)
    {
        Unbind();
        _abilities = pawn != null ? pawn.GetComponent<PawnActiveAbilities>() : null;
        if (_abilities != null)
        {
            _abilities.InventoryChanged += Refresh;
            _abilities.SelectedIndex.OnChange += HandleSelectedChanged;
        }
        Refresh();
    }

    private void HandleLocalDeath()
    {
        Unbind();
        Refresh();
    }

    private void Unbind()
    {
        if (_abilities == null) return;
        _abilities.InventoryChanged -= Refresh;
        _abilities.SelectedIndex.OnChange -= HandleSelectedChanged;
        _abilities = null;
    }

    private void HandleSelectedChanged(int prev, int next, bool asServer) => Refresh();

    private void Refresh()
    {
        int count = 0;
        int selected = 0;
        if (_abilities != null)
        {
            _abilities.GetActives(_activesBuf);
            count = _activesBuf.Count;
            selected = _abilities.SelectedIndex.Value;
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            bool occupied = i < count;
            bool isSelected = occupied && i == selected;
            if (occupied)
            {
                ActiveAbility ability = _activesBuf[i];
                _slots[i].SetData(ability.Type, ability.UsesRemaining.Value, isSelected);
            }
            else
            {
                _slots[i].SetData(null, 0, false);
            }
        }
    }
}
