// Phase 2 builder: blockout materials and Assets/Weapons/Base Weapon.prefab. Safe to rerun:
// existing materials are updated in place and the base prefab is overwritten at the same GUID.
const string MaterialDir = "Assets/Weapons/Nuclear Throne/Materials";
const string BasePath = "Assets/Weapons/Base Weapon.prefab";
const string FlashPath = "Assets/Weapons/Test Uzi/Gun Muzzle Flash Effect.prefab";
const string SmokePath = "Assets/Weapons/Test Uzi/Gun Smoke Effect.prefab";
const int WeaponLayer = 7;

var log = new System.Text.StringBuilder();

// ---- Materials ----
var materials = new (string name, Color color)[]
{
    ("Blockout Metal",       new Color(0.20f, 0.20f, 0.22f)),
    ("Blockout Light Metal", new Color(0.50f, 0.50f, 0.53f)),
    ("Blockout Wood",        new Color(0.45f, 0.28f, 0.14f)),
    ("Blockout Bullet",      new Color(0.95f, 0.80f, 0.15f)),
    ("Blockout Shell",       new Color(0.80f, 0.15f, 0.12f)),
    ("Blockout Bolt",        new Color(0.25f, 0.70f, 0.25f)),
    ("Blockout Explosive",   new Color(0.95f, 0.50f, 0.10f)),
    ("Blockout Hyper",       new Color(0.30f, 0.42f, 0.95f)), // Hyper Rifle fins (added in Batch 3)
};
var lit = Shader.Find("Universal Render Pipeline/Lit");
if (lit == null) return "URP Lit shader not found";
foreach (var (name, color) in materials)
{
    string path = MaterialDir + "/" + name + ".mat";
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    bool isNew = mat == null;
    if (isNew) mat = new Material(lit);
    mat.shader = lit;
    mat.SetColor("_BaseColor", color);
    mat.SetFloat("_Smoothness", 0.3f);
    if (isNew) UnityEditor.AssetDatabase.CreateAsset(mat, path);
    else UnityEditor.EditorUtility.SetDirty(mat);
    log.AppendLine((isNew ? "created " : "updated ") + path);
}
UnityEditor.AssetDatabase.SaveAssets();

// ---- Base Weapon prefab ----
var flashPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(FlashPath);
var smokePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SmokePath);
if (flashPrefab == null || smokePrefab == null) return "missing VFX prefab";

GameObject Child(Transform parent, string name)
{
    var go = new GameObject(name);
    go.layer = WeaponLayer;
    go.transform.SetParent(parent, false);
    return go;
}

var root = new GameObject("Base Weapon");
try
{
    root.layer = WeaponLayer;
    var weapon = root.AddComponent<Weapon>();
    root.AddComponent<FishNet.Object.NetworkObject>();

    Child(root.transform, "Weapon Model");
    var muzzlePoint = Child(root.transform, "Muzzle Point");
    var spawnPoint = Child(root.transform, "Spawn Point");

    var flash = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(flashPrefab, root.transform);
    flash.name = "Muzzle Flash";
    flash.transform.localPosition = Vector3.zero;
    flash.transform.localEulerAngles = new Vector3(270f, 0f, 0f);
    foreach (var t in flash.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = WeaponLayer;

    weapon.muzzlePoint = muzzlePoint.transform;
    weapon.spawnPoint = spawnPoint.transform;
    weapon.muzzleFlash = flash.GetComponent<ParticleSystem>();
    weapon.smokeEffects = smokePrefab;
    weapon.clipSize = 0;
    weapon.animator = null;
    weapon.ragdoll = null;
    weapon.projectilePrefab = null;

    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, BasePath, out bool ok);
    log.AppendLine((ok ? "saved " : "FAILED to save ") + BasePath);
}
finally
{
    UnityEngine.Object.DestroyImmediate(root);
}
return log.ToString();
