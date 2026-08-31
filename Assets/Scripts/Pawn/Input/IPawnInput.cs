using UnityEngine;

/// <summary>
/// Input data sent over the unreliable data channel
/// </summary>
public struct MovementInputData
{
    public Vector2 Move;
    public bool Jump;
    public Vector2 Look;
}
