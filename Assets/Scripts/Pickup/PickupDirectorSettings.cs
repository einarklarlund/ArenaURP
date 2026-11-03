using UnityEngine;

/// <summary>Tuning for <see cref="PickupDirector"/>.</summary>
[CreateAssetMenu(menuName = "Arena/Pickup Director Settings", fileName = "PickupDirectorSettings")]
public class PickupDirectorSettings : ScriptableObject
{
    [Header("Pickups")]
    public Pickup healthPickup;
    public Pickup ammoPickup;

    [Header("Room")]
    [Tooltip("Pickups placed in one room, drawn by the site weights, again in a freshly picked room once they're all taken: at least one health and one ammo, the rest a coin flip.")]
    public int pickupsPerRoom = 4;
    [Tooltip("How far from a chest site a room pickup goes, roughly towards the room's middle, so it doesn't sit in a chest.")]
    public float siteOffset = 1.5f;

    [Header("Refill")]
    [Tooltip("Seconds after the room's or the line's last pickup is taken before it comes back in a freshly picked room or line site.")]
    public float refillDelay = 10f;

    [Header("Site choice")]
    [Tooltip("How rooms and lines are weighed: near players who aren't leading, away from the leaders, never in a living player's room.")]
    public SiteWeights siteWeights = new();

    [Header("Placement")]
    [Tooltip("How far above the surface a pickup floats.")]
    public float hoverHeight = 0.5f;
    [Tooltip("What a room pickup is dropped onto, when the level's colliders exist on this peer.")]
    public LayerMask groundLayers = 1; // Default
}
