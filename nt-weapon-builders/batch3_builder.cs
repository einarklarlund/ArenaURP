// Batch 3 builder: NT projectiles, then weapons copied from Base Weapon.prefab.
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
Projectile("NT Heavy Bullet", Bullet,          damage: 7, speed: 90,  lifetime: 0.25f, visualsScale: V(0.6f, 0.6f, 0.6f));
Projectile("NT Splinter",     Bullet,          damage: 4, speed: 120, lifetime: 0.35f, visualsScale: V(0.08f, 0.08f, 0.5f));
Projectile("NT Mini Grenade", ExplodingBullet, damage: 3, speed: 40,  lifetime: 0.4f,  visualsScale: V(0.3f, 0.3f, 0.3f),
           explosionRadius: 2.5f, explosionDamage: 5);

// -------------------- MATERIALS --------------------
// Batch 3 adds one accent material, made the same way as phase2_base_weapon.cs's (also listed there).
{
    string path = MaterialDir + "/Blockout Hyper.mat";
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    bool isNew = mat == null;
    if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
    mat.SetColor("_BaseColor", new Color(0.30f, 0.42f, 0.95f));
    mat.SetFloat("_Smoothness", 0.3f);
    if (isNew) UnityEditor.AssetDatabase.CreateAsset(mat, path);
    else UnityEditor.EditorUtility.SetDirty(mat);
    log.AppendLine((isNew ? "created " : "updated ") + path);
}

// -------------------- WEAPONS --------------------
// +Z is the barrel, +Y up, root at the top of the grip. Sprite right = +Z, sprite up = +Y.
var Auto = global::Weapon.FireMode.Automatic;
var Single = global::Weapon.FireMode.SingleShot;
var Burst = global::Weapon.FireMode.Burst;

// Compact. Wooden stock, fat grey body, wide barrel with a wooden forend under it.
Weapon("Grenade Shotgun", barrel: "Barrel", projectile: "NT Mini Grenade",
    AmmoType.Explosive, Single, cooldown: 0.53f, burstCount: 1, projectilesPerShot: 4, spreadAngle: 17, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Stock",  "Blockout Wood",        V(0, -0.05f, -0.55f), V(0.12f, 0.16f, 0.40f), V(-8, 0, 0)),
        Cube("Body",   "Blockout Metal",       V(0,  0.00f, -0.05f), V(0.20f, 0.26f, 0.40f)),
        Cube("Grip",   "Blockout Wood",        V(0, -0.20f, -0.10f), V(0.10f, 0.22f, 0.11f), V(-12, 0, 0)),
        Cube("Barrel", "Blockout Light Metal", V(0,  0.02f,  0.50f), V(0.17f, 0.17f, 0.70f)),
        Cube("Forend", "Blockout Explosive",   V(0, -0.10f,  0.40f), V(0.12f, 0.08f, 0.40f)),
    });

// Long. Grey stock, long wooden body, tall grey head at the front with splinters sticking out of it.
Weapon("Splinter Gun", barrel: "Spikes", projectile: "NT Splinter",
    AmmoType.Bolt, Single, cooldown: 0.63f, burstCount: 1, projectilesPerShot: 5, spreadAngle: 10, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Stock",  "Blockout Light Metal", V(0, -0.08f, -0.70f), V(0.12f, 0.18f, 0.60f)),
        Cube("Body",   "Blockout Wood",        V(0,  0.00f,  0.40f), V(0.14f, 0.16f, 1.60f)),
        Cube("Grip",   "Blockout Wood",        V(0, -0.18f, -0.15f), V(0.10f, 0.22f, 0.12f), V(-12, 0, 0)),
        Cube("Head",   "Blockout Metal",       V(0,  0.00f,  1.26f), V(0.16f, 0.40f, 0.12f)),
        Cube("Spikes", "Blockout Bolt",        V(0,  0.00f,  1.50f), V(0.08f, 0.24f, 0.40f)),
    });

// Long, limbs span X (~0.9). Dark crossbow with a bolt magazine on top behind the loaded bolt.
Weapon("Auto Crossbow", barrel: "Bolt", projectile: "NT Bolt",
    AmmoType.Bolt, Auto, cooldown: 0.27f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 5, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Stock",    "Blockout Metal",       V(0, -0.08f, -0.65f), V(0.12f, 0.20f, 0.60f)),
        Cube("Body",     "Blockout Metal",       V(0,  0.00f,  0.525f), V(0.14f, 0.14f, 1.75f)),
        Cube("Grip",     "Blockout Metal",       V(0, -0.18f, -0.10f), V(0.10f, 0.22f, 0.12f), V(-12, 0, 0)),
        Cube("Magazine", "Blockout Bolt",        V(0,  0.13f,  0.15f), V(0.12f, 0.12f, 0.40f)),
        Cube("Limbs",    "Blockout Light Metal", V(0,  0.00f,  1.45f), V(0.90f, 0.08f, 0.10f)),
        Cube("Bolt",     "Blockout Bolt",        V(0,  0.09f,  1.20f), V(0.05f, 0.05f, 1.00f)),
    });

// Heavy, limbs span X (~1.4). Long body and riser, wide limbs swept back into an arrowhead, five bolts
// loaded side by side. More than 6 cubes (the user lifted the limit for this weapon).
Weapon("Super Crossbow", barrel: "Bolt 1,Bolt 2,Bolt 3,Bolt 4,Bolt 5", projectile: "NT Bolt",
    AmmoType.Bolt, Single, cooldown: 1.00f, burstCount: 1, projectilesPerShot: 5, spreadAngle: 0, fixedSpreadAngle: 5, ammoPerShot: 5,
    new[] {
        Cube("Stock",  "Blockout Metal",       V(0,      -0.10f, -0.70f), V(0.18f, 0.24f, 0.70f)),
        Cube("Body",   "Blockout Metal",       V(0,       0.00f,  0.60f), V(0.22f, 0.18f, 1.90f)),
        Cube("Grip",   "Blockout Metal",       V(0,      -0.20f, -0.15f), V(0.12f, 0.24f, 0.13f), V(-12, 0, 0)),
        Cube("Riser",  "Blockout Light Metal", V(0,       0.00f,  1.45f), V(0.36f, 0.24f, 0.22f)),
        Cube("Limb L", "Blockout Light Metal", V(-0.34f,  0.00f,  1.39f), V(0.75f, 0.12f, 0.14f), V(0, -25, 0)),
        Cube("Limb R", "Blockout Light Metal", V(0.34f,   0.00f,  1.39f), V(0.75f, 0.12f, 0.14f), V(0, 25, 0)),
        Cube("Bolt 1", "Blockout Bolt",        V(-0.20f,  0.12f,  1.25f), V(0.05f, 0.05f, 1.00f)),
        Cube("Bolt 2", "Blockout Bolt",        V(-0.10f,  0.12f,  1.25f), V(0.05f, 0.05f, 1.00f)),
        Cube("Bolt 3", "Blockout Bolt",        V(0,       0.12f,  1.25f), V(0.05f, 0.05f, 1.00f)),
        Cube("Bolt 4", "Blockout Bolt",        V(0.10f,   0.12f,  1.25f), V(0.05f, 0.05f, 1.00f)),
        Cube("Bolt 5", "Blockout Bolt",        V(0.20f,   0.12f,  1.25f), V(0.05f, 0.05f, 1.00f)),
    });

// Handheld. Small wooden body on a grey grip, tall head at the front with splinters.
Weapon("Splinter Pistol", barrel: "Spikes", projectile: "NT Splinter",
    AmmoType.Bolt, Single, cooldown: 0.27f, burstCount: 1, projectilesPerShot: 4, spreadAngle: 4, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Body",   "Blockout Wood",        V(0,  0.00f,  0.12f), V(0.12f, 0.12f, 0.55f)),
        Cube("Grip",   "Blockout Light Metal", V(0, -0.17f, -0.05f), V(0.10f, 0.30f, 0.12f), V(-12, 0, 0)),
        Cube("Head",   "Blockout Metal",       V(0,  0.00f,  0.40f), V(0.13f, 0.30f, 0.08f)),
        Cube("Spikes", "Blockout Bolt",        V(0,  0.00f,  0.52f), V(0.06f, 0.16f, 0.20f)),
    });

// Heavy. Splinter Gun scaled up: big grey stock, thick wooden body, large head and splinter bundle.
Weapon("Super Splinter Gun", barrel: "Spikes", projectile: "NT Splinter",
    AmmoType.Bolt, Burst, cooldown: 0.93f, burstCount: 12, projectilesPerShot: 1, spreadAngle: 16, fixedSpreadAngle: 0, ammoPerShot: 2,
    new[] {
        Cube("Stock",  "Blockout Light Metal", V(0, -0.08f, -0.75f), V(0.24f, 0.26f, 0.60f)),
        Cube("Body",   "Blockout Wood",        V(0,  0.00f,  0.40f), V(0.24f, 0.20f, 1.70f)),
        Cube("Grip",   "Blockout Wood",        V(0, -0.21f, -0.15f), V(0.12f, 0.24f, 0.13f), V(-12, 0, 0)),
        Cube("Head",   "Blockout Metal",       V(0,  0.00f,  1.30f), V(0.30f, 0.50f, 0.14f)),
        Cube("Spikes", "Blockout Bolt",        V(0,  0.00f,  1.60f), V(0.20f, 0.34f, 0.50f)),
    },
    burstInterval: 0.017f);

// Long. Sleek dark rifle with blue fins above and below, long thin barrel.
Weapon("Hyper Rifle", barrel: "Barrel", projectile: "NT Bullet",
    AmmoType.Bullet, Burst, cooldown: 0.10f, burstCount: 5, projectilesPerShot: 1, spreadAngle: 2, fixedSpreadAngle: 0, ammoPerShot: 5,
    new[] {
        Cube("Stock",     "Blockout Metal",       V(0, -0.06f, -0.70f), V(0.12f, 0.20f, 0.55f)),
        Cube("Receiver",  "Blockout Metal",       V(0,  0.00f,  0.10f), V(0.16f, 0.20f, 1.10f)),
        Cube("Grip",      "Blockout Metal",       V(0, -0.18f, -0.25f), V(0.10f, 0.22f, 0.12f), V(-12, 0, 0)),
        Cube("Top Fin",   "Blockout Hyper",       V(0,  0.14f,  0.00f), V(0.06f, 0.10f, 0.80f)),
        Cube("Lower Fin", "Blockout Hyper",       V(0, -0.16f,  0.25f), V(0.06f, 0.14f, 0.50f), V(15, 0, 0)),
        Cube("Barrel",    "Blockout Light Metal", V(0,  0.02f,  1.225f), V(0.07f, 0.07f, 1.15f)),
    });

// Handheld. Revolver: frame, cylinder, long barrel, hammer, wooden grip raked back like the sprite.
Weapon("Heavy Revolver", barrel: "Barrel", projectile: "NT Heavy Bullet",
    AmmoType.Bullet, Single, cooldown: 0.17f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 1, fixedSpreadAngle: 0, ammoPerShot: 2,
    new[] {
        Cube("Frame",    "Blockout Metal",       V(0,  0.00f,  0.00f), V(0.12f, 0.16f, 0.30f)),
        Cube("Cylinder", "Blockout Light Metal", V(0, -0.01f,  0.05f), V(0.15f, 0.18f, 0.20f)),
        Cube("Barrel",   "Blockout Light Metal", V(0,  0.03f,  0.40f), V(0.08f, 0.10f, 0.50f)),
        Cube("Grip",     "Blockout Wood",        V(0, -0.18f, -0.12f), V(0.10f, 0.30f, 0.13f), V(20, 0, 0)),
        Cube("Hammer",   "Blockout Metal",       V(0,  0.10f, -0.14f), V(0.05f, 0.06f, 0.08f)),
    });

// Heavy. Wooden stock and grip, grey body, thick barrel cluster with two rings.
Weapon("Gatling Slugger", barrel: "Barrels", projectile: "NT Slug",
    AmmoType.Shell, Auto, cooldown: 0.23f, burstCount: 1, projectilesPerShot: 1, spreadAngle: 6, fixedSpreadAngle: 0, ammoPerShot: 1,
    new[] {
        Cube("Body",       "Blockout Metal",       V(0,  0.00f, -0.35f), V(0.32f, 0.30f, 0.60f)),
        Cube("Stock",      "Blockout Wood",        V(0, -0.05f, -0.85f), V(0.14f, 0.20f, 0.45f)),
        Cube("Grip",       "Blockout Wood",        V(0, -0.24f, -0.30f), V(0.12f, 0.24f, 0.13f), V(-12, 0, 0)),
        Cube("Barrels",    "Blockout Light Metal", V(0,  0.00f,  0.875f), V(0.30f, 0.30f, 1.85f)),
        Cube("Ring Mid",   "Blockout Metal",       V(0,  0.00f,  0.60f), V(0.38f, 0.38f, 0.12f)),
        Cube("Ring Front", "Blockout Metal",       V(0,  0.00f,  1.70f), V(0.38f, 0.38f, 0.12f)),
    });

// Heavy. Wide receiver with three thick barrels side by side, wooden stock and grip.
Weapon("Super Slugger", barrel: "Barrel L,Barrel C,Barrel R", projectile: "NT Slug",
    AmmoType.Shell, Single, cooldown: 1.07f, burstCount: 1, projectilesPerShot: 5, spreadAngle: 4, fixedSpreadAngle: 10, ammoPerShot: 5,
    new[] {
        Cube("Stock",    "Blockout Wood",        V(0,     -0.10f, -0.80f), V(0.16f, 0.26f, 0.55f), V(-10, 0, 0)),
        Cube("Receiver", "Blockout Metal",       V(0,      0.00f, -0.20f), V(0.40f, 0.28f, 0.70f)),
        Cube("Grip",     "Blockout Wood",        V(0,     -0.24f, -0.25f), V(0.12f, 0.24f, 0.13f), V(-12, 0, 0)),
        Cube("Barrel L", "Blockout Light Metal", V(-0.14f, 0.03f,  0.975f), V(0.14f, 0.16f, 1.65f)),
        Cube("Barrel C", "Blockout Metal",       V(0,      0.03f,  0.975f), V(0.14f, 0.16f, 1.65f)),
        Cube("Barrel R", "Blockout Light Metal", V(0.14f,  0.03f,  0.975f), V(0.14f, 0.16f, 1.65f)),
    });

// Heavy. Fat housing with a band round its middle and a rear sight, short wooden stock, wooden grip,
// four barrels side by side held by a front band. More than 6 cubes (the user lifted the limit for this weapon).
Weapon("Quadruple Machinegun", barrel: "Barrel 1,Barrel 2,Barrel 3,Barrel 4", projectile: "NT Bullet",
    AmmoType.Bullet, Auto, cooldown: 0.13f, burstCount: 1, projectilesPerShot: 4, spreadAngle: 3, fixedSpreadAngle: 12, ammoPerShot: 4,
    new[] {
        Cube("Stock",        "Blockout Wood",        V(0,       -0.06f, -0.97f), V(0.12f, 0.22f, 0.35f)),
        Cube("Housing",      "Blockout Light Metal", V(0,        0.00f, -0.05f), V(0.40f, 0.32f, 1.50f)),
        Cube("Housing Band", "Blockout Metal",       V(0,        0.00f,  0.20f), V(0.42f, 0.34f, 0.10f)),
        Cube("Sight",        "Blockout Metal",       V(0,        0.19f, -0.60f), V(0.08f, 0.06f, 0.14f)),
        Cube("Grip",         "Blockout Wood",        V(0,       -0.27f, -0.30f), V(0.12f, 0.26f, 0.13f), V(-12, 0, 0)),
        Cube("Barrel 1",     "Blockout Metal",       V(-0.165f,  0.03f,  1.30f), V(0.09f, 0.09f, 1.20f)),
        Cube("Barrel 2",     "Blockout Metal",       V(-0.055f,  0.03f,  1.30f), V(0.09f, 0.09f, 1.20f)),
        Cube("Barrel 3",     "Blockout Metal",       V(0.055f,   0.03f,  1.30f), V(0.09f, 0.09f, 1.20f)),
        Cube("Barrel 4",     "Blockout Metal",       V(0.165f,   0.03f,  1.30f), V(0.09f, 0.09f, 1.20f)),
        Cube("Barrel Band",  "Blockout Light Metal", V(0,        0.03f,  1.70f), V(0.44f, 0.13f, 0.10f)),
    });

// Heavy. Minigun with two barrel clusters side by side sharing one front ring, carry handle on top.
Weapon("Double Minigun", barrel: "Barrels L,Barrels R", projectile: "NT Bullet",
    AmmoType.Bullet, Auto, cooldown: 0.03f, burstCount: 1, projectilesPerShot: 2, spreadAngle: 9.5f, fixedSpreadAngle: 9.5f, ammoPerShot: 2,
    new[] {
        Cube("Body",         "Blockout Wood",        V(0,     0.00f, -0.50f), V(0.44f, 0.34f, 0.80f)),
        Cube("Grip",         "Blockout Wood",        V(0,    -0.24f, -0.35f), V(0.12f, 0.24f, 0.13f), V(-12, 0, 0)),
        Cube("Barrels L",    "Blockout Light Metal", V(-0.11f, 0.02f, 0.875f), V(0.18f, 0.20f, 1.95f)),
        Cube("Barrels R",    "Blockout Light Metal", V(0.11f,  0.02f, 0.875f), V(0.18f, 0.20f, 1.95f)),
        Cube("Ring Front",   "Blockout Metal",       V(0,     0.02f,  1.75f), V(0.44f, 0.26f, 0.12f)),
        Cube("Carry Handle", "Blockout Metal",       V(0,     0.21f, -0.45f), V(0.08f, 0.10f, 0.50f)),
    });

UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
