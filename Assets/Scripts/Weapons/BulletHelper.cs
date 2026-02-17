using System;
using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

public static class BulletHelper
{
    public static BulletData GetSpawnState(WeaponData weaponData, Transform firePoint, int index, int total)
    {
        Vector3 fireDir = CalculateSpread(weaponData, firePoint, index, total);
        BulletData spawnState = new()
        {
            PreciseTick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick),
            StartDirection = fireDir,
            StartPosition = firePoint.position,
            ID = Guid.NewGuid().ToString()
        };
        return spawnState;
    }

    public static Vector3 CalculateSpread(WeaponData weaponData, Transform firePoint, int index, int total)
    {
        Quaternion spreadRotation;

        if (weaponData.SpreadType == SpreadType.Even && weaponData.ProjectilesPerShot > 1)
        {
            // Evenly distribute across a horizontal plane relative to firePoint
            float angleStep = weaponData.SpreadAngle / (total - 1);
            float currentAngle = -weaponData.SpreadAngle / 2f + (angleStep * index);
            spreadRotation = Quaternion.Euler(0, currentAngle, 0);
        }
        else
        {
            // Random spread within a cone
            float maxPitch = Mathf.Min(2, weaponData.SpreadAngle);
            float randomYaw = UnityEngine.Random.Range(-weaponData.SpreadAngle / 2, weaponData.SpreadAngle / 2);
            float randomPitch = UnityEngine.Random.Range(-maxPitch / 2, maxPitch / 2);
            spreadRotation = Quaternion.Euler(randomPitch, randomYaw, 0);
        }

        return firePoint.rotation * spreadRotation * Vector3.forward;
    }
}
