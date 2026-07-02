using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;

/// <summary>
/// Carries which player owns / fired a spawned networked object, as a plain NetworkPlayer.ObjectId. It is
/// its OWN component (not a field on NetworkedMotion) so motion stays a generic concern and ownership is a
/// separate, reusable one. The id rides the spawn payload so every peer has it atomically with the spawn —
/// the server reads it for owner-safety, owner-reaction lookup, and damage attribution.
///
/// The firer is the one per-spawn value a component cannot self-derive: bots have no owning connection, so
/// <c>NetworkObject.Owner</c> cannot stand in for it. The spawn site sets <see cref="SpawnOwnerId"/> directly
/// after instantiating, before <c>ServerManager.Spawn</c> — FishNet invokes WritePayload after that call
/// begins, so the value rides the payload to every peer.
/// </summary>
public sealed class NetPlayerOwnership : NetworkBehaviour
{
    /// <summary>NetworkPlayer.ObjectId of the owner (firer), or -1 if unowned. Set by the spawn site before <c>ServerManager.Spawn</c>.</summary>
    public int SpawnOwnerId = -1;

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
