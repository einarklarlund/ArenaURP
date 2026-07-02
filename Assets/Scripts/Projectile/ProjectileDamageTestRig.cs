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
}
#endif
