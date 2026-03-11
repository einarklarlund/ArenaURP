using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using FishNet.Object.Synchronizing;
public class NetworkPlayerManager : NetworkBehaviour
{
    public readonly SyncList<NetworkPlayer> ActivePlayers = new();
    public IEnumerable<NetworkPlayer> HumanPlayers => ActivePlayers.Where(p => !p.IsBot);
    public IEnumerable<NetworkPlayer> BotPlayers => ActivePlayers.Where(p => p.IsBot);

    /// <summary>
    /// Adds player to public registry.
    /// </summary>
    [Server]
    public void ServerRegisterPlayer(NetworkPlayer player)
    {
        ActivePlayers.Add(player);
    }

    /// <summary>
    /// Removes player from public registry.
    /// </summary>
    [Server]
    public void ServerUnregisterPlayer(NetworkPlayer player)
    {
        ActivePlayers.Remove(player);
    }
}