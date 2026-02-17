using System.Collections.Generic;
using UnityEngine;

public class GridSpace
{
    public Vector3Int GridPosition;
    public List<GridSpace> SurroundingSpaces;
    public Grid Grid;
}

public class Floor : GridSpace
{
    public int StepNumber;
    public float Openness;
    public Color DebugColor;
    public Room ParentRoom;
    public bool IsEdgeOfRoom;
}

public class Wall : GridSpace { }
public class EmptySpace : GridSpace { }
public class LevelEdge : GridSpace { }
