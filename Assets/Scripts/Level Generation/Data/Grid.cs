using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Grid
{
    public GridSpace[,] GridSpaces;

    public List<Floor> Floors => GridSpaces.Cast<GridSpace>().ToList().Where(gs => gs is Floor).Select(gs => gs as Floor).ToList();
    public List<Wall> Walls => GridSpaces.Cast<GridSpace>().ToList().Where(gs => gs is Wall).Select(gs => gs as Wall).ToList();
    public List<EmptySpace> EmptySpaces => GridSpaces.Cast<GridSpace>().ToList().Where(gs => gs is EmptySpace).Select(gs => gs as EmptySpace).ToList();
    public List<LevelEdge> LevelEdges => GridSpaces.Cast<GridSpace>().ToList().Where(gs => gs is LevelEdge).Select(gs => gs as LevelEdge).ToList();

    public int Width { get; private set; }
    public int Height { get; private set; }
    public float CellSize { get; private set; }
    public int MaxX { get; private set; }
    public int MaxZ { get; private set; }
    public int MinX { get; private set; }
    public int MinZ { get; private set; }

    public event Action<Vector3Int> OnGridChanged;

    public Grid(int width, int height, float cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        GridSpaces = new GridSpace[width, height];
        MaxX = int.MinValue;
        MaxZ = int.MinValue;
        MinX = int.MaxValue;
        MinZ = int.MaxValue;
        
        // Initialize with EmptySpace by default
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                SetSpace<EmptySpace>(new Vector3Int(x,0,y));
    }

    // The "Source of Truth" for coordinate conversion
    public Vector3 GridToWorld(int x, int z)
    {
        float xPos = x * CellSize + (CellSize / 2f);
        float zPos = z * CellSize + (CellSize / 2f);
        return new Vector3(xPos, 0, zPos);
    }
    
    public Vector3 GridToWorld(Vector3Int gridPos) 
        => GridToWorld(gridPos.x, gridPos.z);

    public bool ContainsPosition(Vector3Int pos)
    {
        return pos.x >= 0 && pos.x < Width && pos.z >= 0 && pos.z < Height;
    }

    public List<T> SetSpaces<T>(Vector3Int start, Vector2Int size) where T : GridSpace, new()
    {
        // Return nothing if the spaces cannot fit
        Vector3Int max = new(start.x + size.x, 0, start.z + size.y);
        if (!ContainsPosition(max))
        {
            return new List<T>();
        }

        var spaces = new List<T>();
        for (int x = start.x; x < start.x + size.x; x++)
        {
            for (int z = start.z; z < start.z + size.y; z++)
            {
                Vector3Int pos = new Vector3Int(x, 0, z);
                T space = SetSpace<T>(pos);
                spaces.Add(space);
            }
        }
        return spaces;
    }

    public T SetSpace<T>(Vector3Int pos) where T : GridSpace, new()
    {
        if (pos.x < 0 || pos.x >= Width || pos.z < 0 || pos.z >= Height) 
            throw new IndexOutOfRangeException($"Tried to set a space at {pos.x}, {pos.z}, which is out of bounds.");

        if(GridSpaces[pos.x, pos.z] is T)
        {
            OnGridChanged?.Invoke(pos);
            return GridSpaces[pos.x, pos.z] as T;
        }

        T newSpace = new T();
        
        newSpace.Grid = this;
        newSpace.GridPosition = pos;

        GridSpaces[pos.x, pos.z] = newSpace;

        if(newSpace is not EmptySpace)
        {
            MaxX = Mathf.Max(MaxX, pos.x);
            MaxZ = Mathf.Max(MaxZ, pos.z);
            MinX = Mathf.Min(MinX, pos.x);
            MinZ = Mathf.Min(MinZ, pos.z);
        }

        OnGridChanged?.Invoke(pos);
        return newSpace;
    }
}