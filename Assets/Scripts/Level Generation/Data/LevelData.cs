using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LevelData 
{
    public List<Room> UnplacedRooms = new List<Room>();
    public List<Room> PlacedRooms = new List<Room>();
    public List<Hallway> Hallways = new List<Hallway>();
    
    // Logical Query methods live here!
    public Room GetLargestRoom() => PlacedRooms.OrderByDescending(r => r.Size).FirstOrDefault();
    
    public Room GetClosestRoomTo(Vector3Int position) {
        throw new NotImplementedException();
        // Efficiently find rooms without checking every tile in the grid
        // return Rooms.OrderBy(r => Vector3Int.Distance(position, r.Center)).FirstOrDefault();
    }

    public bool IsPositionInsideAnyRoom(Vector3Int position)
    {
        foreach (var room in PlacedRooms)
        {
            if (room.GetBounds().Contains(new Vector2Int(position.x, position.z)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a proposed room overlaps with any existing rooms.
    /// </summary>
    /// <param name="buffer">Extra space to keep between rooms (padding).</param>
    public bool DoesRoomOverlap(Vector3Int start, Vector2Int size, int buffer = 0)
    {
        // 1. Define the rectangle for the new room
        // We include the buffer to ensure rooms aren't touching walls
        RectInt newRoomRect = new RectInt(
            start.x - buffer, 
            start.z - buffer, 
            size.x + (buffer * 2), 
            size.y + (buffer * 2)
        );

        // 2. Compare against all registered rooms
        foreach (var room in PlacedRooms)
        {
            if (newRoomRect.Overlaps(room.GetBounds()))
            {
                return true; // Collision detected!
            }
        }

        return false; // Path is clear
    }

    /// <summary>
    /// Finds the room at a specific grid coordinate.
    /// Throws an error if overlapping rooms are detected.
    /// </summary>
    public Room GetRoomAt(Vector3Int position)
    {
        Room foundRoom = null;
        Vector2Int pos2D = new Vector2Int(position.x, position.z);

        foreach (var room in PlacedRooms)
        {
            if (room.GetBounds().Contains(pos2D))
            {
                // If we already found a room, we have an overlap error!
                if (foundRoom != null)
                {
                    Debug.LogError($"[LevelData] Overlap detected at {position}! " +
                                   $"Room A: {foundRoom.GetBounds()}, Room B: {room.GetBounds()}");
                }
                
                foundRoom = room;
            }
        }

        return foundRoom;
    }
}