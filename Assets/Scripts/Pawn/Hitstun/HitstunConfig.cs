using UnityEngine;

/// <summary>
/// The hitstun a source DEALS on hit — authored on the dealing entity's <see cref="HitstunController"/> (the
/// projectile prefab), not on the thing being hit. The controller's dealer half folds it with its outgoing
/// modifiers into a duration.
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
