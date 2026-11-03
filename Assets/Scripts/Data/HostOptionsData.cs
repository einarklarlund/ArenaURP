using System;

/// <summary>
/// Plain data class holding per-match host configuration.
/// Add new fields here as more host options are needed in the future.
/// </summary>
[Serializable]
public class HostOptionsData
{
    public int BotCount = 0;

    // The level: a share code wins over the seed and preset when it is set.
    public bool RandomLevelSeed = true;
    public ulong LevelSeed = 0;
    public int LevelPreset = 0;
    public int LevelTheme = 0;
    public string LevelShareCode = "";
}