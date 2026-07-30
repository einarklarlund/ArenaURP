
const RemoteConnections = {};

const ClientConnection = null;

class Connection {
   constructor() {
      this.closing = false;
      this.connectionID = -1;
      this.peerConnection = null;
      this.channelsOpened = 0;
      this.local_unreliableDataChannel = null;
      this.local_reliableDataChannel = null;
      this.remote_unreliableDataChannel = null;
      this.remote_reliableDataChannel = null;

      // Trickle ICE state. Mirrors the standalone transport's Connection +
      // InboundCandidateQueue: candidates go out as they are gathered, inbound ones wait
      // here until this connection has a remote description to attach them to.
      this.inboundCandidates = [];
      this.endOfCandidatesBuffered = false;
      this.remoteDescriptionSet = false;
      this.endOfCandidatesEmitted = false;
    }


    // Candidates - outbound

    /**
     * The single emit/drop predicate for locally gathered candidates on this transport, and the
     * twin of Connection.ShouldTrickle on the standalone side. It reads the candidate's typed
     * fields rather than splitting the SDP line, which is what the old bundled-array scrub did.
     * FilterLocalConnections is an off-by-default diagnostic for forcing traffic off the LAN: it
     * drops host candidates and anything whose address is not a literal IP (an mDNS .local name).
     */
    static shouldTrickle(candidate) {
       // TCP candidates were never signalled by this transport and still are not.
       if (candidate.protocol !== "udp") return false;

       if (FilterLocalConnections !== 1) return true;

       if (candidate.type === "host") return false;

       return Connection.isIPv4(candidate.address) || Connection.isIPv6(candidate.address);
    }

    /**
     * Tell the far side gathering is done so it can stop waiting on candidates that are never
     * coming. Emitted exactly once per connection: a browser announces completion both by an
     * onicecandidate event with a null/empty candidate and by onicegatheringstatechange reaching
     * "complete", either of which may fire (or fire more than once), and the wire form of the
     * marker - an empty candidate - carries no way to tell duplicates apart.
     */
    emitEndOfCandidates() {
       if (this.endOfCandidatesEmitted) return;
       this.endOfCandidatesEmitted = true;

       console.log(`ICE gathering complete on connection ${this.connectionID}; sending end-of-candidates.`);

       this.emitLocalCandidate("");
    }

    /** Hand one candidate to the signalling layer. connectionID -1 is our connection to the host. */
    emitLocalCandidate(candidate) {
       if (this.connectionID === -1) {
          if (typeof LocalIceCandidate_Client !== "function") return;
          LocalIceCandidate_Client(candidate);
       } else {
          if (typeof LocalIceCandidate_Server !== "function") return;
          LocalIceCandidate_Server(this.connectionID, candidate);
       }
    }


    // Candidates - inbound

    /** An empty (or absent) candidate is the end-of-candidates marker. */
    static isEndOfCandidates(candidate) {
       return candidate === null || candidate === undefined || candidate === "";
    }

    /**
     * A candidate the remote peer trickled to us. Applied at once if this connection already has a
     * remote description, otherwise buffered until it does (ADR 0011).
     */
    receiveRemoteCandidate(candidate) {
       if (this.closing || this.peerConnection === null) return;

       if (!this.remoteDescriptionSet) {
          if (Connection.isEndOfCandidates(candidate)) {
             this.endOfCandidatesBuffered = true;
          } else {
             this.inboundCandidates.push(candidate);
          }
          return;
       }

       this.applyRemoteCandidate(candidate);
    }

    /**
     * This connection's remote description is now set: apply everything that arrived early, in
     * arrival order, then let later candidates through untouched. The end-of-candidates marker is
     * always released last - telling ICE gathering is complete before applying the candidates it
     * completes would throw away paths. Idempotent.
     */
    markRemoteDescriptionSet() {
       this.remoteDescriptionSet = true;

       const buffered = this.inboundCandidates;
       this.inboundCandidates = [];

       for (let i = 0; i < buffered.length; i++) {
          this.applyRemoteCandidate(buffered[i]);
       }

       if (this.endOfCandidatesBuffered) {
          this.endOfCandidatesBuffered = false;
          this.applyRemoteCandidate("");
       }
    }

    /** Hand one already-releasable candidate to the ICE agent. */
    applyRemoteCandidate(candidate) {
       if (this.closing || this.peerConnection === null) return;

       // addIceCandidate with an empty candidate string is the end-of-candidates indication for
       // the named m-line. Unlike Unity.WebRTC the browser accepts it, so the marker is a real
       // signal here rather than something to log and drop.
       const init = Connection.isEndOfCandidates(candidate)
          ? { candidate: "", sdpMid: "0", sdpMLineIndex: 0 }
          : { candidate: candidate, sdpMid: "0", sdpMLineIndex: 0 };

       this.peerConnection.addIceCandidate(init).catch(function (error) {
          // A candidate we cannot apply costs us one path, not the connection.
          console.warn("Discarded an unusable remote ICE candidate:", error);
       });
    }


    // Address shape, for the filterLocalConnections diagnostic only.

    static isIPv4(address) {
       if (!address) return false;

       const octets = address.split(".");
       return octets.length === 4 && octets.every(function (octet) {
          return /^\d{1,3}$/.test(octet) && Number(octet) <= 255;
       });
    }

    static isIPv6(address) {
       if (!address || address.indexOf(".") !== -1) return false;

       const doubleColonCount = address.split("::").length - 1;
       if (doubleColonCount > 1) return false;

       const parts = address.split(":").filter(function (part) {
          return part.length > 0;
       });

       if (parts.length > 8) return false;
       if (doubleColonCount === 0 && parts.length !== 8) return false;

       return true;
    }


    /**
     * Make sure a description advertises that this peer will trickle (RFC 8839 section 5.4), so
     * the far side treats the candidate-less SDP as the start of gathering rather than the end of
     * it. Browsers already emit the attribute on both offers and answers - this is the same
     * belt-and-braces pass CanoeWebRTC.EnsureTrickleIceOption makes on the standalone side,
     * inserting it only if it is somehow absent and never duplicating.
     */
    static ensureTrickleIceOption(sdp) {
       if (!sdp || sdp.indexOf("ice-options:trickle") !== -1) return sdp;

       // Session-level attributes follow the timing line, so insert directly after the first "t=".
       // If the description is not shaped the way SDP requires, leave it well alone.
       const timing = sdp.indexOf("\r\nt=");
       if (timing < 0) return sdp;

       const endOfTiming = sdp.indexOf("\r\n", timing + 2);
       if (endOfTiming < 0) return sdp;

       return sdp.slice(0, endOfTiming + 2) + "a=ice-options:trickle\r\n" + sdp.slice(endOfTiming + 2);
    }
 }

 const MemoryManager = {
   pool: [],
   maxPoolSize: 250,
 
   grab: function (array) {
     let size = array.length;
     let allocatedPtr;
 
     if (this.pool.length > 0) {
       let lastBlock = this.pool[this.pool.length - 1];
 
       if (lastBlock.size >= size) {
         let block = this.pool.pop();
         allocatedPtr = block.ptr;
 
         if (block.size > size) {
           let leftoverBlock = {
             ptr: allocatedPtr + size,
             size: block.size - size,
           };
           this.done(leftoverBlock.ptr, leftoverBlock.size);
         }
       } else {
         if (this.pool.length >= this.maxPoolSize) {
           let blockToFree = this.pool.pop();
           _free(blockToFree.ptr);
         }
         allocatedPtr = _malloc(size);
       }
     } else {
       allocatedPtr = _malloc(size);
     }
 
     HEAPU8.set(array, allocatedPtr);
 
     return { ptr: allocatedPtr, length: size };
   },
 
   done: function (ptr, size) {
     if (this.pool.length < this.maxPoolSize) {
       this.pool.push({ ptr, size });
     } else {
       _free(ptr);
     }
   },
 
   clean: function () {
     for (let block of this.pool) {
       _free(block.ptr);
     }
     this.pool = [];
   },
 };
 


 let IceServers = []; 

 function RegisterICEServers(iceServers) {
   const iceServersString = UTF8ToString(iceServers);
   
   if (!iceServersString || iceServersString.trim() === "") {
       IceServers = [];
   } else {
       IceServers = iceServersString.split(";;").map(entry => {
           const [url, username, credential] = entry.split("__");

           if (url && url.trim() !== "") {
               const server = { urls: url };
               if (username && credential) {
                   server.username = username;
                   server.credential = credential;
               }
               return server;
           }

           return null; 
       }).filter(server => server !== null);
   }

}




//This parameter list is the same declaration as WebGLWebRTC.RegisterClientCallbacks in C#.
//Change one without the other and the WebGL build silently mis-marshals.
function RegisterClientCallbacks(
    remoteChannelClosedCallback_Client,
    remoteChannelOpenedCallback_Client,
    reliableMessageReceivedCallback_Client,
    unreliableMessageReceivedCallback_Client,
    respondToOfferCallback,
    localIceCandidateCallback_Client,
    filterLocalConnections,
    onlyAllowRelay
 ) {
    var unity_remoteChannelClosedCallback_Client = {{{ makeDynCall('v', 'remoteChannelClosedCallback_Client') }}};
    RemoteChannelsClosed_Client = function () {
      if (ClientConnection){
         ClientConnection.closing = true;
      
      unity_remoteChannelClosedCallback_Client();
      }
    };
 
    var unity_remoteChannelOpenedCallback_Client = {{{ makeDynCall('v', 'remoteChannelOpenedCallback_Client') }}};
    RemoteChannelsOpened_Client = function () {
       unity_remoteChannelOpenedCallback_Client();
    };
 
    var unity_reliableMessageReceivedCallback_Client = {{{ makeDynCall('vii', 'reliableMessageReceivedCallback_Client') }}};
    ReceivedReliableMessage_Client = function (event) {
       handleReceivedMessage_Client(event, unity_reliableMessageReceivedCallback_Client);
    };
 
    var unity_unreliableMessageReceivedCallback_Client = {{{ makeDynCall('vii', 'unreliableMessageReceivedCallback_Client') }}};
    ReceivedUnreliableMessage_Client = function (event) {
       handleReceivedMessage_Client(event, unity_unreliableMessageReceivedCallback_Client);
    };

    var unity_respondToOfferCallback = {{{ makeDynCall('vi', 'respondToOfferCallback') }}};
    RespondToOfferCallback = function (jsonString) {
       unity_respondToOfferCallback(jsonString);
    };

    var unity_localIceCandidateCallback_Client = {{{ makeDynCall('vi', 'localIceCandidateCallback_Client') }}};
    LocalIceCandidate_Client = function (candidate) {
       let ptr = stringToNewUTF8(candidate);
       unity_localIceCandidateCallback_Client(ptr);
       _free(ptr);
    };

    function handleReceivedMessage_Client(event, unityCallback) {       
         if (event.data instanceof ArrayBuffer) {
            let array = new Uint8Array(event.data);

            let { ptr, length } = MemoryManager.grab(array);
            unityCallback(ptr, length);
            MemoryManager.done(ptr, length);

         } else if (event.data instanceof Blob) {
               // firefox sends blobs instead of arraybuffers
               let reader = new FileReader();
               reader.onload = function() {
                  let arrayBuffer = reader.result;
                  let array = new Uint8Array(arrayBuffer);

                  let { ptr, length } = MemoryManager.grab(array);
                  unityCallback(ptr, length);
                  MemoryManager.done(ptr, length);
               };
               reader.readAsArrayBuffer(event.data);

         } else {
               console.error("Message type not supported");
         }
    }

    FilterLocalConnections = filterLocalConnections;
    OnlyAllowRelay = onlyAllowRelay;

 }

//This parameter list is the same declaration as WebGLWebRTC.RegisterServerCallbacks in C#.
//Change one without the other and the WebGL build silently mis-marshals.
function RegisterServerCallbacks(
    remoteChannelClosedCallback_Server,
    remoteChannelOpenedCallback_Server,
    reliableMessageReceivedCallback_Server,
    unreliableMessageReceivedCallback_Server,
    createOfferCallback,
    localIceCandidateCallback_Server,
    filterLocalConnections,
    onlyAllowRelay
 ) {
    var unity_remoteChannelClosedCallback_Server = {{{ makeDynCall('vi', 'remoteChannelClosedCallback_Server') }}};
    RemoteChannelsClosed_Server = function (connectionID) {
      if(RemoteConnections[connectionID]){
         RemoteConnections[connectionID].closing = true;
         unity_remoteChannelClosedCallback_Server(connectionID);
      }
    };
 
    var unity_remoteChannelOpenedCallback_Server = {{{ makeDynCall('vi', 'remoteChannelOpenedCallback_Server') }}};
    RemoteChannelsOpened_Server = function (connectionID) {
       unity_remoteChannelOpenedCallback_Server(connectionID);
    };
 
    var unity_reliableMessageReceivedCallback_Server = {{{ makeDynCall('viii', 'reliableMessageReceivedCallback_Server') }}};
    ReceivedReliableMessage_Server = function (event, connectionID) {
       handleReceivedMessage_Server(event, connectionID, unity_reliableMessageReceivedCallback_Server);
    };
 
    var unity_unreliableMessageReceivedCallback_Server = {{{ makeDynCall('viii', 'unreliableMessageReceivedCallback_Server') }}};
    ReceivedUnreliableMessage_Server = function (event, connectionID) {
       handleReceivedMessage_Server(event, connectionID, unity_unreliableMessageReceivedCallback_Server);
    };
    
    var unity_createOfferCallback = {{{ makeDynCall('vii', 'createOfferCallback') }}};
    CreateOfferCallback = function (connectionID, jsonString) {
       unity_createOfferCallback(connectionID, jsonString);
    };

    var unity_localIceCandidateCallback_Server = {{{ makeDynCall('vii', 'localIceCandidateCallback_Server') }}};
    LocalIceCandidate_Server = function (connectionID, candidate) {
       let ptr = stringToNewUTF8(candidate);
       unity_localIceCandidateCallback_Server(connectionID, ptr);
       _free(ptr);
    };

    function handleReceivedMessage_Server(event, connectionID, unityCallback) {
       
 
         if (event.data instanceof ArrayBuffer) {
               let array = new Uint8Array(event.data);
      
               let { ptr, length } = MemoryManager.grab(array);
               unityCallback(connectionID, ptr, length);
               MemoryManager.done(ptr, length);
      
         } else if (event.data instanceof Blob) {
               // firefox
               let reader = new FileReader();
               reader.onload = function() {
                  let arrayBuffer = reader.result;
                  let array = new Uint8Array(arrayBuffer);
      
                  let { ptr, length } = MemoryManager.grab(array);
                  unityCallback(connectionID, ptr, length);
                  MemoryManager.done(ptr, length);
               };
               reader.readAsArrayBuffer(event.data);
      
         } else {
               console.error("Message type not supported");
         }
    }

    FilterLocalConnections = filterLocalConnections;
    OnlyAllowRelay = onlyAllowRelay;
 }




function CreateClientConnection() {


   if (window.adapter) {
      console.log("WebRTC Adapter is available and can be used.");
    } else {
      console.log("WebRTC Adapter is not available.");
    }


   if (ClientConnection != null) {
      console.error("[Client] Can't create another client, one already exists.");
      return;
   }

   let newConn = new Connection();

   newConn.connectionID = -1;


   newConn.peerConnection = new RTCPeerConnection({
      iceTransportPolicy: (OnlyAllowRelay === 1 ? 'relay' : 'all'),
      iceServers: IceServers
   });

   newConn.local_unreliableDataChannel = newConn.peerConnection.createDataChannel("Unreliable", {
      ordered: false,
      maxRetransmits: 0
   });

   newConn.local_reliableDataChannel = newConn.peerConnection.createDataChannel("Reliable", {
      ordered: true
   });

   newConn.local_reliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Reliable Channel\nType: ${event.type} Message: ${event.message}`);
      
      if(!newConn.closing){
         RemoteChannelsClosed_Client();
      }
   };
   

   newConn.local_unreliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Unreliable Channel\nType: ${event.type} Message: ${event.message}`);
      if(!newConn.closing){
         RemoteChannelsClosed_Client();
      }
   };

   newConn.peerConnection.ondatachannel = function (event) {
      const channel = event.channel;

      if (channel.label === "Unreliable") {
         //console.log("Remote Unreliable Channel received");
         newConn.remote_unreliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedUnreliableMessage_Client(event);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Unreliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };

         channel.onopen = function () {
            //console.log("Remote Unreliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Unreliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };

      } else if (channel.label === "Reliable") {
         //console.log("Remote Reliable Channel received");
         newConn.remote_reliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedReliableMessage_Client(event);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Reliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };

         channel.onopen = function () {
            //console.log("Remote Reliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Reliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };
      }

      newConn.channelsOpened++;
      if (newConn.channelsOpened === 2) {
         RemoteChannelsOpened_Client();
      }
   };

   // Each candidate is signalled the moment it appears rather than collected into the answer,
   // which is what makes the join fast: negotiation no longer waits on gathering.
   newConn.peerConnection.onicecandidate = function (event) {
      // The browser's other way of saying gathering is over.
      if (!event.candidate || !event.candidate.candidate) {
         newConn.emitEndOfCandidates();
         return;
      }

      const candidate = event.candidate;

      if (!Connection.shouldTrickle(candidate)) {
         console.log(`Ignored local ICE candidate: ${candidate.candidate}`);
         return;
      }

      console.log(`New local ICE candidate: ${candidate.candidate}`);
      newConn.emitLocalCandidate(candidate.candidate);
   };

   newConn.peerConnection.oniceconnectionstatechange = function () {
      const state = newConn.peerConnection.iceConnectionState;
      console.log(`CLIENT -- ICE Connection State changed to: ${state}`);
   };

   newConn.peerConnection.onicegatheringstatechange = function () {
      const state = newConn.peerConnection.iceGatheringState;
      console.log(`CLIENT -- ICE Gathering State changed to: ${state}`);

      if (state === "complete") {
         newConn.emitEndOfCandidates();
      }
   };

   newConn.peerConnection.onconnectionstatechange = function () {
      const state = newConn.peerConnection.connectionState;
      console.log(`Peer Connection State changed to: ${state}`);

      // Only 'failed' is terminal, so it tears down at once. 'disconnected' is transient - ICE
      // routinely recovers from it on its own - so it is deliberately left alone (ADR 0012).
      // Matches Connection.OnConnectionStateChange on the standalone transport.
      if(state == 'failed' || state == 'closed'){
         if(!newConn.closing){
            RemoteChannelsClosed_Client();
         }
      }
      
   };

   ClientConnection = newConn;
}


function CreateRemoteConnection(connectionID) {

   if (window.adapter) {
      //console.log("WebRTC Adapter is available and can be used.");
    } else {
      //console.log("WebRTC Adapter is not available.");
    }

   let newConn = new Connection();

   newConn.connectionID = connectionID;


   newConn.peerConnection = new RTCPeerConnection({
      iceTransportPolicy: (OnlyAllowRelay === 1 ? 'relay' : 'all'),
      iceServers: IceServers
   });

   newConn.local_unreliableDataChannel = newConn.peerConnection.createDataChannel("Unreliable", {
      ordered: false,
      maxRetransmits: 0
   });

   newConn.local_reliableDataChannel = newConn.peerConnection.createDataChannel("Reliable", {
      ordered: true
   });

   newConn.local_reliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Reliable Channel\nType: ${event.type} Message: ${event.message}`);
      if(!newConn.closing){
         RemoteChannelsClosed_Server(connectionID);
      }
   };

   newConn.local_unreliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Unreliable Channel\nType: ${event.type} Message: ${event.message}`);
      if(!newConn.closing){
         RemoteChannelsClosed_Server(connectionID);
      }
   };

   newConn.peerConnection.ondatachannel = function (event) {
      const channel = event.channel;

      if (channel.label === "Unreliable") {
         //console.log("Remote Unreliable Channel received");
         newConn.remote_unreliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedUnreliableMessage_Server(event, connectionID);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Unreliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };

         channel.onopen = function () {
            //console.log("Remote Unreliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Unreliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };

      } else if (channel.label === "Reliable") {
         //console.log("Remote Reliable Channel received");
         newConn.remote_reliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedReliableMessage_Server(event, connectionID);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Reliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };

         channel.onopen = function () {
            //console.log("Remote Reliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Reliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };
      }

      newConn.channelsOpened++;
      if (newConn.channelsOpened === 2) {
         RemoteChannelsOpened_Server(connectionID);
      }
   };

   // Each candidate is signalled the moment it appears rather than collected into the offer,
   // which is what makes the join fast: negotiation no longer waits on gathering.
   newConn.peerConnection.onicecandidate = function (event) {
      // The browser's other way of saying gathering is over.
      if (!event.candidate || !event.candidate.candidate) {
         newConn.emitEndOfCandidates();
         return;
      }

      const candidate = event.candidate;

      if (!Connection.shouldTrickle(candidate)) {
         console.log(`ON SERVER -- Ignored ICE candidate for remote connection: ${candidate.candidate}`);
         return;
      }

      console.log(`ON SERVER -- New ICE candidate for remote connection: ${candidate.candidate}`);
      newConn.emitLocalCandidate(candidate.candidate);
   };

   newConn.peerConnection.oniceconnectionstatechange = function () {
      const state = newConn.peerConnection.iceConnectionState;
      console.log(`SERVER -- ICE Connection State changed to: ${state}`);
      //wont do anything here since below should handle
   };

   newConn.peerConnection.onicegatheringstatechange = function () {
      const state = newConn.peerConnection.iceGatheringState;
      console.log(`SERVER -- ICE Gathering State changed to: ${state}`);

      if (state === "complete") {
         newConn.emitEndOfCandidates();
      }
   };

   newConn.peerConnection.onconnectionstatechange = function () {
      const state = newConn.peerConnection.connectionState;
      //console.log(`Peer Connection State changed to: ${state}`);

      // Only 'failed' is terminal, so it tears down at once. 'disconnected' is transient - ICE
      // routinely recovers from it on its own - so it is deliberately left alone (ADR 0012).
      // Matches Connection.OnConnectionStateChange on the standalone transport.
      if(state == 'failed' || state == 'closed'){
         if(!newConn.closing){
            RemoteChannelsClosed_Server(connectionID);
         }
      }
      
   };

   RemoteConnections[connectionID] = newConn;
}



function CloseClientConnection() {
   //console.log("Attempting to close client connection");

   if (ClientConnection != null) {
      ClientConnection.peerConnection.close();
      ClientConnection = null;
      MemoryManager.clean();
   } else {
      console.error("[Client] Trying to close client connection which doesn't exist.");
   }
}

//only from server
function GetRemoteConnectionState(connectionID){
    if(connectionID in RemoteConnections){
        //it exists
        if(RemoteConnections[connectionID].peerConnection.connectionState == 'connected'){
            return 1;
        }
    }

    return 0;
}

//only from server
function CloseRemoteConnection(connectionID) {
    //console.log("Attempting to close server connection ", connectionID);
    if (connectionID in RemoteConnections) {
      RemoteConnections[connectionID].peerConnection.close();
      RemoteConnections[connectionID] = null;

      delete RemoteConnections[connectionID];
    } else {
        console.error("[Server] Trying to close a connection that doesn't exist.");
    }
}

function CloseAllRemoteConnections() {

   //console.log("If server is not closing... something is wrong. we are closing all connections");
    for (let connectionID in RemoteConnections) {
      RemoteConnections[connectionID].peerConnection.close();
      RemoteConnections[connectionID] = null;

      delete RemoteConnections[connectionID];
        
    }
    MemoryManager.clean();

}

 

function SendUnreliable_ToServer(dataPtr, dataSize) {
   ////console.log("Attempting to send Unreliable");
   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(ClientConnection) {
      if(!ClientConnection.closing) {
         ClientConnection.local_unreliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendUnreliable_ToClient(connectionID, dataPtr, dataSize) {
   ////console.log("Attempting to send Unreliable");
   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(RemoteConnections[connectionID]) {
      if(!RemoteConnections[connectionID].closing) {
         RemoteConnections[connectionID].local_unreliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendUnreliable_ToAllClients(dataPtr, dataSize) {
    ////console.log("Attempting to send Unreliable to all clients");

    var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

    for (let connectionID in RemoteConnections) {
      if(RemoteConnections[connectionID]) {
         if(!RemoteConnections[connectionID].closing) {
            RemoteConnections[connectionID].local_unreliableDataChannel.send(dataArray);
         }else{
            console.error("Tried to send to closing connection");
         }
      }
      else {
         console.error("Tried to send to nonexistant connection");
      }
   }
}


function SendReliable_ToServer(dataPtr, dataSize) {
   ////console.log("Attempting to send Reliable");

   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(ClientConnection) {
      if(!ClientConnection.closing) {
         ClientConnection.local_reliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendReliable_ToClient(connectionID, dataPtr, dataSize) {
   ////console.log("Attempting to send Reliable");

   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(RemoteConnections[connectionID]) {
      if(!RemoteConnections[connectionID].closing) {
         RemoteConnections[connectionID].local_reliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendReliable_ToAllClients(dataPtr, dataSize) {
    ////console.log("Attempting to send Reliable to all clients");

    var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

    for (let connectionID in RemoteConnections) {
      if(RemoteConnections[connectionID]) {
         if(!RemoteConnections[connectionID].closing) {
            RemoteConnections[connectionID].local_reliableDataChannel.send(dataArray);
         }else{
            console.error("Tried to send to closing connection");
         }
      }
      else {
         console.error("Tried to send to nonexistant connection");
      }
    }
}


//create offer for a client from the server
function CreateOffer(connectionID) {
   //console.log("Create Offer Called");

   const conn = RemoteConnections[connectionID];

   conn.peerConnection.createOffer().then(function (offer) {
      return conn.peerConnection.setLocalDescription(offer);
   }).then(function () {

         //The offer goes out the instant the local description is set. Gathering has only just
         //begun and every candidate it produces is trickled separately, so there is nothing left
         //to wait for here - this is where the join's first multi-second stall used to be.
         var data = {
            sdp: Connection.ensureTrickleIceOption(conn.peerConnection.localDescription.sdp)
         };

         var jsonString = JSON.stringify(data);
         let ptr = stringToNewUTF8(jsonString);
         CreateOfferCallback(connectionID, ptr);
         _free(ptr);

      }).catch(function (error) {
            console.error('Failed to create offer or set local description:', error);

            var erroredData = {
               sdp: "",
               error: true,
               errorMessage: "Failed to create offer or set local description: " + error
            };

            var erroredJsonString = JSON.stringify(erroredData);

            let errptr = stringToNewUTF8(erroredJsonString);
            CreateOfferCallback(connectionID, errptr);
            _free(errptr);

         });
}

//only done on server when receiving an answer from the client we sent an offer to
function HandleAnswer(connectionID, answerJSON) {

   var answerJSONHandled = UTF8ToString(answerJSON);
   var answerData = JSON.parse(answerJSONHandled);

   const conn = RemoteConnections[connectionID];

   var answer = new RTCSessionDescription({
       type: 'answer',
       sdp: answerData.sdp
   });

   conn.peerConnection.setRemoteDescription(answer).then(function () {

         //The client will already have trickled candidates at us - it starts gathering as soon as
         //it sets its own local description, which is before we ever see this answer. They were
         //buffered because there was nothing to attach them to; now there is.
         conn.markRemoteDescriptionSet();

   }).catch(function (error) {
       console.error('Failed to set remote description:', error);
       conn.peerConnection.close(); //will cause callback
   });
}

//done on client when server sends us an offer
function HandleOffer(offerJSON) {
   //console.log("[Client] Handle Offer Called");

   var offerJSONHandled = UTF8ToString(offerJSON);

   var offerData = JSON.parse(offerJSONHandled);

   const conn = ClientConnection;
   var peerConnection = conn.peerConnection;

   var offer = new RTCSessionDescription({
       type: 'offer',
       sdp: offerData.sdp
   });

   peerConnection.setRemoteDescription(offer).then(function () {

       //The host began gathering the moment it set its own local description, so its first
       //candidates can beat its offer's arrival here. Release them now that there is a remote
       //description to attach them to; from here the queue passes everything straight through.
       conn.markRemoteDescriptionSet();

       return peerConnection.createAnswer();
   }).then(function (answer) {
       return peerConnection.setLocalDescription(answer);
      }).then(function () {

         //The answer goes out the instant the local description is set - the second of the two
         //multi-second stalls the join used to sit through. Our own candidates follow as they are
         //gathered.
         var responseData = {
            sdp: Connection.ensureTrickleIceOption(peerConnection.localDescription.sdp)
         };

         var jsonResponseString = JSON.stringify(responseData);

         let ptr = stringToNewUTF8(jsonResponseString);
         RespondToOfferCallback(ptr);
         _free(ptr);

         }).catch(function (error) {
            console.error('Failed to handle server offer', error);
            var erroredData = {
               sdp: "",
               error: true,
               errorMessage: "Failed to handle server offer: " + error
            };

      var erroredJsonString = JSON.stringify(erroredData);

      let errptr = stringToNewUTF8(erroredJsonString);
      RespondToOfferCallback(errptr);
      _free(errptr);
      });
}


//A candidate the host trickled to us. We are the client, so there is exactly one peer connection
//it can belong to and the connection id the server stamped is redundant. An empty string is the
//end-of-candidates marker.
function AddRemoteCandidateToClient(candidate) {
   const candidateString = UTF8ToString(candidate);

   if (ClientConnection === null) {
      console.warn("[Client] Dropped a trickled ICE candidate: no connection to the host.");
      return;
   }

   ClientConnection.receiveRemoteCandidate(candidateString);
}

//A candidate a client trickled to us. We are the host, so the connection id the server stamped
//picks which of our peer connections it belongs to.
function AddRemoteCandidateToServer(connectionID, candidate) {
   const candidateString = UTF8ToString(candidate);

   if (!RemoteConnections[connectionID]) {
      //Routine, not alarming: a candidate can still be in flight when the attempt it belongs to
      //is torn down.
      console.log(`[Server] Dropped a trickled ICE candidate for unknown connection ${connectionID}.`);
      return;
   }

   RemoteConnections[connectionID].receiveRemoteCandidate(candidateString);
}

function ClientAcknowledged() {

}

const WebGLWebRTCLib = {
   $Connection: Connection,
   $ClientConnection: ClientConnection,
   $RemoteConnections: RemoteConnections,
   $MemoryManager: MemoryManager,
   $IceServers: IceServers,
   RegisterICEServers,
   RegisterClientCallbacks,
   RegisterServerCallbacks,
   CreateClientConnection,
   CreateRemoteConnection,
   CloseClientConnection,
   GetRemoteConnectionState,
   CloseRemoteConnection,
   CloseAllRemoteConnections,
   SendUnreliable_ToServer,
   SendUnreliable_ToClient,
   SendUnreliable_ToAllClients,
   SendReliable_ToServer,
   SendReliable_ToClient,
   SendReliable_ToAllClients,
   CreateOffer,
   HandleAnswer,
   HandleOffer,
   AddRemoteCandidateToClient,
   AddRemoteCandidateToServer
};

autoAddDeps(WebGLWebRTCLib, "$Connection");
autoAddDeps(WebGLWebRTCLib, "$ClientConnection");
autoAddDeps(WebGLWebRTCLib, "$RemoteConnections");
autoAddDeps(WebGLWebRTCLib, "$MemoryManager");
autoAddDeps(WebGLWebRTCLib, "$IceServers");
mergeInto(LibraryManager.library, WebGLWebRTCLib);