using System;
using System.Collections.Generic;
using System.Text;

namespace FishNet.Transporting.CanoeWebRTC
{
    //same thing as fishnet but without creating a double minimum length
    public static class CustomByteArrayPool
    {

        private static Queue<byte[]> _byteArrays = new Queue<byte[]>();


        public static byte[] Retrieve(int minimumLength)
        {
            byte[] result = null;

            if (_byteArrays.Count > 0)
                result = _byteArrays.Dequeue();

            int doubleMinimumLength = (minimumLength);
            if (result == null)
                result = new byte[doubleMinimumLength];
            else if (result.Length < minimumLength)
                Array.Resize(ref result, doubleMinimumLength);


            return result;
        }


        public static void Store(byte[] buffer)
        {

            if (_byteArrays.Count > 300)
                return;
            _byteArrays.Enqueue(buffer);
        }
    }

    [System.Serializable]
    public class ICEServer
    {
        public string url;
        public string username;
        public string credential;


        public static string GetFullString()
        {
            var iceServers = CanoeWebRTC._iceServers;

            if (iceServers == null || iceServers.Count == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();

            foreach (ICEServer server in iceServers)
            {
                sb.Append(server.url ?? "").Append("__")
                  .Append(server.username ?? "").Append("__")
                  .Append(server.credential ?? "").Append(";;");
            }

            if (sb.Length > 0)
            {
                sb.Length -= 2;
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Wire form of a <b>session token</b> — the opaque, server-minted key naming a signaling
    /// session (CONTEXT.md → Networking). Framed exactly like the room code already is:
    /// one <c>uint8</c> length byte, then that many UTF-8 bytes. Never fixed width, never
    /// null terminated.
    /// <para>
    /// This is one struct written twice in two languages: the mirror image lives in ArenaInfra's
    /// <c>SignalServer.js</c>. The contract is fixed in <c>docs/plans/trickle-ice.md</c> —
    /// "Settled cross-repo contract". Do not change it on one side.
    /// </para>
    /// </summary>
    public static class SessionToken
    {
        /// <summary>
        /// Characters in a server-minted token (a 62-char <c>A–Z a–z 0–9</c> alphabet). The
        /// (de)serializer is length-prefixed and does not depend on this — it is the agreed
        /// size, kept here so a mismatch with the server is visible in one place.
        /// </summary>
        public const int TokenLength = 22;

        /// <summary>Bytes <see cref="Write"/> emits for <paramref name="token"/>: the length byte plus its UTF-8 bytes.</summary>
        public static int ByteLength(string token)
        {
            return sizeof(byte) + Encoding.UTF8.GetByteCount(token ?? "");
        }

        /// <summary>Writes <paramref name="token"/> at <paramref name="offset"/>; returns the number of bytes written.</summary>
        public static int Write(string token, byte[] buffer, int offset)
        {
            token = token ?? "";

            int tokenBytes = Encoding.UTF8.GetByteCount(token);
            if (tokenBytes > byte.MaxValue)
                throw new ArgumentException($"Session token is {tokenBytes} bytes; the wire form allows at most {byte.MaxValue}.", nameof(token));

            buffer[offset] = (byte)tokenBytes;
            Encoding.UTF8.GetBytes(token, 0, token.Length, buffer, offset + sizeof(byte));

            return sizeof(byte) + tokenBytes;
        }

        /// <summary>Reads a token at <paramref name="offset"/>; <paramref name="bytesRead"/> is the length byte plus the token.</summary>
        public static string Read(byte[] buffer, int offset, out int bytesRead)
        {
            int tokenBytes = buffer[offset];
            bytesRead = sizeof(byte) + tokenBytes;

            return Encoding.UTF8.GetString(buffer, offset + sizeof(byte), tokenBytes);
        }
    }

    /// <summary>
    /// The payload of a trickle message (<c>0x08 trickleToClient</c> / <c>0x09 trickleToHost</c>) —
    /// everything after the opcode byte. One <b>candidate</b> per message; an <b>empty</b> candidate
    /// string is the end-of-candidates marker.
    /// <para>
    /// The two hops carry different routing identity, per the settled cross-repo contract:
    /// <code>
    /// peer → server :  [tok] candidate…              the session token alone selects the route
    /// server → peer :  int32 connectionId candidate… the server stamps the session's connection id
    /// </code>
    /// <c>candidate…</c> is the remainder of the buffer read as UTF-8, so <b>zero remaining bytes is
    /// the end-of-candidates marker</b>. A peer serializes the first form and deserializes the
    /// second; the other half of each pair mirrors the server and is what the round-trip test pins.
    /// </para>
    /// <para>
    /// Note the settled contract carries <i>one</i> id, not a (sender, receiver) pair: outbound the
    /// token is the whole route, inbound the server-stamped <c>connectionId</c> names the peer
    /// connection (the host indexes its connections by it; the client ignores it).
    /// </para>
    /// </summary>
    public struct TrickledICE
    {
        /// <summary>An empty candidate is the end-of-candidates marker — gathering is complete.</summary>
        public const string EndOfCandidates = "";

        /// <summary>No connection id is stamped on the peer → server form.</summary>
        public const int NoConnectionId = -1;

        public string candidate;

        /// <summary>Set on the peer → server form; <c>null</c> on the server → peer form.</summary>
        public string sessionToken;

        /// <summary>Set on the server → peer form; <see cref="NoConnectionId"/> on the peer → server form.</summary>
        public int connectionId;

        /// <summary>A candidate addressed to the server by session token (the form a peer sends).</summary>
        public static TrickledICE ToServer(string sessionToken, string candidate)
        {
            return new TrickledICE
            {
                candidate = candidate ?? EndOfCandidates,
                sessionToken = sessionToken,
                connectionId = NoConnectionId,
            };
        }

        /// <summary>A candidate stamped with the session's connection id (the form the server relays).</summary>
        public static TrickledICE ToPeer(int connectionId, string candidate)
        {
            return new TrickledICE
            {
                candidate = candidate ?? EndOfCandidates,
                sessionToken = null,
                connectionId = connectionId,
            };
        }

        public bool IsEndOfCandidates => string.IsNullOrEmpty(candidate);

        /// <summary><c>[tok] candidate…</c></summary>
        public byte[] SerializeToServer()
        {
            string value = candidate ?? EndOfCandidates;

            int tokenLength = SessionToken.ByteLength(sessionToken);
            byte[] result = new byte[tokenLength + Encoding.UTF8.GetByteCount(value)];

            int offset = SessionToken.Write(sessionToken, result, 0);
            Encoding.UTF8.GetBytes(value, 0, value.Length, result, offset);

            return result;
        }

        /// <summary>Inverse of <see cref="SerializeToServer"/> — what the signal server parses.</summary>
        public static TrickledICE DeserializeToServer(ArraySegment<byte> data)
        {
            if (data.Count < sizeof(byte))
                throw new ArgumentException("Trickle payload is too short to hold a session token.", nameof(data));

            if (sizeof(byte) + data.Array[data.Offset] > data.Count)
                throw new ArgumentException("Trickle payload's session token overruns the buffer.", nameof(data));

            string token = SessionToken.Read(data.Array, data.Offset, out int tokenLength);
            string candidate = Encoding.UTF8.GetString(data.Array, data.Offset + tokenLength, data.Count - tokenLength);

            return ToServer(token, candidate);
        }

        /// <summary><c>int32 connectionId candidate…</c></summary>
        public byte[] SerializeToPeer()
        {
            string value = candidate ?? EndOfCandidates;

            byte[] result = new byte[sizeof(int) + Encoding.UTF8.GetByteCount(value)];

            BitConverter.GetBytes(connectionId).CopyTo(result, 0);
            Encoding.UTF8.GetBytes(value, 0, value.Length, result, sizeof(int));

            return result;
        }

        /// <summary>Inverse of <see cref="SerializeToPeer"/> — what a peer parses off the wire.</summary>
        public static TrickledICE DeserializeToPeer(ArraySegment<byte> data)
        {
            if (data.Count < sizeof(int))
                throw new ArgumentException("Trickle payload is too short to hold a connection id.", nameof(data));

            int connectionId = BitConverter.ToInt32(data.Array, data.Offset);
            string candidate = Encoding.UTF8.GetString(data.Array, data.Offset + sizeof(int), data.Count - sizeof(int));

            return ToPeer(connectionId, candidate);
        }
    }

    public struct OfferAnswer
    {
        public string sdp;
        public string[] candidates;

        public bool error;
        public string errorMessage;

        public OfferAnswer(string sdp, string[] candidates, bool error = false, string errorMessage = null)
        {
            this.sdp = sdp;
            this.candidates = candidates;
            this.error = error;
            this.errorMessage = error ? errorMessage : null;
        }

        public byte[] Serialize()
        {
            int sdpLength = Encoding.UTF8.GetByteCount(sdp);
            int totalSize = sizeof(int) + sdpLength + sizeof(int) + sizeof(bool); 

            foreach (string candidate in candidates)
            {
                totalSize += sizeof(int) + Encoding.UTF8.GetByteCount(candidate);
            }

            if (error && errorMessage != null)
            {
                totalSize += sizeof(int) + Encoding.UTF8.GetByteCount(errorMessage); 
            }

            byte[] result = CustomByteArrayPool.Retrieve(totalSize);
            int offset = 0;

            BitConverter.GetBytes(sdpLength).CopyTo(result, offset);
            offset += sizeof(int);
            Encoding.UTF8.GetBytes(sdp, 0, sdp.Length, result, offset);
            offset += sdpLength;

            BitConverter.GetBytes(candidates.Length).CopyTo(result, offset);
            offset += sizeof(int);

            foreach (string candidate in candidates)
            {
                int candidateLength = Encoding.UTF8.GetByteCount(candidate);
                BitConverter.GetBytes(candidateLength).CopyTo(result, offset);
                offset += sizeof(int);
                Encoding.UTF8.GetBytes(candidate, 0, candidate.Length, result, offset);
                offset += candidateLength;
            }

            BitConverter.GetBytes(error).CopyTo(result, offset);
            offset += sizeof(bool);

            if (error && errorMessage != null)
            {
                int errorMessageLength = Encoding.UTF8.GetByteCount(errorMessage);
                BitConverter.GetBytes(errorMessageLength).CopyTo(result, offset);
                offset += sizeof(int);
                Encoding.UTF8.GetBytes(errorMessage, 0, errorMessage.Length, result, offset);
                offset += errorMessageLength;
            }

            return result;
        }

        public static OfferAnswer Deserialize(ArraySegment<byte> data)
        {
            int offset = 0;

            int sdpLength = BitConverter.ToInt32(data.Array, data.Offset + offset);
            offset += sizeof(int);
            string sdp = Encoding.UTF8.GetString(data.Array, data.Offset + offset, sdpLength);
            offset += sdpLength;

            int candidateCount = BitConverter.ToInt32(data.Array, data.Offset + offset);
            offset += sizeof(int);
            string[] candidates = new string[candidateCount];

            for (int i = 0; i < candidateCount; i++)
            {
                int candidateLength = BitConverter.ToInt32(data.Array, data.Offset + offset);
                offset += sizeof(int);
                candidates[i] = Encoding.UTF8.GetString(data.Array, data.Offset + offset, candidateLength);
                offset += candidateLength;
            }

            bool error = BitConverter.ToBoolean(data.Array, data.Offset + offset);
            offset += sizeof(bool);

            string errorMessage = null;
            if (error)
            {
                int errorMessageLength = BitConverter.ToInt32(data.Array, data.Offset + offset);
                offset += sizeof(int);
                errorMessage = Encoding.UTF8.GetString(data.Array, data.Offset + offset, errorMessageLength);
                offset += errorMessageLength;
            }

            return new OfferAnswer(sdp, candidates, error, errorMessage);
        }
    }


    public struct Packet
    {
        public readonly int ConnectionID;
        public readonly byte[] Data;
        public readonly byte Channel;

        public Packet(int connectionID, byte[] segment, byte channel)
        {
            ConnectionID = connectionID;
            Data = segment;
            Channel = channel;
        }

        public Packet(int connectionID, ArraySegment<byte> segment, byte channel, int mtu)
        {

            int arraySize = Math.Max(segment.Count + sizeof(ushort), mtu);
            Data = CustomByteArrayPool.Retrieve(arraySize);

            ushort originalDataLength = (ushort)segment.Count;
            Data[0] = (byte)(originalDataLength & 0xFF);     
            Data[1] = (byte)((originalDataLength >> 8) & 0xFF); 



            Buffer.BlockCopy(segment.Array, segment.Offset, Data, sizeof(ushort), segment.Count);
            ConnectionID = connectionID;
            Channel = channel;
        }


        public ArraySegment<byte> GetArraySegment()
        {
            ushort originalDataLength = BitConverter.ToUInt16(Data, 0);

            return new ArraySegment<byte>(Data, sizeof(ushort), originalDataLength);
        }


        public void Dispose()
        {
            CustomByteArrayPool.Store(Data);
        }

    }


    public struct RemoteConnectionEvent
    {
        public readonly bool Connected;
        public readonly int ConnectionId;
        public RemoteConnectionEvent(bool connected, int connectionId)
        {
            Connected = connected;
            ConnectionId = connectionId;
        }
    }

}