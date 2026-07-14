using Arena.NetworkedMotion;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Spawn the networked prefab(s) configured on a referenced <see cref="SpawnableObjects"/> component, fanned
/// across its spread around the context's direction and originating at the context's point. The ONE generic
/// networked-spawn reaction, reused by a projectile hit (children at the impact point) and by an ability (a fan
/// from the caster); it reads only an origin + direction, so it is typed to <see cref="AimContext"/> — a
/// single <see cref="Apply(AimContext)"/> authorable on every collision context and on <see cref="AimContext"/>.
///
/// The prefab list, count, and spread are all data on a <see cref="SpawnableObjects"/> component (a plain
/// data-holder, not a first-class system). By default it is resolved via GetComponent on the context's
/// <see cref="EffectContext.Source"/> (the projectile on a hit, the caster's pawn on an ability use). Set
/// <see cref="source"/> (the one allowed reference on an otherwise stateless effect) only when the effect is
/// authored on the applier's own prefab AND the source carries several <see cref="SpawnableObjects"/>, which a
/// bare GetComponent could not disambiguate.
///
/// Spawning is just instantiate-at-pose + stamp the firer + network-spawn: the instance's components
/// self-configure (TypeObjectRoot at Awake, NetworkedMotion's anchor at OnStartNetwork from the instantiate
/// pose + current tick). The firer id is the one per-spawn value a component cannot self-derive (bots have no
/// owning connection), so it is stamped here from the context.
/// </summary>
[System.Serializable]
public sealed class SpawnNetworkObjects : IEffect<AimContext>
{
    [SerializeField, Tooltip("Optional explicit SpawnableObjects. Leave null to resolve via GetComponent on the context's Owner; set it only to disambiguate when the owner carries several.")]
    private SpawnableObjects source;

    public SpawnNetworkObjects() { }

    public SpawnNetworkObjects(SpawnableObjects source) => this.source = source;

    public void Apply(AimContext ctx)
    {
        SpawnableObjects resolved = source;
        if (resolved == null && (ctx.Source == null || !ctx.Source.TryGetComponent(out resolved)))
            return;
        if (resolved.Prefabs.Count == 0)
            return;

        var manager = InstanceFinder.NetworkManager;
        if (manager == null)
            return;

        int ownerId = ctx.Instigator != null ? ctx.Instigator.ObjectId : -1;
        int count = resolved.Count;
        for (int i = 0; i < count; i++)
        {
            var prefab = resolved.Prefabs[i % resolved.Prefabs.Count];
            if (prefab == null)
                continue;

            Vector3 dir = SpawnFanMath.Direction(ctx.Direction, i, count, resolved.SpreadDegrees, Vector3.up);
            Quaternion rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            NetworkObject nob = manager.GetPooledInstantiated(prefab, ctx.Point, rot, asServer: true);
            if (nob.TryGetComponent<NetPlayerOwnership>(out var ownership))
                ownership.SpawnOwnerId = ownerId;
            manager.ServerManager.Spawn(nob);
        }
    }
}
