using System;
using System.Text;
using FishNet.Transporting.CanoeWebRTC;
using NUnit.Framework;

/// <summary>
/// Edit-mode tests for the <b>session token</b> wire form — one uint8 length byte then that many
/// UTF-8 bytes. This is the cross-repo contract with ArenaInfra's SignalServer.js
/// (docs/plans/trickle-ice.md, "Settled cross-repo contract"), so these tests assert the exact
/// bytes, not just the round trip: a mismatch here is a hard failure at handshake time.
/// </summary>
[TestFixture]
public class SessionTokenTests
{
    private const string SampleToken = "aB3xQ9zK1mN7pR2sT4vW6y"; // 22 chars, the minted length

    [Test]
    public void MintedTokenLength_IsTwentyTwo()
    {
        Assert.AreEqual(22, SessionToken.TokenLength);
        Assert.AreEqual(SessionToken.TokenLength, SampleToken.Length, "Sample must be a realistically sized token.");
    }

    [Test]
    public void Write_EmitsLengthByteThenUtf8Bytes()
    {
        byte[] buffer = new byte[64];

        int written = SessionToken.Write(SampleToken, buffer, 0);

        Assert.AreEqual(1 + SampleToken.Length, written, "One length byte plus the token's UTF-8 bytes.");
        Assert.AreEqual(SampleToken.Length, buffer[0], "First byte is the length prefix.");
        Assert.AreEqual(SampleToken, Encoding.UTF8.GetString(buffer, 1, SampleToken.Length));
        Assert.AreEqual(0, buffer[written], "Nothing is written past the token - no null terminator, no padding.");
    }

    [Test]
    public void ByteLength_MatchesWhatWriteEmits()
    {
        byte[] buffer = new byte[64];

        Assert.AreEqual(SessionToken.Write(SampleToken, buffer, 0), SessionToken.ByteLength(SampleToken));
    }

    [Test]
    public void WriteThenRead_RoundTrips_AtAnOffset()
    {
        byte[] buffer = new byte[64];
        const int offset = 5; // e.g. after an opcode byte and an int32

        int written = SessionToken.Write(SampleToken, buffer, offset);
        string read = SessionToken.Read(buffer, offset, out int bytesRead);

        Assert.AreEqual(SampleToken, read);
        Assert.AreEqual(written, bytesRead, "Reader and writer must agree on how far the field advances.");
    }

    [Test]
    public void EmptyToken_RoundTripsAsASingleZeroLengthByte()
    {
        byte[] buffer = new byte[4];

        int written = SessionToken.Write("", buffer, 0);

        Assert.AreEqual(1, written);
        Assert.AreEqual(0, buffer[0]);
        Assert.AreEqual("", SessionToken.Read(buffer, 0, out int bytesRead));
        Assert.AreEqual(1, bytesRead);
    }

    [Test]
    public void TokenLongerThanTheLengthByteCanExpress_Throws()
    {
        byte[] buffer = new byte[512];
        string tooLong = new string('a', byte.MaxValue + 1);

        Assert.Throws<ArgumentException>(() => SessionToken.Write(tooLong, buffer, 0));
    }
}
