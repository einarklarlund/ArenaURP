using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every upgrade in the game. An upgrade's index here is its id on the network, so only add to the end.
/// </summary>
[CreateAssetMenu(menuName = "Arena/Upgrade Catalogue")]
public sealed class UpgradeCatalogue : ScriptableObject
{
    [SerializeField] private List<Upgrade> upgrades = new();

    public int Count => upgrades.Count;

    public Upgrade this[int id] => upgrades[id];

    /// <summary>
    /// Up to two different ids, weighted, that the player may still stack. -1 fills a slot when fewer
    /// than two are available.
    /// </summary>
    public (int, int) RollOffer(IReadOnlyList<byte> picks)
    {
        int first = RollOne(picks, exclude: -1);
        int second = RollOne(picks, exclude: first);
        return (first, second);
    }

    /// <summary>One weighted id the player may still stack, or -1.</summary>
    public int RollOne(IReadOnlyList<byte> picks, int exclude)
    {
        float total = 0f;
        for (int id = 0; id < upgrades.Count; id++)
        {
            if (IsAvailable(id, picks, exclude))
                total += upgrades[id].Weight;
        }
        if (total <= 0f)
            return -1;

        float roll = Random.value * total;
        int last = -1;
        for (int id = 0; id < upgrades.Count; id++)
        {
            if (!IsAvailable(id, picks, exclude)) continue;
            last = id;
            roll -= upgrades[id].Weight;
            if (roll < 0f)
                return id;
        }
        return last; // float rounding left a sliver over
    }

    private bool IsAvailable(int id, IReadOnlyList<byte> picks, int exclude)
    {
        Upgrade upgrade = upgrades[id];
        if (id == exclude || upgrade == null || upgrade.Weight <= 0f)
            return false;
        return upgrade.MaxStacks <= 0 || StacksOf(id, picks) < upgrade.MaxStacks;
    }

    public static int StacksOf(int id, IReadOnlyList<byte> picks)
    {
        int stacks = 0;
        foreach (byte pick in picks)
        {
            if (pick == id)
                stacks++;
        }
        return stacks;
    }
}
