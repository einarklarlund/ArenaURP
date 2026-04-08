using FishNet;
using FishNet.Object;

public abstract class ProjectileImpact : NetworkBehaviour
{
    protected ProjectileData data;

    protected virtual void Awake()
    {
        data = GetComponent<ProjectileData>();
    }

    protected void SpawnPrefab(NetworkObject prefab)
    {
        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated
        (
            prefab,
            transform.position,
            transform.rotation,
            false
        );

        // predicted spawn ??
        InstanceFinder.ServerManager.Spawn(nob, Owner);
    }
}
