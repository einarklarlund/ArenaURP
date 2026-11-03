#if !UNITY_WEBGL || UNITY_EDITOR
using FishNet.Managing;
using FishNet.Transporting.CanoeWebRTC.Client;
using FishNet.Transporting.CanoeWebRTC.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Profiling;

namespace FishNet.Transporting.CanoeWebRTC
{
    public class Connection
    {

        public int ConnectionID = -1;

        public bool closing = false;

        public RTCPeerConnection localConnection;

        RTCDataChannel reliableDataChannel;
        RTCDataChannel unreliableDataChannel;

        //purely for keeping alive...
        RTCDataChannel REMOTE_reliableDataChannel;
        RTCDataChannel REMOTE_unreliableDataChannel;


        /// <summary>
        /// Candidates the remote peer trickled in, held until this connection has a remote
        /// description to attach them to. The <b>wrapper owns the queue</b> (ADR 0011) — the
        /// signalling layer routes candidates here and never inspects negotiation state.
        /// </summary>
        public readonly InboundCandidateQueue inboundCandidates = new InboundCandidateQueue();

        /// <summary>Guards the end-of-candidates marker so it is emitted exactly once per connection.</summary>
        private bool _endOfCandidatesEmitted;

        public ClientSocket _clientSocket;
        public ServerSocket _serverSocket;


        public int currentRemoteChannels = 0;

        public string debugHeader = "";
        public Connection(ClientSocket clientSocket, ServerSocket serverSocket, int connectionID = -1)
        {
            if (connectionID == -1)
            {
                //Debug.Log("New Local Client Created");
                debugHeader = "<color=#77DD77>[Client]</color>";
            }
            else
            {
                //Debug.Log("New Remote Client Created");
                debugHeader = "<color=#FFA500>[Server]</color>";

            }

            _clientSocket = clientSocket;
            _serverSocket = serverSocket;
            ConnectionID = connectionID;

            var iceServers = new List<RTCIceServer>();
            foreach (ICEServer s in CanoeWebRTC._iceServers)
            {
                if (s.username == null || s.username == "")
                    iceServers.Add(new RTCIceServer
                    {
                        urls = new[] { s.url }
                    });
                else
                    iceServers.Add(new RTCIceServer
                    {
                        urls = new[] { s.url },
                        username = s.username,
                        credential = s.credential,
                        credentialType = RTCIceCredentialType.Password
                    });
            }

            var configuration = new RTCConfiguration
            {
                iceTransportPolicy = (CanoeWebRTC._onlyAllowRelay ? RTCIceTransportPolicy.Relay : RTCIceTransportPolicy.All),
                iceServers = iceServers.ToArray()
            };

            localConnection = new RTCPeerConnection();

            localConnection.SetConfiguration(ref configuration);

            // Set up the data channel for the local connection
            unreliableDataChannel = localConnection.CreateDataChannel("Unreliable", new RTCDataChannelInit()
            {
                ordered = false,
                maxRetransmits = 0
            });

            reliableDataChannel = localConnection.CreateDataChannel("Reliable", new RTCDataChannelInit()
            {
                ordered = true
            });

            //reliableDataChannel.OnOpen = () => Debug.Log($"reliable channel opened on {ID}");
            //reliableDataChannel.OnClose = () => Debug.Log($"reliable channel CLOSED on {ID}");
            //unreliableDataChannel.OnOpen = () => Debug.Log($"unreliable channel opened on {ID}");
            //unreliableDataChannel.OnClose = () => Debug.Log($"unreliable channel CLOSED on {ID}");


            reliableDataChannel.OnError = (e) => HandleChannelError("Local Reliable", e);
            unreliableDataChannel.OnError = (e) => HandleChannelError("Local Unreliable", e);


            // Set up the remote connection to handle incoming data channels
            localConnection.OnDataChannel = (RTCDataChannel channel) =>
            {

                if (channel.Label == "Unreliable")
                {
                    InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Unreliable Channel received");
                    REMOTE_unreliableDataChannel = channel;
                    channel.OnMessage = ReceiveUnreliableMessage;
                    channel.OnError = (e) => HandleChannelError("Remote Unreliable", e);

                    channel.OnOpen = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Unreliable Channel Opened");
                    };
                    channel.OnClose = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Unreliable Channel Closed");
                        if (!closing)
                            RemoteChannelsClosed();
                    };
                }
                else
                {
                    InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Reliable Channel received");
                    REMOTE_reliableDataChannel = channel;
                    channel.OnMessage = ReceiveReliableMessage;
                    channel.OnError = (e) => HandleChannelError("Remote Reliable", e);

                    //these never go off for some reason.. but I guess it makes sense? its opening remotely so yea
                    channel.OnOpen = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Reliable Channel Opened");
                    };
                    channel.OnClose = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Reliable Channel Closed");
                        if (!closing)
                            RemoteChannelsClosed();
                    };
                }

                currentRemoteChannels++;
                if (currentRemoteChannels == 2)
                {
                    RemoteChannelsOpened();
                }

            };

            //Handle new ICE candidates generated locally. Each one is signalled the moment it
            //appears rather than collected into the offer/answer, which is what makes the join
            //fast: negotiation no longer waits on gathering.
            localConnection.OnIceCandidate = (RTCIceCandidate candidate) =>
            {
                //libwebrtc's other way of saying gathering is over.
                if (candidate == null)
                {
                    EmitEndOfCandidates();
                    return;
                }

                if (!ShouldTrickle(candidate))
                    return;

                Debug.Log($"{debugHeader} New local ICE candidate: {candidate.Candidate}");

                CanoeWebRTC.RaiseLocalIceCandidate(ConnectionID, candidate.Candidate);
            };

            localConnection.OnIceGatheringStateChange = (RTCIceGatheringState state) =>
            {
                if (state == RTCIceGatheringState.Complete)
                    EmitEndOfCandidates();
            };

            localConnection.OnIceConnectionChange = (RTCIceConnectionState state) =>
            {
                InstanceFinder.NetworkManager.Log($"{debugHeader} ICE Connection State changed to: <b><i><color=#DDA0DD>{state}</color></i></b>");
            };

            localConnection.OnConnectionStateChange = (RTCPeerConnectionState state) =>
            {
                InstanceFinder.NetworkManager.Log($"{debugHeader} Peer Connection State changed to: <b><i><color=#DDA0DD>{state}</color></i></b>");

                //Only `Failed` is terminal, so it tears down at once. `Disconnected` is transient -
                //ICE routinely recovers from it on its own - so it is deliberately left alone
                //(ADR 0012). Recovery from `Failed` is a full rejoin; restartIce is not built.
                if (state == RTCPeerConnectionState.Failed)
                {
                    if (!closing)
                        RemoteChannelsClosed();
                }
            };


        }


        /// <summary>
        /// Every data channel error ends the connection. The peer closing its end is one too: it
        /// aborts the SCTP association, and libwebrtc reports that on this end's channels as a
        /// "User-Initiated Abort". That is how a graceful leave looks from here, not a fault.
        /// </summary>
        private void HandleChannelError(string channel, RTCError e)
        {
            string text = $"{debugHeader} {channel} Channel closed\nType:{e.errorType} Message:{e.message}";
            if (e.message != null && e.message.Contains("User-Initiated Abort"))
                InstanceFinder.NetworkManager.Log(text);
            else
                InstanceFinder.NetworkManager.LogError(text);

            if (!closing)
                RemoteChannelsClosed();
        }


        //ICE candidates - outbound

        /// <summary>
        /// The single emit/drop predicate for locally gathered candidates on this transport.
        /// <para>
        /// It replaces the SDP-string scrub that used to run over the bundled candidate array:
        /// under trickle there is no array to scrub, so the decision is made once, here, as each
        /// candidate appears. <c>filterLocalConnections</c> is an off-by-default diagnostic for
        /// forcing traffic off the LAN — it drops host candidates and anything whose address is
        /// not a literal IP (an mDNS <c>.local</c> name, say).
        /// </para>
        /// </summary>
        private static bool ShouldTrickle(RTCIceCandidate candidate)
        {
            //TCP candidates were never signalled by this transport and still are not.
            if (candidate.Protocol != RTCIceProtocol.Udp)
                return false;

            if (!CanoeWebRTC._filterLocalConnections)
                return true;

            if (candidate.Type == RTCIceCandidateType.Host)
                return false;

            return IsIPv4(candidate.Address) || IsIPv6(candidate.Address);
        }

        /// <summary>
        /// Tell the far side that gathering is done, so it can stop waiting on candidates that are
        /// never coming. Emitted <b>exactly once per connection</b>: libwebrtc signals completion
        /// both by a null candidate and by the gathering-state transition, either of which may fire
        /// (or fire more than once), and the wire form of the marker — an empty candidate — carries
        /// no way for a receiver to tell duplicates apart.
        /// </summary>
        private void EmitEndOfCandidates()
        {
            if (_endOfCandidatesEmitted)
                return;

            _endOfCandidatesEmitted = true;

            Debug.Log($"{debugHeader} ICE gathering complete; sending end-of-candidates.");

            CanoeWebRTC.RaiseLocalIceCandidate(ConnectionID, TrickledICE.EndOfCandidates);
        }


        //ICE candidates - inbound

        /// <summary>
        /// A candidate the remote peer trickled to us. Applied at once if this connection already
        /// has a remote description, otherwise buffered until it does (ADR 0011).
        /// </summary>
        public void ReceiveRemoteCandidate(string candidate)
        {
            if (closing || localConnection == null)
                return;

            if (inboundCandidates.TryApplyNow(candidate))
                ApplyRemoteCandidate(candidate);
        }

        /// <summary>
        /// This connection's remote description is now set: apply everything that arrived early, in
        /// arrival order, and let later candidates through untouched. Idempotent.
        /// </summary>
        public void RemoteDescriptionSet()
        {
            foreach (string candidate in inboundCandidates.Release())
                ApplyRemoteCandidate(candidate);
        }

        /// <summary>Hand one already-releasable candidate to the ICE agent.</summary>
        private void ApplyRemoteCandidate(string candidate)
        {
            if (closing || localConnection == null)
                return;

            if (InboundCandidateQueue.IsEndOfCandidates(candidate))
            {
                //Unity.WebRTC documents AddIceCandidate(null) as the end-of-candidates indicator
                //but dereferences its argument regardless, and RTCIceCandidate refuses to construct
                //from an empty candidate string - so on this transport there is no way to hand the
                //marker to the agent. It is purely an optimisation (ICE concludes on its own
                //either way), so it is logged and dropped rather than faked.
                InstanceFinder.NetworkManager.Log($"{debugHeader} Remote peer finished gathering.");
                return;
            }

            try
            {
                localConnection.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                {
                    candidate = candidate,
                    sdpMid = "0",
                    sdpMLineIndex = 0
                }));
            }
            catch (Exception e)
            {
                //A candidate we cannot parse costs us one path, not the connection.
                InstanceFinder.NetworkManager.LogWarning($"{debugHeader} Discarded an unusable remote ICE candidate: {e.Message}");
            }
        }

        private static bool IsIPv4(string address)
        {
            if (string.IsNullOrEmpty(address))
                return false;

            var octets = address.Split('.');
            return octets.Length == 4 && octets.All(octet => byte.TryParse(octet, out _));
        }

        private static bool IsIPv6(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Contains('.'))
            {
                return false;
            }

            var parts = address.Split(':');

            int doubleColonCount = address.Split(new[] { "::" }, StringSplitOptions.None).Length - 1;
            if (doubleColonCount > 1)
            {
                return false;
            }

            var partsList = parts.Where(part => !string.IsNullOrEmpty(part)).ToList();

            if (partsList.Count > 8 || (doubleColonCount == 0 && partsList.Count != 8))
            {
                return false;
            }

            return true;
        }


        public void RemoteChannelsOpened()
        {

            if (ConnectionID != -1)
            {
                _serverSocket.UpdateRemoteConnectionState(RemoteConnectionState.Started, ConnectionID);
            }
            else
            {
                _clientSocket.UpdateLocalConnectionState(LocalConnectionState.Started);

            }

            //setup sending threads!

            Thread u = new Thread(() =>
            {
                sendThreadConfig sendThreadConfig = new sendThreadConfig(this, Channel.Unreliable);

                SendThread(sendThreadConfig);

            });
            unreliableSendThread = u;
            unreliableSendThread.IsBackground = true;
            unreliableSendThread.Name = $"UnreliableSendLoop {ConnectionID}";
            unreliableSendThread.Start();

            Thread r = new Thread(() =>
            {
                sendThreadConfig sendThreadConfig = new sendThreadConfig(this, Channel.Reliable);

                SendThread(sendThreadConfig);
            });
            reliableSendThread = r;
            reliableSendThread.IsBackground = true;
            reliableSendThread.Name = $"ReliableSendLoop {ConnectionID}";
            reliableSendThread.Start();

        }

        public void RemoteChannelsClosed()
        {
            closing = true;

            if (ConnectionID != -1)
            {
                //_serverSocket.UpdateRemoteConnectionState(somenewState, ConnectionID);
                _serverSocket.CloseConnection(ConnectionID);
            }
            else
            {
                _clientSocket.UpdateLocalConnectionState(LocalConnectionState.Stopped);
            }

        }

        //maybe split this so we are not doing an if check each time
        public void ReceiveUnreliableMessage(byte[] bytes)
        {
            //messages received here are from the other client
            //Debug.Log("Receiving a ureliable message");
            if (ConnectionID == -1)
            {
                _clientSocket._incoming.Enqueue(new Packet(0, bytes, (byte)Channel.Unreliable));

            }
            else
            {
                if (bytes.Length > _serverSocket.mtu)
                {
                    _serverSocket.CloseConnection(ConnectionID);
                }
                else
                {
                    _serverSocket._incoming.Enqueue(new Packet(ConnectionID, bytes, (byte)Channel.Unreliable));
                }
            }

        }

        public void ReceiveReliableMessage(byte[] bytes)
        {
            //messages received here are from the other client


            if (ConnectionID == -1)
            {
                _clientSocket._incoming.Enqueue(new Packet(0, bytes, (byte)Channel.Reliable));

            }
            else
            {

                if (bytes.Length > _serverSocket.mtu)
                {
                    _serverSocket.CloseConnection(ConnectionID);
                }
                else
                {
                    _serverSocket._incoming.Enqueue(new Packet(ConnectionID, bytes, (byte)Channel.Reliable));
                }
            }

        }

        //sending stuff
        public Thread unreliableSendThread;
        public Thread reliableSendThread;

        public ConcurrentQueue<byte[]> unreliableSends = new ConcurrentQueue<byte[]>();
        public ManualResetEventSlim unreliablePending = new ManualResetEventSlim(false);
        public ConcurrentQueue<byte[]> reliableSends = new ConcurrentQueue<byte[]>();
        public ManualResetEventSlim reliablePending = new ManualResetEventSlim(false);


        public struct sendThreadConfig
        {
            public readonly Channel channel;
            public readonly ConcurrentQueue<byte[]> sendQueue;
            public readonly ManualResetEventSlim sendPending;
            public readonly RTCDataChannel dataChannel;
            public readonly CommonSocket commonSocket;

            public sendThreadConfig(Connection connection, Channel channel)
            {
                this.channel = channel;
                sendQueue = channel == Channel.Unreliable ? connection.unreliableSends : connection.reliableSends;
                sendPending = channel == Channel.Unreliable ? connection.unreliablePending : connection.reliablePending;
                dataChannel = channel == Channel.Unreliable ? connection.unreliableDataChannel : connection.reliableDataChannel;
                commonSocket = connection.ConnectionID == -1 ? connection._clientSocket : connection._serverSocket;
            }

            public void Deconstruct(out Channel channel, out ConcurrentQueue<byte[]> sendQueue, out ManualResetEventSlim sendPending, out RTCDataChannel dataChannel, out CommonSocket commonSocket)
            {
                channel = this.channel;
                sendQueue = this.sendQueue;
                sendPending = this.sendPending;
                dataChannel = this.dataChannel;
                commonSocket = this.commonSocket;
            }
        }


        void SendThread(sendThreadConfig config)
        {

            Profiler.BeginThreadProfiling("WebRTCConnection", $"SendLoop {ConnectionID}");

            bool dispose = false;

            var (channel, sendQueue, sendPending, dataChannel, socket) = config;

            try
            {

                while (localConnection.ConnectionState == RTCPeerConnectionState.Connected)
                {
                    // wait for message
                    sendPending.Wait();
                    sendPending.Reset();

                    if (dataChannel.ReadyState == RTCDataChannelState.Open)
                    {
                        while (sendQueue.TryDequeue(out byte[] data))
                        {
                            dataChannel.Send(data);
                        }
                    }

                }

                if (dispose)
                {
                    Profiler.EndThreadProfiling();
                    InstanceFinder.NetworkManager.LogError("ERROR Closing connection due to exception");

                    if (!closing)
                        RemoteChannelsClosed();
                }


            }
            catch (ThreadInterruptedException)
            {
                dispose = true;
                //Debug.Log(e);
            }
            catch (ThreadAbortException)
            {
                dispose = true;
                //Debug.Log(e);
            }
            catch (Exception)
            {
                dispose = true;
                //Debug.Log(e);
            }
        }









        readonly object disposedLock = new object();
        bool hasDisposed = false;

        public void CloseAll()
        {

            if (hasDisposed) { return; }

            lock (disposedLock)
            {

                if (hasDisposed) { return; }
                hasDisposed = true;

                //Disposing the peer connection can still fire state callbacks; flagging closing here
                //keeps them from re-entering the teardown path for a connection already gone.
                closing = true;

                unreliableSendThread?.Interrupt();
                reliableSendThread?.Interrupt();

                try
                {
                    localConnection?.Dispose();
                    localConnection = null;
                    reliableDataChannel?.Dispose();
                    reliableDataChannel = null;
                    unreliableDataChannel?.Dispose();
                    unreliableDataChannel = null;
                    REMOTE_reliableDataChannel?.Dispose();
                    REMOTE_reliableDataChannel = null;
                    REMOTE_unreliableDataChannel?.Dispose();
                    REMOTE_unreliableDataChannel = null;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                unreliablePending.Dispose();
                reliablePending.Dispose();

                while (unreliableSends.TryDequeue(out byte[] someData))
                {

                }
                while (reliableSends.TryDequeue(out byte[] someData))
                {

                }

            }
        }

    }
}
#endif