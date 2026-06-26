using System.Collections.Generic;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// HUD widget showing the local player's 5 active-ability slots, mirroring the
/// <see cref="DamageIndicator"/> pattern (subscribe to synced pawn state, refresh on change). Binds to
/// the local pawn's <see cref="PawnAbilities"/> when it spawns (via <see cref="NetworkUIEvents.OnLocalPawnSpawned"/>)
/// and reflects the <see cref="PawnAbilities.Actives"/> list and <see cref="PawnAbilities.SelectedIndex"/>.
/// </summary>
public sealed class AbilityBar : MonoBehaviour
{
    [SerializeField] private Transform slotsParent;
    [SerializeField] private AbilitySlotWidget slotPrefab;

    private readonly List<AbilitySlotWidget> _slots = new();
    private PawnAbilities _abilities;

    private void Awake()
    {
        if (slotPrefab != null && slotsParent != null)
        {
            for (int i = 0; i < PawnAbilities.MaxActives; i++)
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
        _abilities = pawn != null ? pawn.GetComponent<PawnAbilities>() : null;
        if (_abilities != null)
        {
            _abilities.Actives.OnChange += HandleActivesChanged;
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
        _abilities.Actives.OnChange -= HandleActivesChanged;
        _abilities.SelectedIndex.OnChange -= HandleSelectedChanged;
        _abilities = null;
    }

    private void HandleActivesChanged(SyncListOperation op, int index, ActiveSlot oldItem, ActiveSlot newItem, bool asServer)
        => Refresh();

    private void HandleSelectedChanged(int prev, int next, bool asServer) => Refresh();

    private void Refresh()
    {
        int count = _abilities != null ? _abilities.Actives.Count : 0;
        int selected = _abilities != null ? _abilities.SelectedIndex.Value : 0;

        for (int i = 0; i < _slots.Count; i++)
        {
            bool occupied = i < count;
            bool isSelected = occupied && i == selected;
            if (occupied)
            {
                ActiveSlot slot = _abilities.Actives[i];
                _slots[i].SetData(slot.Type, slot.UsesRemaining, isSelected);
            }
            else
            {
                _slots[i].SetData(null, 0, false);
            }
        }
    }
}
