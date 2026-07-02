using FishNet.Object;
using UnityEngine;

/// <summary>
/// The one server-side grant path shared by the active/passive inventory components: instantiate the ability
/// type's INSTANCE prefab and parent it under the granting pawn's NetworkObject via
/// <c>NetworkObject.SetParent</c> (NOT raw transform parenting — only SetParent populates the parent FishNet
/// replicates), then spawn. Children despawn with the pawn automatically.
/// </summary>
internal static class AbilityGrant
{
    /// <summary>Server: spawn <paramref name="ability"/>'s instance prefab under <paramref name="host"/>'s NetworkObject. Returns false when the type has no prefab.</summary>
    public static bool Spawn(NetworkBehaviour host, AbilityType ability)
    {
        if (ability == null || ability.InstancePrefab == null)
            return false;

        NetworkObject nob = Object.Instantiate(ability.InstancePrefab);
        nob.SetParent(host.NetworkObject);
        nob.transform.localPosition = Vector3.zero;
        nob.transform.localRotation = Quaternion.identity;
        host.ServerManager.Spawn(nob);
        return true;
    }
}
