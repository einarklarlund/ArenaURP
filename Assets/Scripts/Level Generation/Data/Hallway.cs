using System.Collections.Generic;
using UnityEngine;

public class Hallway : Room
{
    public Vector3Int StartPoint { get; private set; }
    public Vector3Int EndPoint { get; private set; }

    public Hallway(Vector3Int start, Vector3Int end)
    {
        StartPoint = start;
        EndPoint = end;

        // Calculate the Size based on the span of the L-shape
        int width = Mathf.Abs(end.x - start.x) + 1;
        int height = Mathf.Abs(end.z - start.z) + 1;
        Size = new Vector2Int(width, height);
    }

    /// <summary>
    /// Overrides the standard rectangle fill to draw the L-shape path instead.
    /// </summary>
    protected override List<Floor> SetFloors(Vector3Int start, Grid grid)
    {
        List<Floor> hallwayFloors = new List<Floor>();

        // We use StartPoint and EndPoint directly for the path logic.
        // Horizontal Leg (X)
        int xDir = (int)Mathf.Sign(EndPoint.x - StartPoint.x);
        // If xDir is 0 (rooms aligned vertically), the loop won't execute or handles it.
        for (int x = StartPoint.x; x != EndPoint.x + (xDir == 0 ? 1 : xDir); x += (xDir == 0 ? 1 : xDir))
        {
            Vector3Int pos = new Vector3Int(x, 0, StartPoint.z);
            AddFloorAt(pos, grid, hallwayFloors);
            if (xDir == 0) break; 
        }

        // Vertical Leg (Z)
        int zDir = (int)Mathf.Sign(EndPoint.z - StartPoint.z);
        for (int z = StartPoint.z; z != EndPoint.z + (zDir == 0 ? 1 : zDir); z += (zDir == 0 ? 1 : zDir))
        {
            // We start from EndPoint.x to complete the L-shape corner
            Vector3Int pos = new Vector3Int(EndPoint.x, 0, z);
            AddFloorAt(pos, grid, hallwayFloors);
            if (zDir == 0) break;
        }

        return hallwayFloors;
    }

    private void AddFloorAt(Vector3Int pos, Grid grid, List<Floor> floorList)
    {
        // Check if there is already a floor here to avoid redundant instances
        // and avoid replacing your carefully crafted ProceduralRooms
        if (grid.GridSpaces[pos.x, pos.z] is Floor existingFloor)
        {
            // floorList.Add(existingFloor);
            return;
        }

        // Otherwise, create a new floor tile via the grid
        Floor newFloor = grid.SetSpace<Floor>(pos);
        if (newFloor != null)
        {
            floorList.Add(newFloor);
        }
    }
}