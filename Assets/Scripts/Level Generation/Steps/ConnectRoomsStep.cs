using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Connect Rooms")]
public class ConnectRoomsStep : LevelGenerationStep
{
    public int MinConnections = 1;
    public int MaxConnections = 3;

    public override IEnumerator Execute(GenerationContext context)
    {
        var rooms = context.Data.PlacedRooms;
        var placedHallways = new List<Room>();

        foreach (var roomA in rooms)
        {
            int targetConnections = Random.Range(MinConnections, MaxConnections + 1);

            while (roomA.ConnectedRooms.Count < targetConnections)
            {
                // Find a room we aren't already connected to
                Room roomB = FindPotentialConnection(roomA, rooms);
                if (roomB == null) break; // No more eligible rooms

                var hallway = CreateHallwayConnection(roomA, roomB, context);
                placedHallways.Add(hallway);
            }
        }

        context.Data.PlacedRooms.AddRange(placedHallways);
        yield return null;
    }

    private Hallway CreateHallwayConnection(Room a, Room b, GenerationContext context)
    {
        Vector3Int startPos = GetRandomPointInRoom(a);
        Vector3Int endPos = GetRandomPointInRoom(b);

        // 1. Create the Hallway instance
        Hallway hallway = new Hallway(startPos, endPos);

        // 2. Use the standard Stamp method. 
        // We pass the minimum corner of the bounding box as the 'start' for the RectInt
        Vector3Int minCorner = new Vector3Int(
            Mathf.Min(startPos.x, endPos.x),
            0,
            Mathf.Min(startPos.z, endPos.z)
        );

        hallway.Stamp(minCorner, context.Grid);
        
        // Cross-link the rooms
        a.ConnectedRooms.Add(b);
        b.ConnectedRooms.Add(a);
        hallway.ConnectedRooms.Add(a);
        hallway.ConnectedRooms.Add(b);

        // Add the Hallway to level data so that we can perform logic on hallways
        context.Data.Hallways.Add(hallway);

        return hallway;
    }

    private Vector3Int GetRandomPointInRoom(Room r)
    {
        return r.MemberTiles[Random.Range(0, r.MemberTiles.Count)].GridPosition;
    }

    private Room FindPotentialConnection(Room main, List<Room> allRooms)
    {
        Room closestRoom = null;
        float minDistance = float.MaxValue;

        // Get the center of our main room to measure from
        Vector2 mainCenter = main.GetBounds().center;

        foreach (var candidate in allRooms)
        {
            // 1. Validation Checks: 
            // - Don't connect to self
            // - Don't connect to something already connected
            // - (Optional) Don't connect to existing Hallways if you want room-to-room only
            if (candidate == main || main.ConnectedRooms.Contains(candidate) || candidate is Hallway)
                continue;

            // 2. Calculate Distance
            Vector2 candidateCenter = candidate.GetBounds().center;
            float distance = Vector2.Distance(mainCenter, candidateCenter);

            // 3. Keep the closest one
            if (distance < minDistance)
            {
                minDistance = distance;
                closestRoom = candidate;
            }
        }

        return closestRoom;
    }
}