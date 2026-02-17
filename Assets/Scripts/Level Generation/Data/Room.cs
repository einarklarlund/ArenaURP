

using System;
using System.Collections.Generic;
using UnityEngine;

public class Room
{
    public List<Floor> MemberTiles { get; private set; } = new List<Floor>();
    public List<Room> ConnectedRooms { get; private set; } = new List<Room>();
    public Vector2Int Size { get; protected set; }
    private RectInt? bounds = null;

    public Room() { }

    public Room(Vector2Int size)
    {
        Size = size;
    }

    public RectInt GetBounds()
    {
        if(bounds == null)
            throw new NullReferenceException("Bounds have not been set.");
        
        return (RectInt) bounds;
    }

    public void Stamp(Vector3Int start, Grid grid)
    {
        // Place the room in the grid data structure
        var floors = SetFloors(start, grid);

        foreach(Floor floor in floors)
        {
            floor.ParentRoom = this;
            MemberTiles.Add(floor);
        }

        bounds = new RectInt(start.x, start.z, Size.x, Size.y);
    }

    protected virtual List<Floor> SetFloors(Vector3Int start, Grid grid)
    {
        return grid.SetSpaces<Floor>(start, Size);
    }

    // other values that are fun to consider in terms of rooms and their types, ie. theme, congestion, etc.
}