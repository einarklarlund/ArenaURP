#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace FishNet.Transporting.CanoeWebRTC
{
    public static class WebGLWebRTC
    {

        [DllImport("__Internal")]
        private static extern void RegisterICEServers(string iceServersJSON);


        //The candidate-collect duration is gone from both signatures: nothing stalls to bundle any
        //more. `localIceCandidateCallback` is what replaced it - the jslib raises it once per
        //gathered candidate and once more, empty, at gathering-complete. `filterLocalConnections`
        //moved down here because the gather site is now where that predicate is applied, exactly as
        //on the standalone transport.
        //
        //These signatures and the jslib's own parameter lists are one declaration written twice.
        //Change them together or the WebGL build silently mis-marshals.

        [DllImport("__Internal")]
        private static extern void RegisterClientCallbacks(
            Action remoteChannelClosedCallback_Client,
            Action remoteChannelOpenedCallback_Client,
            Action<IntPtr, int> reliableMessageReceivedCallback_Client,
            Action<IntPtr, int> unreliableMessageReceivedCallback_Client,
            Action<string> respondToOfferCallback,
            Action<string> localIceCandidateCallback_Client,
            int filterLocalConnections,
            int onlyAllowRelay
        );

        [DllImport("__Internal")]
        private static extern void RegisterServerCallbacks(
            Action<int> remoteChannelClosedCallback_Server,
            Action<int> remoteChannelOpenedCallback_Server,
            Action<int, IntPtr, int> reliableMessageReceivedCallback_Server,
            Action<int, IntPtr, int> unreliableMessageReceivedCallback_Server,
            Action<int, string> createOfferCallback,
            Action<int, string> localIceCandidateCallback_Server,
            int filterLocalConnections,
            int onlyAllowRelay
        );


        [DllImport("__Internal")]
        private static extern void CreateClientConnection();

        [DllImport("__Internal")]
        private static extern void CreateRemoteConnection(int connectionID);

        [DllImport("__Internal")]
        private static extern int GetRemoteConnectionState(int connectionID);

        [DllImport("__Internal")]
        private static extern void CloseClientConnection();

        [DllImport("__Internal")]
        private static extern void CloseRemoteConnection(int connectionID);

        [DllImport("__Internal")]
        private static extern void CloseAllRemoteConnections();

        [DllImport("__Internal")]
        private static extern void SendUnreliable_ToServer(IntPtr dataPtr, int size);

        [DllImport("__Internal")]
        private static extern void SendUnreliable_ToClient(int connectionID, IntPtr dataPtr, int size);

        [DllImport("__Internal")]
        private static extern void SendUnreliable_ToAllClients(IntPtr dataPtr, int size);

        [DllImport("__Internal")]
        private static extern void SendReliable_ToServer(IntPtr dataPtr, int size);

        [DllImport("__Internal")]
        private static extern void SendReliable_ToClient(int connectionID, IntPtr dataPtr, int size);

        [DllImport("__Internal")]
        private static extern void SendReliable_ToAllClients(IntPtr dataPtr, int size);

        [DllImport("__Internal")]
        private static extern void CreateOffer(int connectionID);

        [DllImport("__Internal")]
        private static extern void HandleAnswer(int connectionID, string answer);

        [DllImport("__Internal")]
        private static extern void HandleOffer(string offer);

        [DllImport("__Internal")]
        private static extern void AddRemoteCandidateToClient(string candidate);

        [DllImport("__Internal")]
        private static extern void AddRemoteCandidateToServer(int connectionID, string candidate);


        public static void _RegisterICEServers(string iceServersJSON) => RegisterICEServers(iceServersJSON);

        public static void InitializeClientCallbacks(
            Action remoteChannelClosedCallback_Client,
            Action remoteChannelOpenedCallback_Client,
            Action<IntPtr, int> reliableMessageReceivedCallback_Client,
            Action<IntPtr, int> unreliableMessageReceivedCallback_Client,
            Action<string> respondToOfferCallback,
            Action<string> localIceCandidateCallback_Client,
            int filterLocalConnections,
            int onlyAllowRelay)
        {
            RegisterClientCallbacks(
                remoteChannelClosedCallback_Client,
                remoteChannelOpenedCallback_Client,
                reliableMessageReceivedCallback_Client,
                unreliableMessageReceivedCallback_Client,
                respondToOfferCallback,
                localIceCandidateCallback_Client,
                filterLocalConnections,
                onlyAllowRelay
            );
        }

        public static void InitializeServerCallbacks(
        Action<int> remoteChannelClosedCallback_Server,
        Action<int> remoteChannelOpenedCallback_Server,
        Action<int, IntPtr, int> reliableMessageReceivedCallback_Server,
        Action<int, IntPtr, int> unreliableMessageReceivedCallback_Server,
        Action<int, string> createOfferCallback,
        Action<int, string> localIceCandidateCallback_Server,
        int filterLocalConnections,
        int onlyAllowRelay)
        {
            RegisterServerCallbacks(
                remoteChannelClosedCallback_Server,
                remoteChannelOpenedCallback_Server,
                reliableMessageReceivedCallback_Server,
                unreliableMessageReceivedCallback_Server,
                createOfferCallback,
                localIceCandidateCallback_Server,
                filterLocalConnections,
                onlyAllowRelay
            );
        }


        public static void CreateClient() => CreateClientConnection();

        public static void _CreateRemoteConnection(int connectionID) => CreateRemoteConnection(connectionID);

        public static int _GetRemoteConnectionState(int connectionID) => GetRemoteConnectionState(connectionID);

        public static void CloseClient() => CloseClientConnection();

        public static void _CloseRemoteConnection(int connectionID) => CloseRemoteConnection(connectionID);

        public static void _CloseAllRemoteConnection() => CloseAllRemoteConnections();

        public static void SendUnreliableToServer(IntPtr dataPtr, int size) => SendUnreliable_ToServer(dataPtr, size);

        public static void SendUnreliableToClient(int connectionID, IntPtr dataPtr, int size) => SendUnreliable_ToClient(connectionID, dataPtr, size);

        public static void SendUnreliableToAllClients(IntPtr dataPtr, int size) => SendUnreliable_ToAllClients(dataPtr, size);

        public static void SendReliableToServer(IntPtr dataPtr, int size) => SendReliable_ToServer(dataPtr, size);

        public static void SendReliableToClient(int connectionID, IntPtr dataPtr, int size) => SendReliable_ToClient(connectionID, dataPtr, size);

        public static void SendReliableToAllClients(IntPtr dataPtr, int size) => SendReliable_ToAllClients(dataPtr, size);

        public static void _CreateOffer(int connectionID) => CreateOffer(connectionID);

        public static void _HandleAnswerToOffer(int connectionID, string answer) => HandleAnswer(connectionID, answer);

        public static void _HandleOffer(string offer) => HandleOffer(offer);

        /// <summary>A candidate the host trickled to us; an empty string is end-of-candidates.</summary>
        public static void _AddRemoteCandidateToClient(string candidate) => AddRemoteCandidateToClient(candidate);

        /// <summary>A candidate one of our clients trickled to us; an empty string is end-of-candidates.</summary>
        public static void _AddRemoteCandidateToServer(int connectionID, string candidate) => AddRemoteCandidateToServer(connectionID, candidate);
    }
}
#endif
