using System;
using System.Collections.Generic;
using FishNet.Component.Ownership;
using FishNet.Connection;
using UnityEngine;

/// <summary>
/// A chest that drops random weapons when opened, one by default. The weapons come from a pool that grows
/// as the match goes on: each tier joins the pool once the match has run for its unlock time.
/// Only the server spawns chests. Any client may despawn one: that is the predicted despawn
/// that <see cref="PredictedSpawn"/> provides, so this component is the object's PredictedSpawn.
/// The server spawns the weapons when it accepts the despawn.
/// </summary>
public class WeaponChest : PredictedSpawn
{
    [Serializable]
    private struct PoolTier
    {
        [Tooltip("Seconds into the match after which these weapons can drop.")]
        public float unlockTime;
        public Weapon[] weapons;
    }

    [SerializeField] private PoolTier[] pool;
    [Tooltip("Weapons per open, before upgrades.")]
    [SerializeField] private int weaponCount = 1;
    [Tooltip("Weapons spawn this far above the chest's center, spread along its right axis.")]
    [SerializeField] private float dropHeight = 0.75f;
    [SerializeField] private float dropSpacing = 0.6f;

    /// <summary>Written by the opener's upgrades on the server as it opens; scales <see cref="weaponCount"/>.</summary>
    [NonSerialized] public float WeaponMultiplier = 1f;

    // -------------------- API --------------------

    /// <summary>
    /// Called by the opening pawn's owner. The despawn is all that reaches the server;
    /// the server spawns the weapons when it accepts it.
    /// </summary>
    public void Open(Pawn opener)
    {
        if (!IsSpawned || IsDeinitializing)
            return;

        // A host's despawn is not predicted, so OnTryDespawnServer never runs for it.
        if (IsServerStarted)
            ServerDropWeapons(opener.ControllingPlayer.Value);

        Despawn();
    }

    // -------------------- PREDICTED SPAWN --------------------

    public override bool OnTrySpawnClient(NetworkConnection owner = null) => false;

    public override bool OnTrySpawnServer(NetworkConnection spawner, NetworkConnection owner = null) => false;

    public override bool OnTryDespawnServer(NetworkConnection despawner)
    {
        if (!base.OnTryDespawnServer(despawner))
            return false;

        ServerDropWeapons(ServerPlayerOf(despawner));
        return true;
    }

    // -------------------- DROPS --------------------

    /// <param name="opener">The player whose upgrades set the count; null for the base count.</param>
    private void ServerDropWeapons(NetworkPlayer opener)
    {
        WeaponMultiplier = 1f;
        if (opener != null)
            opener.ApplyUpgradesTo(this);
        int count = Mathf.RoundToInt(weaponCount * WeaponMultiplier);

        // The chest's origin is a bottom corner (see ChestPlacement), so drops go from its body's center.
        Vector3 center = GetComponent<Collider>().bounds.center;
        List<Weapon> unlocked = new();
        for (int i = 0; i < count; i++)
        {
            // Distinct picks while the pool lasts, then it starts over.
            if (unlocked.Count == 0)
                unlocked = UnlockedWeapons();
            if (unlocked.Count == 0)
                break;

            int index = UnityEngine.Random.Range(0, unlocked.Count);
            Weapon prefab = unlocked[index];
            unlocked.RemoveAt(index);

            float offset = (i - (count - 1) * 0.5f) * dropSpacing;
            Vector3 position = center + transform.up * dropHeight + transform.right * offset;
            Weapon weapon = Instantiate(prefab, position, transform.rotation);
            Spawn(weapon.gameObject);
        }
    }

    /// <summary>The player a client despawner plays as, or null.</summary>
    private static NetworkPlayer ServerPlayerOf(NetworkConnection connection)
    {
        if (MatchManager.Instance == null || connection == null)
            return null;

        foreach (NetworkPlayer player in MatchManager.Instance.ActivePlayers)
        {
            if (player != null && player.Owner == connection)
                return player;
        }
        return null;
    }

    private List<Weapon> UnlockedWeapons()
    {
        double matchTime = MatchManager.Instance != null ? MatchManager.Instance.ServerMatchTime : 0;

        List<Weapon> unlocked = new();
        foreach (PoolTier tier in pool)
        {
            if (tier.unlockTime > matchTime)
                continue;

            foreach (Weapon weapon in tier.weapons)
            {
                if (weapon != null)
                    unlocked.Add(weapon);
            }
        }

        return unlocked;
    }
}
