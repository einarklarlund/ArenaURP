using System.Collections.Generic;

namespace FishNet.Transporting.CanoeWebRTC
{
    /// <summary>
    /// Holds trickled <b>candidates</b> that arrive before the remote description is set, hands them
    /// back in <b>arrival order</b> once it is, and passes everything through afterwards.
    /// <para>
    /// One instance per peer connection — the peer-connection wrapper owns it (ADR 0011). This unit
    /// is deliberately pure: no <c>RTCPeerConnection</c>, no WebGL interop, no engine types.
    /// <i>Applying</i> a candidate — and translating the end-of-candidates marker into
    /// <c>AddIceCandidate(null)</c> / <c>addIceCandidate({candidate:""})</c> — is the caller's job.
    /// </para>
    /// <para>
    /// The end-of-candidates marker rides the same queue and is always released <b>last</b>: telling
    /// ICE that gathering is complete before the candidates it completes have been applied would
    /// throw away paths. Repeated markers collapse into one.
    /// </para>
    /// <code>
    /// // on a trickled candidate
    /// if (_inbound.TryApplyNow(candidate)) Apply(candidate);
    /// // when SetRemoteDescription completes
    /// foreach (string c in _inbound.Release()) Apply(c);
    /// </code>
    /// </summary>
    public sealed class InboundCandidateQueue
    {
        private static readonly IReadOnlyList<string> Nothing = new string[0];

        private readonly List<string> _buffered = new List<string>();
        private bool _endOfCandidatesBuffered;
        private bool _remoteDescriptionSet;

        /// <summary>True once <see cref="Release"/> has been called — the queue is in pass-through.</summary>
        public bool RemoteDescriptionSet => _remoteDescriptionSet;

        /// <summary>How many entries <see cref="Release"/> would hand back right now, marker included.</summary>
        public int BufferedCount => _buffered.Count + (_endOfCandidatesBuffered ? 1 : 0);

        /// <summary>An empty (or absent) candidate is the end-of-candidates marker.</summary>
        public static bool IsEndOfCandidates(string candidate) => string.IsNullOrEmpty(candidate);

        /// <summary>
        /// Offer an inbound candidate. Returns <c>true</c> when the caller should apply it
        /// immediately, <c>false</c> when the queue has buffered it until <see cref="Release"/>.
        /// </summary>
        public bool TryApplyNow(string candidate)
        {
            if (_remoteDescriptionSet)
                return true;

            if (IsEndOfCandidates(candidate))
                _endOfCandidatesBuffered = true;
            else
                _buffered.Add(candidate);

            return false;
        }

        /// <summary>
        /// The remote description is now set. Returns everything buffered in arrival order, with the
        /// end-of-candidates marker last if one arrived, and switches the queue to pass-through.
        /// Idempotent: a second call returns nothing.
        /// </summary>
        public IReadOnlyList<string> Release()
        {
            _remoteDescriptionSet = true;

            if (_buffered.Count == 0 && !_endOfCandidatesBuffered)
                return Nothing;

            List<string> flush = new List<string>(BufferedCount);
            flush.AddRange(_buffered);
            if (_endOfCandidatesBuffered)
                flush.Add(TrickledICE.EndOfCandidates);

            _buffered.Clear();
            _endOfCandidatesBuffered = false;

            return flush;
        }

        /// <summary>Back to buffering with nothing held — for a connection wrapper that is reused.</summary>
        public void Reset()
        {
            _buffered.Clear();
            _endOfCandidatesBuffered = false;
            _remoteDescriptionSet = false;
        }
    }
}
