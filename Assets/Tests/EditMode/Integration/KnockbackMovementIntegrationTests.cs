using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Cross-processor integration test for the Knockback -> Movement impulse handshake.
///
/// Asserts on the CONTRACT: that the Vector3 impulse produced by KnockbackHelper.ComputeImpulse
/// is consumed by MovementProcessor.Process via MovementInput.Impulses and observably displaces
/// the pawn in the impulse direction. If MovementProcessor stops summing the impulses list, or
/// the consumer/producer drift on direction conventions, hits would stop knocking pawns.
///
/// Observable assertion: integrate velocity over a few frames after applying the impulse and
/// compare displacement vs. a no-impulse baseline. Compares the X axis specifically because the
/// hit direction is along +X.
/// </summary>
[TestFixture]
public class KnockbackMovementIntegrationTests
{
    private MovementConfig _movementConfig;
    private KnockbackConfig _knockbackConfig;

    [SetUp]
    public void SetUp()
    {
        _movementConfig = MovementConfig.Default;
        _knockbackConfig = KnockbackConfig.Default;
    }

    /// <summary>
    /// Simulates the airborne integration loop a pawn would experience after taking a hit:
    /// the impulse is applied on the first tick (as PawnKnockback would push it into
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
        // Arrange: compute the impulse the way PawnKnockback does, for a hit from +X.
        Vector3 hitDirection = new Vector3(1f, 0f, 0f);
        Vector3 impulse = KnockbackHelper.ComputeImpulse(hitDirection, in _knockbackConfig);

        // Sanity: the impulse should have a positive X component (preserves hit direction).
        Assert.Greater(impulse.x, 0f,
            "Knockback impulse should have a positive X component for a +X hit direction.");

        // Act: feed the impulse into MovementInput.Impulses (the same list PawnMovement.Update
        // sources from PawnMovement.PendingImpulses).
        Vector3 displacementWithImpulse = SimulateAirborneDisplacement(
            new List<Vector3> { impulse });

        Vector3 displacementBaseline = SimulateAirborneDisplacement(impulsesFirstFrameOnly: null);

        // Assert: observable behaviour - the pawn must end up measurably further along +X
        // than the no-impulse baseline. Loose threshold (1 world unit) shrugs off tuning
        // drift in SpeedOnHit / MinimumAngleOnHit but still catches a broken handshake.
        Assert.Greater(displacementWithImpulse.x, displacementBaseline.x + 1f,
            "MovementProcessor should consume the knockback impulse from MovementInput.Impulses " +
            "and displace the pawn in the hit direction.");
    }
}
