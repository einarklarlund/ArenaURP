using UnityEngine;

/// <summary>
/// Shared owner-id → <see cref="NetworkPlayer"/> lookup, hoisted out of ServerCollision so every site that turns a
/// <see cref="NetPlayerOwnership.SpawnOwnerId"/> back into a player uses the same scan and they can't drift.
///
/// CLIENT-SAFE: <see cref="NetworkPlayerManager.ActivePlayers"/> is a SyncList and each
/// <c>NetworkPlayer.ObjectId</c> replicates, so the identical scan runs on every peer — which is what lets the
/// on-spawn buff path resolve the firer on observers, not just the server. Caveat (fine to accept): on a client a
/// just-registered player can lag the SyncList by a tick; callers treat an unresolved id as a quiet no-op.
/// </summary>
public static class NetPlayerResolver
{
    private static NetworkPlayerManager _players;

    // Lazily find + cache the manager. A destroyed manager compares == null in Unity, so a stale cache re-finds.
    private static NetworkPlayerManager Players => _players != null
        ? _players
        : (_players = Object.FindAnyObjectByType<NetworkPlayerManager>());

    /// <summary>
    /// Resolve the <see cref="NetworkPlayer"/> whose NetworkObject id is <paramref name="objectId"/>, or null for a
    /// negative/unowned id, no manager present, or an id not (yet) in the replicated roster.
    /// </summary>
    public static NetworkPlayer Resolve(int objectId)
    {
        NetworkPlayerManager players = Players;
        if (players == null || objectId < 0)
            return null;
        foreach (NetworkPlayer player in players.ActivePlayers)
            if (player != null && player.ObjectId == objectId)
                return player;
        return null;
    }
}
