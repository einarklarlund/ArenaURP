using System;

/// <summary>
/// Plain data class holding per-match host configuration.
/// Add new fields here as more host options are needed in the future.
/// </summary>
[Serializable]
public class HostOptionsData
{
    public int BotCount = 0;
}