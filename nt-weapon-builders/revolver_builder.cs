// Revolver builder: the starting weapon, copied from Base Weapon.prefab.
// Safe to rerun: existing assets are overwritten in place (same GUIDs). To change a weapon, edit its
// entry in the WEAPONS section and rerun.
const string BasePath = "Assets/Weapons/Base Weapon.prefab";
const string WeaponDir = "Assets/Weapons/Nuclear Throne";
const string MaterialDir = "Assets/Weapons/Nuclear Throne/Materials";
const string ProjectileDir = "Assets/Projectiles";
const int ModelLayer = 7;   // everything under the root
const int WeaponLayer = 11; // the root: free weapons collide with Default only

var log = new System.Text.StringBuilder();

// -------------------- helpers --------------------

Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

// Copies `from` to `to` unless `to` already exists (keeps its GUID on reruns).
bool EnsureCopy(string from, string to)
{
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(to) != null) return true;
    bool ok = UnityEditor.AssetDatabase.CopyAsset(from, to);
    if (!ok) log.AppendLine("FAILED copy " + from + " -> " + to);
    return ok;
}

// Projectile: copy of `from`, with only fields Projectile already has changed.
void Projectile(string name, string from, int damage, float speed, float lifetime, Vector3 visualsScale,
                float explosionRadius = -1f, int explosionDamage = -1)
{
    string path = ProjectileDir + "/" + name + ".prefab";
    if (!EnsureCopy(from, path)) return;
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    try
    {
        root.name = name;
        var so = new UnityEditor.SerializedObject(root.GetComponent<global::Projectile>());
        so.FindProperty("damage").intValue = damage;
        so.FindProperty("speed").floatValue = speed;
        so.FindProperty("lifetime").floatValue = lifetime;
        if (explosionRadius >= 0f) so.FindProperty("explosion.Radius").floatValue = explosionRadius;
        if (explosionDamage >= 0) so.FindProperty("explosion.Damage").intValue = explosionDamage;
        so.ApplyModifiedPropertiesWithoutUndo();
        root.transform.Find("Visuals").localScale = visualsScale;
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
        log.AppendLine((ok ? "projectile " : "FAILED projectile ") + path);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}

(string part, string mat, Vector3 pos, Vector3 rot, Vector3 scale) Cube(string part, string mat, Vector3 pos, Vector3 scale, Vector3 rot = default)
    => (part, mat, pos, rot, scale);

// Weapon: copy of the base, cubes under Weapon Model moved so the top-centre of the `grip` part is at
// the origin, points placed from the barrel, stats set.
// `barrel` may name several parts separated by commas; the points then use the group's centre.
void Weapon(string name, string barrel, string projectile,
            AmmoType ammoType, global::Weapon.FireMode fireMode, float cooldown, int burstCount,
            int projectilesPerShot, float spreadAngle, float fixedSpreadAngle, int ammoPerShot,
            (string part, string mat, Vector3 pos, Vector3 rot, Vector3 scale)[] cubes,
            float burstInterval = -1f, string grip = "Grip")
{
    string path = WeaponDir + "/" + name + ".prefab";
    if (!EnsureCopy(BasePath, path)) return;
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    try
    {
        root.name = name;
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var model = root.transform.Find("Weapon Model");
        for (int i = model.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(model.GetChild(i).gameObject);

        foreach (var c in cubes)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.name = c.part;
            go.layer = ModelLayer;
            go.transform.SetParent(model, false);
            go.transform.localPosition = c.pos;
            go.transform.localEulerAngles = c.rot;
            go.transform.localScale = c.scale;
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/" + c.mat + ".mat");
            if (mat == null) log.AppendLine("  missing material " + c.mat);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // Top-centre of the grip goes to the origin (where the hand holds it).
        var gripPart = model.Find(grip);
        if (gripPart == null) log.AppendLine("  missing grip part " + grip);
        else
        {
            Vector3 gripTop = gripPart.localPosition + gripPart.localRotation * new Vector3(0f, gripPart.localScale.y * 0.5f, 0f);
            foreach (Transform cube in model) cube.localPosition -= gripTop;
        }

        // Root is at the origin with no rotation, so world bounds are root-local bounds.
        var modelBounds = new Bounds();
        bool first = true;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (first) { modelBounds = r.bounds; first = false; }
            else modelBounds.Encapsulate(r.bounds);
        }
        var barrelBounds = new Bounds();
        first = true;
        foreach (var part in barrel.Split(','))
        {
            var t = model.Find(part.Trim());
            if (t == null) { log.AppendLine("  missing barrel part " + part); continue; }
            var b = t.GetComponent<Renderer>().bounds;
            if (first) { barrelBounds = b; first = false; }
            else barrelBounds.Encapsulate(b);
        }
        float barrelFront = barrelBounds.max.z;
        float x = barrelBounds.center.x, y = barrelBounds.center.y;

        var muzzlePoint = root.transform.Find("Muzzle Point");
        var spawnPoint = root.transform.Find("Spawn Point");
        var flash = root.transform.Find("Muzzle Flash");
        muzzlePoint.localPosition = V(x, y, barrelFront + 0.2f);
        muzzlePoint.localRotation = Quaternion.identity;
        spawnPoint.localPosition = V(x, y, modelBounds.min.z);
        spawnPoint.localRotation = Quaternion.identity;
        flash.localPosition = V(x, y, barrelFront + 0.1f);

        // World physics, as on Base Weapon (copies made before Phase 3 lack it).
        root.layer = WeaponLayer;
        if (!root.TryGetComponent(out Rigidbody body)) body = root.AddComponent<Rigidbody>();
        if (!root.TryGetComponent(out BoxCollider box)) box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = Vector3.one;
        box.excludeLayers = ~(1 << 0);

        var w = root.GetComponent<global::Weapon>();
        var wso = new UnityEditor.SerializedObject(w);
        wso.FindProperty("body").objectReferenceValue = body;
        wso.FindProperty("worldCollider").objectReferenceValue = box;
        wso.ApplyModifiedPropertiesWithoutUndo();
        w.ammoType = ammoType;
        w.fireMode = fireMode;
        w.cooldown = cooldown;
        w.burstCount = burstCount;
        w.projectilesPerShot = projectilesPerShot;
        w.spreadAngle = spreadAngle;
        w.fixedSpreadAngle = fixedSpreadAngle;
        w.ammoPerShot = ammoPerShot;
        w.clipSize = 0;
        w.burstInterval = burstInterval >= 0f ? burstInterval : 0.067f;
        w.projectilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<global::Projectile>(ProjectileDir + "/" + projectile + ".prefab");
        if (w.projectilePrefab == null) log.AppendLine("  missing projectile " + projectile);

        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
        log.AppendLine((ok ? "weapon " : "FAILED weapon ") + path + "  bounds size " + modelBounds.size.ToString("F2")
                       + " z " + modelBounds.min.z.ToString("F2") + ".." + modelBounds.max.z.ToString("F2"));
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}

// -------------------- WEAPONS --------------------
// +Z is the barrel, +Y up, root at the top of the grip. Sprite right = +Z, sprite up = +Y.
// The Revolver fires the existing NT Bullet (3 damage), so no projectiles are made here.
var Single = global::Weapon.FireMode.SingleShot;

// Handheld. Small pistol: grey frame and cylinder, short barrel with a front sight, hammer at the back,
// straight wooden grip (the sprite's grip is vertical, so no rake).
Weapon("Revolver", barrel: "Barrel", projectile: "NT Bullet",
    AmmoType.Bullet, Single, cooldown: 0.20f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 4, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Frame",       "Blockout Light Metal", V(0,  0.00f,  0.12f), V(0.11f, 0.12f, 0.40f)),
        Cube("Cylinder",    "Blockout Metal",       V(0, -0.03f,  0.10f), V(0.13f, 0.14f, 0.16f)),
        Cube("Barrel",      "Blockout Light Metal", V(0,  0.02f,  0.47f), V(0.07f, 0.08f, 0.30f)),
        Cube("Grip",        "Blockout Wood",        V(0, -0.20f, -0.04f), V(0.09f, 0.32f, 0.11f)),
        Cube("Hammer",      "Blockout Metal",       V(0,  0.08f, -0.10f), V(0.05f, 0.06f, 0.07f)),
        Cube("Front Sight", "Blockout Metal",       V(0,  0.08f,  0.58f), V(0.03f, 0.05f, 0.04f)),
    });

return log.ToString();
