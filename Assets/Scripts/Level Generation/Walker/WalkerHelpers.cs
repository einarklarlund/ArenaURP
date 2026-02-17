using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class WalkerHelpers
{
    public static Vector3Int GetRandomDirection()
    {
        Vector3Int[] dirs = { Vector3Int.forward, Vector3Int.back, Vector3Int.left, Vector3Int.right };
        return dirs[UnityEngine.Random.Range(0, dirs.Length)];
    }

    public static List<GridSpace> GetSurroundingSpaces(int x, int z, Grid grid, bool includeDiagonals = false)
    {
        List<GridSpace> neighbors = new();
        // Check all 8 neighbors (including diagonals)
        for (int nx = -1; nx <= 1; nx++)
        {
            for (int nz = -1; nz <= 1; nz++)
            {
                if (nx == 0 && nz == 0) continue;
                if (!includeDiagonals && Mathf.Abs(nx) + Math.Abs(nz) == 2)
                    continue;

                Vector3Int checkPos = new Vector3Int(x + nx, 0, z + nz);
                if (grid.ContainsPosition(checkPos))
                {
                    neighbors.Add(grid.GridSpaces[checkPos.x, checkPos.z]);
                }
            }
        }
        return neighbors;
    }

    public static List<GridSpace> GetSurroundingSpaces(GridSpace gridSpace, bool includeDiagonals)
    {
        Vector3Int pos = gridSpace.GridPosition;
        return GetSurroundingSpaces(pos.x, pos.z, gridSpace.Grid, includeDiagonals);
    }

    public static List<Floor> GetNeighboringFloors(GridSpace gridSpace, bool includeDiagonals = false)
    {
        Vector3Int pos = gridSpace.GridPosition;
        return GetNeighboringFloors(pos.x, pos.z, gridSpace.Grid, includeDiagonals);
    }

    public static List<Floor> GetNeighboringFloors(int x, int z, Grid grid, bool includeDiagonals = false)
    {
        return GetSurroundingSpaces(x, z, grid, includeDiagonals)
            .Where(space => space is Floor)
            .Cast<Floor>()
            .ToList();
    }

    public static List<EmptySpace> GetNeighboringEmptySpaces(GridSpace gridSpace, bool includeDiagonals = false)
    {
        return GetSurroundingSpaces(gridSpace, includeDiagonals)
            .Where(space => space is EmptySpace)
            .Cast<EmptySpace>()
            .ToList();
    }

    public static bool HasNeighboringFloor(int x, int z, Grid grid, bool includeDiagonals = false)
    {
        return GetNeighboringFloors(x, z, grid, includeDiagonals).Count > 0;
    }

    public static Floor GetNeighboringHallwayTile(int x, int z, Grid grid, bool includeDiagonals)
    {
        var neighboringFloors = GetNeighboringFloors(x, z, grid, includeDiagonals);
        return neighboringFloors.Find(f => f.ParentRoom is Hallway);
    }
}