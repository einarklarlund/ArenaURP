using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Server-only lifetime: after <see cref="lifetime"/> seconds, deals lethal damage
/// to the referenced <see cref="Health"/> so it dies through the same Health.OnDeath
/// path as any other kill (VFX wiring, despawnOnDeath, etc.).
///
/// Counts time via TimeManager.OnPostTick rather than coroutines so it stays
/// tick-deterministic alongside the rest of the simulation.
/// </summary>
public class KillTimer : NetworkBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private float lifetime = 2f;

    private float _elapsed;
    private bool _fired;

    private void OnEnable()
    {
        base.OnStartServer();
        _elapsed = 0f;
        _fired = false;
        InstanceFinder.TimeManager.OnPostTick += OnPostTick;
    }

    private void OnDisable()
    {
        base.OnStopServer();
        if (TimeManager != null)
            TimeManager.OnPostTick -= OnPostTick;
    }

    private void OnPostTick()
    {
        if (_fired) return;

        _elapsed += (float)TimeManager.TickDelta;
        if (_elapsed < lifetime) return;

        _fired = true;

        var amount = Mathf.Max(1, health.CurrentHealth.Value);
        var info = new DamageInfo
        {
            Amount = amount,
            Type = DamageType.Environment,
            HitPoint = transform.position,
            Direction = transform.forward,
        };
        health.TakeDamage(info);
    }
}
