// Kick builder: creates Assets/Weapons/Weapon Kick Settings.asset and gives Base Weapon and every
// Nuclear Throne weapon a WeaponKick on its "Weapon Model". Safe to rerun: existing kicks are rewired in place.
// The Test Uzi and Test Shotgun keep their hand-made fire clips as the reference the kick is fitted to.
const string SettingsPath = "Assets/Weapons/Weapon Kick Settings.asset";

var log = new System.Text.StringBuilder();

var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponKickSettings>(SettingsPath);
if (settings == null)
{
    settings = ScriptableObject.CreateInstance<WeaponKickSettings>();
    UnityEditor.AssetDatabase.CreateAsset(settings, SettingsPath);
    log.AppendLine("created " + SettingsPath);
}

var paths = new System.Collections.Generic.List<string> { "Assets/Weapons/Base Weapon.prefab" };
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Weapons/Nuclear Throne" }))
    paths.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));

foreach (var path in paths)
{
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    try
    {
        var weapon = root.GetComponent<Weapon>();
        var model = root.transform.Find("Weapon Model");
        if (weapon == null || model == null)
        {
            log.AppendLine("skipped " + path + " (no Weapon or no Weapon Model)");
            continue;
        }

        var kick = root.GetComponent<WeaponKick>() ?? root.AddComponent<WeaponKick>();
        var so = new UnityEditor.SerializedObject(kick);
        so.FindProperty("settings").objectReferenceValue = settings;
        so.FindProperty("model").objectReferenceValue = model;
        so.ApplyModifiedPropertiesWithoutUndo();
        weapon.kick = kick;

        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
        log.AppendLine("kicked " + path);
    }
    finally
    {
        UnityEditor.PrefabUtility.UnloadPrefabContents(root);
    }
}
UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
