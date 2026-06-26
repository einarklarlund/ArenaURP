using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;

/// <summary>
/// Carries which player owns / fired a spawned networked object, as a plain NetworkPlayer.ObjectId. It is
/// its OWN component (not a field on NetworkedMotion) so motion stays a generic concern and ownership is a
/// separate, reusable one. The id rides the spawn payload so every peer has it atomically with the spawn —
/// the server reads it for owner-safety, owner-reaction lookup, and damage attribution.
/// </summary>
public sealed class NetPlayerOwnership : NetworkBehaviour
{
    /// <summary>NetworkPlayer.ObjectId of the owner (firer), or -1 if unowned.</summary>
    public int SpawnOwnerId = -1;

    /// <summary>Set on the spawning side before <c>ServerManager.Spawn</c> so WritePayload has a value.</summary>
    public void SetupOwner(int ownerId) => SpawnOwnerId = ownerId;

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WriteInt32(SpawnOwnerId);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        SpawnOwnerId = reader.ReadInt32();
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        SpawnOwnerId = -1;
    }
}
