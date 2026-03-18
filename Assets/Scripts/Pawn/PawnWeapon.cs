using System.Collections;
using FishNet;
using FishNet.Object;
using UnityEngine;

public class PawnWeapon : NetworkBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnInventory inventory;
    [SerializeField] private Transform firePoint;
    [SerializeField] private PawnAmmo pawnAmmo;
    [SerializeField] private PawnInputProvider input;

    private float lastFireTime;
    private bool isFiringSequence;
    private bool fireHeldLastFrame;

    private void Update()
    {
        if (!input.IsActive) return;

        bool fireHeld = input.Data.Fire;
        bool firePressed = fireHeld && !fireHeldLastFrame; // rising edge for semi-auto
        fireHeldLastFrame = fireHeld;

        var currentWeapon = inventory.CurrentWeaponData;

        switch (currentWeapon.FireMode)
        {
            case FireMode.Automatic when fireHeld:
                TryFire(currentWeapon);
                break;
            case FireMode.SemiAuto when firePressed:
                TryFire(currentWeapon);
                break;
        }
    }

    private void TryFire(WeaponData currentWeapon)
    {
        if (!CanFire(currentWeapon)) return;
        StartCoroutine(FireSequence(currentWeapon));
    }

    private bool CanFire(WeaponData currentWeapon)
    {
        bool hasAmmo = pawnAmmo.AmmoPools.TryGetValue(currentWeapon.AmmoType, out int amount)
                       && amount >= currentWeapon.AmmoPerFire;
        return Time.time >= lastFireTime + currentWeapon.FireRate && !isFiringSequence && hasAmmo;
    }

    private IEnumerator FireSequence(WeaponData currentWeapon)
    {
        if (IsServerInitialized)
        {
            if (!pawnAmmo.ConsumeAmmo(currentWeapon.AmmoType, currentWeapon.AmmoPerFire))
            {
                isFiringSequence = false;
                yield break;
            }
        }

        isFiringSequence = true;
        lastFireTime = Time.time;

        int shotsToFire = currentWeapon.ShotsPerFire;
        float timeBetweenShots = currentWeapon.FireDuration > 0
            ? currentWeapon.FireDuration / shotsToFire
            : 0;

        for (int i = 0; i < shotsToFire; i++)
        {
            ExecuteShot(currentWeapon);
            if (timeBetweenShots > 0) yield return new WaitForSeconds(timeBetweenShots);
        }

        isFiringSequence = false;
    }

    private void ExecuteShot(WeaponData currentWeapon)
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

            if (nob.TryGetComponent<Bullet>(out var bullet))
            {
                var bulletData = BulletHelper.GetSpawnState(currentWeapon, firePoint, i, count);
                bulletData.Firer = pawn.ControllingPlayer.Value;
                bullet.data = bulletData;
            }

            InstanceFinder.ServerManager.Spawn(nob, Owner);
        }
    }
}
