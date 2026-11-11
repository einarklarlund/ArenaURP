using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The collision side of movement: the hull that <see cref="MovementProcessor"/> moves and probes, in metres.
/// Plays the part of Source's hull traces.
/// </summary>
public interface IMovementBody
{
    /// <summary>Moves by <paramref name="displacement"/>, sliding along what it hits, and adds the normal of each
    /// surface it ran into to <paramref name="hitNormals"/>. Returns how far it actually moved.</summary>
    Vector3 Move(Vector3 displacement, List<Vector3> hitNormals);

    /// <summary>Whether ground whose normal's up component is at least <paramref name="minNormalY"/> is within
    /// <paramref name="distance"/> below the hull, and that ground's normal.</summary>
    bool ProbeGround(float distance, float minNormalY, out Vector3 normal);

    /// <summary>Lowers the hull onto standable ground up to <paramref name="maxDrop"/> below it (StayOnGround()).</summary>
    void StayOnGround(float maxDrop, float minNormalY);

    /// <summary>Whether the hull has room to be <paramref name="height"/> tall.</summary>
    bool HasRoomForHeight(float height, bool keepTop);

    /// <summary>Changes the hull's height if there is room. <paramref name="keepTop"/> keeps the top of the hull where
    /// it is and moves the feet (crouching in the air), otherwise the feet stay.</summary>
    bool TrySetHeight(float height, bool keepTop);
}
