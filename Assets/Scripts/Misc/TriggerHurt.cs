using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TriggerHurt : NetworkBehaviour
{
    [Header("Boxcast Settings")]
    [SerializeField] private int damage;

    private void OnTriggerEnter(Collider other)
    {
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