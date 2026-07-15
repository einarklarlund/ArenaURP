#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Editor/test-only helper that performs the typed, server-side wiring a projectile-damage PlayMode
/// test needs. It lives in Assembly-CSharp so it can touch Pawn / NetworkPlayer / NetworkPlayerManager
/// and their SyncVars directly ??? the Arena.PlayTests asmdef cannot reference Assembly-CSharp, so the
/// test calls these methods by reflection (one contained seam, per the integration-testing skill).
///
/// Gated by UNITY_EDITOR so it never ships in a player build.
/// </summary>
public static class ProjectileDamageTestRig
{
    /// <summary>
    /// Wire a spawned target pawn to a player and register the player, exactly as the match setup would.
    /// Server-only: call after both NetworkObjects are spawned.
    /// </summary>
    public static void Wire(NetworkPlayerManager npm, NetworkPlayer player, Pawn pawn, int health)
    {
        pawn.MaxHealth.Value = health;
        pawn.Health.Value = health;
        pawn.ControllingPlayer.Value = player;
        player.ControlledPawn.Value = pawn;
        npm.ActivePlayers.Add(player);
    }

    public static int Health(Pawn pawn) => pawn.Health.Value;

    public static int PlayerObjectId(NetworkPlayer player) => player.ObjectId;

    /// <summary>The authored Damage of a projectile kind (read off its InstancePrefab's Damage component), for exact assertions.</summary>
    public static int TypeDamageAt(string assetPath)
        => AssetDatabase.LoadAssetAtPath<ProjectileType>(assetPath).InstancePrefab.GetComponent<Damage>().Amount;

    /// <summary>The authored baseline InitialSpeed of a projectile kind (off its InstancePrefab's NetworkedMotion).</summary>
    public static float TypeInitialSpeedAt(string assetPath)
        => AssetDatabase.LoadAssetAtPath<ProjectileType>(assetPath).InstancePrefab.GetComponent<NetworkedMotion>().InitialSpeed;

    /// <summary>The current segment-origin speed of a live spawned projectile (baseline × any on-spawn speed buff).</summary>
    public static float OriginSpeed(UnityEngine.GameObject bullet)
        => bullet.GetComponent<NetworkedMotion>().State.OriginSpeed;

    /// <summary>The z of a live projectile's current motion heading (segment-origin forward) — negative once it has
    /// ricocheted back off a +z-facing wall. Reads the server instance's motion state.</summary>
    public static float MotionForwardZ(UnityEngine.GameObject bullet)
        => bullet.GetComponent<NetworkedMotion>().State.OriginForward.z;

    /// <summary>The z of a live projectile's current segment ORIGIN — where its trajectory resumed. After a ricochet
    /// off a +z-facing wall this should stand a collision-radius off the wall face (the mover's centre), not sit on
    /// the surface. Reads the server instance's motion state.</summary>
    public static float MotionOriginZ(UnityEngine.GameObject bullet)
        => bullet.GetComponent<NetworkedMotion>().State.OriginPos.z;

    /// <summary>
    /// Author exactly <paramref name="count"/> surface-enabled RicochetHandler components on a live bullet, and bump
    /// its durability high so the per-hit durability spend (ADR 0003) doesn't kill it before it can bounce. TWO
    /// Ricochets stands in for the powerup-stacking case a bouncy shot actually hits — one on the projectile's own
    /// prefab, another contributed by the owner's "bouncy" passive — which must still bounce, not cancel (reflecting
    /// the same incoming direction is idempotent; reflecting the live velocity twice composes to the identity and
    /// drives straight through). Mutates only this spawned instance's components, never the asset.
    /// </summary>
    public static void AuthorRicochetOnSurface(UnityEngine.GameObject bullet, int count)
    {
        foreach (var existing in bullet.GetComponents<RicochetHandler>())
            UnityEngine.Object.DestroyImmediate(existing);
        for (int i = 0; i < count; i++)
            bullet.AddComponent<RicochetHandler>(); // default EnabledChannels = Surface
        bullet.GetComponent<Health>().ServerInitMax(100); // survive the per-hit durability spend so it can keep bouncing
    }

    /// <summary>Set the per-spawn speed buff directly on a live projectile (used to construct the two-peer self-heal gap).</summary>
    public static void SetSpeedMultiplier(UnityEngine.GameObject bullet, float multiplier)
        => bullet.GetComponent<NetworkedMotion>().SetSpeedMultiplier(multiplier);

    /// <summary>The current world-space X scale of a live spawned projectile (baseline × any on-spawn scale buff).</summary>
    public static float LocalScaleX(UnityEngine.GameObject bullet)
        => bullet.transform.localScale.x;

    /// <summary>The authored baseline localScale.x of a projectile kind (off its InstancePrefab's transform).</summary>
    public static float TypeLocalScaleXAt(string assetPath)
        => AssetDatabase.LoadAssetAtPath<ProjectileType>(assetPath).InstancePrefab.transform.localScale.x;

    /// <summary>The current effective collision radius of a live spawned projectile (baseline × any on-spawn scale buff).</summary>
    public static float CollisionRadius(UnityEngine.GameObject bullet)
        => bullet.GetComponent<ServerCollision>().EffectiveRadius;

    /// <summary>The authored baseline collision radius of a projectile kind (off its InstancePrefab's ServerCollision).</summary>
    public static float TypeCollisionRadiusAt(string assetPath)
        => AssetDatabase.LoadAssetAtPath<ProjectileType>(assetPath).InstancePrefab.GetComponent<ServerCollision>().Radius;

    /// <summary>
    /// Make a live bullet 1-HP so the per-hit durability spend (the dissolved LoseHealth, ADR 0003) kills it INSIDE
    /// the hit that damages the pawn. This pins the atomic-hit rule: the damage EXCHANGE (fired from the Damage
    /// component, not an authored row) and the durability spend resolve as one hit even though the projectile dies
    /// mid-hit — dying cannot retract the exchange. Mutates only this spawned instance's Health, never the asset.
    /// </summary>
    public static void AuthorLethalFirstOnPlayer(UnityEngine.GameObject bullet)
    {
        bullet.GetComponent<Health>().ServerInitMax(1);   // the per-hit durability spend is lethal at 1 HP
    }

    /// <summary>
    /// Server-spawns a projectile prefab with its <c>NetPlayerOwnership.SpawnOwnerId</c> left at -1 (so a receiving
    /// client's SpawnBuffReceiver no-ops) but a per-spawn speed multiplier set on the SERVER instance BEFORE the
    /// spawn — standing in for a server-side-only buff resolution. The client must then self-heal to the buffed
    /// speed from the seeded segment SyncVars. <paramref name="owner"/> is the FishNet connection to grant network
    /// ownership to purely so the object REPLICATES to that peer — it is INDEPENDENT of SpawnOwnerId (nothing in the
    /// buff path reads <c>NetworkObject.Owner</c>). Ordering matters: the multiplier is set before <c>Spawn</c> so
    /// the buffed OriginSpeed is already in the state when OnStartServer seeds the seg SyncVars. Returns the id.
    /// </summary>
    public static int ServerSpawnBulletWithSpeedMultiplier(FishNet.Managing.NetworkManager server,
        FishNet.Connection.NetworkConnection owner, string prefabPath, UnityEngine.Vector3 position, float multiplier)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
        var instance = UnityEngine.Object.Instantiate(prefab, position, UnityEngine.Quaternion.identity);
        instance.GetComponent<NetworkedMotion>().SetSpeedMultiplier(multiplier);
        server.ServerManager.Spawn(instance, owner);
        return instance.GetComponent<FishNet.Object.NetworkObject>().ObjectId;
    }
}
#endif
