using UnityEngine;

/// <summary>
/// Pure, stateless processor that advances weapon firing simulation by one step.
/// Has zero Unity lifecycle dependencies - all state flows in and out via structs.
/// </summary>
public static class WeaponProcessor
{
    /// <summary>
    /// Advance the weapon simulation by one tick.
    /// </summary>
    /// <param name="state">Current weapon state.</param>
    /// <param name="config">Weapon tuning parameters.</param>
    /// <param name="input">All inputs that will affect the weapon state.</param>
    /// <param name="deltaTime">Time step for this tick.</param>
    /// <param name="shotsToFire">Number of shot pulses the caller should execute this frame.</param>
    /// <returns>The new weapon state after this tick.</returns>
    public static WeaponState Process(
        WeaponState state,
        in WeaponConfig config,
        in WeaponInput input,
        float deltaTime,
        out int shotsToFire)
    {
        // --- Combine modifiers ---
        float fireRateMult = 1f;
        if (input.Modifiers != null)
        {
            foreach (var mod in input.Modifiers)
            {
                fireRateMult *= mod.FireRateMultiplier;
            }
        }

        float effectiveFireRate = config.FireRate * fireRateMult;

        // --- Advance cooldown timer ---
        state.TimeSinceLastFire += deltaTime;

        shotsToFire = 0;

        // --- Handle active burst ---
        if (state.IsBurstActive)
        {
            shotsToFire = AdvanceBurst(ref state, in config, deltaTime);
            return state;
        }

        // --- Check if we should start firing ---
        bool wantsToFire = config.FireMode == FireMode.Automatic
            ? input.FireHeld
            : input.FirePressed;

        bool cooldownReady = state.TimeSinceLastFire >= effectiveFireRate;

        if (wantsToFire && cooldownReady && input.HasAmmo)
        {
            state.TimeSinceLastFire = 0f;
            state = StartBurst(state, in config, out shotsToFire);
        }

        return state;
    }

    /// <summary>
    /// Initiates a new fire event. For single-shot weapons (ShotsPerFire &lt;= 1 or
    /// FireDuration &lt;= 0), fires immediately with no burst tracking.
    /// For burst weapons, fires the first shot and begins burst state tracking.
    /// </summary>
    private static WeaponState StartBurst(
        WeaponState state,
        in WeaponConfig config,
        out int shotsToFire)
    {
        if (config.ShotsPerFire <= 1 || config.FireDuration <= 0f)
        {
            // Single-shot or zero-duration burst: fire all shots this frame
            shotsToFire = Mathf.Max(1, config.ShotsPerFire);
            return state;
        }

        // Multi-shot burst: fire the first shot and begin tracking
        state.IsBurstActive = true;
        state.BurstShotsFired = 1;
        state.BurstElapsedTime = 0f;
        shotsToFire = 1;
        return state;
    }

    /// <summary>
    /// Advances an in-progress burst sequence. Computes how many shots should
    /// have fired by now based on elapsed time, and returns the delta.
    /// Robust to variable frame rates.
    /// </summary>
    private static int AdvanceBurst(
        ref WeaponState state,
        in WeaponConfig config,
        float deltaTime)
    {
        state.BurstElapsedTime += deltaTime;

        // How many total shots should have been fired by now?
        float timeBetweenShots = config.FireDuration / config.ShotsPerFire;
        int expectedShotsFired;

        if (timeBetweenShots > 0f)
        {
            expectedShotsFired = Mathf.Min(
                Mathf.FloorToInt(state.BurstElapsedTime / timeBetweenShots) + 1,
                config.ShotsPerFire);
        }
        else
        {
            expectedShotsFired = config.ShotsPerFire;
        }

        int newShots = expectedShotsFired - state.BurstShotsFired;
        state.BurstShotsFired = expectedShotsFired;

        // Check if burst is complete
        if (state.BurstShotsFired >= config.ShotsPerFire)
        {
            state.IsBurstActive = false;
            state.BurstShotsFired = 0;
            state.BurstElapsedTime = 0f;
        }

        return newShots;
    }

    /// <summary>
    /// Computes the effective spread angle after applying modifiers.
    /// Called by PawnWeapon when it needs to spawn bullets.
    /// </summary>
    public static float GetEffectiveSpreadAngle(
        in WeaponConfig config,
        in WeaponInput input)
    {
        float spreadMult = 1f;
        if (input.Modifiers != null)
        {
            foreach (var mod in input.Modifiers)
                spreadMult *= mod.SpreadMultiplier;
        }
        return config.SpreadAngle * spreadMult;
    }
}
