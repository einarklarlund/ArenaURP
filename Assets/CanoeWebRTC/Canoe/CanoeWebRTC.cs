using FishNet.Managing;
using FishNet.Transporting.CanoeWebRTC.Client;
using FishNet.Transporting.CanoeWebRTC.Server;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if !UNITY_WEBGL || UNITY_EDITOR
using Unity.WebRTC;
#endif
using UnityEngine;



namespace FishNet.Transporting.CanoeWebRTC
{

    [DisallowMultipleComponent]
    [AddComponentMenu("FishNet/Transport/CanoeWebRTC")]
    public class CanoeWebRTC : Transport
    {

#if !UNITY_WEBGL || UNITY_EDITOR
        public static ClientSocket _client = new ClientSocket();
        public static ServerSocket _server = new ServerSocket();

#else
    public static WebGLClientSocket _client = new WebGLClientSocket();
    public static WebGLServerSocket _server = new WebGLServerSocket();
#endif





        public override string GetConnectionAddress(int connectionId)
        {
            InstanceFinder.NetworkManager.LogWarning("Connection Address is not implemented.");
            return string.Empty;
        }



        public override int GetMTU(byte channel)
        {
            _MTU = MTU;
            return _MTU;
        }



        public int MTU = 1023;
        public static int _MTU;


        public bool filterLocalConnections = false;
        public static bool _filterLocalConnections;

        public bool onlyAllowRelay = false;
        public static bool _onlyAllowRelay;

        public List<ICEServer> iceServers = new List<ICEServer>();
        public static List<ICEServer> _iceServers;



        public override bool StartConnection(bool server)
        {
            _iceServers = iceServers;
            _filterLocalConnections = filterLocalConnections;
            _onlyAllowRelay = onlyAllowRelay;

            _operationTimeoutDurationMilliseconds = operationTimeoutDurationMilliseconds;
            _candidateCollectDurationMilliseconds = candidateCollectDurationMilliseconds;
            _signalTimeoutDurationMilliseconds = signalTimeoutDurationMilliseconds;

            if (server)
            {
                InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Starting");
                return _server.StartServer(this, GetMTU(0));
            }
            else
            {
                InstanceFinder.NetworkManager.Log($"<color=#77DD77>[Client]</color> Starting");
                return _client.StartClient(this, GetMTU(0));
            }
        }


        public override bool StopConnection(bool server)
        {

            if (server)
            {
                InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Stopping");
                return _server.StopServer();
            }
            else
            {
                InstanceFinder.NetworkManager.Log($"<color=#77DD77>[Client]</color> Stopping");
                return _client.StopClient();
            }
        }

        public override bool StopConnection(int connectionId, bool immediately)
        {
            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Stopping client <b><i><color=#DDA0DD>{connectionId}</color></i></b>");
            return _server.CloseConnection(connectionId);




        }


        public override LocalConnectionState GetConnectionState(bool server)
        {
            if (server)
            {
                return _server.GetLocalConnectionState();
            }
            else
            {
                return _client.GetLocalConnectionState();
            }
        }

        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return _server.GetConnectionState(connectionId);
        }


        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {

            _client.Send(0, channelId, segment);
        }


        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;
        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            _server.Send(connectionId, channelId, segment);
        }




        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            if (connectionStateArgs.ConnectionState == LocalConnectionState.Started)
            {
                StopSignalTimeout(-1);
            }

            OnClientConnectionState?.Invoke(connectionStateArgs);
        }

        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);

        }

        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {
            //The signal timeout is only cancelled once the remote connection actually *starts*.
            //Cancelling it earlier (on answer receipt) left a connection that negotiated but never
            //established alive forever - wrapper and RTCPeerConnection both (ADR 0012).
            if (connectionStateArgs.ConnectionState == RemoteConnectionState.Started)
            {
                StopSignalTimeout(connectionStateArgs.ConnectionId);
            }

            OnRemoteConnectionState?.Invoke(connectionStateArgs);

        }


        public override void IterateIncoming(bool server)
        {
            if (server)
            {
                _server.IterateIncoming();
            }
            else
            {
                _client.IterateIncoming();
            }
        }

        public override void IterateOutgoing(bool server)
        {

            if (server)
            {
                _server.IterateOutgoing();
            }
            else
            {
                _client.IterateOutgoing();
            }
        }


        public override void Shutdown()
        {
            InstanceFinder.NetworkManager.Log("Shutting Down");
            if (_server.GetLocalConnectionState() != LocalConnectionState.Stopped)
                StopConnection(true);
            if (_client.GetLocalConnectionState() != LocalConnectionState.Stopped)
                StopConnection(false);
        }


        public static int CreateNewRemoteConnection()
        {
            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Creating new remote connection");
            return _server.CreateConnection();
        }


        //Trickled ICE candidates
        //
        //The transport gathers and applies; the signalling layer addresses and relays. These three
        //members are the whole seam between them, and it only runs one way at compile time: the
        //transport raises an event it does not know who listens to, and exposes two entry points
        //the signal manager calls. SignalManager lives in Assembly-CSharp, which references this
        //assembly and cannot be referenced back.

        /// <summary>
        /// Raised as each local ICE candidate is gathered, and once more with
        /// <see cref="TrickledICE.EndOfCandidates"/> when gathering completes.
        /// <c>connectionId</c> is <c>-1</c> for the local client's connection to its host, and the
        /// server-side connection id otherwise — which is also what selects the trickle direction.
        /// </summary>
        public static Action<int, string> OnLocalIceCandidate;

        internal static void RaiseLocalIceCandidate(int connectionId, string candidate)
        {
            OnLocalIceCandidate?.Invoke(connectionId, candidate);
        }

        /// <summary>
        /// A candidate the host trickled to us. We are the client, so there is exactly one peer
        /// connection it can belong to and the connection id the server stamped is redundant.
        /// </summary>
        public static void AddRemoteCandidateToClient(string candidate)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            Connection connection = _client.connectionWithServer;

            if (connection == null)
            {
                InstanceFinder.NetworkManager.LogWarning($"<color=#77DD77>[Client]</color> Dropped a trickled ICE candidate: no connection to the host.");
                return;
            }

            connection.ReceiveRemoteCandidate(candidate);
#else
            //The WebGL transport still negotiates with candidates bundled into the SDP and has no
            //inbound queue to route this into; ticket 04 gives it one.
            InstanceFinder.NetworkManager.LogWarning($"<color=#77DD77>[Client]</color> Dropped a trickled ICE candidate: the WebGL transport does not handle them yet.");
#endif
        }

        /// <summary>
        /// A candidate a client trickled to us. We are the host, so the connection id the server
        /// stamped picks which of our peer connections it belongs to.
        /// </summary>
        public static void AddRemoteCandidateToServer(int connectionId, string candidate)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (!_server.connections.TryGetValue(connectionId, out Connection connection))
            {
                //Routine, not alarming: a candidate can still be in flight when the attempt it
                //belongs to is torn down.
                InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Dropped a trickled ICE candidate for unknown connection <b><i><color=#DDA0DD>{connectionId}</color></i></b>.");
                return;
            }

            connection.ReceiveRemoteCandidate(candidate);
#else
            InstanceFinder.NetworkManager.LogWarning($"<color=#FFA500>[Server]</color> Dropped a trickled ICE candidate: the WebGL transport does not handle them yet.");
#endif
        }



        //Handle Signalling

        public static void SignalShutdown()
        {
            if (_server.GetLocalConnectionState() != LocalConnectionState.Stopped)
                _server.StopServer();
            if (_client.GetLocalConnectionState() != LocalConnectionState.Stopped)
                _client.StopClient();
        }


        public int operationTimeoutDurationMilliseconds = 2500; //this is the per operation timeout duration
        private static int _operationTimeoutDurationMilliseconds; //this is the per operation timeout duration

        //How long a peer stalls negotiation to collect candidates before bundling them into the
        //SDP. The standalone transport no longer does this at all - it trickles - so this is read
        //only by the WebGL sockets, until ticket 04 moves them across and deletes it outright.
        public int candidateCollectDurationMilliseconds = 2000; //this is the candidate collection duration
        internal static int _candidateCollectDurationMilliseconds; //this is the candidate collection duration

        //Handling timeout for signalling
        //ADR 0012: 39.5s matches RFC 8863's recommended PAC timer, so the app never declares a join
        //dead while the ICE agent is still legitimately allowed to be working. Keep the Boot-scene
        //serialized value in step with this default.
        public int signalTimeoutDurationMilliseconds = 39500; //this is the overall process timeout duration
        private static int _signalTimeoutDurationMilliseconds; 

        public static CancellationTokenSource clientCancellationTokenSource;
        public static Dictionary<int, CancellationTokenSource> remoteCancellationTokenSources = new Dictionary<int, CancellationTokenSource>();

        private static CancellationToken StartSignalTimeout(int connectionID)
        {
            InstanceFinder.NetworkManager.Log("Starting signal timeout");
            CancellationToken token;

            if (connectionID != -1)
            {
                CancellationTokenSource remoteClientCancellationTokenSources = new CancellationTokenSource();
                remoteCancellationTokenSources.Add(connectionID, remoteClientCancellationTokenSources);
                token = remoteClientCancellationTokenSources.Token;

                _ = SignalTimeoutAsync(connectionID, token);
            }
            else
            {
                clientCancellationTokenSource = new CancellationTokenSource();
                token = clientCancellationTokenSource.Token;
                _ = SignalTimeoutAsync(connectionID, token);

            }

            return token;
        }

        //this occurs once the connection is actually established -- the client cancels when its local
        //connection starts, the host when the remote connection starts. Idempotent: the timer may
        //already have been stopped (or never started) by the time the connection reports Started.
        private static void StopSignalTimeout(int connectionID)
        {
            if (connectionID != -1)
            {
                if (!remoteCancellationTokenSources.TryGetValue(connectionID, out CancellationTokenSource remoteSource))
                    return;

                remoteSource.Cancel();
                remoteSource.Dispose();
                remoteCancellationTokenSources.Remove(connectionID);
            }
            else
            {
                if (clientCancellationTokenSource == null)
                    return;

                clientCancellationTokenSource.Cancel();
                clientCancellationTokenSource.Dispose();
                clientCancellationTokenSource = null;
            }
        }

        private static async Task SignalTimeoutAsync(int connectionID, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_signalTimeoutDurationMilliseconds, cancellationToken);
                InstanceFinder.NetworkManager.LogWarning("Signal timeout occured! - consider extending the timeout duration");

                if (connectionID != -1)
                {
                    remoteCancellationTokenSources[connectionID].Cancel();

                    await Task.Delay(_operationTimeoutDurationMilliseconds + 100); //allow the operation to have time or to timeout on its own

                    if (remoteCancellationTokenSources.ContainsKey(connectionID))
                    {
                        HandleSignalTimeout(connectionID);
                    }

                }
                else
                {
                    clientCancellationTokenSource.Cancel();

                    await Task.Delay(_operationTimeoutDurationMilliseconds + 100);

                    if (clientCancellationTokenSource != null)
                    {
                        HandleSignalTimeout(-1);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                //timeout cancelled -- connection successful
                InstanceFinder.NetworkManager.Log("Signal timeout was canceled since connection was successful!");
            }
        }

        //this occurs if it times out - needs to be handled by the server on offer creation up until handle recevied answer at which point we consider a success in the signalling (before the handling but after receiving)
        private static void HandleSignalTimeout(int connectionID)
        {
            if (connectionID != -1)
            {
                if (remoteCancellationTokenSources.TryGetValue(connectionID, out CancellationTokenSource remoteSource))
                {
                    remoteSource.Dispose();
                    remoteCancellationTokenSources.Remove(connectionID);
                }

                _server.CloseConnection(connectionID);
            }
            else
            {
                clientCancellationTokenSource?.Dispose();
                clientCancellationTokenSource = null;
                _client.StopClient();
            }
        }

        //Trickling peers put no candidates in the description they send. The field itself stays on
        //OfferAnswer only because the WebGL transport still bundles; ticket 04 removes both.
        private static readonly string[] NoBundledCandidates = new string[0];

        /// <summary>
        /// Make sure a description advertises that this peer will trickle (RFC 8839 §5.4), so the
        /// far side treats the candidate-less SDP as the start of gathering rather than the end of
        /// it. libwebrtc already emits the attribute on both offers and answers — this is a
        /// belt-and-braces pass that inserts it only if it is somehow absent, and never duplicates.
        /// </summary>
        private static string EnsureTrickleIceOption(string sdp)
        {
            const string attribute = "a=ice-options:trickle\r\n";

            if (string.IsNullOrEmpty(sdp) || sdp.Contains("ice-options:trickle"))
                return sdp;

            //Session-level attributes follow the timing line, so insert directly after the first
            //"t=". If the description is not shaped the way SDP requires, leave it well alone.
            int timing = sdp.IndexOf("\r\nt=", StringComparison.Ordinal);
            if (timing < 0)
                return sdp;

            int endOfTiming = sdp.IndexOf("\r\n", timing + 2, StringComparison.Ordinal);
            if (endOfTiming < 0)
                return sdp;

            return sdp.Insert(endOfTiming + 2, attribute);
        }

        //Server

        //Create Offer For a client from the server (us)
        public static async Task<OfferAnswer> CreateOfferForClient(int connectionID = -1)
        {

            if (_server.GetLocalConnectionState() != LocalConnectionState.Started)
            {
                return new OfferAnswer("", null, true, "Server not started");
            }



            if (connectionID == -1)
            {
                connectionID = CanoeWebRTC.CreateNewRemoteConnection();
            }

            CancellationToken token = StartSignalTimeout(connectionID);

            var timeoutTask = Task.Delay(_operationTimeoutDurationMilliseconds);

            Task<OfferAnswer> operationTask;

#if !UNITY_WEBGL || UNITY_EDITOR
            Connection connection = _server.connections[connectionID];
            operationTask = CreateOfferAsync(connection, token);
#else
    TaskCompletionSource<OfferAnswer> offerCompletionSource = new TaskCompletionSource<OfferAnswer>();
    WebGLServerSocket.CreateOffer(connectionID, offerCompletionSource);
    operationTask = offerCompletionSource.Task;
#endif

            var completedTask = await Task.WhenAny(operationTask, timeoutTask);


            if (completedTask == operationTask)
            {
                if (token.IsCancellationRequested)
                {
                    HandleSignalTimeout(connectionID);
                    return new OfferAnswer("", null, true, "Operation was cancelled before starting");

                }
                return await operationTask;
            }
            else
            {
                _server.CloseConnection(connectionID);
                InstanceFinder.NetworkManager.LogError("Timeout occurred while creating offer.");
                return new OfferAnswer("", null, true, "Timeout occurred while creating offer");
            }
        }


#if !UNITY_WEBGL || UNITY_EDITOR
        private static async Task<OfferAnswer> CreateOfferAsync(Connection clientConnection, CancellationToken token)
        {

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled before starting.");
                return new OfferAnswer("", null, true, "Operation was cancelled");
            }


            RTCSessionDescriptionAsyncOperation offerOp = clientConnection.localConnection.CreateOffer();

            await WaitUntilOperationIsDone(offerOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled after creating offer.");
                return new OfferAnswer("", null, true, "Operation was cancelled");
            }

            if (offerOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to create offer");
                return new OfferAnswer("", null, true, "Failed to create offer");

            }

            RTCSessionDescription desc = offerOp.Desc;


            var localDescOp = clientConnection.localConnection.SetLocalDescription(ref desc);

            await WaitUntilOperationIsDone(localDescOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled after setting local description.");
                return new OfferAnswer("", null, true, "Operation was cancelled");
            }

            if (localDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to set local description");
                return new OfferAnswer("", null, true, "Failed to set local description");

            }

            //The offer goes out the instant the local description is set. Gathering has only just
            //begun and every candidate it produces is trickled separately, so there is nothing left
            //to wait for here - this is where the join's first multi-second stall used to be.
            return new OfferAnswer(EnsureTrickleIceOption(offerOp.Desc.sdp), NoBundledCandidates);

        }

        private static async Task WaitUntilOperationIsDone(RTCSessionDescriptionAsyncOperation operation)
        {
            while (!operation.IsDone)
            {
                await Task.Yield();
            }
        }

        private static async Task WaitUntilOperationIsDone(RTCSetSessionDescriptionAsyncOperation operation)
        {
            while (!operation.IsDone)
            {
                await Task.Yield();
            }
        }
#endif

        //Handle Response - Server
        // this has 0 callbacks both on webgl and on standalones maybe add
        public static async Task HandleAnswerFromClient(int connectionID, OfferAnswer answer)
        {
            if (_server.GetLocalConnectionState() != LocalConnectionState.Started)
            {
                return;
            }

            if (remoteCancellationTokenSources.ContainsKey(connectionID))
            {
                if (remoteCancellationTokenSources[connectionID].IsCancellationRequested)
                {
                    Debug.Log($"<color=#FFA500>[Server]</color> Ignoring answer from client due to timeout.");
                    StopSignalTimeout(connectionID); //make sure it gets removed - maybe ignore and allow to go through even past timeout since this only gets called if its been completed already
                    return;
                }
            }

            //The signal timeout deliberately keeps running past the answer: an answer only means the
            //two peers negotiated, not that they connected. It is cancelled in
            //HandleRemoteConnectionState once the connection reports Started (ADR 0012).

            var timeoutTask = Task.Delay(_operationTimeoutDurationMilliseconds);

#if !UNITY_WEBGL || UNITY_EDITOR
            Connection connection = _server.connections[connectionID];
            Task operationTask = ServerReceiveAnswerAsync(connection, answer);

            var completedTask = await Task.WhenAny(operationTask, timeoutTask);
            if (completedTask == operationTask)
            {

                await operationTask;

            }
            else
            {
                _server.CloseConnection(connectionID);
                InstanceFinder.NetworkManager.Log("Timeout occurred while handling response from client.");

            }

#else

    WebGLServerSocket.HandleResponse(connectionID, answer);
    return; // No async operation to await in WebGL case

#endif

        }


#if !UNITY_WEBGL || UNITY_EDITOR
        private static async Task ServerReceiveAnswerAsync(Connection remoteConnection, OfferAnswer answer)
        {

            RTCSessionDescription desc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = answer.sdp
            };

            var remoteDescOp = remoteConnection.localConnection.SetRemoteDescription(ref desc);

            await WaitUntilOperationIsDone(remoteDescOp);

            if (remoteDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to set remote description");
                return;
            }

            //The client will already have trickled candidates at us - it starts gathering as soon
            //as it sets its own local description, which is before we ever see this answer. They
            //were buffered because there was nothing to attach them to; now there is.
            remoteConnection.RemoteDescriptionSet();

            //A peer that still bundles (WebGL, until ticket 04) sends its candidates in here.
            ApplyBundledCandidates(remoteConnection, answer.candidates);

        }

        /// <summary>
        /// Candidates carried inside an offer or answer by a peer that has not moved to trickle yet.
        /// A trickling peer sends none, so this is a no-op between two standalone peers.
        /// </summary>
        private static void ApplyBundledCandidates(Connection connection, string[] candidates)
        {
            if (candidates == null)
                return;

            foreach (string candidate in candidates)
                connection.ReceiveRemoteCandidate(candidate);
        }


#endif

        //CLIENT
        //Create answer for server / Recieve offer from server
        public static async Task<OfferAnswer> CreateAnswerForServer(OfferAnswer offer)
        {

            if (_client.GetLocalConnectionState() != LocalConnectionState.Starting)
            {
                return new OfferAnswer("", null, true, "Client not started");
            }

            CancellationToken token = StartSignalTimeout(-1);


            var timeoutTask = Task.Delay(_operationTimeoutDurationMilliseconds);

            Task<OfferAnswer> operationTask;

#if !UNITY_WEBGL || UNITY_EDITOR
            Connection connection = _client.connectionWithServer;
            operationTask = CreateResponseAsync(connection, offer, token);
#else
        //Debug.Log("Received Offer from server1111");
        TaskCompletionSource<OfferAnswer> offerCompletionSource = new TaskCompletionSource<OfferAnswer>();
        WebGLClientSocket.ReceivedOffer(offer, offerCompletionSource);
        operationTask = offerCompletionSource.Task;
#endif

            // Wait for either the operation task or the timeout task to complete
            var completedTask = await Task.WhenAny(operationTask, timeoutTask);



            if (completedTask == operationTask)
            {
                if (token.IsCancellationRequested)
                {
                    HandleSignalTimeout(-1);
                    return new OfferAnswer("", null, true, "Signal timeout error");

                }

                // The main task completed within the timeout
                return await operationTask;
            }
            else
            {
                // Timeout occurred
                _client.StopClient();
                InstanceFinder.NetworkManager.LogError("Timeout occurred while creating answer for server.");
                return new OfferAnswer("", null, true, "Timeout occurred while creating offer");

            }
        }


#if !UNITY_WEBGL || UNITY_EDITOR
        private static async Task<OfferAnswer> CreateResponseAsync(Connection connection, OfferAnswer offer, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.LogWarning("Operation was cancelled before starting.");
                return new OfferAnswer("", null, true, "Operation was cancelled before starting");
            }

            // Debug.Log("Received an Offer");

            RTCSessionDescription descOffer = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = offer.sdp
            };

            var remoteDescOp = connection.localConnection.SetRemoteDescription(ref descOffer);

            await WaitUntilOperationIsDone(remoteDescOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.LogWarning("Operation was cancelled after setting remote description.");
                return new OfferAnswer("", null, true, "Operation was cancelled after setting remote description");
            }

            if (remoteDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogWarning("Failed to set remote description");
                return new OfferAnswer("", null, true, "Failed to set remote description");
            }

            //The host began gathering the moment it set its own local description, so its first
            //candidates can beat its offer's arrival here. Release them now that there is a remote
            //description to attach them to; from here the queue passes everything straight through.
            connection.RemoteDescriptionSet();

            //A peer that still bundles (WebGL, until ticket 04) sends its candidates in here.
            ApplyBundledCandidates(connection, offer.candidates);

            RTCSessionDescriptionAsyncOperation answerOp = connection.localConnection.CreateAnswer();

            await WaitUntilOperationIsDone(answerOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.LogWarning("Operation was cancelled after creating answer.");
                return new OfferAnswer("", null, true, "Operation was cancelled after creating answer");
            }

            if (answerOp.IsError)
            {
                InstanceFinder.NetworkManager.LogWarning("Failed to create answer");
                return new OfferAnswer("", null, true, "Failed to create answer");
            }

            RTCSessionDescription descAnswer = answerOp.Desc;

            var localDescOp = connection.localConnection.SetLocalDescription(ref descAnswer);

            await WaitUntilOperationIsDone(localDescOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled after setting local description.");
                return new OfferAnswer("", null, true, "Operation was cancelled after setting local description");
            }

            if (localDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to set local description");
                return new OfferAnswer("", null, true, "Failed to set local description");
            }

            //The answer goes out the instant the local description is set - the second of the two
            //multi-second stalls the join used to sit through. Our own candidates follow as they
            //are gathered.
            return new OfferAnswer(EnsureTrickleIceOption(answerOp.Desc.sdp), NoBundledCandidates);
        }

#endif



        public void OnApplicationQuit()
        {
            Shutdown();
        }

    }
}