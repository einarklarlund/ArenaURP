using NUnit.Framework;

/// <summary>
/// Edit-mode tests for the hitstun gate in WeaponProcessor.
///
/// The contract: hitstun blocks the START of a fire event, and deliberately does NOT interrupt a burst that is
/// already committed. Both halves are pinned here — the second one is the easy thing to "fix" by accident.
/// </summary>
[TestFixture]
public class WeaponHitstunTests
{
    const float Dt = 1f / 60f;

    static WeaponConfig AutoSingleShot() => new()
    {
        FireMode = FireMode.Automatic,
        FireRate = 0.1f,
        AmmoPerFire = 1,
        ShotsPerFire = 1,
        FireDuration = 0f,
        ProjectilesPerShot = 1,
        SpreadAngle = 0f,
        SpreadType = SpreadType.Random,
    };

    static WeaponConfig AutoBurst() => new()
    {
        FireMode = FireMode.Automatic,
        FireRate = 1f,
        AmmoPerFire = 1,
        ShotsPerFire = 3,
        FireDuration = 0.3f,      // three shots spread over 0.3s
        ProjectilesPerShot = 1,
        SpreadAngle = 0f,
        SpreadType = SpreadType.Random,
    };

    static WeaponInput Firing(bool stunned) => new()
    {
        FireHeld = true,
        FirePressed = true,
        HasAmmo = true,
        Stunned = stunned,
        Modifiers = null,
    };

    [Test]
    public void NotStunned_HoldingFire_Fires()
    {
        var state = WeaponState.Default;
        var config = AutoSingleShot();

        WeaponProcessor.Process(state, in config, Firing(stunned: false), Dt, out int shots);

        Assert.AreEqual(1, shots, "An unstunned weapon with a ready cooldown fires.");
    }

    [Test]
    public void Stunned_HoldingFire_DoesNotFire()
    {
        var state = WeaponState.Default;
        var config = AutoSingleShot();

        WeaponProcessor.Process(state, in config, Firing(stunned: true), Dt, out int shots);

        Assert.AreEqual(0, shots, "A stunned weapon may not start a fire event.");
    }

    [Test]
    public void Stunned_ThenUnstunned_FiresAgain()
    {
        var state = WeaponState.Default;
        var config = AutoSingleShot();

        state = WeaponProcessor.Process(state, in config, Firing(stunned: true), Dt, out int stunnedShots);
        Assert.AreEqual(0, stunnedShots);

        WeaponProcessor.Process(state, in config, Firing(stunned: false), Dt, out int freeShots);
        Assert.AreEqual(1, freeShots, "Once the stun expires the weapon fires normally again.");
    }

    [Test]
    public void Stunned_DoesNotBlockCooldownFromAdvancing()
    {
        var config = AutoSingleShot();
        var state = WeaponState.Default;
        state.TimeSinceLastFire = 0f;

        // Stunned for a full cooldown's worth of frames...
        for (int i = 0; i < 10; i++)
            state = WeaponProcessor.Process(state, in config, Firing(stunned: true), Dt, out _);

        // ...so the moment the stun lifts, the weapon is ready — the stun doesn't also cost a cooldown.
        WeaponProcessor.Process(state, in config, Firing(stunned: false), Dt, out int shots);
        Assert.AreEqual(1, shots, "Hitstun suppresses firing; it must not silently re-arm the fire-rate cooldown.");
    }

    [Test]
    public void Stunned_DoesNotCancelAnInFlightBurst()
    {
        var config = AutoBurst();
        var state = WeaponState.Default;

        // Frame 1: start the burst (fires shot 1 of 3) while unstunned.
        state = WeaponProcessor.Process(state, in config, Firing(stunned: false), Dt, out int first);
        Assert.AreEqual(1, first, "precondition: the burst started");
        Assert.IsTrue(state.IsBurstActive, "precondition: a burst is in progress");

        // Now get stunned mid-burst. The committed burst must still play out to all 3 shots.
        int total = first;
        for (int i = 0; i < 40 && state.IsBurstActive; i++)
        {
            state = WeaponProcessor.Process(state, in config, Firing(stunned: true), Dt, out int shots);
            total += shots;
        }

        Assert.AreEqual(3, total, "A burst already committed plays out to completion even while stunned.");
        Assert.IsFalse(state.IsBurstActive, "the burst finished");
    }

    [Test]
    public void Stunned_BlocksTheNextBurstAfterOneFinishes()
    {
        var config = AutoBurst();
        var state = WeaponState.Default;

        // Run one burst to completion while stunned (it finishes, per the test above).
        state = WeaponProcessor.Process(state, in config, Firing(stunned: false), Dt, out _);
        for (int i = 0; i < 40 && state.IsBurstActive; i++)
            state = WeaponProcessor.Process(state, in config, Firing(stunned: true), Dt, out _);
        Assert.IsFalse(state.IsBurstActive, "precondition: the first burst finished");

        // Let the fire-rate cooldown expire, still stunned and still holding fire.
        for (int i = 0; i < 80; i++)
            state = WeaponProcessor.Process(state, in config, Firing(stunned: true), Dt, out _);

        WeaponProcessor.Process(state, in config, Firing(stunned: true), Dt, out int shots);
        Assert.AreEqual(0, shots, "With the burst over, the stun blocks the NEXT fire event.");
    }
}
