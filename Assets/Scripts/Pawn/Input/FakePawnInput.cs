#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// Editor/test-only <see cref="PawnInput"/> for PlayMode integration tests.
/// Exposes <see cref="Move"/> and <see cref="Jump"/> as public fields so the
/// test fixture can drive input each frame without going through hardware
/// input or networking. Always <see cref="IsActive"/> = true.
///
/// Kept in Assembly-CSharp so prefabs can reference it directly; gated by
/// UNITY_EDITOR so it never ships in a player build.
/// </summary>
public sealed class FakePawnInput : PawnInput
{
    public Vector2 Move;
    public bool Jump;
    public bool Fire;

    public override bool IsActive => true;

    private void Update()
    {
        Data = new PawnInputData
        {
            Move = this.Move,
            Jump = this.Jump,
            Fire = this.Fire,
        };
    }
}
#endif
