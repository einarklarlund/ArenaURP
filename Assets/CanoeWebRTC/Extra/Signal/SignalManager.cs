using System.Text;
using System;
using System.Collections.Generic;
using FishNet;
using JamesFrowen.SimpleWeb;
using System.Linq;
using FishNet.Transporting.CanoeWebRTC;
using UnityEngine;

public class SignalManager : MonoBehaviour
{
    public static SignalManager Instance { get; private set; }

    [SerializeField] private string SignalAddress;
    [SerializeField] private bool copyCreatedRoomCodeToClipboard = false; //only works in standalone / editor
    [SerializeField] private string roomCode = "";

    public static Action<string> OnRoomCreated;
    public static Action<bool> OnRoomJoined;
    public static Action<ClientState> OnClientStateChanged;
    public ClientState ClientState => client.ConnectionState;

    private const byte createRoom = 0x01; //responded to directly with the room code
    private const byte attemptToJoinRoom = 0x02; //responded to directly if join code is valid and if host has been notified
    private const byte joinRoomCallback = 0x03; //this is the callback for the client who initiated the attempt
    private const byte receivedOfferFromHost = 0x04; //may contain error details if not allowed!
    private const byte receivedAnswerFromClient = 0x05; //client has received the offer, has started to join, and is sending answer
    //0x06 was `trickleICE`, a scaffold that was never implemented. It is retired, and the byte is
    //deliberately NOT reused, so a stale peer can never half-parse a new message.
    private const byte ping = 0x07;
    private const byte trickleToClient = 0x08; //host->client candidate; [tok] outbound, int32 connectionId inbound
    private const byte trickleToHost = 0x09;   //client->host candidate; [tok] outbound, int32 connectionId inbound

    private static SimpleWebClient client;


    //Trickle routing
    //
    //A trickled candidate carries no target player id - the session token is the whole route - so
    //this is the bookkeeping that lets each side address one. Nothing here inspects negotiation
    //state; deciding whether a candidate can be applied yet belongs to the peer-connection wrapper
    //that owns it (ADR 0011). This is a pipe.

    /// <summary>The route key for this peer's own connection to its host, matching <c>Connection.ConnectionID</c>.</summary>
    private const int clientRouteID = -1;

    /// <summary>
    /// One live trickle route: the session token addressing a peer-connection attempt on the signal
    /// server, and whether the server can resolve it yet. The server binds the session's connection
    /// id when it relays the host's offer and <b>silently drops</b> any trickle that arrives before
    /// that, so candidates gathered ahead of the offer wait in <see cref="pending"/>.
    /// </summary>
    private class TrickleRoute
    {
        public string sessionToken;
        public bool open;
        public List<string> pending = new List<string>();
    }

    /// <summary>Live routes by connection id; <see cref="clientRouteID"/> is our own client connection.</summary>
    private readonly Dictionary<int, TrickleRoute> trickleRoutes = new Dictionary<int, TrickleRoute>();

    /// <summary>
    /// Host side: the token from a join notify, held under the joining client's signal id until
    /// <see cref="CreateAndSendOffer"/> mints a connection id to file it under instead.
    /// </summary>
    private readonly Dictionary<int, string> pendingSessionTokens = new Dictionary<int, string>();


    /// <summary>
    /// Points the editor at a signal server other than the default one. Set it and a harness can
    /// run against an isolated instance instead of whatever is already holding the default port —
    /// the matching half of ArenaInfra's <c>SIGNAL_PORT</c>.
    /// </summary>
    private const string EditorSignalAddressVariable = "ARENA_SIGNAL_ADDRESS";

    private const string DefaultEditorSignalAddress = "ws://localhost:9001/Signal";

    private void Start()
    {
        if (Debug.isDebugBuild || Application.isEditor)
        {
            string overridden = Environment.GetEnvironmentVariable(EditorSignalAddressVariable);
            SignalAddress = string.IsNullOrEmpty(overridden) ? DefaultEditorSignalAddress : overridden;
        }
        if (Instance != null)
        {
            Debug.LogError("Tried to set singleton instance of SignalManager, but it was already set");
            return;
        }
        Instance = this;

        //The transport gathers candidates; we address and relay them. It raises this without
        //knowing anything about sessions or opcodes.
        CanoeWebRTC.OnLocalIceCandidate += HandleLocalIceCandidate;

        StartSignalClient();
    }

    public void StartSignalClient()
    {
        OnClientStateChanged?.Invoke(ClientState.Connected);
        var tcpConfig = new TcpConfig(noDelay: false, sendTimeout: 120000, receiveTimeout: 120000);
        client = SimpleWebClient.Create(ushort.MaxValue, 5000, tcpConfig);

        client.onConnect += () =>
        {
            Debug.Log($"<color=cyan>[Signal]</color> Connected to signaling server.");
            OnClientStateChanged?.Invoke(ClientState.Connected);
        };
        client.onDisconnect += () =>
        {
            Debug.Log($"<color=cyan>[Signal]</color> Disconnected.");
            OnClientStateChanged?.Invoke(ClientState.NotConnected);
        };
        client.onData += HandleReceivedData;
        client.onError += (exception) =>
        {
            Debug.Log($"<color=cyan>[Signal]</color> Error:{exception}");
            OnClientStateChanged?.Invoke(ClientState.NotConnected);
        };

        client.Connect(new Uri(SignalAddress));
    }

    private void OnDestroy()
    {
        CanoeWebRTC.OnLocalIceCandidate -= HandleLocalIceCandidate;

        client.Disconnect();
        client = null;
    }

    public void CreateRoom()
    {
        if (InstanceFinder.IsServerStarted)
        {
            ArraySegment<byte> createRoomSegment = new (new byte[] { createRoom });
            client.Send(createRoomSegment);
        }
        else
        {
            Debug.Log($"<color=cyan>[Signal]</color> Cannot create a room unless server is started.");
        }
    }

    public void JoinRoom(string roomID)
    {
        Debug.Log($"<color=cyan>[Signal]</color> Attempting to join room <b><i><color=#DDA0DD>{roomID}</color></i></b>.");
        LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.ContactingSignal);

        byte[] joinRoomBuffer = new byte[10];
        byte roomIDLength = (byte)roomID.Length;

        joinRoomBuffer[0] = attemptToJoinRoom;
        joinRoomBuffer[1] = roomIDLength;
        Encoding.UTF8.GetBytes(roomID, 0, roomID.Length, joinRoomBuffer, 2);

        client.Send(joinRoomBuffer);
    }


    //SERVER

    //Client has joined the room validly, lets create an offer and then send it
    public async void CreateAndSendOffer(int SignalID)
    {
        if (!pendingSessionTokens.TryGetValue(SignalID, out string sessionToken))
        {
            //Without the token there is nothing to echo on the offer, so the server would drop it
            //and the join would hang in silence. Fail loudly here instead.
            Debug.LogError($"<color=cyan>[Signal]</color> No signaling session for joining client {SignalID}; not sending an offer.");
            return;
        }

        pendingSessionTokens.Remove(SignalID);

        int connectionID = CanoeWebRTC.CreateNewRemoteConnection();

        //Filed before the offer is created, not after: creating it sets the local description,
        //which starts gathering, so candidates can appear while we are still awaiting it.
        trickleRoutes[connectionID] = new TrickleRoute { sessionToken = sessionToken };

        OfferAnswer offerResult = await CanoeWebRTC.CreateOfferForClient(connectionID);

        if (offerResult.error)
        {
            Debug.Log($"<color=cyan>[Signal]</color> Error when creating offer: {offerResult.errorMessage}");
            trickleRoutes.Remove(connectionID);
            return;
        }

        //Order matters and is not negotiable: the server binds this connection id onto the session
        //as it relays the offer, and drops any trickle for a session that has none yet. So the
        //offer goes first, and only then does the route open and release what it was holding.
        client.Send(sendOfferSerializer(offerResult, SignalID, connectionID, sessionToken));

        OpenTrickleRoute(connectionID);
    }

    //`0x04 int32 targetClientSignalId int32 connectionId [tok] sdp...` - the host echoes the token
    //it was handed in the join notify, and that echo is what binds the connection id to the session.
    private static ArraySegment<byte> sendOfferSerializer(OfferAnswer offerAnswer, int SignalID, int connectionID, string sessionToken)
    {
        byte[] requestObjectBytes = offerAnswer.Serialize();

        int totalLength = sizeof(byte) + sizeof(int) + sizeof(int) + SessionToken.ByteLength(sessionToken) + requestObjectBytes.Length;

        byte[] result = CustomByteArrayPool.Retrieve(totalLength);

        int offset = 0;
        result[offset] = receivedOfferFromHost;
        offset += sizeof(byte);

        BitConverter.GetBytes(SignalID).CopyTo(result, offset);
        offset += sizeof(int);

        BitConverter.GetBytes(connectionID).CopyTo(result, offset);
        offset += sizeof(int);

        offset += SessionToken.Write(sessionToken, result, offset);

        Array.Copy(requestObjectBytes, 0, result, offset, requestObjectBytes.Length);

        //A pooled buffer can be longer than asked for; send exactly the message.
        return new ArraySegment<byte>(result, 0, totalLength);
    }


    //client has sent us their response! (answer to our offer)
    public async void HandleResponse(int connectionID, OfferAnswer offerAnswer)
    {
        await CanoeWebRTC.HandleAnswerFromClient(connectionID, offerAnswer);
    }

    //Client

    //handle offer, generate reponse, send response
    //Only the client half of the flow publishes JoinPhase: these points describe *this* peer's own
    //join attempt. The host halves (CreateAndSendOffer / HandleResponse) run once per joining peer
    //and would otherwise rewrite the local player's phase for somebody else's join.
    public async void ReceivedOfferFromServer(int signalID, OfferAnswer offerAnswer)
    {
        if (!trickleRoutes.TryGetValue(clientRouteID, out TrickleRoute route))
        {
            //Without the token the answer names no session, so the server would drop it and the
            //join would hang in silence. Fail loudly here instead.
            Debug.LogError($"<color=cyan>[Signal]</color> No signaling session for our own join attempt; not sending an answer.");
            LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.Failed);
            return;
        }

        string sessionToken = route.sessionToken;

        OfferAnswer answerResult = await CanoeWebRTC.CreateAnswerForServer(offerAnswer);

        if (answerResult.error)
        {
            Debug.Log($"<color=cyan>[Signal]</color> Error when handling offer from server: {answerResult.errorMessage}");
            LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.Failed);
        }
        else
        {
            client.Send(sendAnswerSerializer(answerResult, signalID, sessionToken));
            //Offer in, answer out: negotiation is done and ICE now has to establish the connection.
            LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.EstablishingConnection);
        }

    }

    //`0x05 int32 targetPlayerId [tok] sdp...` - the client names the signaling session it was
    //handed at join-attempt, so the server resolves it directly instead of guessing from the pair.
    private static ArraySegment<byte> sendAnswerSerializer(OfferAnswer offerAnswer, int targetPlayerID, string sessionToken)
    {
        byte[] respondObjectBytes = offerAnswer.Serialize();

        int totalLength = sizeof(byte) + sizeof(int) + SessionToken.ByteLength(sessionToken) + respondObjectBytes.Length;
        byte[] result = CustomByteArrayPool.Retrieve(totalLength);

        int offset = 0;
        result[offset] = receivedAnswerFromClient;
        offset += sizeof(byte);

        BitConverter.GetBytes(targetPlayerID).CopyTo(result, offset);
        offset += sizeof(int);

        offset += SessionToken.Write(sessionToken, result, offset);

        Array.Copy(respondObjectBytes, 0, result, offset, respondObjectBytes.Length);

        //A pooled buffer can be longer than asked for; send exactly the message.
        return new ArraySegment<byte>(result, 0, totalLength);
    }


    //Trickled candidates - outbound

    /// <summary>
    /// A candidate the local ICE agent just gathered, or the empty end-of-candidates marker. The
    /// connection it came from selects both the route and the direction: our own client connection
    /// trickles to the host, any other is one of our clients and trickles to it.
    /// </summary>
    private void HandleLocalIceCandidate(int connectionID, string candidate)
    {
        if (!trickleRoutes.TryGetValue(connectionID, out TrickleRoute route))
        {
            //Either the attempt is already over or it never had a session. Nothing to address the
            //candidate with, and a trickle carries no other routing information.
            Debug.Log($"<color=cyan>[Signal]</color> No signaling session for connection {connectionID}; dropping a local ICE candidate.");
            return;
        }

        if (!route.open)
        {
            route.pending.Add(candidate);
            return;
        }

        SendTrickle(connectionID, route, candidate);
    }

    /// <summary>
    /// The server can now resolve this route: send everything gathered while it could not, in the
    /// order it was gathered, and let the rest through as they come.
    /// </summary>
    private void OpenTrickleRoute(int connectionID)
    {
        if (!trickleRoutes.TryGetValue(connectionID, out TrickleRoute route))
            return;

        route.open = true;

        List<string> buffered = route.pending;
        route.pending = new List<string>();

        foreach (string candidate in buffered)
            SendTrickle(connectionID, route, candidate);
    }

    private void SendTrickle(int connectionID, TrickleRoute route, string candidate)
    {
        client.Send(trickleSerializer(connectionID == clientRouteID ? trickleToHost : trickleToClient, route.sessionToken, candidate));

        //The marker means this agent has gathered its last candidate, so the route has no further
        //use. Retiring it here also keeps a host's table from growing one entry per join.
        if (string.IsNullOrEmpty(candidate))
            trickleRoutes.Remove(connectionID);
    }

    //`0x08|0x09 [tok] candidate...` - no target player id, the token is the whole route. The
    //candidate is the remainder of the buffer, so the segment must be exactly the message: a
    //pooled buffer's slack would be read as trailing garbage on the candidate.
    private static ArraySegment<byte> trickleSerializer(byte opcode, string sessionToken, string candidate)
    {
        byte[] payload = TrickledICE.ToServer(sessionToken, candidate).SerializeToServer();

        byte[] result = new byte[sizeof(byte) + payload.Length];
        result[0] = opcode;
        Array.Copy(payload, 0, result, sizeof(byte), payload.Length);

        return new ArraySegment<byte>(result);
    }

    /// <summary>Everything after the opcode byte — the trickle payload proper.</summary>
    private static ArraySegment<byte> trickleBody(ArraySegment<byte> data)
    {
        return new ArraySegment<byte>(data.Array, data.Offset + sizeof(byte), data.Count - sizeof(byte));
    }


    public void HandleReceivedData(ArraySegment<byte> data)
    {
        byte command = data.Array[data.Offset];

        switch (command)
        {
            case createRoom:

                int roomIDLength = data.Array[data.Offset + 1];

                string roomID = Encoding.UTF8.GetString(data.Array, data.Offset + 2, roomIDLength);

                roomCode = roomID;

                OnRoomCreated?.Invoke(roomID);

                Debug.Log($"<color=cyan>[Signal]</color> Room <b><i><color=#DDA0DD>{roomID}</color></i></b> created.");
                if (copyCreatedRoomCodeToClipboard)
                {
                    GUIUtility.systemCopyBuffer = roomCode;
                }

                break;

            case attemptToJoinRoom:
                // this is the host
                // a client is trying to join our game!
                // this is where we would initiate the create offer and then send

                Debug.Log("<color=cyan>[Signal]</color> Received request to join room, creating an offer.");

                //`0x02 int32 clientSignalId [tok]`
                int clientAttemptingToJoin_SignalID = BitConverter.ToInt32(data.Array, data.Offset + 1);

                //The token names the signaling session minted for this attempt. Held under the
                //joining client's signal id only until the offer files it under a connection id.
                pendingSessionTokens[clientAttemptingToJoin_SignalID] =
                    SessionToken.Read(data.Array, data.Offset + 1 + sizeof(int), out _);

                CreateAndSendOffer(clientAttemptingToJoin_SignalID);

                break;

            case joinRoomCallback:

                byte confirmation = data.Array[data.Offset + 1];

                if (confirmation == 0x00)
                {
                    //we did not join
                    Debug.Log("<color=cyan>[Signal]</color> Join Code Invalid.");
                    OnRoomJoined?.Invoke(false);
                    LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.Failed);
                }
                else if (confirmation == 0x01)
                {
                    //we did join -- `0x03 0x01 [tok]`
                    //
                    //The token names the signaling session this attempt owns, and is the only thing
                    //that addresses our candidates. The route opens immediately: the server binds
                    //the session's connection id when it relays the host's offer, which is strictly
                    //before that offer can reach us, and we gather nothing until we answer it.
                    trickleRoutes[clientRouteID] = new TrickleRoute
                    {
                        sessionToken = SessionToken.Read(data.Array, data.Offset + 2, out _),
                        open = true,
                    };

                    OnRoomJoined?.Invoke(true);
                    Debug.Log("<color=cyan>[Signal]</color> Join Code Valid, waiting for offer from host.");
                    LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.Negotiating);
                }

                break;

            case receivedOfferFromHost:
                // We should make an answer and respond
                Debug.Log("<color=cyan>[Signal]</color> Received an offer from a Host, creating an answer.");

                int receivedOffer_HostSignalID = BitConverter.ToInt32(data.Array, data.Offset + 1);

                byte[] sendOffer_remainingData = data.Array.Skip(data.Offset + 5).Take(data.Count - 5).ToArray();

                //create an answer and send it back to them
                OfferAnswer offer = OfferAnswer.Deserialize(sendOffer_remainingData);

                ReceivedOfferFromServer(receivedOffer_HostSignalID, offer);

                break;

            case receivedAnswerFromClient:
                // server received an answer from the client
                // need to handle their answer

                Debug.Log("<color=cyan>[Signal]</color> Client sent us their answer.");

                int connID = BitConverter.ToInt32(data.Array, data.Offset + 1);

                byte[] sendAnswer_remainingData = data.Array
                    .Skip(data.Offset + 5)
                    .Take(data.Count - 5)
                    .ToArray();

                HandleResponse(connID, OfferAnswer.Deserialize(sendAnswer_remainingData));

                break;

            case trickleToClient:
                //`0x08 int32 connectionId candidate...` - we are the client. The server stamps the
                //connection id so one parser serves both directions; we have exactly one connection
                //(to the host), so ours is redundant and ignored.
                CanoeWebRTC.AddRemoteCandidateToClient(TrickledICE.DeserializeToPeer(trickleBody(data)).candidate);

                break;

            case trickleToHost:
                //`0x09 int32 connectionId candidate...` - we are the host, so the stamped connection
                //id is what says which of our peer connections this candidate belongs to.
                TrickledICE inboundForHost = TrickledICE.DeserializeToPeer(trickleBody(data));
                CanoeWebRTC.AddRemoteCandidateToServer(inboundForHost.connectionId, inboundForHost.candidate);

                break;

            case ping:
                //simple ping
                client.Send(data);
                break;

            default:
                throw new InvalidOperationException("Unknown command received");
        }
    }

    public void Update()
    {
        client?.ProcessMessageQueue();
    }
}



