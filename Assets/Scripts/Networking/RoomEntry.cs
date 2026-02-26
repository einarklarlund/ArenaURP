using System;

[Serializable]
public class RoomEntry
{
    public string id;       // The room code (e.g., "ax78j")
    public string hostID;   // The playerID of the host
    public long lastSeen;   // Timestamp from Redis
    // Add custom metadata here later, like mapName or mode
}

[Serializable]
public class RoomListResponse
{
    // This matches the "rooms" key in your MainServer.js responseObject
    public RoomEntry[] rooms;
}