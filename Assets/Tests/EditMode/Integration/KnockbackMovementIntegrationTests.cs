using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Cross-processor integration test for the Knockback -> Movement impulse handshake.
///
/// Asserts on the CONTRACT: that a Vector3 impulse (of the shape KnockbackController produces — a
/// horizontal hit lifted to the minimum launch angle, at SpeedOnHit) is consumed by
/// MovementProcessor.Process via MovementInput.Impulses and observably displaces the pawn in the impulse
/// direction. If MovementProcessor stops summing the impulses list, or the consumer/producer drift on
/// direction conventions, hits would stop knocking pawns.
///
/// The impulse is HARDCODED here rather than computed: the outgoing-impulse arithmetic now lives inside
/// KnockbackController (an Assembly-CSharp MonoBehaviour, unreachable from this pure-Core test asmdef), and
/// its own edge cases are covered by the PlayMode combat suite. This test owns only the Movement half of the
/// handshake, so a representative +X impulse is all it needs.
/// </summary>
[TestFixture]
public class KnockbackMovementIntegrationTests
{
    private MovementConfig _movementConfig;

    // A representative outgoing knockback impulse for a hit from +X: the horizontal direction lifted to the
    // default 35-degree launch angle, at the default SpeedOnHit of 7 → roughly (5.7, 4.0, 0). The exact vector
    // is immaterial; the test asserts only that a positive-X impulse displaces the pawn along +X.
    private static readonly Vector3 KnockbackImpulseFromPlusX = new(5.735f, 4.015f, 0f);

    [SetUp]
    public void SetUp()
    {
        _movementConfig = MovementConfig.Default;
    }

    /// <summary>
    /// Simulates the airborne integration loop a pawn would experience after taking a hit:
    /// the impulse is applied on the first tick (as KnockbackController would push it into
    /// PendingImpulses), and subsequent ticks integrate velocity into a position estimate.
    /// </summary>
    private Vector3 SimulateAirborneDisplacement(List<Vector3> impulsesFirstFrameOnly, int frames = 30)
    {
        var state = MovementState.Default;
        Vector3 position = Vector3.zero;

        // Airborne so the impulse persists - grounded drag would kill horizontal velocity
        // in just a few ticks and obscure the contract under test.
        var inputBase = new MovementInput
        {
            Move = Vector2.zero,
            JumpPressed = false,
            IsGrounded = false,
            WorldOrientation = Quaternion.identity,
            Modifiers = new List<MovementModifier>(),
        };

        const float dt = 1f / 60f;
        for (int i = 0; i < frames; i++)
        {
            // Apply impulses only on the first tick - mirrors PawnMovement.Update clearing
            // PendingImpulses immediately after processing.
            inputBase.Impulses = (i == 0) ? impulsesFirstFrameOnly : null;
            state = MovementProcessor.Process(state, in _movementConfig, in inputBase, dt);
            position += state.Velocity * dt;
        }

        return position;
    }

    [Test]
    public void KnockbackImpulse_DisplacesPawnInHitDirection_ViaMovementProcessor()
    {
        // Sanity: the representative impulse preserves the hit direction (+X).
        Assert.Greater(KnockbackImpulseFromPlusX.x, 0f,
            "Knockback impulse should have a positive X component for a +X hit direction.");

        // Act: feed the impulse into MovementInput.Impulses (the same list PawnMovement.Update
        // sources from PawnMovement.PendingImpulses).
        Vector3 displacementWithImpulse = SimulateAirborneDisplacement(
            new List<Vector3> { KnockbackImpulseFromPlusX });

        Vector3 displacementBaseline = SimulateAirborneDisplacement(impulsesFirstFrameOnly: null);

        // Assert: observable behaviour - the pawn must end up measurably further along +X
        // than the no-impulse baseline. Loose threshold (1 world unit) shrugs off tuning
        // drift in SpeedOnHit / MinimumAngleOnHit but still catches a broken handshake.
        Assert.Greater(displacementWithImpulse.x, displacementBaseline.x + 1f,
            "MovementProcessor should consume the knockback impulse from MovementInput.Impulses " +
            "and displace the pawn in the hit direction.");
    }
}
