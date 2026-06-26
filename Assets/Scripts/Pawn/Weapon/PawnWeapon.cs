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

            if (currentWeapon.Projectile == null)
                continue;

            // Shared spawn path. Use THIS pawn's own NetworkManager (not InstanceFinder): on the owning
            // client that is the client's manager, so the spawn takes FishNet's predicted-spawn path and the
            // bullet appears locally on the SAME frame; on the server/host it spawns authoritatively. Owner
            // = this pawn's connection so the predicting client owns its bullet.
            int ownerId = pawn.ControllingPlayer.Value != null ? pawn.ControllingPlayer.Value.ObjectId : -1;
            PreciseTick tick = TimeManager.GetPreciseTick(TickType.Tick);
            ProjectileSpawner.Spawn(currentWeapon.Projectile, NetworkManager, ownerId, tick, spawnPos, fireDir, asServer: false, Owner);
        }
    }
}
