using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// Edit-mode unit tests for WeaponProcessor.
/// These run instantly in the Unity Test Runner - no Play Mode, no networking, no GameObjects.
/// </summary>
[TestFixture]
public class WeaponProcessorTests
{
    private WeaponConfig _config;

    [SetUp]
    public void SetUp()
    {
        _config = WeaponConfig.Default;
    }

    // =====================================================================
    // Helper
    // =====================================================================

    /// <summary>
    /// Run the processor for N frames with the given inputs.
    /// Returns the final state and accumulates total shots fired.
    /// </summary>
    private WeaponState Simulate(
        WeaponState state,
        bool fireHeld,
        bool firePressed,
        bool hasAmmo,
        int frames,
        out int totalShotsFired,
        float dt = 1f / 60f,
        List<WeaponModifier> modifiers = null)
    {
        var input = new WeaponInput
        {
            FireHeld = fireHeld,
            FirePressed = firePressed,
            HasAmmo = hasAmmo,
            Modifiers = modifiers,
        };

        totalShotsFired = 0;

        for (int i = 0; i < frames; i++)
        {
            state = WeaponProcessor.Process(state, in _config, in input, dt, out int shots);
            totalShotsFired += shots;
        }

        return state;
    }

    /// <summary>
    /// Overload without shot counting for state-only assertions.
    /// </summary>
    private WeaponState Simulate(
        WeaponState state,
        bool fireHeld,
        bool firePressed,
        bool hasAmmo,
        int frames,
        float dt = 1f / 60f,
        List<WeaponModifier> modifiers = null)
    {
        return Simulate(state, fireHeld, firePressed, hasAmmo, frames,
            out _, dt, modifiers);
    }

    // =====================================================================
    // Fire Rate / Cooldown
    // =====================================================================

    [Test]
    public void FirstShot_AlwaysReady()
    {
        var state = WeaponState.Default;
        state = Simulate(state, true, true, true, 1, out int shots);

        Assert.AreEqual(1, shots, "First shot should always fire (cooldown starts expired).");
    }

    [Test]
    public void Cooldown_PreventsImmediateRefire()
    {
        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };

        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int shots1);
        Assert.AreEqual(1, shots1);

        // Next frame - cooldown not yet elapsed (0.1s fire rate, 0.016s elapsed)
        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int shots2);
        Assert.AreEqual(0, shots2, "Should not fire again before cooldown expires.");
    }

    [Test]
    public void Cooldown_AllowsFireAfterElapsed()
    {
        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };

        // Fire once
        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out _);

        // Advance past the cooldown (FireRate = 0.1s)
        state = Simulate(state, false, false, true, 10);

        // Now fire again
        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int shots);
        Assert.AreEqual(1, shots, "Should be able to fire after cooldown elapses.");
    }

    [Test]
    public void Automatic_FiresRepeatedly()
    {
        var state = WeaponState.Default;

        // Simulate holding fire for 1 second at 60fps (FireRate = 0.1s -> ~10 shots)
        state = Simulate(state, true, false, true, 60, out int shots);

        Assert.Greater(shots, 5, "Automatic mode should fire repeatedly while held.");
        Assert.LessOrEqual(shots, 11, "Should not fire more than expected given fire rate.");
    }

    // =====================================================================
    // Fire Modes
    // =====================================================================

    [Test]
    public void SemiAuto_OnlyFiresOnRisingEdge()
    {
        _config.FireMode = FireMode.SemiAuto;

        var state = WeaponState.Default;

        // First frame: FirePressed = true (rising edge)
        var inputPressed = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };
        state = WeaponProcessor.Process(state, in _config, in inputPressed, 1f / 60f, out int shots1);
        Assert.AreEqual(1, shots1, "SemiAuto should fire on rising edge.");

        // Subsequent frames: FireHeld = true but FirePressed = false
        state = Simulate(state, true, false, true, 60, out int shots2);

        Assert.AreEqual(0, shots2, "SemiAuto should not fire when only held (no rising edge).");
    }

    [Test]
    public void SemiAuto_ReleaseAndRepress_FiresAgain()
    {
        _config.FireMode = FireMode.SemiAuto;

        var state = WeaponState.Default;

        var inputPressed = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };

        // First press
        state = WeaponProcessor.Process(state, in _config, in inputPressed, 1f / 60f, out int shots1);
        Assert.AreEqual(1, shots1);

        // Wait for cooldown
        state = Simulate(state, false, false, true, 30);

        // Second press
        state = WeaponProcessor.Process(state, in _config, in inputPressed, 1f / 60f, out int shots2);
        Assert.AreEqual(1, shots2, "SemiAuto should fire again on a new rising edge after cooldown.");
    }

    [Test]
    public void Automatic_FireHeld_NoFirePressed_StillFires()
    {
        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = false,
            HasAmmo = true,
        };

        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int shots);
        Assert.AreEqual(1, shots, "Automatic mode should fire on FireHeld alone.");
    }

    // =====================================================================
    // Ammo
    // =====================================================================

    [Test]
    public void NoAmmo_DoesNotFire()
    {
        var state = WeaponState.Default;
        state = Simulate(state, true, true, false, 10, out int shots);

        Assert.AreEqual(0, shots, "Should not fire without ammo.");
    }

    [Test]
    public void HasAmmo_Fires()
    {
        var state = WeaponState.Default;
        state = Simulate(state, true, true, true, 1, out int shots);

        Assert.AreEqual(1, shots, "Should fire when ammo is available.");
    }

    // =====================================================================
    // Burst / Sequence
    // =====================================================================

    [Test]
    public void Burst_ZeroDuration_AllShotsSameFrame()
    {
        _config.ShotsPerFire = 3;
        _config.FireDuration = 0f;

        var state = WeaponState.Default;
        state = Simulate(state, true, true, true, 1, out int shots);

        Assert.AreEqual(3, shots, "Zero-duration burst should fire all shots on one frame.");
        Assert.IsFalse(state.IsBurstActive, "Burst should not be active after zero-duration fire.");
    }

    [Test]
    public void Burst_SpreadOverTime_FirstFrameFiresOnce()
    {
        _config.ShotsPerFire = 3;
        _config.FireDuration = 0.2f;

        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };

        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int shots);
        Assert.AreEqual(1, shots, "First frame of burst should fire one shot.");
        Assert.IsTrue(state.IsBurstActive, "Burst should be active after first shot.");
    }

    [Test]
    public void Burst_SpreadOverTime_AllShotsEventuallyFire()
    {
        _config.ShotsPerFire = 3;
        _config.FireDuration = 0.2f;

        var state = WeaponState.Default;

        // First frame triggers the burst
        state = Simulate(state, true, true, true, 1, out int shotsFirst);
        // Remaining frames let the burst complete (release fire to prevent refire in Automatic mode)
        state = Simulate(state, false, false, true, 29, out int shotsRest);

        int shots = shotsFirst + shotsRest;
        Assert.AreEqual(3, shots, "All burst shots should fire over the duration.");
        Assert.IsFalse(state.IsBurstActive, "Burst should complete.");
    }

    [Test]
    public void Burst_BlocksNewFireDuringSequence()
    {
        _config.ShotsPerFire = 3;
        _config.FireDuration = 0.5f; // long burst

        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };

        // Start burst
        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out _);
        Assert.IsTrue(state.IsBurstActive);

        // Try to fire again during burst - should only get burst continuation shots
        int burstShots = 0;
        for (int i = 0; i < 5; i++)
        {
            state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int s);
            burstShots += s;
        }

        // Total shots should not exceed ShotsPerFire - 1 (remaining from burst)
        Assert.LessOrEqual(burstShots, _config.ShotsPerFire - 1,
            "New fire events should be blocked during an active burst.");
    }

    [Test]
    public void Burst_CompletesAndAllowsRefire()
    {
        _config.ShotsPerFire = 2;
        _config.FireDuration = 0.1f;

        var state = WeaponState.Default;

        // Fire first frame to trigger burst, then release fire to let burst complete
        state = Simulate(state, true, true, true, 1, out int shotsFirst);
        state = Simulate(state, false, false, true, 29, out int shotsRest);
        int shots1 = shotsFirst + shotsRest;
        Assert.AreEqual(2, shots1, "Burst should complete.");
        Assert.IsFalse(state.IsBurstActive);

        // Wait for cooldown
        state = Simulate(state, false, false, true, 30);

        // Fire again
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };
        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int shots2);
        Assert.AreEqual(1, shots2, "Should be able to fire again after burst completes and cooldown elapses.");
    }

    [Test]
    public void Burst_VariableFrameRate_CorrectTotalShots()
    {
        _config.ShotsPerFire = 4;
        _config.FireDuration = 0.2f;

        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };

        // First frame fires 1st shot and starts burst
        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out int shots1);
        Assert.AreEqual(1, shots1);

        // Large frame catches up remaining shots
        state = WeaponProcessor.Process(state, in _config, in input, 0.25f, out int shots2);
        Assert.AreEqual(3, shots2, "Large delta time should catch up all remaining burst shots.");
        Assert.IsFalse(state.IsBurstActive, "Burst should be complete.");
    }

    // =====================================================================
    // Modifiers
    // =====================================================================

    [Test]
    public void FireRateModifier_HalvesCooldown()
    {
        var fast = new WeaponModifier
        {
            FireRateMultiplier = 0.5f,
            SpreadMultiplier = 1f,
        };
        var mods = new[] { fast }.ToList();

        var state = WeaponState.Default;
        state = Simulate(state, true, false, true, 60, out int shotsWithMod, modifiers: mods);

        var stateNoMod = WeaponState.Default;
        stateNoMod = Simulate(stateNoMod, true, false, true, 60, out int shotsNoMod);

        Assert.Greater(shotsWithMod, shotsNoMod,
            "0.5x fire rate multiplier should allow more shots in the same time.");
    }

    [Test]
    public void FireRateModifier_DoublesCooldown()
    {
        var slow = new WeaponModifier
        {
            FireRateMultiplier = 2f,
            SpreadMultiplier = 1f,
        };
        var mods = new[] { slow }.ToList();

        var state = WeaponState.Default;
        state = Simulate(state, true, false, true, 60, out int shotsWithMod, modifiers: mods);

        var stateNoMod = WeaponState.Default;
        stateNoMod = Simulate(stateNoMod, true, false, true, 60, out int shotsNoMod);

        Assert.Less(shotsWithMod, shotsNoMod,
            "2x fire rate multiplier should allow fewer shots in the same time.");
    }

    [Test]
    public void SpreadModifier_AffectsEffectiveSpread()
    {
        var tightSpread = new WeaponModifier
        {
            FireRateMultiplier = 1f,
            SpreadMultiplier = 0.5f,
        };
        var input = new WeaponInput
        {
            Modifiers = new[] { tightSpread }.ToList(),
        };

        float effective = WeaponProcessor.GetEffectiveSpreadAngle(in _config, in input);
        Assert.AreEqual(_config.SpreadAngle * 0.5f, effective, 0.001f,
            "0.5x spread modifier should halve the spread angle.");
    }

    [Test]
    public void IdentityModifier_HasNoEffect()
    {
        var identity = new[] { WeaponModifier.Identity }.ToList();

        var stateWithMod = WeaponState.Default;
        stateWithMod = Simulate(stateWithMod, true, false, true, 60,
            out int shotsWithMod, modifiers: identity);

        var stateNoMod = WeaponState.Default;
        stateNoMod = Simulate(stateNoMod, true, false, true, 60, out int shotsNoMod);

        Assert.AreEqual(shotsNoMod, shotsWithMod,
            "An identity modifier should produce identical results to no modifier.");
    }

    [Test]
    public void MultipleModifiers_StackMultiplicatively()
    {
        var mod1 = new WeaponModifier { FireRateMultiplier = 0.5f, SpreadMultiplier = 1f };
        var mod2 = new WeaponModifier { FireRateMultiplier = 0.5f, SpreadMultiplier = 1f };
        var mods = new[] { mod1, mod2 }.ToList(); // 0.25x effective fire rate

        var state = WeaponState.Default;
        state = Simulate(state, true, false, true, 60,
            out int shotsStacked, modifiers: mods);

        var singleMod = new[] { new WeaponModifier { FireRateMultiplier = 0.5f, SpreadMultiplier = 1f } }.ToList();
        var stateSingle = WeaponState.Default;
        stateSingle = Simulate(stateSingle, true, false, true, 60,
            out int shotsSingle, modifiers: singleMod);

        Assert.Greater(shotsStacked, shotsSingle,
            "Two 0.5x modifiers stacked (= 0.25x) should fire faster than a single 0.5x modifier.");
    }

    [Test]
    public void SpreadModifiers_StackMultiplicatively()
    {
        var mod1 = new WeaponModifier { FireRateMultiplier = 1f, SpreadMultiplier = 0.5f };
        var mod2 = new WeaponModifier { FireRateMultiplier = 1f, SpreadMultiplier = 0.5f };
        var input = new WeaponInput
        {
            Modifiers = new[] { mod1, mod2 }.ToList(),
        };

        float effective = WeaponProcessor.GetEffectiveSpreadAngle(in _config, in input);
        Assert.AreEqual(_config.SpreadAngle * 0.25f, effective, 0.001f,
            "Two 0.5x spread modifiers should stack to 0.25x.");
    }

    // =====================================================================
    // Determinism
    // =====================================================================

    [Test]
    public void Determinism_SameInputs_SameOutput()
    {
        var state1 = WeaponState.Default;
        state1 = Simulate(state1, true, true, true, 30, out int shots1);

        var state2 = WeaponState.Default;
        state2 = Simulate(state2, true, true, true, 30, out int shots2);

        Assert.AreEqual(shots1, shots2, "Same inputs should produce the same shot count.");
        Assert.AreEqual(state1.TimeSinceLastFire, state2.TimeSinceLastFire, 0.0001f);
        Assert.AreEqual(state1.IsBurstActive, state2.IsBurstActive);
    }

    [Test]
    public void Determinism_SnapshotResume()
    {
        var state = WeaponState.Default;
        state = Simulate(state, true, true, true, 10, out int shotsBefore);

        // Snapshot state
        var snapshot = state;

        // Continue from original
        state = Simulate(state, true, false, true, 20, out int shotsAfter1);

        // Continue from snapshot
        var resumed = Simulate(snapshot, true, false, true, 20, out int shotsAfter2);

        Assert.AreEqual(shotsAfter1, shotsAfter2,
            "Resuming from a snapshot should produce identical results.");
    }

    // =====================================================================
    // Edge Cases
    // =====================================================================

    [Test]
    public void NotFiring_NoShots()
    {
        var state = WeaponState.Default;
        state = Simulate(state, false, false, true, 60, out int shots);

        Assert.AreEqual(0, shots, "No input should produce zero shots.");
    }

    [Test]
    public void CooldownTimer_AdvancesDuringBurst()
    {
        _config.ShotsPerFire = 3;
        _config.FireDuration = 0.3f;

        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
        };

        // Fire and start burst
        state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out _);
        float timerAfterFire = state.TimeSinceLastFire;

        // Advance a few frames during burst
        for (int i = 0; i < 5; i++)
        {
            state = WeaponProcessor.Process(state, in _config, in input, 1f / 60f, out _);
        }

        Assert.Greater(state.TimeSinceLastFire, timerAfterFire,
            "Cooldown timer should continue advancing during burst.");
    }

    [Test]
    public void SingleShotPerFire_NoBurstState()
    {
        _config.ShotsPerFire = 1;
        _config.FireDuration = 0f;

        var state = WeaponState.Default;
        state = Simulate(state, true, true, true, 1, out int shots);

        Assert.AreEqual(1, shots);
        Assert.IsFalse(state.IsBurstActive,
            "Single shot should not activate burst state.");
    }
}
