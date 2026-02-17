using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Fit and Stamp Rooms")]
public class RoomStamperStep : LevelGenerationStep
{
    public int MaxPlacementAttempts = 100;
    public LevelBoundary Boundary;
    public bool IsDebugEnabled = false;

    public override IEnumerator Execute(GenerationContext context)
    {
        Debug.Log("Begin RoomStamperStep");
        // Register this boundary so the Visualizer can see it
        context.Generator.ActiveBoundary = Boundary;
        context.Data.UnplacedRooms.Sort((a, b) => (b.Size.x * b.Size.y).CompareTo(a.Size.x * a.Size.y));

        var stampedRooms = new List<Room>();

        // Stamp each unplaced room into the level at a random position in the boundary
        foreach (var room in context.Data.UnplacedRooms)
        {
            // Find a valid random position in which the room can fit
            Vector3Int? validPos = FindPosition(room, context);

            // Stamp to the Grid
            if (validPos != null)
            {
                room.Stamp((Vector3Int) validPos, context.Grid);
                context.Data.PlacedRooms.Add(room);
                stampedRooms.Add(room);
            }
        }

        // Remove the stamped rooms from the level data's record of unplaced rooms
        foreach(Room room in stampedRooms)
        {
            context.Data.UnplacedRooms.Remove(room);
        }

        if(IsDebugEnabled)
            context.Generator.NetworkPlayer.Setup(context);

        yield break;
    }

    private Vector3Int? FindPosition(Room room, GenerationContext context)
    {
        for (int i = 0; i < MaxPlacementAttempts; i++)
        {
            // 1. Pick a random coordinate
            int x = Random.Range(0, context.Grid.Width - room.Size.x);
            int z = Random.Range(0, context.Grid.Height - room.Size.y);
            Vector3Int attemptPos = new Vector3Int(x, 0, z);

            // 2. Check if the FOUR CORNERS of the room are inside the boundary
            if (IsRoomInBoundary(attemptPos, room.Size, context.Grid))
            {
                // 3. Check if it overlaps existing rooms
                if (!context.Data.DoesRoomOverlap(attemptPos, room.Size, 2))
                {
                    return attemptPos;
                }
            }
        }
        return null; // Could not find a spot
    }

    private bool IsRoomInBoundary(Vector3Int pos, Vector2Int size, Grid grid)
    {
        if (Boundary == null) return true;

        // We check all four corners of the room for a strict boundary fit
        return Boundary.IsInside(pos.x, pos.z, grid) &&
               Boundary.IsInside(pos.x + size.x, pos.z, grid) &&
               Boundary.IsInside(pos.x, pos.z + size.y, grid) &&
               Boundary.IsInside(pos.x + size.x, pos.z + size.y, grid);
    }
}