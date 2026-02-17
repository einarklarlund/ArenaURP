using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

public class WeaponDatabaseGenerator
{
    [MenuItem("FPS/Update Weapon Database")]
    public static void UpdateDatabase()
    {
        // 1. Find the Database asset
        WeaponDatabase db = WeaponDatabase.Instance;
        if (db == null)
        {
            Debug.LogError("No WeaponDatabase found in Resources! Create one first.");
            return;
        }

        // 2. Find all WeaponData assets in the project
        // This looks specifically for WeaponData type assets
        string[] guids = AssetDatabase.FindAssets("t:WeaponData");
        
        List<WeaponData> foundWeapons = guids
            .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid)))
            .OrderBy(w => w.name) // IMPORTANT: Alphabetical order ensures consistent IDs
            .ToList();

        // 3. Update and Save
        db.ReplaceWeapons(foundWeapons);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"Successfully synchronized {foundWeapons.Count} weapons to the Database.");
    }
}