using FishNet.Object;
using UnityEngine;

/// <summary>
/// Minimal sensing contract consumed by BotBrain's state machine.
/// Abstracting this out of BotVision allows the state machine logic to be
/// tested in EditMode without any scene or network dependencies.
/// </summary>
public abstract class Sensor : NetworkBehaviour
{
    /// <summary>True while a player pawn is within FOV and line-of-sight.</summary>
    public bool HasVisibleTarget { get; protected set; }

    /// <summary>The currently visible player Pawn, or null.</summary>
    public Pawn VisibleTarget { get; protected set; }

    /// <summary>
    /// The last world-space position where a target was seen.
    /// Valid even when HasVisibleTarget is false.
    /// </summary>
    public Vector3 LastKnownTargetPosition { get; protected set; }
}
