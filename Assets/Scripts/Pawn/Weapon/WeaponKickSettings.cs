using UnityEngine;

/// <summary>
/// One weakest-to-strongest pair of a kick's outputs. A weapon's kick strength (0..1) picks a value between them.
/// </summary>
[System.Serializable]
public struct KickRange
{
    public float weakest;
    public float strongest;

    public KickRange(float weakest, float strongest)
    {
        this.weakest = weakest;
        this.strongest = strongest;
    }

    public float Lerp(float t) => Mathf.Lerp(weakest, strongest, t);

    /// <summary>Interpolates by ratio, so each step of strength multiplies the value by the same amount. Both ends must be positive.</summary>
    public float LerpRatio(float t) => weakest * Mathf.Pow(strongest / weakest, t);
}

/// <summary>
/// Everything a weapon's firing kick and muzzle flash need, worked out from its stats and size.
/// </summary>
public struct KickProfile
{
    /// <summary>0 = the weakest kick the settings allow, 1 = the strongest.</summary>
    public float Strength;
    /// <summary>0 = every shot kicks the same, 1 = fully randomized.</summary>
    public float Randomness;

    public float BackDistance;
    public float VerticalOffset;
    public float MaxLateralOffset;
    public float Pitch;
    public float Dip;
    public float MaxYaw;
    public float StrengthJitter;

    public float AttackTime;
    public float HoldTime;
    public float PitchPeakTime;
    public float Duration;

    public float FlashScale;
    public float FlashJitter;
}

/// <summary>
/// Shared tuning for every weapon's procedural firing kick and muzzle flash.
/// The defaults are fitted to the hand-made clips of the Test Uzi (a weak kick) and the Test Shotgun (a strong one).
/// </summary>
[CreateAssetMenu(menuName = "Arena/Weapon Kick Settings")]
public class WeaponKickSettings : ScriptableObject
{
    [Header("Kick Strength")]

    [Tooltip("Raw kick = interval^a × projectiles^b / size^c, with the interval between shots in seconds and size the diagonal of the model's bounding box in meters. This is a: the larger, the more slow weapons out-kick fast ones.")]
    [Min(0)] public float intervalExponent = 0.6f;

    [Tooltip("b in the raw kick: the larger, the more each extra projectile per shot adds.")]
    [Min(0)] public float projectileExponent = 0.35f;

    [Tooltip("c in the raw kick: the larger, the more small weapons out-kick big ones.")]
    [Min(0)] public float sizeExponent = 0.5f;

    [Tooltip("Raw kicks at or below this get the weakest kick. The Test Uzi's is about 0.13.")]
    [Min(0.0001f)] public float weakestRawKick = 0.1f;

    [Tooltip("Raw kicks at or above this get the strongest kick. The Test Shotgun's is about 1.8.")]
    [Min(0.0001f)] public float strongestRawKick = 2f;

    [Header("Kick Bounds")]

    [Tooltip("Meters the model moves back. Interpolated by ratio.")]
    public KickRange backDistance = new(0.045f, 0.85f);

    [Tooltip("Meters the model moves up (negative: down) while kicked. Light weapons hop up; heavy ones sink as their nose rises.")]
    public KickRange verticalOffset = new(0.04f, -0.12f);

    [Tooltip("Degrees the muzzle rises. Interpolated by ratio.")]
    public KickRange pitch = new(2f, 27f);

    [Tooltip("Degrees the muzzle first dips before it rises. Light weapons jitter; heavy ones just rise.")]
    public KickRange dip = new(2f, 0f);

    [Tooltip("Seconds to snap to the kicked pose.")]
    public KickRange attackTime = new(1 / 60f, 1 / 60f);

    [Tooltip("Seconds the kicked pose holds before the muzzle starts to rise.")]
    public KickRange holdTime = new(0f, 0.05f);

    [Tooltip("Seconds from the shot to the muzzle's highest point.")]
    public KickRange pitchPeakTime = new(0.083f, 0.133f);

    [Tooltip("Seconds from the shot until the model is back at rest.")]
    public KickRange duration = new(0.217f, 0.333f);

    [Header("Randomization")]

    [Tooltip("Weapons whose shots are further apart than this (seconds) kick the same way every shot.")]
    [Min(0)] public float randomizeBelowInterval = 0.25f;

    [Tooltip("Weapons whose shots are this close (seconds) or closer are fully randomized.")]
    [Min(0)] public float fullyRandomAtInterval = 0.06f;

    [Tooltip("Randomness of every weapon that fires bursts, however slowly.")]
    [Range(0, 1)] public float burstRandomness = 0.75f;

    [Tooltip("Largest sideways shift of a fully randomized shot, as a fraction of Back Distance.")]
    [Min(0)] public float lateralFraction = 0.7f;

    [Tooltip("Largest yaw of a fully randomized shot, as a fraction of Pitch.")]
    [Min(0)] public float yawFraction = 1f;

    [Tooltip("How much a fully randomized shot's kick varies in size, as a fraction (0.25 = ±25%).")]
    [Range(0, 1)] public float strengthJitter = 0.25f;

    [Header("Muzzle Flash")]

    [Tooltip("Multiplies the muzzle flash's authored particle size and speed. Interpolated by ratio.")]
    public KickRange flashScale = new(0.7f, 3f);

    [Tooltip("How much a fully randomized shot's flash varies in size, as a fraction.")]
    [Range(0, 1)] public float flashJitter = 0.2f;

    /// <summary>
    /// Works out a weapon's kick. Slow-firing, small, and many-projectile weapons kick harder.
    /// </summary>
    /// <param name="size">Diagonal of the weapon model's bounding box, in meters.</param>
    /// <param name="strengthMultiplier">Scales the raw kick, for weapons the formula gets wrong.</param>
    public KickProfile Evaluate(Weapon weapon, float size, float strengthMultiplier = 1f)
    {
        bool bursts = (weapon.fireMode is Weapon.FireMode.Burst or Weapon.FireMode.BurstAutomatic) && weapon.burstCount > 1;
        float interval = Mathf.Max(bursts ? weapon.burstInterval : weapon.cooldown, 0.001f);

        float raw = strengthMultiplier
            * Mathf.Pow(interval, intervalExponent)
            * Mathf.Pow(Mathf.Max(weapon.projectilesPerShot, 1), projectileExponent)
            / Mathf.Pow(Mathf.Max(size, 0.01f), sizeExponent);
        float strength = Mathf.InverseLerp(Mathf.Log(weakestRawKick), Mathf.Log(strongestRawKick), Mathf.Log(Mathf.Max(raw, 0.0001f)));

        float randomness = Mathf.InverseLerp(randomizeBelowInterval, fullyRandomAtInterval, interval);
        if (bursts)
            randomness = Mathf.Max(randomness, burstRandomness);

        float back = backDistance.LerpRatio(strength);
        float rise = pitch.LerpRatio(strength);
        return new KickProfile
        {
            Strength = strength,
            Randomness = randomness,
            BackDistance = back,
            VerticalOffset = verticalOffset.Lerp(strength),
            MaxLateralOffset = back * lateralFraction,
            Pitch = rise,
            Dip = dip.Lerp(strength),
            MaxYaw = rise * yawFraction,
            StrengthJitter = strengthJitter,
            AttackTime = attackTime.Lerp(strength),
            HoldTime = holdTime.Lerp(strength),
            PitchPeakTime = pitchPeakTime.Lerp(strength),
            Duration = duration.Lerp(strength),
            FlashScale = flashScale.LerpRatio(strength),
            FlashJitter = flashJitter,
        };
    }
}
