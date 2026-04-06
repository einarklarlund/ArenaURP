using FishNet.Connection;
using FishNet.Object;

public class ProjectileSpawnOnDeath : NetworkBehaviour
{
    private ProjectileData data;

    private void Awake()
    {
        data = GetComponent<ProjectileData>();
    }

    public override void OnDespawnServer(NetworkConnection connection)
    {
        base.OnDespawnServer(connection);
    }
}
