using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Server-authoritative health for non-Pawn entities (bullets, enemies, props).
/// Wire HurtBox.OnHurt → Health.TakeDamage in the inspector.
///
/// Mirrors Pawn's predictive client + authoritative server pattern: clients invoke
/// OnDamage/OnDeath immediately for responsive VFX, and the server is the source of
/// truth for the synced health value.
/// </summary>
public class Health : NetworkBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 1;
    [SerializeField] private bool despawnOnDeath = true;

    public readonly SyncVar<int> CurrentHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));
    public readonly SyncVar<int> MaxHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));

    [SerializeField] private DamageInfoEvent onServerDamage = new();
    [SerializeField] private DamageInfoEvent onServerDeath = new();

    public override void OnStartServer()
    {
        base.OnStartServer();
        MaxHealth.Value = maxHealth;
        CurrentHealth.Value = maxHealth;
    }

    public void TakeDamage(DamageInfo info)
    {
        if (!IsServerInitialized) return;

        if (CurrentHealth.Value <= 0) return;

        onServerDamage?.Invoke(info);

        CurrentHealth.Value -= info.Amount;

        if (CurrentHealth.Value <= 0)
        {
            onServerDeath?.Invoke(info);

            if (despawnOnDeath)
                Despawn(DespawnType.Pool);
        }
    }
}
