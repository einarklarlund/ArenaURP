using System.Collections.Generic;
using FishNet.Transporting.CanoeWebRTC;
using NUnit.Framework;

/// <summary>
/// Edit-mode tests for <see cref="OutboundCandidateBuffer"/> — the ordering invariant of the
/// outbound trickle path. No sockets, no session tokens: candidates in, candidates out in order.
///
/// The invariant under test: candidates gathered before the signal server can resolve the route
/// must be sent in arrival order once it can.
///
/// It is <b>not</b> the inbound queue and deliberately inherits neither of its end-of-candidates
/// rules — no marker-collapse, no marker-last. The local ICE agent emits the marker last by
/// construction, so reordering here could only ever be wrong.
/// </summary>
[TestFixture]
public class OutboundCandidateBufferTests
{
    private OutboundCandidateBuffer _buffer;

    private const string A = "candidate:1 1 udp 2113937151 192.168.1.20 51234 typ host";
    private const string B = "candidate:2 1 udp 1677729535 203.0.113.7 51235 typ srflx";
    private const string C = "candidate:3 1 udp 1677729535 203.0.113.7 51236 typ srflx";

    [SetUp]
    public void SetUp()
    {
        _buffer = new OutboundCandidateBuffer();
    }

    // =====================================================================
    // Before the route resolves: hold
    // =====================================================================

    [Test]
    public void BeforeTheRouteOpens_CandidatesAreHeld_NotSent()
    {
        Assert.IsFalse(_buffer.TrySendNow(A), "Nothing may be sent before the route resolves.");
        Assert.IsFalse(_buffer.TrySendNow(B));

        Assert.IsFalse(_buffer.RouteOpen);
        Assert.AreEqual(2, _buffer.HeldCount);
    }

    // =====================================================================
    // Opening the route: release in arrival order
    // =====================================================================

    [Test]
    public void OpeningTheRoute_ReleasesHeldCandidatesInArrivalOrder()
    {
        _buffer.TrySendNow(A);
        _buffer.TrySendNow(B);
        _buffer.TrySendNow(C);

        IReadOnlyList<string> released = _buffer.Open();

        Assert.AreEqual(new[] { A, B, C }, released);
        Assert.AreEqual(0, _buffer.HeldCount);
        Assert.IsTrue(_buffer.RouteOpen);
    }

    [Test]
    public void OpeningARouteThatHeldNothing_ReleasesNothing()
    {
        Assert.IsEmpty(_buffer.Open());
        Assert.IsTrue(_buffer.RouteOpen);
    }

    // =====================================================================
    // After the route is open: pass through
    // =====================================================================

    [Test]
    public void AfterTheRouteOpens_CandidatesPassStraightThrough()
    {
        _buffer.Open();

        Assert.IsTrue(_buffer.TrySendNow(A));
        Assert.IsTrue(_buffer.TrySendNow(B));

        Assert.AreEqual(0, _buffer.HeldCount, "A pass-through candidate must not be held as well as sent.");
    }

    [Test]
    public void OpeningASecondTime_ReleasesNothing()
    {
        _buffer.TrySendNow(A);
        Assert.AreEqual(new[] { A }, _buffer.Open());

        Assert.IsEmpty(_buffer.Open(), "The held candidates have already been sent once.");
    }

    // =====================================================================
    // Reset
    // =====================================================================

    [Test]
    public void Reset_ReturnsToHoldingWithNothingHeld()
    {
        _buffer.TrySendNow(A);
        _buffer.Open();
        _buffer.TrySendNow(B);

        _buffer.Reset();

        Assert.IsFalse(_buffer.RouteOpen);
        Assert.AreEqual(0, _buffer.HeldCount);
        Assert.IsFalse(_buffer.TrySendNow(C), "After a reset the buffer holds again.");
    }
}
