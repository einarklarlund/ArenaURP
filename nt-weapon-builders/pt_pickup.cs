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
Weapon mg = null;
foreach (var w in UnityEngine.Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None)) if (w.name.StartsWith("Machinegun")) mg = w;
var cam = pawn.transform.Find("Camera");
cam.rotation = Quaternion.LookRotation(mg.transform.position - cam.position);
bool hit = Physics.Raycast(cam.position, cam.forward, out RaycastHit h, 3f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore);
typeof(Pawn).GetMethod("TryPickUpWeapon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(pawn, null);
return "raycast hit=" + hit + (hit ? " " + h.collider.name + " d=" + h.distance.ToString("F2") : "") + "\nimmediately after:\n" + All();
