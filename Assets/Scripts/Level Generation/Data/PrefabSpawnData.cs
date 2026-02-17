using UnityEngine;

public class PrefabSpawnData
{
    public Vector2Int Position { get; private set; }

    public PrefabSpawnData(Vector2Int position)
    {
        Position = position;
    }
}