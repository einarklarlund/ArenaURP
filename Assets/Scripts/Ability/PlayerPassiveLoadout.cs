using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// The authoritative PASSIVE-ability ownership roster, on the <see cref="NetworkPlayer"/> object (ownership on the
/// player, execution on the pawn). It holds a <see cref="SyncList{T}"/> of REFERENCES to the shared passive-kind
/// children under the passive registry — native FishNet refs, resolved on every peer like <c>SyncVar&lt;Pawn&gt;</c>,
/// so there is no per-player copy and no id→asset indirection. Because it lives on the persistent player and
/// replicates to all peers, it survives respawn AND is readable for a FOREIGN player (an enemy-loadout HUD).
///
/// Grant = add a ref; remove = remove; steal = move a ref across two loadouts — no spawn/despawn per grant.
/// Applying is TARGET-driven: a target (pawn, projectile) calls <see cref="ApplyOwnedTo"/> at its own lifecycle
/// moment, which runs each owned kind's <see cref="Passive"/> applicators that <see cref="Passive.CanApplyTo"/>
/// the target.
/// </summary>
public sealed class PlayerPassiveLoadout : NetworkBehaviour
{
    [Tooltip("Per-GAME persistence: the loadout survives death/respawn. Uncheck for per-life. The single home for " +
             "the persistence policy (game-mode-set); never re-answered per passive.")]
    [SerializeField] private bool persistAcrossDeath = true;

    /// <summary>The owned passive KINDS, as references to their shared registry children. Foreign-readable.</summary>
    public readonly SyncList<NetworkObject> Owned = new();

    /// <summary>Raised on ANY peer whenever the owned set changes.</summary>
    public event System.Action Changed;

    // Server-only-ish scratch (Unity main thread, single-threaded); apply runs per-target, never re-entrant.
    private static readonly List<Passive> _applicators = new();

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Owned.OnChange += OnOwnedChanged;
        Changed?.Invoke(); // fold in any already-replicated refs (late join / initial state)
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        Owned.OnChange -= OnOwnedChanged;
    }

    private void OnOwnedChanged(SyncListOperation op, int index, NetworkObject oldItem, NetworkObject newItem, bool asServer)
    {
        // A mid-life grant (genuine Add) applies the new kind to the owner's current pawn on every peer. Initial
        // replication arrives as a Complete op, not per-item Adds, so this doesn't double-apply with the pawn's
        // own spawn-time apply. A removed (stolen) kind's effect is NOT torn down here — it lingers until the
        // pawn respawns and re-derives (pooling/removal cleanup is deferred).
        if (op == SyncListOperation.Add && newItem != null)
        {
            GameObject pawn = OwnerPawn();
            if (pawn != null)
                ApplyKindTo(newItem, pawn);
        }
        Changed?.Invoke();
    }

    private GameObject OwnerPawn()
    {
        var np = GetComponent<NetworkPlayer>();
        var pawn = np != null ? np.ControlledPawn.Value : null;
        return pawn != null ? pawn.gameObject : null;
    }

    // ---- server: acquisition / removal / respawn policy ----

    /// <summary>Server: grant a passive KIND (a registry child). A no-op if already owned (shared refs don't stack).</summary>
    [Server]
    public void Grant(NetworkObject passiveKind)
    {
        if (passiveKind != null && !Owned.Contains(passiveKind))
            Owned.Add(passiveKind);
    }

    /// <summary>Server: remove one owned kind. Returns false if it was not owned (a hit resolving twice must not
    /// double-remove).</summary>
    [Server]
    public bool Remove(NetworkObject passiveKind) => passiveKind != null && Owned.Remove(passiveKind);

    /// <summary>Server: apply the respawn persistence policy — per-game (default) keeps the loadout, per-life clears it.</summary>
    [Server]
    public void ResetForRespawn()
    {
        if (!persistAcrossDeath)
            Owned.Clear();
    }

    // ---- target-driven application ----

    /// <summary>Apply every owned passive that can act on <paramref name="target"/>. The target calls this at its
    /// own moment (a pawn on spawn/grant, a projectile on spawn).</summary>
    public void ApplyOwnedTo(GameObject target)
    {
        for (int i = 0; i < Owned.Count; i++)
            ApplyKindTo(Owned[i], target);
    }

    /// <summary>Apply one kind's applicators to <paramref name="target"/> (the newly-granted-passive path).</summary>
    public static void ApplyKindTo(NetworkObject passiveKind, GameObject target)
    {
        if (passiveKind == null || target == null)
            return;
        passiveKind.GetComponents(_applicators); // GetComponents(List) clears the buffer itself
        for (int i = 0; i < _applicators.Count; i++)
            _applicators[i].TryApply(target);
    }
}
