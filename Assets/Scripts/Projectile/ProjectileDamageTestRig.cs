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
    /// Author <paramref name="count"/> Ricochet operations on a live bullet's OnSurface, clearing any authored
    /// kill reaction so the bullet survives to bounce. TWO Ricochets stands in for the powerup-stacking case a
    /// bouncy shot actually hits — one authored on the projectile's own OnSurface, another contributed by the
    /// owner's "bouncy" passive — which must still bounce, not cancel (reflecting the same incoming direction is
    /// idempotent; reflecting the live velocity twice composes to the identity and drives straight through).
    /// Mutates only this spawned instance's [SerializeReference] list, never the asset.
    /// </summary>
    public static void AuthorRicochetOnSurface(UnityEngine.GameObject bullet, int count)
    {
        var collision = bullet.GetComponent<ServerCollision>();
        collision.OnSurface.Clear();
        for (int i = 0; i < count; i++)
            collision.OnSurface.Add(Commands.Ricochet());
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
    /// Rewrite a live bullet's OnPlayer operations to [LoseHealth, DealDamage] — LETHAL FIRST. With a 1-HP
    /// bullet the LoseHealth kills it before DealDamage runs, so this pins the atomic-hit rule: every authored
    /// operation of a classified hit runs even if the projectile dies mid-list (authored order can never change
    /// a hit's outcome). Mutates only this spawned instance's [SerializeReference] list, never the asset.
    /// </summary>
    public static void AuthorLethalFirstOnPlayer(UnityEngine.GameObject bullet)
    {
        var collision = bullet.GetComponent<ServerCollision>();
        collision.OnPlayer.Clear();
        collision.OnPlayer.Add(Commands.LoseHealth());
        collision.OnPlayer.Add(Commands.DealDamage());
        bullet.GetComponent<Health>().ServerInitMax(1);   // one LoseHealth is lethal, whatever the authored durability
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
