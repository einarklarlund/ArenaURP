using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using UnityEngine;

public class AmmoPickup : NetworkBehaviour
{
    public AmmoType AmmoType;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private float detectionRadius = 15f;
    [SerializeField] private float collectionRadius = 3f;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private LayerMask playerLayer;

    private readonly Dictionary<AmmoType, int> ammoValues = new()
    {
        { AmmoType.Bullet, 32 },
        { AmmoType.Shell, 8 },
        { AmmoType.Bolt, 7 },
        { AmmoType.Explosive, 6 },
        { AmmoType.Energy, 5 },
    };

    void Update()
    {
        if (!IsServerInitialized) return;

        // Find players within the detection radius
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, detectionRadius, Vector3.one, 0, playerLayer);
        if (hits.Length > 0)
        {
            Transform closestPlayer = GetClosestPlayer(hits);

            float distance = Vector3.Distance(transform.position, closestPlayer.position);
            
            // Move towards the torso of the closest player
            var towardsPlayer = (closestPlayer.position + Vector3.up - transform.position).normalized;
            var targetVelocity = towardsPlayer * moveSpeed;
            var deltaVelocity = targetVelocity - rb.linearVelocity;
            rb.AddForce(deltaVelocity, ForceMode.Acceleration);

            // If close enough, "collect" it
            if (distance <= collectionRadius)
            {
                Collect(closestPlayer);
            }
        }
    }

    Transform GetClosestPlayer(RaycastHit[] players)
    {
        Transform bestTarget = null;
        float closestDistanceSqr = Mathf.Infinity;
        Vector3 currentPosition = transform.position;

        foreach (var potentialTarget in players)
        {
            Vector3 directionToTarget = potentialTarget.transform.position - currentPosition;
            float dSqrToTarget = directionToTarget.sqrMagnitude;

            if (dSqrToTarget < closestDistanceSqr)
            {
                closestDistanceSqr = dSqrToTarget;
                bestTarget = potentialTarget.transform;
            }
        }

        return bestTarget;
    }

    void Collect(Transform targetPlayer)
    {
        if (targetPlayer.TryGetComponent<Pawn>(out var pawn))
        {
            pawn.Ammo.AddAmmo(AmmoType, ammoValues[AmmoType]);
            Despawn();
        }
    }
}