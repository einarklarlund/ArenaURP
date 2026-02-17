using UnityEngine;

public abstract class LevelBoundary : ScriptableObject
{
    // Returns true if the coordinate is allowed
    public abstract bool IsInside(int x, int z, Grid grid);
}
