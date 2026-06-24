using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Thin NetworkBehaviour that wires the pure WeaponProcessor into Unity and FishNet.
/// Owns the fire point, reads input, feeds data to the processor, and spawns bullets.
/// </summary>
public class PawnWeapon : NetworkBehaviour
{
    // Configuration
    [SerializeField] private Pawn pawn;
    [SerializeField] private Transform firePoint;
    [SerializeField] private WeaponData weaponData;
    [SerializeField] private PawnInput input;
    [SerializeField] private Collider pawnCollider;

    // API
    public readonly List<WeaponModifier> Modifiers = new();

    // State
    private WeaponState _state = WeaponState.Default;
    private bool _fireHeldLastFrame;

    private void Update()
    {
        if (!input.IsActive) return;

        bool fireHeld = input.Data.Fire;
        bool firePressed = fireHeld && !_fireHeldLastFrame;
        _fireHeldLastFrame = fireHeld;

        var currentWeapon = weaponData;

        var config = new WeaponConfig
        {
            FireMode = currentWeapon.FireMode,
            FireRate = currentWeapon.FireRate,
            AmmoPerFire = currentWeapon.AmmoPerFire,
            ShotsPerFire = currentWeapon.ShotsPerFire,
            FireDuration = currentWeapon.FireDuration,
            ProjectilesPerShot = currentWeapon.ProjectilesPerShot,
            SpreadAngle = currentWeapon.SpreadAngle,
            SpreadType = currentWeapon.SpreadType,
        };

        var weaponInput = new WeaponInput
        {
            FireHeld = fireHeld,
            FirePressed = firePressed,
            HasAmmo = true,
            Modifiers = Modifiers,
        };

        _state = WeaponProcessor.Process(
            _state, in config, in weaponInput, Time.deltaTime, out int shotsToFire);

        if (shotsToFire > 0)
        {
            float effectiveSpread = WeaponProcessor.GetEffectiveSpreadAngle(in config, in weaponInput);

            for (int shot = 0; shot < shotsToFire; shot++)
            {
                ExecuteShot(currentWeapon, effectiveSpread);
            }
        }
    }

    private void ExecuteShot(WeaponData currentWeapon, float spreadAngle)
    {
        int count = currentWeapon.ProjectilesPerShot;

        for (int i = 0; i < count; i++)
        {
            var fireDir = WeaponHelper.CalculateSpread(currentWeapon, firePoint, 0, currentWeapon.ProjectilesPerShot);
            Vector3 spawnPos = firePoint.TransformPoint(currentWeapon.SpawnPositionOffset);

            // Use THIS pawn's own NetworkManager (not InstanceFinder). On the owning client that is the
            // client's manager, so the spawn below takes FishNet's predicted-spawn path and the bullet
            // appears locally on the SAME frame; on the server/host it spawns authoritatively. Resolving
            // a different manager could turn an owning-client shot into a server-only spawn, so the bullet
            // only appears after a full server round-trip.
            NetworkObject nob = NetworkManager.GetPooledInstantiated(
                currentWeapon.BulletPrefab.gameObject,
                spawnPos,
                Quaternion.LookRotation(fireDir),
                false
            );

            if (nob.TryGetComponent<PawnProjectile>(out var projectile))
            {
                int ownerId = pawn.ControllingPlayer.Value != null ? pawn.ControllingPlayer.Value.ObjectId : -1;
                PreciseTick tick = TimeManager.GetPreciseTick(TickType.Tick);
                projectile.SetupSpawn(ownerId, tick, spawnPos, fireDir, health: 0);
                ServerManager.Spawn(nob, Owner); // own manager: predicted on the owning client, authoritative on the server
                continue;
            }

            if (nob.TryGetComponent<NetworkPlayerOwnership>(out var ownership))
            {
                ownership.AssignOwner(pawn.ControllingPlayer.Value);
            }

            if (nob.TryGetComponent<Collider>(out var bulletCollider))
            {
                Physics.IgnoreCollision(pawnCollider, bulletCollider);
            }

            InstanceFinder.ServerManager.Spawn(nob, Owner);
        }
    }
}
