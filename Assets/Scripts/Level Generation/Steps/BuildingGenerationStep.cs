using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Generate Buildings")]
public class BuildingGeneratorStep : LevelGenerationStep
{
    public int MinRoomArea = 100; // e.g., 10x10
    public WalkerProfile ExteriorWalkerProfile;
    public WalkerProfile InteriorWalkerProfile;
    public bool IsDebugEnabled;

    public override IEnumerator Execute(GenerationContext context)
    {
        Debug.Log("Being BuildingGeneratorStep");
        foreach (var room in context.Data.PlacedRooms)
        {
            // 1. Threshold Check
            if (room.GetBounds().width * room.GetBounds().height < MinRoomArea) continue;

            // 2. Build the Exterior Shell
            yield return GenerateShell(room, context);

            // 3. Build Interior Rooms
            yield return GenerateInteriors(room, context);

            // 4. Punch Entrances
            CreateEntrances(room, context);
        }

        if(IsDebugEnabled)
            context.Generator.NetworkPlayer.Setup(context);
    }

    private IEnumerator GenerateShell(Room room, GenerationContext context)
    {
        WalkerManager manager = context.Generator.WalkerManager;

        // Spawn walker at the edge of the room
        Vector3Int startPos = new Vector3Int
        (
            room.GetBounds().xMin + Random.Range(1, room.GetBounds().size.x),
            0,
            room.GetBounds().yMin + Random.Range(1, room.GetBounds().size.y)
        );
        manager.SpawnWalker(ExteriorWalkerProfile, startPos, Vector3Int.right);
        
        // We run a specialized exit condition: "Until the perimeter is roughly traced"
        // For simplicity, let's run it for a set number of steps or until it returns to start
        yield return manager.ExecuteSimulation
        (
            new ExitAfterSteps(50, context.Generator.WalkerManager.CurrentSimulationStep),
            context,
            IsDebugEnabled
        );
    }

    private IEnumerator GenerateInteriors(Room room, GenerationContext context)
    {
        WalkerManager manager = context.Generator.WalkerManager;
        // Spawn walkers in the center to build "dividers"
        Vector3Int center = new Vector3Int((int)room.GetBounds().center.x, 0, (int)room.GetBounds().center.y);
        manager.SpawnWalker(InteriorWalkerProfile, center, Vector3Int.forward);
        
        yield return manager.ExecuteSimulation
        (
            new ExitAfterSteps(50, context.Generator.WalkerManager.CurrentSimulationStep),
            context,
            IsDebugEnabled
        );
    }

    private void CreateEntrances(Room room, GenerationContext context)
    {
        // Simple Logic: Find a BuildingWall tile that touches a Floor tile and replace it with Floor
        // This acts as a "Doorway"
        for (int x = room.GetBounds().xMin; x < room.GetBounds().xMax; x++)
        {
            for (int z = room.GetBounds().yMin; z < room.GetBounds().yMax; z++)
            {
                Vector3Int pos = new Vector3Int(x, 0, z);
                if (context.Grid.GridSpaces[x, z] is BuildingWall or InteriorWall)
                {
                    // If neighbor is floor, 10% chance to punch a door
                    if (WalkerHelpers.HasNeighboringFloor(x, z, context.Grid) && Random.value < 0.1f)
                    {
                        context.Grid.SetSpace<Floor>(pos);
                    }
                }
            }
        }
    }
    
}