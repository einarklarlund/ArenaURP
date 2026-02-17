using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Map Prefabs")]
public class PrefabMapStep : LevelGenerationStep
{
    public List<GameObject> FloorPrefabs;
    public List<GameObject> WallPrefabs;
    public List<GameObject> HallwayPrefabs;
    public List<GameObject> EmptyPrefab; // Optional

    public override IEnumerator Execute(GenerationContext context)
    {
        Debug.Log("Begin PrefabMapStep");
        Dictionary<Room, GameObject> roomToParentObject = new();
        Dictionary<Hallway, GameObject> hallwayToParentObject = new();
        Dictionary<ProceduralRoom, GameObject> procRoomToParentObject = new();
        GameObject procRoomsParent = new GameObject("Procedural Rooms");
        GameObject wallsParent = new GameObject("Walls");
        GameObject roomsParent = new GameObject("Rooms");
        GameObject hallwaysParent = new GameObject("Hallways");
        procRoomsParent.transform.SetParent(context.Generator.transform);
        hallwaysParent.transform.SetParent(context.Generator.transform);
        wallsParent.transform.SetParent(context.Generator.transform);
        roomsParent.transform.SetParent(context.Generator.transform);

        var grid = context.Grid;

        for (int x = 0; x < grid.Width; x++)
        {
            for (int z = 0; z < grid.Height; z++)
            {
                GridSpace space = grid.GridSpaces[x, z];
                Vector3 worldPos = grid.GridToWorld(x, z);
                
                GameObject prefabToSpawn = GetPrefabForSpace(space);

                if (prefabToSpawn != null)
                {
                    GameObject instance = Instantiate(prefabToSpawn, worldPos, Quaternion.identity);

                     // Keep the hierarchy clean by setting a parent for each instantiated gameobject
                    if (space is Floor floor)
                    {
                        if (floor.ParentRoom != null)
                        {
                            // Maintain a map of parent room to parent gameobject
                            if (floor.ParentRoom is Hallway)
                                AddToParentDictionary(floor, instance, hallwayToParentObject, hallwaysParent);
                            else if (floor.ParentRoom is ProceduralRoom)
                                AddToParentDictionary(floor, instance, procRoomToParentObject, procRoomsParent);
                            else
                                AddToParentDictionary(floor, instance, roomToParentObject, roomsParent);
                        }
                    }
                    else if (space is Wall)
                    {
                        instance.transform.SetParent(wallsParent.transform);
                    }
                }
            }
        }
        yield break;
    }

    private void AddToParentDictionary<T>(Floor floor, GameObject prefabInstance, Dictionary<T, GameObject> dictionary, GameObject mainParent) where T : Room
    {
        if (!dictionary.ContainsKey((T) floor.ParentRoom))
        {
            var pos = floor.Grid.GridToWorld(floor.ParentRoom.GetBounds().xMin, floor.ParentRoom.GetBounds().yMin);
            var parentGO = new GameObject($"{typeof(T).FullName} {dictionary.Count}");
            parentGO.transform.position = pos;
            parentGO.transform.SetParent(mainParent.transform);
            dictionary.Add((T) floor.ParentRoom, parentGO);
        }

        // parent each floor to the gameobject that corresponds to its room
        prefabInstance.transform.SetParent(dictionary[(T) floor.ParentRoom].transform);
    }

    private GameObject GetPrefabForSpace(GridSpace space)
    {
        // Simple type checking to choose the prefab
        if (space is Floor floor)
        {
            if(floor.ParentRoom is Hallway)
                return GetRandomFromList(HallwayPrefabs);
            return GetRandomFromList(FloorPrefabs);
        }
        if (space is Wall) return GetRandomFromList(WallPrefabs);
        if (space is Floor) return GetRandomFromList(FloorPrefabs);
        return null; // Don't spawn anything for EmptySpace
    }

    private T GetRandomFromList<T>(List<T> list)
    {
        return list[Random.Range(0, list.Count)];
    }
}