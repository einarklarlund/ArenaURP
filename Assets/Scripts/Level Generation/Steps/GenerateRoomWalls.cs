using System.Collections;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Generate Room Walls")]
public class GenerateRoomWalls : LevelGenerationStep
{
    [Range(0, 1)]
    public float WallGenerationChance = 0.25f;


    public override IEnumerator Execute(GenerationContext context)
    {
        Debug.Log("Begin WallGenerationStep");
        var grid = context.Grid;
        // We iterate through every tile to find empty spots next to floors
        for (int x = 0; x < grid.Width; x++)
        {
            for (int z = 0; z < grid.Height; z++)
            {
                // We only care about EmptySpaces
                if (grid.GridSpaces[x, z] is EmptySpace)
                {
                    // Only place a wall if the empty space is neighboring a floor that isn't a hallway
                    if (WalkerHelpers.HasNeighboringFloor(x, z, grid, false)
                        && WalkerHelpers.GetNeighboringHallwayTile(x, z, grid, false) == null
                        && Random.value < WallGenerationChance)
                    {
                        // Convert this EmptySpace into a wall
                        grid.SetSpace<Wall>(new Vector3Int(x, 0, z));
                    }
                }
            }
        }

        yield break;
    }
}