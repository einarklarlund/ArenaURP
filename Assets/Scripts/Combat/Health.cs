using System;
using Arena.Health;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Events;
using HealthCoreState = Arena.Health.HealthState;

/// <summary>
/// The ONE health system for every damageable entity — pawns, bullets, props. Current/Max HP are SyncVars; the
/// arithmetic is the pure <see cref="HealthProcessor"/> (EditMode-tested in Arena.Health.Core). This is the sole
/// <see cref="IDamageable"/>: hit operations and HurtBoxes deal damage here, and everything that cares about a
/// health value reads/subscribes to it (a pawn's <c>Pawn</c> forwards to it so its own consumers don't change).
///
/// Two notification surfaces, by audience:
///  - C# events <see cref="OnDamageTaken"/> / <see cref="OnDeath"/> fire on EVERY peer, BEFORE the SyncVar
///    settles, so gameplay reactions (knockback, VFX, the pawn death chain) can react predictively — the
///    behaviour the pawn health path used to own. <see cref="OnDamageTaken"/> is the predictive hook; on a client
///    <see cref="OnDeath"/> is raised speculatively when the hit is predicted-lethal (subject to rollback).
///  - Serialized <see cref="onServerDamage"/> / <see cref="onServerDeath"/> UnityEvents fire SERVER-ONLY, for
///    inspector-wired destructibles.
///
/// The max is AUTHORED ON THE PREFAB (<c>maxHealth</c>); <see cref="OnStartServer"/> re-seeds current/max from it
/// each (pooled) spawn. Set <c>despawnOnDeath = false</c> when something else owns the despawn (a pawn — the
/// PawnManager despawns it; a projectile — its ServerCollision drives despawn off <see cref="IsDead"/>).
/// Projectiles drain durability via <see cref="ServerLose"/> (no events, no despawn).
/// </summary>
public class Health : NetworkBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 1;
    [Tooltip("Despawn this object when it dies. FALSE when something else owns the despawn (pawns: PawnManager; projectiles: ServerCollision).")]
    [SerializeField] private bool despawnOnDeath = true;

    public readonly SyncVar<int> CurrentHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));
    public readonly SyncVar<int> MaxHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));

    /// <summary>Fires on EVERY peer the instant damage is applied here, BEFORE the SyncVar settles — the
    /// predictive hook knockback / VFX rely on.</summary>
    public event Action<DamageInfo> OnDamageTaken;

    /// <summary>Fires when this dies. Server: on the authoritative HP&lt;=0 transition. Client: speculatively when a
    /// hit is predicted-lethal (subject to rollback — a UI consumer must tolerate that).</summary>
    public event Action<DamageInfo> OnDeath;

    [SerializeField] private DamageInfoEvent onServerDamage = new();
    [SerializeField] private DamageInfoEvent onServerDeath = new();

    public bool IsDead => CurrentHealth.Value <= 0;

    // Per-spawn on-spawn buff layer over the authored max (SpawnBuffReceiver → BuffMaxHealth). Kept SEPARATE
    // from the baseline `maxHealth` field so a pooled reuse never inherits a previous spawn's buff; reset to identity
    // in ResetState. Server-only knob — the SyncVars are ServerOnly, so clients self-heal from the seeded values.
    private float _maxMultiplier = 1f;

    public override void OnStartServer()
    {
        base.OnStartServer();
        SeedFromMax();
    }

    // Effective max = authored baseline × per-spawn buff layer (rounded, floored at 1). Seeds both SyncVars — on
    // spawn current == max — without mutating the authored `maxHealth` field.
    private void SeedFromMax()
    {
        int max = Mathf.Max(1, Mathf.RoundToInt(maxHealth * _maxMultiplier));
        MaxHealth.Value = max;
        CurrentHealth.Value = max;
    }

    /// <summary>
    /// Apply an on-spawn max-health buff as a multiplier over the authored baseline. ORDER-INDEPENDENT (house rule
    /// for shared init across siblings with no guaranteed OnStartNetwork order), mirroring
    /// <see cref="NetworkedMotion.SetSpeedMultiplier"/>: safe to call before <see cref="OnStartServer"/> (the
    /// multiplier is stored and folded in when it seeds) or after (an already-seeded max is recomputed in place).
    ///
    /// Server-only effect: the SyncVars are ServerOnly, so this writes nothing on a client (the buffed values ride
    /// the spawn message and land via SyncVar replication — a client that missed the just-granted passive still gets
    /// the server-authoritative max). Per-spawn buff layer, reset to identity in <see cref="ResetState"/>.
    /// </summary>
    public void SetMaxHealthMultiplier(float multiplier)
    {
        _maxMultiplier = multiplier;
        if (IsServerInitialized)
            SeedFromMax();
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _maxMultiplier = 1f;   // pooled reuse starts unbuffed; OnStartServer re-seeds from the baseline max
    }

    /// <summary>Server: (re)seed max + current HP at runtime (e.g. a buff raising an entity's max).</summary>
    [Server]
    public void ServerInitMax(int max)
    {
        maxHealth = Mathf.Max(1, max);
        MaxHealth.Value = maxHealth;
        CurrentHealth.Value = maxHealth;
    }

    /// <summary>
    /// Apply damage. Fires the predictive all-peer <see cref="OnDamageTaken"/> before the SyncVar settles, raises
    /// a speculative <see cref="OnDeath"/> on a client that predicts the hit is lethal, then — on the server —
    /// applies the loss and, on death, raises the authoritative <see cref="OnDeath"/> / <see cref="onServerDeath"/>
    /// and optionally despawns. Mirrors the structure the bespoke pawn health path used to have, so the same
    /// reactions fire in the same order.
    /// </summary>
    public void TakeDamage(DamageInfo info)
    {
        if (CurrentHealth.Value <= 0) return;

        // Predictive: every peer reacts before the SyncVar is updated (knockback/VFX/colour flash).
        OnDamageTaken?.Invoke(info);

        // Client-side predictive death (subject to rollback; UI consumers must tolerate it).
        if (IsClientInitialized && CurrentHealth.Value - info.Amount <= 0)
            OnDeath?.Invoke(info);

        if (!IsServerInitialized)
            return;

        onServerDamage?.Invoke(info);
        ApplyLoss(info.Amount);

        if (CurrentHealth.Value <= 0)
        {
            OnDeath?.Invoke(info);
            onServerDeath?.Invoke(info);
            if (despawnOnDeath)
                Despawn(DespawnType.Pool);
        }
    }

    /// <summary>Server: heal, clamped at <see cref="MaxHealth"/>.</summary>
    [Server]
    public void ServerHeal(int amount)
    {
        var state = new HealthCoreState { Hp = CurrentHealth.Value, MaxHp = MaxHealth.Value };
        CurrentHealth.Value = HealthProcessor.Heal(state, amount).Hp;
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
