using System.Collections.Generic;
using FishNet.Transporting.CanoeWebRTC;
using NUnit.Framework;

/// <summary>
/// Edit-mode tests for <see cref="InboundCandidateQueue"/> — the ordering invariant of the trickle
/// path. No RTCPeerConnection, no interop: candidates in, candidates out in order.
///
/// The invariant under test: candidates that arrive before the remote description is set must be
/// applied in arrival order once it is, with the end-of-candidates marker last.
/// </summary>
[TestFixture]
public class InboundCandidateQueueTests
{
    private InboundCandidateQueue _queue;

    private const string A = "candidate:1 1 udp 2113937151 192.168.1.20 51234 typ host";
    private const string B = "candidate:2 1 udp 1677729535 203.0.113.7 51235 typ srflx";
    private const string C = "candidate:3 1 udp 1677729535 203.0.113.7 51236 typ srflx";

    [SetUp]
    public void SetUp()
    {
        _queue = new InboundCandidateQueue();
    }

    // =====================================================================
    // Before the remote description is set: buffer
    // =====================================================================

    [Test]
    public void BeforeRemoteDescription_CandidatesAreBuffered_NotApplied()
    {
        Assert.IsFalse(_queue.TryApplyNow(A), "Nothing may be applied before the remote description is set.");
        Assert.IsFalse(_queue.TryApplyNow(B));

        Assert.IsFalse(_queue.RemoteDescriptionSet);
        Assert.AreEqual(2, _queue.BufferedCount);
    }

    [Test]
    public void Release_FlushesInArrivalOrder()
    {
        _queue.TryApplyNow(A);
        _queue.TryApplyNow(B);
        _queue.TryApplyNow(C);

        CollectionAssert.AreEqual(new[] { A, B, C }, _queue.Release());
        Assert.AreEqual(0, _queue.BufferedCount, "Release hands the buffer over; it does not keep a copy.");
    }

    [Test]
    public void Release_WithNothingBuffered_ReturnsNothing()
    {
        CollectionAssert.IsEmpty(_queue.Release());
        Assert.IsTrue(_queue.RemoteDescriptionSet);
    }

    // =====================================================================
    // After the remote description is set: pass through
    // =====================================================================

    [Test]
    public void AfterRelease_CandidatesPassThrough()
    {
        _queue.TryApplyNow(A);
        _queue.Release();

        Assert.IsTrue(_queue.TryApplyNow(B), "Once released the queue is a pass-through.");
        Assert.IsTrue(_queue.TryApplyNow(TrickledICE.EndOfCandidates), "A late end marker passes through too.");
        Assert.AreEqual(0, _queue.BufferedCount, "Pass-through must not accumulate.");
    }

    [Test]
    public void Release_IsIdempotent()
    {
        _queue.TryApplyNow(A);
        _queue.Release();

        CollectionAssert.IsEmpty(_queue.Release(), "A second release must not replay the flush.");
    }

    // =====================================================================
    // The end-of-candidates marker rides the same queue and is applied last
    // =====================================================================

    [Test]
    public void EndOfCandidatesMarker_IsReleasedLast()
    {
        _queue.TryApplyNow(A);
        _queue.TryApplyNow(TrickledICE.EndOfCandidates);
        _queue.TryApplyNow(B);

        IReadOnlyList<string> flushed = _queue.Release();

        CollectionAssert.AreEqual(new[] { A, B, TrickledICE.EndOfCandidates }, flushed,
            "Real candidates keep arrival order; the marker is applied last whatever order it arrived in.");
        Assert.IsTrue(InboundCandidateQueue.IsEndOfCandidates(flushed[flushed.Count - 1]));
    }

    [Test]
    public void EndOfCandidatesMarker_IsBufferedLikeAnyOtherCandidate()
    {
        Assert.IsFalse(_queue.TryApplyNow(TrickledICE.EndOfCandidates), "The marker rides the same queue.");
        Assert.AreEqual(1, _queue.BufferedCount);

        CollectionAssert.AreEqual(new[] { TrickledICE.EndOfCandidates }, _queue.Release());
    }

    [Test]
    public void RepeatedEndOfCandidatesMarkers_CollapseToOne()
    {
        _queue.TryApplyNow(A);
        _queue.TryApplyNow(TrickledICE.EndOfCandidates);
        _queue.TryApplyNow(null);

        CollectionAssert.AreEqual(new[] { A, TrickledICE.EndOfCandidates }, _queue.Release());
    }

    // =====================================================================
    // Reuse
    // =====================================================================

    [Test]
    public void Reset_ReturnsToBuffering_WithNothingHeld()
    {
        _queue.TryApplyNow(A);
        _queue.Release();

        _queue.Reset();

        Assert.IsFalse(_queue.RemoteDescriptionSet);
        Assert.IsFalse(_queue.TryApplyNow(B), "After a reset the queue buffers again.");
        CollectionAssert.AreEqual(new[] { B }, _queue.Release(), "Nothing from before the reset survives.");
    }
}
