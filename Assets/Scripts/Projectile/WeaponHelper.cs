using UnityEngine;

public static class WeaponHelper
{
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
            float randomYaw = Random.Range(-spreadAngle / 2, spreadAngle / 2);
            float randomPitch = Random.Range(-maxPitch / 2, maxPitch / 2);
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
