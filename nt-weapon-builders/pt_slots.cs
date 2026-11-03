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
if (pawn == null) return "no pawn";
var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
string S(string field) { var w = (Weapon)typeof(Pawn).GetField(field, f).GetValue(pawn); return w == null ? "empty" : w.name + " local=" + w.transform.localPosition.ToString("F3") + " rot=" + w.transform.localEulerAngles.ToString("F1") + " anim=" + (w.animator ? w.animator.enabled.ToString() : "-") + " active=" + w.gameObject.activeSelf; }
return "frame " + Time.frameCount + "\n primary:   " + S("_primaryWeapon") + "\n secondary: " + S("_secondaryWeapon");
