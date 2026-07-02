using FishNet.Object;
using UnityEngine;

/// <summary>
/// Collision-based ability pickup — a passive power-up or a Mario-Kart-style active item box. Collision
/// is SERVER-AUTHORITATIVE and CAST-BASED, exactly like projectile collision (see PawnProjectile): the
/// server ticks an overlap query each network tick and grants <see cref="ability"/> to the first pawn in
/// range, then despawns. We do NOT use <c>OnTriggerEnter</c> — that fires on every peer off client-side
/// physics; driving detection from the server tick keeps the authority on the server and off the clients.
///
/// Handles both kinds — a passive routes to <see cref="PawnPassiveAbilities.AddPassive"/>, an active to
/// <see cref="PawnActiveAbilities.AddActive"/> — and a full (5/5) active inventory leaves the box in place
/// for someone else.
/// </summary>
public sealed class AbilityPickup : NetworkBehaviour
{
    [Tooltip("The ability granted to whoever overlaps this pickup.")]
    [SerializeField] private AbilityType ability;

    [Tooltip("Radius of the server-side overlap query that detects pawns.")]
    [SerializeField] private float detectionRadius = 1.5f;

    private readonly Collider[] _hits = new Collider[8];

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
        if (ability == null)
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

            bool granted;
            if (ability is PassiveAbilityType passive && pawn.TryGetComponent<PawnPassiveAbilities>(out var passives))
            {
                passives.AddPassive(passive);
                granted = true;
            }
            else if (ability is ActiveAbilityType active && pawn.TryGetComponent<PawnActiveAbilities>(out var actives))
            {
                granted = actives.AddActive(active); // false when the 5 slots are full -> leave the box
            }
            else
            {
                granted = false;
            }

            if (granted)
            {
                Despawn();
                return;
            }
        }
    }
}
