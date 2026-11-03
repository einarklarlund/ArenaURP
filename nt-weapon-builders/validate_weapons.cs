// Validates NT weapon prefabs against the plan's end-of-batch checks. Add rows to EXPECTED for later
// batches. Row: name, class, barrel part(s), projectile, ammo, mode, cooldown, burst, proj, random, fixed, ammoPerShot, burstInterval
const string WeaponDir = "Assets/Weapons/Nuclear Throne";
var M = global::Weapon.FireMode.Automatic; var S = global::Weapon.FireMode.SingleShot;
var B = global::Weapon.FireMode.Burst;
var EXPECTED = new (string name, string cls, string barrel, string proj, AmmoType ammo, global::Weapon.FireMode mode,
                    float cd, int burst, int pps, float rnd, float fix, int aps, float bi)[]
{
    ("Machinegun",    "Long", "Barrel", "NT Bullet", AmmoType.Bullet, M, 0.17f, 1, 1, 6,  0, 1, 0.067f),
    ("Assault Rifle", "Long", "Barrel", "NT Bullet", AmmoType.Bullet, B, 0.37f, 3, 1, 2,  0, 3, 0.067f),
    ("Shotgun",       "Long", "Barrel", "NT Pellet", AmmoType.Shell,  S, 0.57f, 1, 7, 20, 0, 1, 0.067f),
    ("Slugger",       "Long", "Barrel", "NT Slug",   AmmoType.Shell,  S, 0.73f, 1, 1, 5,  0, 1, 0.067f),
    ("Crossbow",      "Long", "Bolt",   "NT Bolt",   AmmoType.Bolt,   S, 0.87f, 1, 1, 0,  0, 1, 0.067f),
    ("Grenade Launcher",  "Compact",  "Barrel",                   "NT Grenade",    AmmoType.Explosive, S, 0.67f, 1, 1,  3,  0,  1, 0.067f),
    ("SMG",               "Handheld", "Barrel",                   "NT Bullet",     AmmoType.Bullet,    M, 0.10f, 1, 1,  16, 0,  1, 0.067f),
    ("Triple Machinegun", "Heavy",    "Barrel L,Barrel C,Barrel R", "NT Bullet",   AmmoType.Bullet,    M, 0.13f, 1, 3,  3,  15, 3, 0.067f),
    ("Double Shotgun",    "Long",     "Barrel Top,Barrel Bottom", "NT Pellet",     AmmoType.Shell,     S, 0.93f, 1, 14, 30, 0,  2, 0.067f),
    ("Assault Slugger",   "Long",     "Barrel",                   "NT Slug",       AmmoType.Shell,     B, 1.17f, 3, 1,  4,  0,  3, 0.067f),
    ("Heavy Crossbow",    "Heavy",    "Bolt",                     "NT Heavy Bolt", AmmoType.Bolt,      S, 1.33f, 1, 1,  0,  0,  2, 0.067f),
    ("Bazooka",           "Heavy",    "Tube,Front Cap",           "NT Rocket",     AmmoType.Explosive, S, 1.00f, 1, 1,  2,  0,  1, 0.067f),
    ("Minigun",           "Heavy",    "Barrels",                  "NT Bullet",     AmmoType.Bullet,    M, 0.03f, 1, 1,  13, 0,  1, 0.067f),
    ("Sawed-off Shotgun", "Compact",  "Barrel Top,Barrel Bottom", "NT Pellet",     AmmoType.Shell,     S, 0.93f, 1, 18, 45, 0,  2, 0.067f),
    ("Auto Shotgun",      "Long",     "Barrel",                   "NT Pellet",     AmmoType.Shell,     M, 0.13f, 1, 6,  15, 0,  1, 0.067f),
    ("Grenade Shotgun",      "Compact",  "Barrel",                   "NT Mini Grenade", AmmoType.Explosive, S, 0.53f, 1,  4, 17,   0,    1, 0.067f),
    ("Splinter Gun",         "Long",     "Spikes",                   "NT Splinter",     AmmoType.Bolt,      S, 0.63f, 1,  5, 10,   0,    1, 0.067f),
    ("Auto Crossbow",        "Long",     "Bolt",                     "NT Bolt",         AmmoType.Bolt,      M, 0.27f, 1,  1, 5,    0,    1, 0.067f),
    ("Super Crossbow",       "Heavy",    "Bolt 1,Bolt 2,Bolt 3,Bolt 4,Bolt 5", "NT Bolt",         AmmoType.Bolt,      S, 1.00f, 1,  5, 0,    5,    5, 0.067f),
    ("Splinter Pistol",      "Handheld", "Spikes",                   "NT Splinter",     AmmoType.Bolt,      S, 0.27f, 1,  4, 4,    0,    1, 0.067f),
    ("Super Splinter Gun",   "Heavy",    "Spikes",                   "NT Splinter",     AmmoType.Bolt,      B, 0.93f, 12, 1, 16,   0,    2, 0.017f),
    ("Hyper Rifle",          "Long",     "Barrel",                   "NT Bullet",       AmmoType.Bullet,    B, 0.10f, 5,  1, 2,    0,    5, 0.067f),
    ("Heavy Revolver",       "Handheld", "Barrel",                   "NT Heavy Bullet", AmmoType.Bullet,    S, 0.17f, 1,  1, 1,    0,    2, 0.067f),
    ("Gatling Slugger",      "Heavy",    "Barrels",                  "NT Slug",         AmmoType.Shell,     M, 0.23f, 1,  1, 6,    0,    1, 0.067f),
    ("Super Slugger",        "Heavy",    "Barrel L,Barrel C,Barrel R", "NT Slug",       AmmoType.Shell,     S, 1.07f, 1,  5, 4,    10,   5, 0.067f),
    ("Quadruple Machinegun", "Heavy",    "Barrel 1,Barrel 2,Barrel 3,Barrel 4", "NT Bullet", AmmoType.Bullet, M, 0.13f, 1, 4, 3,  12,   4, 0.067f),
    ("Double Minigun",       "Heavy",    "Barrels L,Barrels R",      "NT Bullet",       AmmoType.Bullet,    M, 0.03f, 1,  2, 9.5f, 9.5f, 2, 0.067f),
    ("Revolver",             "Handheld", "Barrel",                   "NT Bullet",       AmmoType.Bullet,    S, 0.20f, 1,  1, 4,    0,    1, 0.067f),
};
// Weapons the user allowed more than 6 cubes (2026-10-21).
var MAX_CUBES = new System.Collections.Generic.Dictionary<string, int> { { "Quadruple Machinegun", 12 }, { "Super Crossbow", 12 } };
// Size classes: length (Z), width (X), height (Y) targets; heavy uses the middle of its ranges.
var CLASSES = new System.Collections.Generic.Dictionary<string, Vector3>
{
    { "Handheld", new Vector3(0.13f, 0.5f, 0.85f) },
    { "Compact",  new Vector3(0.18f, 0.45f, 1.5f) },
    { "Long",     new Vector3(0.2f, 0.46f, 2.7f) },
    { "Heavy",    new Vector3(0.375f, 0.55f, 2.85f) },
};

var sb = new System.Text.StringBuilder();
int failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; sb.AppendLine("   FAIL " + what); } }
bool Near(float a, float b, float tol = 0.001f) => Mathf.Abs(a - b) <= tol;

foreach (var e in EXPECTED)
{
    string path = WeaponDir + "/" + e.name + ".prefab";
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    int before = failures;
    try
    {
        var w = root.GetComponent<global::Weapon>();
        Check(w != null, "Weapon component");
        Check(root.GetComponent<FishNet.Object.NetworkObject>() != null, "NetworkObject");
        Check(root.layer == 11, "root on layer 11");
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            Check(t == root.transform || t.gameObject.layer == 7, "layer 7 on " + t.name);
        var box = root.GetComponent<BoxCollider>();
        var body = root.GetComponent<Rigidbody>();
        Check(box != null && box.size == Vector3.one && box.center == Vector3.zero, "1x1x1 BoxCollider on root");
        Check(body != null && body.useGravity, "Rigidbody with gravity on root");
        Check(root.GetComponentsInChildren<Collider>(true).Length == 1, "no colliders under the root");
        var wso = new UnityEditor.SerializedObject(root.GetComponent<global::Weapon>());
        Check(wso.FindProperty("body").objectReferenceValue == body && wso.FindProperty("worldCollider").objectReferenceValue == box, "body / worldCollider wired");

        var model = root.transform.Find("Weapon Model");
        int cubes = 0;
        foreach (Transform c in model) if (c.GetComponent<MeshFilter>()?.sharedMesh?.name == "Cube") cubes++;
        int maxCubes = MAX_CUBES.TryGetValue(e.name, out int mc) ? mc : 6;
        Check(model.childCount == cubes && cubes >= 3 && cubes <= maxCubes, "3–" + maxCubes + " cube children (has " + model.childCount + ")");

        Bounds mb = default; bool first = true;
        foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (first) { mb = r.bounds; first = false; } else mb.Encapsulate(r.bounds); }
        var target = CLASSES[e.cls];
        bool isCrossbow = e.name.Contains("Crossbow");
        Check(Mathf.Abs(mb.size.z / target.z - 1) <= 0.25f, "length " + mb.size.z.ToString("F2") + " vs " + target.z);
        Check(isCrossbow || Mathf.Abs(mb.size.x / target.x - 1) <= 0.25f || (e.cls == "Heavy" && mb.size.x >= 0.3f * 0.75f && mb.size.x <= 0.45f * 1.25f),
              "width " + mb.size.x.ToString("F2") + " vs " + target.x);
        Check(Mathf.Abs(mb.size.y / target.y - 1) <= 0.25f, "height " + mb.size.y.ToString("F2") + " vs " + target.y);

        var grip = model.Find("Grip");
        Check(grip != null, "Grip part");
        if (grip != null)
        {
            Vector3 gripTop = model.TransformPoint(grip.localPosition + grip.localRotation * new Vector3(0f, grip.localScale.y * 0.5f, 0f));
            Check(gripTop.magnitude < 0.001f, "grip top at origin (is " + gripTop.ToString("F3") + ")");
        }

        Bounds bb = default; first = true;
        foreach (var part in e.barrel.Split(','))
        {
            var r = model.Find(part.Trim())?.GetComponent<Renderer>();
            Check(r != null, "barrel part " + part);
            if (r == null) continue;
            if (first) { bb = r.bounds; first = false; } else bb.Encapsulate(r.bounds);
        }
        Check(Near(w.muzzlePoint.localPosition.z - bb.max.z, 0.2f, 0.02f), "muzzle 0.2 ahead of barrel (is " + (w.muzzlePoint.localPosition.z - bb.max.z).ToString("F3") + ")");
        Check(Near(w.spawnPoint.localPosition.z, mb.min.z, 0.02f), "spawn point at model rear");

        Check(w.muzzlePoint != null && w.muzzlePoint.name == "Muzzle Point", "muzzlePoint set");
        Check(w.spawnPoint != null && w.spawnPoint.name == "Spawn Point", "spawnPoint set");
        Check(w.muzzleFlash != null && w.muzzleFlash.transform.IsChildOf(root.transform), "muzzleFlash set");
        Check(w.smokeEffects != null, "smokeEffects set");
        Check(w.projectilePrefab != null && w.projectilePrefab.gameObject.name == e.proj, "projectilePrefab = " + e.proj + " (is " + (w.projectilePrefab ? w.projectilePrefab.gameObject.name : "null") + ")");
        Check(w.animator == null, "animator empty");
        Check(w.ragdoll == null, "ragdoll empty");

        Check(w.ammoType == e.ammo, "ammoType " + w.ammoType);
        Check(w.fireMode == e.mode, "fireMode " + w.fireMode);
        Check(Near(w.cooldown, e.cd), "cooldown " + w.cooldown);
        Check(w.burstCount == e.burst, "burstCount " + w.burstCount);
        Check(w.projectilesPerShot == e.pps, "projectilesPerShot " + w.projectilesPerShot);
        Check(Near(w.spreadAngle, e.rnd), "spreadAngle " + w.spreadAngle);
        Check(Near(w.fixedSpreadAngle, e.fix), "fixedSpreadAngle " + w.fixedSpreadAngle);
        Check(w.ammoPerShot == e.aps, "ammoPerShot " + w.ammoPerShot);
        Check(w.clipSize == 0, "clipSize " + w.clipSize);
        Check(Near(w.burstInterval, e.bi), "burstInterval " + w.burstInterval);

        sb.AppendLine((failures == before ? "PASS " : "^^ FAIL ") + e.name + "  size " + mb.size.ToString("F2"));
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
sb.AppendLine(failures == 0 ? "ALL PASS" : failures + " failures");
return sb.ToString();
