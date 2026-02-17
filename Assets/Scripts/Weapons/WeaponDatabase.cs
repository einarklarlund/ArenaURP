using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "WeaponDatabase", menuName = "FPS/Weapon Database")]
public class WeaponDatabase : ScriptableObject 
{
    [SerializeField] private List<WeaponData> weapons = new();

    public int GetID(WeaponData data) => weapons.IndexOf(data);

    public WeaponData GetWeapon(int id) 
    {
        if (id < 0 || id >= weapons.Count) return null;
        return weapons[id];
    }

    private static WeaponDatabase _instance;
    public static WeaponDatabase Instance 
    {
        get {
            if (_instance == null) _instance = Resources.Load<WeaponDatabase>("WeaponDatabase");
            return _instance;
        }
    }

    #if UNITY_EDITOR
    public void ReplaceWeapons(List<WeaponData> newWeapons) 
    {
        weapons = newWeapons;
        UnityEditor.EditorUtility.SetDirty(this);
    }
    #endif
}