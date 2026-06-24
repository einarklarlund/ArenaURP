using UnityEngine;

/// <summary>
/// Pure, stateless helpers that transform raw hardware input into
/// game-space values. Has zero Unity lifecycle dependencies so it can be
/// covered by edit-mode tests.
/// </summary>
public static class HumanInputMath
{
    /// <summary>
    /// Mouse-look delta in WebGL is roughly an order of magnitude larger
    /// than in the editor / standalone player, so we scale it down to match.
    /// </summary>
    public const float WebGLLookDivisor = 10f;

    /// <summary>
    /// Scales a raw look delta by the player's sensitivity setting,
    /// applying a WebGL-specific correction when running in the browser.
    /// </summary>
    /// <param name="rawLookDelta">Raw look vector read from the input device.</param>
    /// <param name="sensitivity">Player-configured mouse sensitivity multiplier.</param>
    /// <param name="isWebGL">True if running in a WebGL build (not the editor).</param>
    /// <returns>The look delta to feed into PawnInputData.</returns>
    public static Vector2 ApplyLookSensitivity(Vector2 rawLookDelta, float sensitivity, bool isWebGL)
    {
        Vector2 look = rawLookDelta * sensitivity;
        if (isWebGL)
        {
            look /= WebGLLookDivisor;
        }
        return look;
    }
}
