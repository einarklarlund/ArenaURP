// shared: describe a weapon's state
string Describe(Weapon w)
{
    if (w == null) return "null";
    var body = w.GetComponent<Rigidbody>();
    var col = w.GetComponent<BoxCollider>();
    return w.name + " holder=" + (w.Holder ? w.Holder.name : "-") + " parent=" + (w.transform.parent ? w.transform.parent.name : "-")
        + " owner=" + w.Owner.ClientId + " free=" + w.IsFree + " kinematic=" + body.isKinematic + " collider=" + col.enabled
        + " pos=" + w.transform.position.ToString("F2") + " vel=" + (body.isKinematic ? "-" : body.linearVelocity.magnitude.ToString("F2"));
}
string All()
{
    var sb = new System.Text.StringBuilder();
    foreach (var w in UnityEngine.Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None)) sb.AppendLine("  " + Describe(w));
    return sb.ToString();
}
Pawn LocalPawn()
{
    var p = NetworkPlayer.LocalInstance;
    return p != null ? p.ControlledPawn.Value : null;
}
var pawn = LocalPawn();
var cam = pawn.transform.Find("Camera");
Vector3 flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
Vector3 pos = pawn.transform.position + flat * 2.2f + Vector3.up * 2.5f;
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Weapon>("Assets/Weapons/Nuclear Throne/Machinegun.prefab");
var w = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0, 30, 0));
FishNet.InstanceFinder.ServerManager.Spawn(w.gameObject);
return "spawned at " + pos.ToString("F2") + " pawn at " + pawn.transform.position.ToString("F2") + "\n" + Describe(w);
