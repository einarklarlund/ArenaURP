using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TriggerHurt : NetworkBehaviour
{
    [Header("Boxcast Settings")]
    [SerializeField] private int damage;

    private void OnTriggerEnter(Collider other)
    {
        // Hazard damage is server-authoritative; clients react via replication. Without this guard every peer's
        // local physics would call the all-peer predictive Health.TakeDamage — spurious client hit VFX, and on a
        // host the death/respawn chain runs twice.
        if (!IsServerStarted) return;

        if (other.TryGetComponent<IDamageable>(out var damageable))
        {
            DamageInfo info = new()
            {
                Amount = damage,
                Attacker = null,
                HitPoint = other.transform.position,
                Type = DamageType.Environment
            };
            damageable.TakeDamage(info);
        }
    }
}