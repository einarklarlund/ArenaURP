using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Edit-mode unit tests for <see cref="RespawnProcessor"/>.
/// No GameObjects, no FishNet, no networking - pure struct in, struct out.
///
/// Player identities are represented by trivial sentinel objects; Core uses reference
/// equality and never inspects them.
/// </summary>
[TestFixture]
public class RespawnProcessorTests
{
    private RespawnConfig _config;

    private sealed class PlayerKey
    {
        public readonly string Name;
        public PlayerKey(string name) { Name = name; }
        public override string ToString() => Name;
    }

    [SetUp]
    public void SetUp()
    {
        _config = new RespawnConfig { DelaySeconds = 3f };
    }

    private static RespawnInput InputWith(params object[] keys) => new()
    {
        NewlyKilled = keys != null && keys.Length > 0 ? (IReadOnlyList<object>)keys : null,
    };

    private static RespawnInput EmptyInput() => RespawnInput.Default;

    // =====================================================================
    // Single timer
    // =====================================================================

    [Test]
    public void Process_SingleTimer_CountsDownByDeltaTime_AndReportsReadyWhenExpired()
    {
        var state = RespawnState.Default;
        var player = new PlayerKey("p");

        // Enqueue the player on the first tick (zero dt so the timer doesn't tick yet).
        state = RespawnProcessor.Process(state, in _config, InputWith(player), 0f);
        Assert.AreEqual(1, state.PendingRespawns.Count, "Newly killed player should be pending.");
        Assert.AreEqual(0, state.ReadyThisTick.Count, "Player should not be ready yet.");
        Assert.AreEqual(_config.DelaySeconds, state.PendingRespawns[0].RemainingSeconds, 0.0001f);

        // Tick down well short of the threshold.
        state = RespawnProcessor.Process(state, in _config, EmptyInput(), 1f);
        Assert.AreEqual(1, state.PendingRespawns.Count);
        Assert.AreEqual(0, state.ReadyThisTick.Count);
        Assert.AreEqual(_config.DelaySeconds - 1f, state.PendingRespawns[0].RemainingSeconds, 0.0001f);

        // One big tick that crosses zero - player should be reported ready and removed.
        state = RespawnProcessor.Process(state, in _config, EmptyInput(), _config.DelaySeconds);
        Assert.AreEqual(0, state.PendingRespawns.Count, "Ready entries must be removed from the pending list.");
        Assert.AreEqual(1, state.ReadyThisTick.Count);
        Assert.AreSame(player, state.ReadyThisTick[0]);
    }

    // =====================================================================
    // Many concurrent timers
    // =====================================================================

    [Test]
    public void Process_MultipleTimers_CountDownIndependently()
    {
        var state = RespawnState.Default;
        var a = new PlayerKey("a");
        var b = new PlayerKey("b");

        // Enqueue 'a' at t=0 with a 3s delay.
        state = RespawnProcessor.Process(state, in _config, InputWith(a), 0f);

        // 1 second later, enqueue 'b'. 'a' has 2s left, 'b' has 3s left.
        state = RespawnProcessor.Process(state, in _config, InputWith(b), 1f);
        Assert.AreEqual(2, state.PendingRespawns.Count);
        Assert.AreEqual(2f, FindRemaining(state, a), 0.0001f, "'a' must have decremented while 'b' was added.");
        Assert.AreEqual(3f, FindRemaining(state, b), 0.0001f, "'b' must enter the queue with the full delay.");
        Assert.AreEqual(0, state.ReadyThisTick.Count);

        // 2 more seconds: 'a' hits zero, 'b' has 1s left.
        state = RespawnProcessor.Process(state, in _config, EmptyInput(), 2f);
        Assert.AreEqual(1, state.PendingRespawns.Count, "Only 'a' should have been harvested.");
        Assert.AreEqual(1, state.ReadyThisTick.Count);
        Assert.AreSame(a, state.ReadyThisTick[0]);
        Assert.AreEqual(1f, FindRemaining(state, b), 0.0001f);

        // 1 more second: 'b' hits zero.
        state = RespawnProcessor.Process(state, in _config, EmptyInput(), 1f);
        Assert.AreEqual(0, state.PendingRespawns.Count);
        Assert.AreEqual(1, state.ReadyThisTick.Count);
        Assert.AreSame(b, state.ReadyThisTick[0]);
    }

    [Test]
    public void Process_AddingNewPending_PreservesExistingTimers()
    {
        var state = RespawnState.Default;
        var a = new PlayerKey("a");
        var b = new PlayerKey("b");
        var c = new PlayerKey("c");

        state = RespawnProcessor.Process(state, in _config, InputWith(a, b), 0f);
        Assert.AreEqual(2, state.PendingRespawns.Count);

        // Advance 0.5s and add c.
        state = RespawnProcessor.Process(state, in _config, InputWith(c), 0.5f);

        Assert.AreEqual(3, state.PendingRespawns.Count, "All three players should be tracked.");
        Assert.AreEqual(2.5f, FindRemaining(state, a), 0.0001f);
        Assert.AreEqual(2.5f, FindRemaining(state, b), 0.0001f);
        Assert.AreEqual(3f, FindRemaining(state, c), 0.0001f, "Newcomer 'c' must start with the full delay.");
        Assert.AreEqual(0, state.ReadyThisTick.Count);
    }

    [Test]
    public void Process_MultiplePlayersExpiringOnSameTick_AreAllReported()
    {
        var state = RespawnState.Default;
        var a = new PlayerKey("a");
        var b = new PlayerKey("b");

        state = RespawnProcessor.Process(state, in _config, InputWith(a, b), 0f);
        state = RespawnProcessor.Process(state, in _config, EmptyInput(), _config.DelaySeconds);

        Assert.AreEqual(0, state.PendingRespawns.Count);
        Assert.AreEqual(2, state.ReadyThisTick.Count);
        CollectionAssert.AreEquivalent(new object[] { a, b }, state.ReadyThisTick);
    }

    // =====================================================================
    // One-shot edge semantics for ReadyThisTick
    // =====================================================================

    [Test]
    public void Process_ReadyThisTick_IsClearedOnNextProcessCall()
    {
        var state = RespawnState.Default;
        var player = new PlayerKey("p");

        state = RespawnProcessor.Process(state, in _config, InputWith(player), 0f);
        state = RespawnProcessor.Process(state, in _config, EmptyInput(), _config.DelaySeconds);
        Assert.AreEqual(1, state.ReadyThisTick.Count, "Player should be reported on the tick they expire.");

        // Any subsequent Process call - even with zero dt and no input - must clear the edge list.
        state = RespawnProcessor.Process(state, in _config, EmptyInput(), 0f);
        Assert.AreEqual(0, state.ReadyThisTick.Count,
            "ReadyThisTick is a one-shot signal and must clear on the next Process call.");

        // And it must stay clear for many subsequent quiet ticks.
        for (int i = 0; i < 50; i++)
        {
            state = RespawnProcessor.Process(state, in _config, EmptyInput(), 1f / 60f);
            Assert.AreEqual(0, state.ReadyThisTick.Count);
        }
    }

    // =====================================================================
    // Idempotency
    // =====================================================================

    [Test]
    public void Process_EmptyStateEmptyInput_RemainsEmpty_ForAnyDeltaTime()
    {
        var state = RespawnState.Default;

        foreach (var dt in new[] { 0f, 0.016f, 1f, 100f, -1f })
        {
            state = RespawnProcessor.Process(state, in _config, EmptyInput(), dt);
            Assert.AreEqual(0, state.PendingRespawns.Count,
                $"Empty state with empty input must stay empty (dt={dt}).");
            Assert.AreEqual(0, state.ReadyThisTick.Count,
                $"Empty state with empty input must produce no ready entries (dt={dt}).");
        }
    }

    [Test]
    public void Process_NegativeDeltaTime_IsClampedToZero_DoesNotRewindTimers()
    {
        var state = RespawnState.Default;
        var player = new PlayerKey("p");

        state = RespawnProcessor.Process(state, in _config, InputWith(player), 0f);
        var initial = state.PendingRespawns[0].RemainingSeconds;

        state = RespawnProcessor.Process(state, in _config, EmptyInput(), -10f);
        Assert.AreEqual(initial, state.PendingRespawns[0].RemainingSeconds, 0.0001f,
            "Negative dt must not advance or rewind the timer.");
        Assert.AreEqual(0, state.ReadyThisTick.Count);
    }

    [Test]
    public void Process_LazilyAllocatesLists_WhenStateBuiltWithoutDefault()
    {
        // Caller built a struct without calling RespawnState.Default - lists are null.
        var state = new RespawnState();
        var player = new PlayerKey("p");

        // Should not throw; processor must lazily initialise the lists.
        state = RespawnProcessor.Process(state, in _config, InputWith(player), 0f);

        Assert.IsNotNull(state.PendingRespawns);
        Assert.IsNotNull(state.ReadyThisTick);
        Assert.AreEqual(1, state.PendingRespawns.Count);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static float FindRemaining(RespawnState state, object key)
    {
        foreach (var p in state.PendingRespawns)
        {
            if (ReferenceEquals(p.Key, key)) return p.RemainingSeconds;
        }
        Assert.Fail($"Key {key} not found in pending list.");
        return float.NaN;
    }
}
