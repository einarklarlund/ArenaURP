using UnityEngine;
using System.Collections;
using System.Linq;

[CreateAssetMenu(menuName = "Generation/Steps/Build Hallway Walls")]
public class PlaceHallwayWallsStep : LevelGenerationStep
{
    public GameObject HallwayWallPrefab;

    public override IEnumerator Execute(GenerationContext context)
    {
        Debug.Log("Begin PlaceHallwaysStep");
        var parent = new GameObject("Hallway Walls");
        parent.transform.SetParent(context.Generator.transform);

        foreach(Hallway hallway in context.Data.Hallways)
        {
            foreach(var tile in hallway.MemberTiles)
            {
                PlaceWallsAround(tile, parent);
            }
        }

        yield return null;
    }

    void PlaceWallsAround(Floor floor, GameObject parentGameObject)
    {
        var emptyNeighbors = WalkerHelpers.GetNeighboringEmptySpaces(floor, false);

        // place walls in any empty space around the floor
        foreach(var emptySpace in emptyNeighbors)
        {
            // for any empty space near the floor, put a wall near any of the neighboring floors
            var spacesToWallOff = WalkerHelpers.GetNeighboringFloors(emptySpace, false);

            foreach(var space in spacesToWallOff)
            {
                // spawn the wall at the empty space's position, facing the space that we want to wall off
                var walledSpacePos = floor.Grid.GridToWorld(space.GridPosition);
                var spawnPos = floor.Grid.GridToWorld(emptySpace.GridPosition);
                var rotation = Quaternion.LookRotation(walledSpacePos - spawnPos);
                var go = Instantiate(HallwayWallPrefab, spawnPos, rotation);
                go.transform.SetParent(parentGameObject.transform);
            }
        }
    }
}