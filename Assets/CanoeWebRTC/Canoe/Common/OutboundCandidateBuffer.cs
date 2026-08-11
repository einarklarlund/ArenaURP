using System.Collections.Generic;

namespace FishNet.Transporting.CanoeWebRTC
{
    /// <summary>
    /// Holds locally gathered <b>candidates</b> that are ready before the signal server can resolve
    /// the route they would be sent on, hands them back in <b>arrival order</b> once it can, and
    /// passes everything through afterwards.
    /// <para>
    /// One instance per live trickle route — the signalling layer owns it. Deliberately pure: it
    /// knows nothing about session tokens, opcodes, or sockets. <i>Sending</i> a candidate, and
    /// retiring the route when the end-of-candidates marker goes out, are the caller's job.
    /// </para>
    /// <para>
    /// The sibling <see cref="InboundCandidateQueue"/> solves the mirrored problem and is
    /// deliberately <b>not</b> reused here: its end-of-candidates rule reorders the marker to last
    /// and collapses repeats, which the outbound path must never do and never needs — the local ICE
    /// agent emits the marker last by construction.
    /// </para>
    /// <code>
    /// // on a locally gathered candidate
    /// if (_outbound.TrySendNow(candidate)) Send(candidate);
    /// // when the route resolves
    /// foreach (string c in _outbound.Open()) Send(c);
    /// </code>
    /// </summary>
    public sealed class OutboundCandidateBuffer
    {
        private static readonly IReadOnlyList<string> Nothing = new string[0];

        private readonly List<string> _held = new List<string>();
        private bool _routeOpen;

        /// <summary>True once <see cref="Open"/> has been called — the buffer is in pass-through.</summary>
        public bool RouteOpen => _routeOpen;

        /// <summary>How many candidates <see cref="Open"/> would hand back right now.</summary>
        public int HeldCount => _held.Count;

        /// <summary>
        /// Offer a locally gathered candidate. Returns <c>true</c> when the caller should send it
        /// immediately, <c>false</c> when the buffer has held it until <see cref="Open"/>.
        /// </summary>
        public bool TrySendNow(string candidate)
        {
            if (_routeOpen)
                return true;

            _held.Add(candidate);
            return false;
        }

        /// <summary>
        /// The route now resolves. Returns everything held in arrival order and switches the buffer
        /// to pass-through. Idempotent: a second call returns nothing.
        /// </summary>
        public IReadOnlyList<string> Open()
        {
            _routeOpen = true;

            if (_held.Count == 0)
                return Nothing;

            List<string> flush = new List<string>(_held);
            _held.Clear();

            return flush;
        }

        /// <summary>Back to holding with nothing held — for a route object that is reused.</summary>
        public void Reset()
        {
            _held.Clear();
            _routeOpen = false;
        }
    }
}
