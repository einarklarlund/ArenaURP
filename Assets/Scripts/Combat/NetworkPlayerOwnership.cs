using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class NetworkPlayerEvent : UnityEvent<NetworkPlayer> { }

/// <summary>
/// Carries the owning NetworkPlayer for a spawned object (e.g. who fired this bullet).
/// The owner is serialized through FishNet's predicted-spawn payload so clients receive
/// it atomically with the spawn rather than via a separate RPC or SyncVar tick.
///
/// Other components (HitBox.AssignFirer, etc.) hook into <see cref="OnAssignment"/>
/// via the inspector to receive the value when it lands.
/// </summary>
public class NetworkPlayerOwnership : NetworkBehaviour
{
    [SerializeField] private NetworkPlayerEvent onAssignment = new();

    public NetworkPlayerEvent OnAssignment => onAssignment;
    public NetworkPlayer NPOwner { get; private set; }

    /// <summary>
    /// Set the owner and broadcast it to listeners. Call this on the spawning side
    /// (e.g. from PawnWeapon) before the NetworkObject is spawned, so WritePayload
    /// has a value to serialize.
    /// </summary>
    public void AssignOwner(NetworkPlayer player)
    {
        NPOwner = player;
        onAssignment?.Invoke(player);
    }

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        writer.Write(NPOwner);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        AssignOwner(reader.Read<NetworkPlayer>());
    }
}
