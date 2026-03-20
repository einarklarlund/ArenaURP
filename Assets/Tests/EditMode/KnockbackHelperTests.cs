using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode unit tests for KnockbackHelper.
/// These run instantly in the Unity Test Runner - no Play Mode, no networking, no GameObjects.
/// </summary>
[TestFixture]
public class KnockbackHelperTests
{
    private KnockbackConfig _config;

    [SetUp]
    public void SetUp()
    {
        _config = KnockbackConfig.Default;
    }

    [Test]
    public void ComputeImpulse_EnforcesMinimumAngle()
    {
        // Purely horizontal hit direction
        Vector3 impulse = KnockbackHelper.ComputeImpulse(new Vector3(1f, 0f, 0f), in _config);

        float angle = Vector3.Angle(Vector3.ProjectOnPlane(impulse, Vector3.up), impulse);
        Assert.GreaterOrEqual(angle, _config.MinimumAngleOnHit - 0.1f,
            $"Knockback should enforce a minimum {_config.MinimumAngleOnHit}-degree launch angle.");
    }

    [Test]
    public void ComputeImpulse_SpeedMatchesConfig()
    {
        // Purely upward direction (no angle adjustment needed)
        Vector3 impulse = KnockbackHelper.ComputeImpulse(new Vector3(0f, 1f, 0f), in _config);

        Assert.AreEqual(_config.SpeedOnHit, impulse.magnitude, 0.01f,
            "Knockback impulse magnitude should match SpeedOnHit config value.");
    }

    [Test]
    public void ComputeImpulse_PreservesHorizontalDirection()
    {
        Vector3 impulse = KnockbackHelper.ComputeImpulse(new Vector3(1f, 0f, 0f), in _config);

        Assert.Greater(impulse.x, 0f,
            "Knockback should preserve the horizontal direction of the hit.");
    }

    [Test]
    public void ComputeImpulse_AlreadyAboveMinAngle_NotAltered()
    {
        // Direction well above the minimum angle (straight up)
        Vector3 upDirection = new Vector3(0f, 1f, 0f);
        Vector3 impulse = KnockbackHelper.ComputeImpulse(upDirection, in _config);

        // Should point straight up with magnitude equal to SpeedOnHit
        Assert.AreEqual(0f, impulse.x, 0.01f,
            "A purely upward hit should not gain horizontal components.");
        Assert.AreEqual(_config.SpeedOnHit, impulse.y, 0.01f,
            "A purely upward hit should have full SpeedOnHit in Y.");
    }
}
