using UnityEngine;

/// <summary>
/// How experience turns into levels. Thresholds are total experience: 15, 30, 50, 70, 90, 110, …
/// (the first two levels take 15 each, every later one 20).
/// </summary>
public static class Levels
{
    /// <summary>Total experience needed to reach <paramref name="level"/>. Level 0 needs none.</summary>
    public static int Threshold(int level)
    {
        if (level <= 0) return 0;
        if (level <= 2) return 15 * level;
        return 30 + 20 * (level - 2);
    }

    public static int ForExperience(int experience)
    {
        int level = 0;
        while (experience >= Threshold(level + 1))
            level++;
        return level;
    }

    /// <summary>How much experience a player with <paramref name="experience"/> drops when killed: half, at least 15.</summary>
    public static int DeathDrop(int experience) => Mathf.Max(15, experience / 2);
}
