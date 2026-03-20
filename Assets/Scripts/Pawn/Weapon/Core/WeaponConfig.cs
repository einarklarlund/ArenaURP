/// <summary>
/// Immutable configuration for weapon firing behaviour.
/// Typically populated from a WeaponData ScriptableObject.
/// </summary>
[System.Serializable]
public struct WeaponConfig
{
    public FireMode FireMode;

    /// <summary>Minimum seconds between fire events.</summary>
    public float FireRate;

    /// <summary>Ammo consumed per fire event.</summary>
    public int AmmoPerFire;

    /// <summary>Number of shot pulses per fire event (burst count).</summary>
    public int ShotsPerFire;

    /// <summary>Seconds over which ShotsPerFire are spread. 0 = all on one frame.</summary>
    public float FireDuration;

    /// <summary>Projectiles spawned per shot pulse (e.g. shotgun pellets).</summary>
    public int ProjectilesPerShot;

    /// <summary>Spread angle in degrees.</summary>
    public float SpreadAngle;

    public SpreadType SpreadType;

    public static WeaponConfig Default => new()
    {
        FireMode = FireMode.Automatic,
        FireRate = 0.1f,
        AmmoPerFire = 1,
        ShotsPerFire = 1,
        FireDuration = 0f,
        ProjectilesPerShot = 1,
        SpreadAngle = 5f,
        SpreadType = SpreadType.Random,
    };
}
