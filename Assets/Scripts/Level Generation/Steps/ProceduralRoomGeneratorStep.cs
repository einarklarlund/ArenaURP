using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Generate Procedural Rooms")]
public class ProceduralRoomGeneratorStep : LevelGenerationStep
{
	[Serializable]
    public struct ProceduralRoomSpawningRule
    {
        public List<WalkerProfile> Profiles;
        public int RoomCount;
        public int FloorsPerRoom;
    }

    public List<ProceduralRoomSpawningRule> RoomSpawningRules;
    public Vector2Int InternalGridSize = new Vector2Int(30, 30);
    public bool IsDebugEnabled = false;

    public override IEnumerator Execute(GenerationContext context)
    {
        Debug.Log("Begin ProceduralRoomGeneratorStep");

        if(RoomSpawningRules.Count == 0)
            throw new Exception("No RoomSpawningRules assigned to ProceduralRoomGeneratorStep");

        foreach(var rule in RoomSpawningRules)
        {
            for(int i = 0; i < rule.RoomCount; i++)
            {
                if(rule.Profiles.Count == 0)
                    throw new Exception("Tried to create a room but no WalkerProfiles were assigned to the RoomSpawningRule.");

                // 1. Create a "Virtual" context for the walker
                Grid virtualGrid = new Grid(InternalGridSize.x, InternalGridSize.y, context.Generator.CellSize);
                LevelData virtualData = new LevelData(); 
                GenerationContext virtualContext = new GenerationContext(virtualGrid, context.Generator, virtualData);

                // 2. Run a specialized walker simulation in this tiny sandbox
                yield return RunInternalWalkers(rule.Profiles, rule.FloorsPerRoom, virtualContext);

                // 3. Package the results into a Room object
                ProceduralRoom newRoom = new ProceduralRoom(virtualGrid);

                // 4. Store in LevelData (but DON'T stamp it on the main grid yet!)
                context.Data.UnplacedRooms.Add(newRoom);
            }
        }
    }

    private IEnumerator RunInternalWalkers(List<WalkerProfile> profiles, int numFloors, GenerationContext virtualContext)
    {
        var manager = virtualContext.Generator.WalkerManager;

        foreach(var profile in profiles)
        {
            var center = new Vector3Int(InternalGridSize.x / 2, 0, InternalGridSize.y / 2);
            var walker = manager.SpawnWalker(profile, center, WalkerHelpers.GetRandomDirection());
        }

        yield return manager.ExecuteSimulation(new ExitAtFloorCount(numFloors), virtualContext, IsDebugEnabled);
    }
}