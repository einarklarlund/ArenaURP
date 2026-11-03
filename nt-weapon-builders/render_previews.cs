// Renders each weapon from the side (looking down -X) and at a three-quarter angle into one PNG per weapon.
const string WeaponDir = "Assets/Weapons/Nuclear Throne";
string OutDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Temp/nt-weapon-previews")); // outside Assets and git
string[] NAMES = { "Grenade Shotgun", "Splinter Gun", "Auto Crossbow", "Super Crossbow", "Splinter Pistol", "Super Splinter Gun", "Hyper Rifle", "Heavy Revolver", "Gatling Slugger", "Super Slugger", "Quadruple Machinegun", "Double Minigun" };
const int W = 640, H = 360;

System.IO.Directory.CreateDirectory(OutDir);
var log = new System.Text.StringBuilder();
var pru = new UnityEditor.PreviewRenderUtility();
try
{
    pru.camera.fieldOfView = 25f;
    pru.camera.nearClipPlane = 0.05f;
    pru.camera.farClipPlane = 100f;
    pru.camera.clearFlags = CameraClearFlags.SolidColor;
    pru.camera.backgroundColor = new Color(0.82f, 0.84f, 0.86f);
    pru.lights[0].intensity = 1.2f;
    pru.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
    pru.lights[1].intensity = 0.6f;
    pru.ambientColor = new Color(0.45f, 0.45f, 0.45f);

    foreach (var name in NAMES)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(WeaponDir + "/" + name + ".prefab");
        var go = pru.InstantiatePrefabInScene(prefab);
        // The muzzle flash would render its first particles; hide it for a clean silhouette.
        go.transform.Find("Muzzle Flash").gameObject.SetActive(false);
        var model = go.transform.Find("Weapon Model");
        Bounds b = default; bool first = true;
        foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }

        var sheet = new Texture2D(W * 2, H, TextureFormat.RGB24, false);
        var views = new[] { Quaternion.LookRotation(Vector3.left), Quaternion.LookRotation(new Vector3(-1f, -0.6f, -1f)) };
        for (int v = 0; v < views.Length; v++)
        {
            float radius = b.extents.magnitude;
            float dist = radius / Mathf.Sin(pru.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.62f;
            pru.camera.transform.rotation = views[v];
            pru.camera.transform.position = b.center - views[v] * Vector3.forward * dist;
            pru.BeginStaticPreview(new Rect(0, 0, W, H));
            pru.Render(true);
            var tex = pru.EndStaticPreview();
            var rt = RenderTexture.GetTemporary(W, H, 0);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, W, H), v * W, 0);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        }
        UnityEngine.Object.DestroyImmediate(go);
        sheet.Apply();
        string file = OutDir + "/" + name + ".png";
        System.IO.File.WriteAllBytes(file, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        log.AppendLine(file);
    }
}
finally { pru.Cleanup(); }
return log.ToString();
