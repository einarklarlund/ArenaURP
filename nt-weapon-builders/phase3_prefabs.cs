// Phase 3 prefab changes. Safe to rerun.
// 1. Layer 11 "Weapon".
// 2. World physics (Rigidbody + 1x1x1 BoxCollider, root on layer 11) on Base Weapon, Test Uzi, Test Shotgun.
// 3. Pawn Root: nested starting weapons removed; starting-weapon prefabs, hold points and pickup layers set.
const int WeaponLayer = 11;
const string UziPath = "Assets/Weapons/Test Uzi/Uzi Prefab.prefab";
const string ShotgunPath = "Assets/Weapons/Test Shotgun/Shotgun Prefab.prefab";
const string PawnRootPath = "Assets/Prefabs/Player/Pawn Root.prefab";
var log = new System.Text.StringBuilder();

// ---- 1. Layer ----
var tagManager = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
var layers = tagManager.FindProperty("layers");
var layer = layers.GetArrayElementAtIndex(WeaponLayer);
if (layer.stringValue != "Weapon")
{
    if (!string.IsNullOrEmpty(layer.stringValue)) return "layer 11 is already '" + layer.stringValue + "'";
    layer.stringValue = "Weapon";
    tagManager.ApplyModifiedPropertiesWithoutUndo();
    log.AppendLine("added layer 11 Weapon");
}
log.AppendLine("ignore 11-0: " + Physics.GetIgnoreLayerCollision(11, 0) + ", 11-11: " + Physics.GetIgnoreLayerCollision(11, 11));

// ---- 2. World physics ----
// Free weapons rest on level geometry only: pawns walk through them, projectiles ignore them, and a weapon dropped where another lay never gets pushed out of it.
int excludeLayers = ~(1 << 0);
foreach (var path in new[] { "Assets/Weapons/Base Weapon.prefab", UziPath, ShotgunPath })
{
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    try
    {
        root.layer = WeaponLayer;
        if (!root.TryGetComponent(out Rigidbody body)) body = root.AddComponent<Rigidbody>();
        if (!root.TryGetComponent(out BoxCollider box)) box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = Vector3.one;
        box.excludeLayers = excludeLayers;
        var so = new UnityEditor.SerializedObject(root.GetComponent<Weapon>());
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("worldCollider").objectReferenceValue = box;
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
        log.AppendLine((ok ? "world physics: " : "FAILED: ") + path);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}

// ---- 3. Pawn Root ----
{
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(PawnRootPath);
    try
    {
        var camera = root.transform.Find("Camera");
        var primaryHold = camera.Find("Primary Weapon");
        var secondaryHold = camera.Find("Secondary Weapon");
        foreach (var hold in new[] { primaryHold, secondaryHold })
        {
            for (int i = hold.childCount - 1; i >= 0; i--)
            {
                log.AppendLine("removed nested " + hold.name + "/" + hold.GetChild(i).name);
                UnityEngine.Object.DestroyImmediate(hold.GetChild(i).gameObject);
            }
        }
        primaryHold.localPosition = new Vector3(0.46f, -0.187f, 0.75f);
        secondaryHold.localPosition = new Vector3(-0.46f, -0.187f, 0.75f);

        var so = new UnityEditor.SerializedObject(root.GetComponent<Pawn>());
        so.FindProperty("startingPrimaryWeapon").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<Weapon>(UziPath);
        so.FindProperty("startingSecondaryWeapon").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<Weapon>(ShotgunPath);
        so.FindProperty("primaryHoldPoint").objectReferenceValue = primaryHold;
        so.FindProperty("secondaryHoldPoint").objectReferenceValue = secondaryHold;
        so.FindProperty("pickupLayers").intValue = (1 << 0) | (1 << WeaponLayer);
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, PawnRootPath, out bool ok);
        log.AppendLine((ok ? "saved " : "FAILED ") + PawnRootPath);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
