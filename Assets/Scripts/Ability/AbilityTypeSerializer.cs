using FishNet.Serializing;

/// <summary>
/// FishNet custom serializer for <see cref="AbilityType"/> references. ScriptableObject references
/// can't ride the wire directly, so (exactly like weapons resolve through WeaponDatabase) we serialize
/// the ability's index in the shared <see cref="AbilityDatabase"/> and resolve it locally on each peer.
/// FishNet's codegen auto-discovers these <c>Write*</c>/<c>Read*</c> extension methods and uses them
/// for <c>SyncList&lt;AbilityType&gt;</c>, <see cref="ActiveSlot"/>'s field, and any RPC carrying one.
/// </summary>
public static class AbilityTypeSerializer
{
    public static void WriteAbilityType(this Writer writer, AbilityType value)
    {
        int id = (value != null && AbilityDatabase.Instance != null)
            ? AbilityDatabase.Instance.GetID(value)
            : -1;
        writer.WriteInt32(id);
    }

    public static AbilityType ReadAbilityType(this Reader reader)
    {
        int id = reader.ReadInt32();
        return id >= 0 && AbilityDatabase.Instance != null
            ? AbilityDatabase.Instance.GetAbility(id)
            : null;
    }
}
