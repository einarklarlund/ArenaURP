using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of all <see cref="AbilityType"/> assets, indexed by list position. Mirrors
/// <c>WeaponDatabase</c>: because ScriptableObject references can't be serialized across the network,
/// the network layer syncs the ability's <see cref="GetID"/> index and resolves it locally via
/// <see cref="GetAbility"/>. Lives at <c>Resources/AbilityDatabase.asset</c> so every peer loads the
/// same instance and indices line up. Add every new ability here.
/// </summary>
[CreateAssetMenu(fileName = "AbilityDatabase", menuName = "Arena/Ability Database")]
public class AbilityDatabase : ScriptableObject
{
    [SerializeField] private List<AbilityType> abilities = new();

    public int GetID(AbilityType ability) => abilities.IndexOf(ability);

    public AbilityType GetAbility(int id)
    {
        if (id < 0 || id >= abilities.Count) return null;
        return abilities[id];
    }

    public IReadOnlyList<AbilityType> All => abilities;

    private static AbilityDatabase _instance;
    public static AbilityDatabase Instance
    {
        get
        {
            if (_instance == null) _instance = Resources.Load<AbilityDatabase>("AbilityDatabase");
            return _instance;
        }
    }
}
