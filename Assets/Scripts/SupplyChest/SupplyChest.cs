using FishNet.Component.Ownership;
using FishNet.Connection;
using UnityEngine;

/// <summary>
/// A chest that drops small health and ammo pickups for the pawn that opens it.
/// The pickups exist on the opener's side only, already claimed by the opener.
/// Only the server spawns chests. Any client may despawn one: that is the predicted despawn
/// that <see cref="PredictedSpawn"/> provides, so this component is the object's PredictedSpawn.
/// </summary>
public class SupplyChest : PredictedSpawn
{
    [SerializeField] private Pickup healthPickup;
    [SerializeField] private Pickup ammoPickup;
    [Tooltip("Pickups per open, before upgrades: at least one health and one ammo, the rest a coin flip.")]
    [SerializeField] private int pickupCount = 4;
    [Tooltip("How fast pickups tumble out of the chest, in units per second.")]
    [SerializeField] private float launchSpeed = 8f;

    /// <summary>Written by the opener's upgrades as it opens; scales <see cref="pickupCount"/>.</summary>
    [System.NonSerialized] public float PickupMultiplier = 1f;

    // -------------------- API --------------------

    /// <summary>
    /// Called by the opening pawn's owner. The pickups are local (ammo is client-authoritative,
    /// and health goes through the pawn's own ServerRpc), so the despawn is all that reaches the server.
    /// </summary>
    public void Open(Pawn opener)
    {
        if (!IsSpawned || IsDeinitializing)
            return;

        NetworkPlayer player = opener.ControllingPlayer.Value;
        if (player == null)
            return;

        PickupMultiplier = 1f;
        player.ApplyUpgradesTo(this);

        DropPickups(player);
        Despawn();
    }

    // -------------------- PREDICTED SPAWN --------------------

    public override bool OnTrySpawnClient(NetworkConnection owner = null) => false;

    public override bool OnTrySpawnServer(NetworkConnection spawner, NetworkConnection owner = null) => false;

    // -------------------- DROPS --------------------

    private void DropPickups(NetworkPlayer player)
    {
        // The chest's origin is a bottom corner (see ChestPlacement), so drops go from above its body's center,
        // clear of its collider.
        Bounds bounds = GetComponent<Collider>().bounds;
        Vector3 origin = new(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z);
        int count = Mathf.RoundToInt(pickupCount * PickupMultiplier);
        for (int i = 0; i < count; i++)
        {
            Pickup prefab = i switch
            {
                0 => healthPickup,
                1 => ammoPickup,
                _ => Random.value < 0.5f ? healthPickup : ammoPickup,
            };

            Vector3 direction = new Vector3(Random.Range(-1f, 1f), Random.Range(1f, 2f), Random.Range(-1f, 1f)).normalized;
            Pickup pickup = Instantiate(prefab, origin, Quaternion.LookRotation(direction));
            pickup.Launch(player, launchSpeed * direction);
        }
    }
}
