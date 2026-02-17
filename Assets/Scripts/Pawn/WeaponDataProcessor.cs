using System.Collections;
using FishNet;
using FishNet.Object;
using UnityEngine;

public class WeaponDataProcessor : NetworkBehaviour
{
    [SerializeField] private Transform firePoint;
    [SerializeField] private PawnAmmo pawnAmmo;

    private float lastFireTime;
    private bool isFiringSequence;

    public void TryFireAuto(WeaponData currentWeapon)
    {
        if (currentWeapon.FireMode == FireMode.Automatic)
            TryFire(currentWeapon);
    }
    
    public void TryFireSemiAuto(WeaponData currentWeapon)
    {
        if (currentWeapon.FireMode == FireMode.SemiAuto)
            TryFire(currentWeapon);
    }
    
    public void TryFire(WeaponData currentWeapon)
    {
        if (!CanFireClient(currentWeapon)) return;
        
        StartCoroutine(FireSequence(currentWeapon));
    }

    private bool CanFireClient(WeaponData currentWeapon)
    {
        bool hasAmmo = pawnAmmo.AmmoPools.TryGetValue(currentWeapon.AmmoType, out int amount) && amount >= currentWeapon.AmmoPerFire;
        return Time.time >= lastFireTime + currentWeapon.FireRate && !isFiringSequence && hasAmmo;
    }

    private IEnumerator FireSequence(WeaponData currentWeapon)
    {
        if (IsServerInitialized)
        {
            if (!pawnAmmo.ConsumeAmmo(currentWeapon.AmmoType, currentWeapon.AmmoPerFire))
            {
                isFiringSequence = false;
                yield break; // Not enough ammo to start the sequence
            }
        }
        
        isFiringSequence = true;
        lastFireTime = Time.time;
        
        int shotsToFire = currentWeapon.ShotsPerFire;
        float timeBetweenShots = currentWeapon.FireDuration > 0 ? currentWeapon.FireDuration / shotsToFire : 0;

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
            // Client-side Predicted Spawn
            NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(
                currentWeapon.BulletPrefab.gameObject, 
                firePoint.position, 
                Quaternion.LookRotation(firePoint.forward), 
                false 
            );

            if (nob.TryGetComponent<Bullet>(out var bullet))
            {
                bullet.data = BulletHelper.GetSpawnState(currentWeapon, firePoint, i, count);
            }

            // Call server manager to spawn the bullet in the client code
            // since the Bullet utilizes PredictedSpawn
            InstanceFinder.ServerManager.Spawn(nob, Owner);
        }
    }
}