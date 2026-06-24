using Arena.Steering.Core;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode unit tests for SeekMovement.
///
/// SeekMovement.Tick reads via ctx.Body?.AddForce — by passing a null Body we let the
/// strategy compute LastDesiredVelocity / LastSteeringForce without needing a real
/// PredictionRigidbody. We do need a real Transform for TargetInfo.Transform.position,
/// so a throwaway GameObject is created per test and destroyed in [TearDown].
/// </summary>
[TestFixture]
public class SeekMovementTests
{
    private GameObject _targetObject;

    [SetUp]
    public void SetUp()
    {
        _targetObject = new GameObject("SeekMovementTest_Target");
    }

    [TearDown]
    public void TearDown()
    {
        if (_targetObject != null)
            Object.DestroyImmediate(_targetObject);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static SeekConfig MakeConfig(
        float maxSpeed = 6f,
        float maxAccel = 30f,
        float arriveRadius = 0f,
        bool flatXZ = false) => new()
    {
        MaxSpeed = maxSpeed,
        MaxAccel = maxAccel,
        ArriveRadius = arriveRadius,
        FlatXZ = flatXZ,
    };

    private static PerceptionContext MakeCtx(Vector3 selfPos, Vector3 selfVel) => new()
    {
        SelfPosition = selfPos,
        SelfVelocity = selfVel,
        SelfRotation = Quaternion.identity,
        Body = null, // Tick uses Body?.AddForce — null is fine for steering output tests.
        DeltaTime = 1f / 60f,
    };

    private TargetInfo MakeTarget(Vector3 worldPos)
    {
        _targetObject.transform.position = worldPos;
        return new TargetInfo
        {
            HasTarget = true,
            Transform = _targetObject.transform,
        };
    }

    private static TargetInfo NoTarget() => new() { HasTarget = false, Transform = null };

    // ------------------------------------------------------------------
    // Tests
    // ------------------------------------------------------------------

    [Test]
    public void Tick_NoTarget_ProducesZeroSteering()
    {
        // Arrange
        SeekConfig config = MakeConfig();
        var seek = new SeekMovement(() => config);
        PerceptionContext ctx = MakeCtx(selfPos: Vector3.zero, selfVel: new Vector3(2f, 0f, 0f));

        // Act
        seek.Tick(ctx, NoTarget());

        // Assert
        Assert.AreEqual(Vector3.zero, seek.LastDesiredVelocity,
            "No target should zero out LastDesiredVelocity.");
        Assert.AreEqual(Vector3.zero, seek.LastSteeringForce,
            "No target should zero out LastSteeringForce.");
    }

    [Test]
    public void Tick_TargetReached_StopsWithinArriveRadius()
    {
        // Arrange: target is at distance 1 inside an ArriveRadius of 4, so desired speed
        // should ramp to MaxSpeed * (1/4) = MaxSpeed * 0.25, not full MaxSpeed.
        SeekConfig config = MakeConfig(maxSpeed: 10f, maxAccel: 100f, arriveRadius: 4f);
        var seek = new SeekMovement(() => config);
        PerceptionContext ctx = MakeCtx(selfPos: Vector3.zero, selfVel: Vector3.zero);
        TargetInfo target = MakeTarget(new Vector3(1f, 0f, 0f));

        // Act
        seek.Tick(ctx, target);

        // Assert: desired velocity magnitude is ramped down inside the arrive radius.
        float expectedDesiredSpeed = config.MaxSpeed * (1f / config.ArriveRadius);
        Assert.AreEqual(expectedDesiredSpeed, seek.LastDesiredVelocity.magnitude, 0.0001f,
            "Inside ArriveRadius, desired speed should ramp linearly with distance.");

        // And it should be strictly less than MaxSpeed (i.e. the ramp actually fired).
        Assert.Less(seek.LastDesiredVelocity.magnitude, config.MaxSpeed,
            "Desired speed must be below MaxSpeed when inside the arrive radius.");
    }

    [Test]
    public void Tick_FlatXZTrue_ZerosOutYComponent()
    {
        // Arrange: target is above and ahead. With FlatXZ, the Y delta should be ignored
        // so both LastDesiredVelocity and LastSteeringForce have Y == 0.
        SeekConfig config = MakeConfig(maxSpeed: 6f, maxAccel: 100f, flatXZ: true);
        var seek = new SeekMovement(() => config);
        PerceptionContext ctx = MakeCtx(selfPos: Vector3.zero, selfVel: new Vector3(0f, 5f, 0f));
        TargetInfo target = MakeTarget(new Vector3(10f, 10f, 0f));

        // Act
        seek.Tick(ctx, target);

        // Assert
        Assert.AreEqual(0f, seek.LastDesiredVelocity.y, 0.0001f,
            "FlatXZ should produce zero Y on the desired velocity.");
        Assert.AreEqual(0f, seek.LastSteeringForce.y, 0.0001f,
            "FlatXZ should produce zero Y on the steering force (current velocity Y is zeroed too).");
    }

    [Test]
    public void Tick_MaxAccelExceeded_ClampsAcceleration()
    {
        // Arrange: a huge desired-vs-current gap should be clamped to MaxAccel.
        // Self moving backward at -100 along +X, target far ahead. Desired velocity is
        // +MaxSpeed along +X, so the raw steering (desired - current) is huge.
        const float maxAccel = 5f;
        SeekConfig config = MakeConfig(maxSpeed: 100f, maxAccel: maxAccel, flatXZ: false);
        var seek = new SeekMovement(() => config);
        PerceptionContext ctx = MakeCtx(
            selfPos: Vector3.zero,
            selfVel: new Vector3(-100f, 0f, 0f));
        TargetInfo target = MakeTarget(new Vector3(1000f, 0f, 0f));

        // Act
        seek.Tick(ctx, target);

        // Assert: steering force magnitude must be clamped to MaxAccel.
        // Note: the production code clamps to MaxAccel (not MaxAccel * dt) — the
        // PerceptionContext doesn't pass dt through to the clamp.
        Assert.LessOrEqual(seek.LastSteeringForce.magnitude, maxAccel + 0.0001f,
            "Steering force magnitude must be clamped to MaxAccel.");
        // And we should actually be hitting the clamp (not coincidentally below it).
        Assert.AreEqual(maxAccel, seek.LastSteeringForce.magnitude, 0.0001f,
            "A huge desired-vs-current gap should clamp exactly to MaxAccel.");
    }
}
