using UnityEngine;

/// <summary>
/// Spawns hit / death particle bursts for a pawn, driven by the REPLICATED health value
/// (<see cref="global::Pawn.Health"/>'s <c>CurrentHealth</c> SyncVar OnChange) — the same signal
/// <see cref="PawnColor"/> flashes off. The projectile-hit path resolves damage server-only, so the in-process
/// damage events never reach a remote client; the SyncVar does, so reacting to it is what makes the bursts play
/// on EVERY peer (including remote clients on a dedicated server). See ADR 0004.
///
/// Trade: the synced value carries no impact point, so the bursts play at the pawn's transform rather than the
/// exact hit location. That is the accepted cost of a machinery-free cross-peer reaction — cosmetic fidelity is
/// not where this project spends a networking pattern.
/// </summary>
public class PawnDamageVfx : MonoBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] GameObject onHitPrefab;
    [SerializeField] GameObject onDeathPrefab;

    private static GameObjectPool onHitEffectPool;
    private static GameObjectPool onDeathEffectPool;

    private void Awake()
    {
        onHitEffectPool ??= new GameObjectPool(onHitPrefab);
        onDeathEffectPool ??= new GameObjectPool(onDeathPrefab);
    }

    private void Start()
    {
        // Subscribe in Start, not Awake: pawn.Health resolves the sibling Health SyncVar, which Pawn.Awake wires
        // up. Matches PawnColor / DamageIndicator.
        pawn.Health.OnChange += OnHealthChanged;
    }

    private void OnDestroy()
    {
        if (pawn != null)
            pawn.Health.OnChange -= OnHealthChanged;
    }

    // Spawn only where a CLIENT is running on this peer to render the burst — a headless dedicated server has none
    // and skips. Gate on IsClientStarted (the client MANAGER is up on this peer), NOT IsClientInitialized (this
    // object ran OnStartClient): server-driven pawns (bots, scripted) are never client-initialized even on a host,
    // so an object-level gate would wrongly skip them. Do NOT gate on asServer: for a ServerOnly SyncVar, OnChange
    // fires exactly ONCE per change on a given peer (asServer==true on the listen-server host, asServer==false on a
    // remote client — no twin to dedupe), so gating on asServer would suppress the host. Only a decrease is a hit;
    // a heal or the initial max-HP seed is an increase and is ignored.
    private void OnHealthChanged(int prev, int next, bool asServer)
    {
        if (!pawn.IsClientStarted) return;
        if (next >= prev) return;

        Play(onHitEffectPool);
        if (next <= 0) Play(onDeathEffectPool);
    }

    private void Play(GameObjectPool pool)
    {
        var effect = pool.Get(pawn.transform.position, Quaternion.identity);
        effect.GetComponent<ParticleSystem>().Play();
        if (effect.TryGetComponent<PooledLifetime>(out var lifetime))
            lifetime.Initialize(pool);
    }
}
