using FishNet.Object;
using UnityEngine;

/// <summary>
/// The pawn-side loadout script (ADR-pending: shared-registry passive model): when the owner (and its
/// <see cref="PlayerPassiveLoadout"/>) resolve at/after this pawn's spawn, it applies the whole current loadout to
/// this pawn ONCE — each owned passive's <see cref="Passive.TryApply"/> runs against this pawn's hierarchy.
///
/// No clear/reapply/teardown (pooling deferred): a mid-life grant is applied to the current pawn by
/// <see cref="PlayerPassiveLoadout"/> itself; a fresh pawn re-derives the whole loadout here; a removed (stolen)
/// passive's pawn effect lingers until respawn.
/// </summary>
public sealed class PawnPassiveLoadout : NetworkBehaviour
{
    private Pawn _pawn;
    private bool _applied;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _pawn = GetComponent<Pawn>();
        if (_pawn == null)
            _pawn = GetComponentInParent<Pawn>();
        if (_pawn == null)
            return;
        _pawn.ControllingPlayer.OnChange += OnOwnerChanged;
        ApplyOwned(_pawn.ControllingPlayer.Value);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (_pawn != null)
            _pawn.ControllingPlayer.OnChange -= OnOwnerChanged;
    }

    private void OnOwnerChanged(NetworkPlayer prev, NetworkPlayer next, bool asServer) => ApplyOwned(next);

    private void ApplyOwned(NetworkPlayer owner)
    {
        if (_applied || owner == null)
            return;
        var loadout = owner.PassiveLoadout;
        if (loadout == null)
            return;
        loadout.ApplyOwnedTo(gameObject);
        _applied = true;
    }
}
