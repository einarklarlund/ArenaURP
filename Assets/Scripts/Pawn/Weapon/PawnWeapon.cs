using System.Collections.Generic;
using FishNet;
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
    [SerializeField] private PawnInventory inventory;
    [SerializeField] private Transform firePoint;
    [SerializeField] private PawnAmmo pawnAmmo;
    [SerializeField] private PawnInput input;

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

        var currentWeapon = inventory.CurrentWeaponData;

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

        bool hasAmmo = pawnAmmo.AmmoPools.TryGetValue(currentWeapon.AmmoType, out int amount)
                       && amount >= currentWeapon.AmmoPerFire;

        var weaponInput = new WeaponInput
        {
            FireHeld = fireHeld,
            FirePressed = firePressed,
            HasAmmo = hasAmmo,
            Modifiers = Modifiers,
        };

        bool wasBurstActive = _state.IsBurstActive;

        _state = WeaponProcessor.Process(
            _state, in config, in weaponInput, Time.deltaTime, out int shotsToFire);

        // Consume ammo once when a new fire event begins (server only)
        if (shotsToFire > 0 && !wasBurstActive && IsServerInitialized)
        {
            if (!pawnAmmo.ConsumeAmmo(currentWeapon.AmmoType, currentWeapon.AmmoPerFire))
                return;
        }

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
            NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(
                currentWeapon.BulletPrefab.gameObject,
                firePoint.position,
                Quaternion.LookRotation(firePoint.forward),
                false
            );

            if (nob.TryGetComponent<ProjectileData>(out var bullet))
            {
                var bulletSpawnState = BulletHelper.GetSpawnState(
                    currentWeapon, firePoint, i, count, spreadAngle);
                bulletSpawnState.Firer = pawn.ControllingPlayer.Value;
                bullet.spawnState = bulletSpawnState;
            }

            InstanceFinder.ServerManager.Spawn(nob, Owner);
        }
    }
}
