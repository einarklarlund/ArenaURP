using FishNet.Object;
using UnityEngine;

/// <summary>
/// Collision-based ability pickup — a passive power-up or a Mario-Kart-style active item box. Collision
/// is SERVER-AUTHORITATIVE and CAST-BASED, exactly like projectile collision (see PawnProjectile): the
/// server ticks an overlap query each network tick and grants to the first pawn in range, then despawns.
/// We do NOT use <c>OnTriggerEnter</c> — that fires on every peer off client-side physics; driving detection
/// from the server tick keeps the authority on the server and off the clients.
///
/// A pickup grants EITHER a passive KIND or an active item, never both:
/// - PASSIVE: set <see cref="passiveKindName"/> to a passive KIND's child name under the shared
///   <see cref="PassiveRegistry"/>; the grant is a reference add into the pawn's owner's
///   <see cref="PlayerPassiveLoadout"/> (ownership on the player). A pawn that already owns the kind leaves the box.
/// - ACTIVE: set <see cref="ability"/> to an <see cref="ActiveAbilityType"/>; a full (5/5) inventory leaves the box.
/// </summary>
public sealed class AbilityPickup : NetworkBehaviour
{
    [Tooltip("Passive pickup: the KIND to grant, named by its child GameObject under the PassiveRegistry. " +
             "Leave empty for an active-item pickup.")]
    [SerializeField] private string passiveKindName;

    [Tooltip("Active-item pickup: the active ability granted to whoever overlaps. Leave empty for a passive pickup.")]
    [SerializeField] private ActiveAbilityType ability;

    [Tooltip("Radius of the server-side overlap query that detects pawns.")]
    [SerializeField] private float detectionRadius = 1.5f;

    private readonly Collider[] _hits = new Collider[8];

    private bool GrantsPassive => !string.IsNullOrEmpty(passiveKindName);

    public override void OnStartServer()
    {
        base.OnStartServer();
        TimeManager.OnTick += ServerTick;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (TimeManager != null)
            TimeManager.OnTick -= ServerTick;
    }

    private void ServerTick()
    {
        if (!GrantsPassive && ability == null)
            return;

        // Same approach as PawnProjectile's server collision: a physics overlap resolved only on the
        // server, then map colliders to the gameplay entity via GetComponentInParent.
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, detectionRadius, _hits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var pawn = _hits[i].GetComponentInParent<Pawn>();
            if (pawn == null)
                continue;

            bool granted = GrantsPassive ? TryGrantPassive(pawn) : TryGrantActive(pawn);
            if (granted)
            {
                Despawn();
                return;
            }
        }
    }

    // Passive: resolve the KIND to its shared registry child and add a reference to the owner's loadout (ownership
    // on the player). False (leave the box) when the pawn has no owner/loadout, the kind name is unknown, or the
    // pawn already owns that kind.
    private bool TryGrantPassive(Pawn pawn)
    {
        var owner = pawn.ControllingPlayer.Value;
        var loadout = owner != null ? owner.PassiveLoadout : null;
        if (loadout == null)
            return false;
        var kind = PassiveRegistry.Resolve(passiveKindName); // warns on an unknown name
        return kind != null && loadout.Grant(kind);
    }

    // Active: false (leave the box) when the pawn has no active inventory or its 5 slots are full.
    private bool TryGrantActive(Pawn pawn)
        => pawn.TryGetComponent<PawnActiveAbilities>(out var actives) && actives.AddActive(ability);
}
