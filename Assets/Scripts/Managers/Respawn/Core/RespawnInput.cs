using System.Collections.Generic;

/// <summary>
/// Per-tick input to <see cref="RespawnProcessor"/>.
/// </summary>
[System.Serializable]
public struct RespawnInput
{
    /// <summary>
    /// Newly-killed players this tick. The processor enqueues a fresh pending respawn for
    /// each key, using the current <see cref="RespawnConfig.DelaySeconds"/>.
    /// May be null or empty when no new deaths occurred this tick.
    /// </summary>
    public IReadOnlyList<object> NewlyKilled;

    public static RespawnInput Default => new()
    {
        NewlyKilled = null,
    };
}
