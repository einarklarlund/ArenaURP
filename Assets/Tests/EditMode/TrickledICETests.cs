using System;
using System.Text;
using FishNet.Transporting.CanoeWebRTC;
using NUnit.Framework;

/// <summary>
/// Edit-mode tests for the trickle payload — the bytes after the 0x08 / 0x09 opcode.
///
/// Settled cross-repo contract (docs/plans/trickle-ice.md):
///   peer -> server :  [tok] candidate...
///   server -> peer :  int32 connectionId candidate...
/// `candidate...` is the remainder of the buffer, so zero remaining bytes is the
/// end-of-candidates marker. The exact bytes are asserted, not just the round trip: the mirror
/// image of this parser lives in ArenaInfra's SignalServer.js.
/// </summary>
[TestFixture]
public class TrickledICETests
{
    private const string Token = "aB3xQ9zK1mN7pR2sT4vW6y";
    private const string Candidate = "candidate:1 1 udp 2113937151 192.168.1.20 51234 typ host";

    // =====================================================================
    // peer -> server:  [tok] candidate...
    // =====================================================================

    [Test]
    public void ToServer_Serialize_EmitsLengthPrefixedTokenThenTheCandidate()
    {
        byte[] bytes = TrickledICE.ToServer(Token, Candidate).SerializeToServer();

        Assert.AreEqual(1 + Token.Length + Candidate.Length, bytes.Length, "No target player id rides an outbound trickle - the token is the whole route.");
        Assert.AreEqual(Token.Length, bytes[0]);
        Assert.AreEqual(Token, Encoding.UTF8.GetString(bytes, 1, Token.Length));
        Assert.AreEqual(Candidate, Encoding.UTF8.GetString(bytes, 1 + Token.Length, Candidate.Length));
    }

    [Test]
    public void ToServer_RoundTrips_CandidateAndToken()
    {
        byte[] bytes = TrickledICE.ToServer(Token, Candidate).SerializeToServer();

        TrickledICE parsed = TrickledICE.DeserializeToServer(bytes);

        Assert.AreEqual(Candidate, parsed.candidate);
        Assert.AreEqual(Token, parsed.sessionToken, "The deserializer must not discard the routing identity.");
        Assert.AreEqual(TrickledICE.NoConnectionId, parsed.connectionId, "The peer -> server form carries no connection id.");
        Assert.IsFalse(parsed.IsEndOfCandidates);
    }

    [Test]
    public void ToServer_EndOfCandidates_IsATokenAndNothingElse()
    {
        byte[] bytes = TrickledICE.ToServer(Token, TrickledICE.EndOfCandidates).SerializeToServer();

        Assert.AreEqual(1 + Token.Length, bytes.Length, "Zero remaining bytes is the end-of-candidates marker.");

        TrickledICE parsed = TrickledICE.DeserializeToServer(bytes);

        Assert.AreEqual(Token, parsed.sessionToken);
        Assert.AreEqual(TrickledICE.EndOfCandidates, parsed.candidate);
        Assert.IsTrue(parsed.IsEndOfCandidates);
    }

    // =====================================================================
    // server -> peer:  int32 connectionId candidate...
    // =====================================================================

    [Test]
    public void ToPeer_Serialize_EmitsTheStampedConnectionIdThenTheCandidate()
    {
        byte[] bytes = TrickledICE.ToPeer(7, Candidate).SerializeToPeer();

        Assert.AreEqual(sizeof(int) + Candidate.Length, bytes.Length);
        Assert.AreEqual(7, BitConverter.ToInt32(bytes, 0));
        Assert.AreEqual(Candidate, Encoding.UTF8.GetString(bytes, sizeof(int), Candidate.Length));
    }

    [Test]
    public void ToPeer_RoundTrips_CandidateAndConnectionId()
    {
        byte[] bytes = TrickledICE.ToPeer(42, Candidate).SerializeToPeer();

        TrickledICE parsed = TrickledICE.DeserializeToPeer(bytes);

        Assert.AreEqual(Candidate, parsed.candidate);
        Assert.AreEqual(42, parsed.connectionId, "The deserializer used to throw the stamped identity away and return -1.");
        Assert.IsNull(parsed.sessionToken, "The server -> peer form carries no token.");
    }

    [Test]
    public void ToPeer_EndOfCandidates_IsAConnectionIdAndNothingElse()
    {
        byte[] bytes = TrickledICE.ToPeer(3, TrickledICE.EndOfCandidates).SerializeToPeer();

        Assert.AreEqual(sizeof(int), bytes.Length);

        TrickledICE parsed = TrickledICE.DeserializeToPeer(bytes);

        Assert.AreEqual(3, parsed.connectionId);
        Assert.IsTrue(parsed.IsEndOfCandidates);
    }

    [Test]
    public void Deserialize_ReadsFromASegmentsOffset_NotTheArrayStart()
    {
        // What HandleReceivedData actually hands over: the payload sits after the opcode byte.
        byte[] payload = TrickledICE.ToPeer(9, Candidate).SerializeToPeer();
        byte[] message = new byte[1 + payload.Length];
        message[0] = 0x08;
        Array.Copy(payload, 0, message, 1, payload.Length);

        TrickledICE parsed = TrickledICE.DeserializeToPeer(new ArraySegment<byte>(message, 1, payload.Length));

        Assert.AreEqual(9, parsed.connectionId);
        Assert.AreEqual(Candidate, parsed.candidate);
    }

    [Test]
    public void Deserialize_TruncatedPayload_Throws()
    {
        Assert.Throws<ArgumentException>(() => TrickledICE.DeserializeToPeer(new byte[2]));
        Assert.Throws<ArgumentException>(() => TrickledICE.DeserializeToServer(new byte[0]));
    }
}
