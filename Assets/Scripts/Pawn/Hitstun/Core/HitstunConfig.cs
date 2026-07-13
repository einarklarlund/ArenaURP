using UnityEngine;

/// <summary>
/// The hitstun a source DEALS on hit — authored on the dealing entity's <c>HitstunDealer</c> (the projectile
/// prefab), not on the thing being hit. Folded with the dealer's modifiers into a duration by
/// <see cref="HitstunProcessor.ResolveDuration"/>.
/// </summary>
[System.Serializable]
public struct HitstunConfig
{
    [Tooltip("Seconds the struck entity is prevented from starting a new fire event.")]
    public float Seconds;

    public static HitstunConfig Default => new()
    {
        Seconds = 0.35f,
    };
}
