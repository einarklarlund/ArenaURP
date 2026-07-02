using Arena.Health;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Events;
using HealthCoreState = Arena.Health.HealthState;

/// <summary>
/// Server-authoritative health for non-Pawn entities (bullets, enemies, props) — the generic Health
/// system's shell. Current/Max HP are SyncVars; the arithmetic is the pure
/// <see cref="HealthProcessor"/> (EditMode-tested in Arena.Health.Core). The max is AUTHORED ON THE PREFAB
/// (<c>maxHealth</c>); <see cref="OnStartServer"/> re-seeds current/max from it each (pooled) spawn.
/// Projectiles drain it via <see cref="ServerLose"/> and let their ServerCollision drive despawn (so set
/// despawnOnDeath = false on a projectile); other entities can wire HurtBox.OnHurt →
/// <see cref="TakeDamage"/> and self-despawn.
/// </summary>
public class Health : NetworkBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 1;
    [SerializeField] private bool despawnOnDeath = true;

    public readonly SyncVar<int> CurrentHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));
    public readonly SyncVar<int> MaxHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));

    [SerializeField] private DamageInfoEvent onServerDamage = new();
    [SerializeField] private DamageInfoEvent onServerDeath = new();

    public bool IsDead => CurrentHealth.Value <= 0;

    public override void OnStartServer()
    {
        base.OnStartServer();
        MaxHealth.Value = maxHealth;
        CurrentHealth.Value = maxHealth;
    }

    /// <summary>Server: (re)seed max + current HP at runtime (e.g. a buff raising an entity's max).</summary>
    [Server]
    public void ServerInitMax(int max)
    {
        maxHealth = Mathf.Max(1, max);
        MaxHealth.Value = maxHealth;
        CurrentHealth.Value = maxHealth;
    }

    public void TakeDamage(DamageInfo info)
    {
        if (!IsServerInitialized) return;
        if (CurrentHealth.Value <= 0) return;

        onServerDamage?.Invoke(info);
        ApplyLoss(info.Amount);

        if (CurrentHealth.Value <= 0)
        {
            onServerDeath?.Invoke(info);
            if (despawnOnDeath)
                Despawn(DespawnType.Pool);
        }
    }

    /// <summary>Server: drain HP without the damage-event / despawn path (e.g. projectile self-damage).</summary>
    [Server]
    public void ServerLose(int amount) => ApplyLoss(amount);

    private void ApplyLoss(int amount)
    {
        var state = new HealthCoreState { Hp = CurrentHealth.Value, MaxHp = MaxHealth.Value };
        CurrentHealth.Value = HealthProcessor.ApplyDamage(state, amount).Hp;
    }
}
