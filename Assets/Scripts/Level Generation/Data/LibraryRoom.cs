using System.Collections.Generic;
using UnityEngine;

public class LibraryRoom : Room
{
    private RoomLibraryEntry data;

    public LibraryRoom(RoomLibraryEntry data) : base(data.CalculatedSize)
    {
        this.data = data;
    }

    // We override SetFloors to place tiles based on the baked offsets
    protected override List<Floor> SetFloors(Vector3Int start, Grid grid)
    {
        List<Floor> placedFloors = new List<Floor>();

        foreach (Vector2Int offset in data.FloorOffsets)
        {
            Vector3Int targetPos = start + new Vector3Int(offset.x, 0, offset.y);
            
            // Use the standard grid placement
            Floor floor = grid.SetSpace<Floor>(targetPos);
            if (floor != null)
            {
                placedFloors.Add(floor);
            }
        }

        return placedFloors;
    }
    
    public GameObject GetPrefab() => data.RoomPrefab;
}