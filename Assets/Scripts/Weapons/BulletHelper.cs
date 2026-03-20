using System;
using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

public static class BulletHelper
{
    /// <summary>
    /// Creates a BulletData with spread applied. Uses the provided spread angle
    /// (which may have been modified by WeaponProcessor) instead of reading from WeaponData directly.
    /// </summary>
    public static BulletData GetSpawnState(
        WeaponData weaponData, Transform firePoint, int index, int total, float spreadAngle)
    {
        Vector3 fireDir = CalculateSpread(
            weaponData.SpreadType, weaponData.ProjectilesPerShot,
            spreadAngle, firePoint, index, total);

        BulletData spawnState = new()
        {
            PreciseTick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick),
            StartDirection = fireDir,
            StartPosition = firePoint.position,
            ID = Guid.NewGuid().ToString()
        };
        return spawnState;
    }

    /// <summary>
    /// Original overload for backward compatibility. Reads spread angle from WeaponData.
    /// </summary>
    public static BulletData GetSpawnState(
        WeaponData weaponData, Transform firePoint, int index, int total)
    {
        return GetSpawnState(weaponData, firePoint, index, total, weaponData.SpreadAngle);
    }

    /// <summary>
    /// Calculates spread direction with an explicit spread angle parameter.
    /// </summary>
    public static Vector3 CalculateSpread(
        SpreadType spreadType, int projectilesPerShot,
        float spreadAngle, Transform firePoint, int index, int total)
    {
        Quaternion spreadRotation;

        if (spreadType == SpreadType.Even && projectilesPerShot > 1)
        {
            // Evenly distribute across a horizontal plane relative to firePoint
            float angleStep = spreadAngle / (total - 1);
            float currentAngle = -spreadAngle / 2f + (angleStep * index);
            spreadRotation = Quaternion.Euler(0, currentAngle, 0);
        }
        else
        {
            // Random spread within a cone
            float maxPitch = Mathf.Min(2, spreadAngle);
            float randomYaw = UnityEngine.Random.Range(-spreadAngle / 2, spreadAngle / 2);
            float randomPitch = UnityEngine.Random.Range(-maxPitch / 2, maxPitch / 2);
            spreadRotation = Quaternion.Euler(randomPitch, randomYaw, 0);
        }

        return firePoint.rotation * spreadRotation * Vector3.forward;
    }

    /// <summary>
    /// Original overload for backward compatibility.
    /// </summary>
    public static Vector3 CalculateSpread(
        WeaponData weaponData, Transform firePoint, int index, int total)
    {
        return CalculateSpread(
            weaponData.SpreadType, weaponData.ProjectilesPerShot,
            weaponData.SpreadAngle, firePoint, index, total);
    }
}
