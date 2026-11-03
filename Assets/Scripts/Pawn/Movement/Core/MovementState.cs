using UnityEngine;

/// <summary>
/// Snapshot of the CharacterController this frame, before it moves.
/// </summary>
[System.Serializable]
public struct MovementState
{
    /// <summary>CharacterController velocity of the player.</summary>
    public Vector3 Velocity;

    /// <summary>Velocity the processor asked the CharacterController for last tick.</summary>
    public Vector3 RequestedVelocity;

    /// <summary>Snap velocity the processor asked for last tick, on top of <see cref="RequestedVelocity"/>.</summary>
    public Vector3 SnapVelocity;

    /// <summary>World position of the bottom of the capsule, where the ground probes start.</summary>
    public Vector3 Position;

    /// <summary>Radius of the capsule.</summary>
    public float Radius;

    /// <summary>Gap the CharacterController keeps between the capsule and what it touches.</summary>
    public float SkinWidth;

    /// <summary>Steepest ground the CharacterController can walk on, in degrees.</summary>
    public float SlopeLimit;

    /// <summary>Yaw rotation of the character, used to transform local move input to world space.</summary>
    public Quaternion Rotation;
}
