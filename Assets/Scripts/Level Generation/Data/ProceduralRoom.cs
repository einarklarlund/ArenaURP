using System.Collections.Generic;
using UnityEngine;

public class ProceduralRoom : Room
{
    // Local data representing the shape the walker made
    public Grid LocalLayout; 
    public bool HasBeenPlaced = false;

    public ProceduralRoom(Grid localLayout)
    {
        LocalLayout = localLayout;
        
        // set size of the room according to the min and max non-empty spaces in the grid
        Size = new Vector2Int(LocalLayout.MaxX - LocalLayout.MinX, LocalLayout.MaxZ - LocalLayout.MinZ);
    }

    protected override List<Floor> SetFloors(Vector3Int start, Grid masterGrid)
    {
        List<Floor> floors = new List<Floor>();

        for (int x = 0; x < Size.x; x++)
        {
            for (int z = 0; z < Size.y; z++)
            {
                var space = LocalLayout.GridSpaces[x + LocalLayout.MinX, z + LocalLayout.MinZ];
                if (space is Floor)
                {
                    Vector3Int worldPos = new Vector3Int(start.x + x, 0, start.z + z);
                    Floor masterGridFloor = masterGrid.SetSpace<Floor>(worldPos);
                    floors.Add(masterGridFloor);
                }
            }
        }

        return floors;
    }
}