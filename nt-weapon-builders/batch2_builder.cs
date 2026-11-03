// Batch 2 builder: NT projectiles, then weapons copied from Base Weapon.prefab.
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

// -------------------- PROJECTILES --------------------
// Existing bullet: Visuals scale 0.4 under a root scale of 2. Explosive projectiles explode both on
// contact and when their lifetime runs out (Projectile.Die always resolves the explosion).

const string Bullet = "Assets/Prefabs/Projectiles/Bullet Prefab.prefab";
const string ExplodingBullet = "Assets/Prefabs/Projectiles/Exploding Bullet Prefab.prefab";
Projectile("NT Heavy Bolt", Bullet,          damage: 50, speed: 110, lifetime: 0.5f, visualsScale: V(0.2f, 0.2f, 1.2f));
Projectile("NT Grenade",    ExplodingBullet, damage: 15, speed: 35,  lifetime: 0.6f, visualsScale: V(0.5f, 0.5f, 0.5f),
           explosionRadius: 4f, explosionDamage: 5);
Projectile("NT Rocket",     ExplodingBullet, damage: 20, speed: 45,  lifetime: 0.8f, visualsScale: V(0.3f, 0.3f, 0.8f),
           explosionRadius: 4.5f, explosionDamage: 5);

// -------------------- WEAPONS --------------------
// +Z is the barrel, +Y up, root at the top of the grip. Sprite right = +Z, sprite up = +Y.
var Auto = global::Weapon.FireMode.Automatic;
var Single = global::Weapon.FireMode.SingleShot;
var Burst = global::Weapon.FireMode.Burst;

// Compact. Short wooden stock, boxy body, fat short barrel along the top, big drum hanging under the body.
Weapon("Grenade Launcher", barrel: "Barrel", projectile: "NT Grenade",
    AmmoType.Explosive, Single, cooldown: 0.67f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 3, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Stock",  "Blockout Wood",        V(0, -0.05f, -0.55f), V(0.12f, 0.18f, 0.40f), V(-8, 0, 0)),
        Cube("Body",   "Blockout Metal",       V(0,  0.00f, -0.05f), V(0.18f, 0.20f, 0.50f)),
        Cube("Grip",   "Blockout Wood",        V(0, -0.17f, -0.22f), V(0.10f, 0.22f, 0.11f), V(-12, 0, 0)),
        Cube("Drum",   "Blockout Explosive",   V(0, -0.18f,  0.05f), V(0.16f, 0.22f, 0.24f)),
        Cube("Barrel", "Blockout Light Metal", V(0,  0.02f,  0.45f), V(0.13f, 0.13f, 0.70f)),
    });

// Handheld. Uzi-like: boxy body, stub barrel, short rear stub, magazine in the grip.
Weapon("SMG", barrel: "Barrel", projectile: "NT Bullet",
    AmmoType.Bullet, Auto, cooldown: 0.10f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 16, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Body",     "Blockout Metal",       V(0,  0.00f,  0.05f), V(0.13f, 0.16f, 0.55f)),
        Cube("Barrel",   "Blockout Light Metal", V(0,  0.02f,  0.425f), V(0.06f, 0.06f, 0.20f)),
        Cube("Stock",    "Blockout Metal",       V(0,  0.01f, -0.32f), V(0.08f, 0.10f, 0.20f)),
        Cube("Grip",     "Blockout Metal",       V(0, -0.21f, -0.02f), V(0.10f, 0.34f, 0.12f), V(-8, 0, 0)),
        Cube("Magazine", "Blockout Bullet",      V(0, -0.40f, -0.05f), V(0.08f, 0.10f, 0.09f), V(-8, 0, 0)),
    });

// Heavy. Fat grey housing over most of the length, three barrels side by side poking out the front,
// wooden grip and stock.
Weapon("Triple Machinegun", barrel: "Barrel L,Barrel C,Barrel R", projectile: "NT Bullet",
    AmmoType.Bullet, Auto, cooldown: 0.13f, burstCount: 1, projectilesPerShot: 3, spreadAngle: 3, fixedSpreadAngle: 15, ammoPerShot: 3,
    new[] {
        Cube("Stock",    "Blockout Wood",        V(0,     -0.08f, -0.85f), V(0.14f, 0.24f, 0.50f), V(-6, 0, 0)),
        Cube("Housing",  "Blockout Light Metal", V(0,      0.00f,  0.10f), V(0.36f, 0.30f, 1.40f)),
        Cube("Grip",     "Blockout Wood",        V(0,     -0.26f, -0.20f), V(0.12f, 0.26f, 0.13f), V(-12, 0, 0)),
        Cube("Barrel L", "Blockout Metal",       V(-0.12f, 0.03f,  1.325f), V(0.09f, 0.09f, 1.05f)),
        Cube("Barrel C", "Blockout Metal",       V(0,      0.03f,  1.325f), V(0.09f, 0.09f, 1.05f)),
        Cube("Barrel R", "Blockout Metal",       V(0.12f,  0.03f,  1.325f), V(0.09f, 0.09f, 1.05f)),
    });

// Long. Shotgun layout with two barrels stacked and a long wooden forend.
Weapon("Double Shotgun", barrel: "Barrel Top,Barrel Bottom", projectile: "NT Pellet",
    AmmoType.Shell, Single, cooldown: 0.93f, burstCount: 1, projectilesPerShot: 14, spreadAngle: 30, fixedSpreadAngle: 0, ammoPerShot: 2,
    new[] {
        Cube("Stock",         "Blockout Wood",        V(0, -0.11f, -0.68f), V(0.14f, 0.28f, 0.56f), V(-10, 0, 0)),
        Cube("Receiver",      "Blockout Metal",       V(0,  0.00f, -0.15f), V(0.16f, 0.22f, 0.50f)),
        Cube("Grip",          "Blockout Wood",        V(0, -0.19f, -0.20f), V(0.10f, 0.20f, 0.12f), V(-12, 0, 0)),
        Cube("Barrel Top",    "Blockout Light Metal", V(0,  0.06f,  0.94f), V(0.09f, 0.09f, 1.68f)),
        Cube("Barrel Bottom", "Blockout Light Metal", V(0, -0.04f,  0.94f), V(0.09f, 0.09f, 1.68f)),
        Cube("Forend",        "Blockout Wood",        V(0, -0.12f,  0.55f), V(0.13f, 0.10f, 0.60f)),
    });

// Long. Slugger with a box magazine under the receiver.
Weapon("Assault Slugger", barrel: "Barrel", projectile: "NT Slug",
    AmmoType.Shell, Burst, cooldown: 1.17f, burstCount: 3, projectilesPerShot: 1, spreadAngle: 4, fixedSpreadAngle: 0, ammoPerShot: 3,
    new[] {
        Cube("Stock",    "Blockout Wood",        V(0, -0.10f, -0.70f), V(0.14f, 0.26f, 0.50f), V(-10, 0, 0)),
        Cube("Receiver", "Blockout Metal",       V(0,  0.00f, -0.10f), V(0.18f, 0.24f, 0.60f)),
        Cube("Grip",     "Blockout Wood",        V(0, -0.20f, -0.25f), V(0.10f, 0.20f, 0.12f), V(-12, 0, 0)),
        Cube("Magazine", "Blockout Shell",       V(0, -0.24f,  0.08f), V(0.12f, 0.26f, 0.16f), V(-6, 0, 0)),
        Cube("Barrel",   "Blockout Light Metal", V(0,  0.03f,  0.99f), V(0.16f, 0.16f, 1.58f)),
        Cube("Pump",     "Blockout Wood",        V(0, -0.10f,  0.95f), V(0.15f, 0.12f, 0.45f)),
    });

// Heavy, limbs span X (~1.3). Thick wooden body, grey riser block at the front, thick limbs, heavy bolt on top.
Weapon("Heavy Crossbow", barrel: "Bolt", projectile: "NT Heavy Bolt",
    AmmoType.Bolt, Single, cooldown: 1.33f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 0, fixedSpreadAngle: 0, ammoPerShot: 2,
    new[] {
        Cube("Stock", "Blockout Wood",        V(0, -0.10f, -0.65f), V(0.18f, 0.26f, 0.75f)),
        Cube("Body",  "Blockout Wood",        V(0,  0.00f,  0.70f), V(0.20f, 0.18f, 2.00f)),
        Cube("Grip",  "Blockout Wood",        V(0, -0.20f, -0.10f), V(0.12f, 0.24f, 0.13f), V(-12, 0, 0)),
        Cube("Riser", "Blockout Metal",       V(0,  0.00f,  1.40f), V(0.34f, 0.24f, 0.22f)),
        Cube("Limbs", "Blockout Light Metal", V(0,  0.00f,  1.58f), V(1.30f, 0.14f, 0.16f)),
        Cube("Bolt",  "Blockout Bolt",        V(0,  0.13f,  1.00f), V(0.09f, 0.09f, 1.75f)),
    });

// Heavy. One long tube with end caps, pistol grip and foregrip below, sight on top.
Weapon("Bazooka", barrel: "Tube,Front Cap", projectile: "NT Rocket",
    AmmoType.Explosive, Single, cooldown: 1.00f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 2, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Tube",      "Blockout Metal",       V(0,  0.02f,  0.45f), V(0.32f, 0.32f, 2.85f)),
        Cube("Rear Cap",  "Blockout Light Metal", V(0,  0.02f, -0.90f), V(0.36f, 0.36f, 0.18f)),
        Cube("Front Cap", "Blockout Light Metal", V(0,  0.02f,  1.78f), V(0.36f, 0.36f, 0.22f)),
        Cube("Grip",      "Blockout Metal",       V(0, -0.24f, -0.05f), V(0.12f, 0.26f, 0.13f), V(-10, 0, 0)),
        Cube("Foregrip",  "Blockout Metal",       V(0, -0.22f,  0.75f), V(0.10f, 0.22f, 0.12f)),
        Cube("Sight",     "Blockout Explosive",   V(0,  0.22f,  0.10f), V(0.06f, 0.10f, 0.20f)),
    });

// Heavy. Brown body at the rear with a carry handle, long barrel cluster with two rings.
Weapon("Minigun", barrel: "Barrels", projectile: "NT Bullet",
    AmmoType.Bullet, Auto, cooldown: 0.03f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 13, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Body",         "Blockout Wood",        V(0,  0.00f, -0.525f), V(0.36f, 0.34f, 0.85f)),
        Cube("Grip",         "Blockout Wood",        V(0, -0.24f, -0.35f), V(0.12f, 0.24f, 0.13f), V(-12, 0, 0)),
        Cube("Barrels",      "Blockout Light Metal", V(0,  0.02f,  0.875f), V(0.24f, 0.24f, 1.95f)),
        Cube("Ring Mid",     "Blockout Metal",       V(0,  0.02f,  0.70f), V(0.32f, 0.32f, 0.12f)),
        Cube("Ring Front",   "Blockout Metal",       V(0,  0.02f,  1.75f), V(0.32f, 0.32f, 0.12f)),
        Cube("Carry Handle", "Blockout Metal",       V(0,  0.21f, -0.35f), V(0.08f, 0.10f, 0.50f)),
    });

// Compact. Two short stacked barrels, wooden forend, pistol grip raked back like the sprite, no stock.
Weapon("Sawed-off Shotgun", barrel: "Barrel Top,Barrel Bottom", projectile: "NT Pellet",
    AmmoType.Shell, Single, cooldown: 0.93f, burstCount: 1, projectilesPerShot: 18, spreadAngle: 45, fixedSpreadAngle: 0, ammoPerShot: 2,
    new[] {
        Cube("Grip",          "Blockout Wood",        V(0, -0.18f, -0.42f), V(0.11f, 0.26f, 0.14f), V(20, 0, 0)),
        Cube("Receiver",      "Blockout Metal",       V(0,  0.00f, -0.25f), V(0.16f, 0.20f, 0.40f)),
        Cube("Barrel Top",    "Blockout Light Metal", V(0,  0.05f,  0.45f), V(0.09f, 0.09f, 1.00f)),
        Cube("Barrel Bottom", "Blockout Light Metal", V(0, -0.05f,  0.45f), V(0.09f, 0.09f, 1.00f)),
        Cube("Forend",        "Blockout Wood",        V(0, -0.12f,  0.30f), V(0.13f, 0.10f, 0.45f)),
    });

// Long. Slim dark shotgun: stock, receiver, small drum under it, long barrel over a thick forend.
Weapon("Auto Shotgun", barrel: "Barrel", projectile: "NT Pellet",
    AmmoType.Shell, Auto, cooldown: 0.13f, burstCount: 1, projectilesPerShot: 6, spreadAngle: 15, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Stock",    "Blockout Metal",       V(0, -0.08f, -0.68f), V(0.12f, 0.20f, 0.60f), V(-6, 0, 0)),
        Cube("Receiver", "Blockout Metal",       V(0,  0.00f, -0.05f), V(0.16f, 0.20f, 0.70f)),
        Cube("Grip",     "Blockout Metal",       V(0, -0.18f, -0.20f), V(0.10f, 0.22f, 0.12f), V(-12, 0, 0)),
        Cube("Drum",     "Blockout Shell",       V(0, -0.17f,  0.18f), V(0.14f, 0.18f, 0.20f)),
        Cube("Barrel",   "Blockout Light Metal", V(0,  0.04f,  1.04f), V(0.09f, 0.09f, 1.48f)),
        Cube("Forend",   "Blockout Metal",       V(0, -0.05f,  0.75f), V(0.13f, 0.12f, 0.60f)),
    });

UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
