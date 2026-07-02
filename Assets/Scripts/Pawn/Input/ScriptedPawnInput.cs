#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// Editor/test-only <see cref="PawnInput"/> that lets a test rig drive the FULL
/// <see cref="PawnInputData"/> surface (Move, Look, Fire, Jump) directly ??? no BotBrain
/// AI, no hardware input, no networking. This is the input component on the scripted
/// "combatant" test pawn (<c>TestCombatant.prefab</c>) used by the PlayMode
/// projectile/ability integration tests (see <c>CombatTestRig</c> + the CombatArena scene).
///
/// Continuous combat inputs flow through here every server frame so they exercise the
/// exact <c>Pawn&lt;X&gt; -&gt; Processor</c> path the shipped game uses. Ability USE/CYCLE are
/// deliberately NOT driven here: those are server methods
/// (<c>PawnActiveAbilities.ServerUse</c> / <c>ServerCycleSelected</c>) the rig calls directly,
/// matching <c>AbilityTestRig</c> and avoiding edge-trigger timing fragility.
///
/// <see cref="IsActive"/> mirrors <see cref="BotPawnInput"/> (server-authoritative): the
/// pawn's logic runs on the server/host this fixture brings up. This is the same role
/// BotPawnInput plays on the real Bot Pawn ??? the combatant prefab simply swaps the AI-driven
/// input for this directly-driven one. Gated by UNITY_EDITOR so it never ships.
///
/// This is a separate class from <see cref="FakePawnInput"/> on purpose: FakePawnInput is the
/// minimal legacy input (Move/Jump/Fire only) baked into the older TestShooter prefab and the
/// 27 green PlayMode tests; leaving it untouched keeps that suite stable. Consolidate later if
/// the duplication ever bites.
/// </summary>
public sealed class ScriptedPawnInput : PawnInput
{
    /// <summary>Local-space move axes (forward/right), magnitude clamped to 1 by the processor.</summary>
    public Vector2 Move;

    /// <summary>Per-frame look delta (yaw_x, pitch_y). Most tests aim by setting the pawn's
    /// rotation directly via <c>CombatTestRig.AimAt</c>; this field exists for tests that want to
    /// exercise the turn path itself.</summary>
    public Vector2 Look;

    /// <summary>Held trigger state ??? PawnWeapon frame-differences this into a shot.</summary>
    public bool Fire;

    public bool Jump;

    public override bool IsActive => IsServerInitialized;

    private void Update()
    {
        if (!IsServerInitialized) return;

        Data = new PawnInputData
        {
            Move = Move,
            Look = Look,
            Fire = Fire,
            Jump = Jump,
        };
    }
}
#endif
