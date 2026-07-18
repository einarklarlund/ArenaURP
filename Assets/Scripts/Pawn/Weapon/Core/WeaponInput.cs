using System.Collections.Generic;

/// <summary>
/// All per-frame inputs consumed by WeaponProcessor.
/// Bundles fire controls, ammo availability, and active modifiers.
/// </summary>
[System.Serializable]
public struct WeaponInput
{
    /// <summary>Whether the fire button is held this frame.</summary>
    public bool FireHeld;

    /// <summary>
    /// Whether this is the first frame the fire button is held (rising edge).
    /// Computed by the MonoBehaviour before calling Process.
    /// </summary>
    public bool FirePressed;

    /// <summary>Whether the pawn has enough ammo for the current weapon's AmmoPerFire.</summary>
    public bool HasAmmo;

    /// <summary>
    /// Whether the pawn is in hitstun and may not START a new fire event. An already-committed burst is NOT
    /// interrupted — see <see cref="WeaponProcessor.Process"/>. Sourced from the pawn's HitstunController, which
    /// drives PawnWeapon.CanFire (Stunned = !CanFire).
    /// </summary>
    public bool Stunned;

    /// <summary>Active weapon modifiers, stacked multiplicatively.</summary>
    public List<WeaponModifier> Modifiers;

    public static WeaponInput Default => new()
    {
        FireHeld = false,
        FirePressed = false,
        HasAmmo = true,
        Stunned = false,
    };
}
