/// <summary>
/// Complete snapshot of a weapon's firing simulation state.
/// Pure data - no behaviour, no Unity dependencies.
/// </summary>
[System.Serializable]
public struct WeaponState
{
    /// <summary>
    /// Time elapsed since the last fire event was initiated.
    /// Compared against FireRate to enforce cooldown.
    /// </summary>
    public float TimeSinceLastFire;

    /// <summary>Whether a burst sequence is currently in progress.</summary>
    public bool IsBurstActive;

    /// <summary>How many burst shots have been fired so far in the current sequence.</summary>
    public int BurstShotsFired;

    /// <summary>Time elapsed since the current burst sequence began.</summary>
    public float BurstElapsedTime;

    /// <summary>Sentinel: fire cooldown already expired at start.</summary>
    public const float CooldownExpired = 1000f;

    public static WeaponState Default => new()
    {
        TimeSinceLastFire = CooldownExpired,
        IsBurstActive = false,
        BurstShotsFired = 0,
        BurstElapsedTime = 0f,
    };
}
