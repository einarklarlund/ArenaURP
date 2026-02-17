using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Walker Simulation")]
public class WalkerStep : LevelGenerationStep
{

	[Serializable]
    public struct SpawningRule
    {
        public WalkerProfile Profile;
        public int Count;
        public Vector3Int StartOffset;
    }

    public int TargetFloors = 150;
    public List<SpawningRule> InitialWalkers;
    public bool IsDebugEnabled = false;

    public override IEnumerator Execute(GenerationContext context)
    {
        Debug.Log("Begin WalkerStep");
        WalkerManager manager = context.Generator.WalkerManager;
        Vector3Int center = new Vector3Int(context.Grid.Width / 2, 0, context.Grid.Height / 2);

        foreach(var rule in InitialWalkers)
        {
            for (int i = 0; i < rule.Count; i++)
            {
                Vector3Int pos = center + rule.StartOffset;
                manager.SpawnWalker(rule.Profile, pos, WalkerHelpers.GetRandomDirection());
            }
        }
        
        yield return manager.ExecuteSimulation(new ExitAtFloorCount(TargetFloors), context, IsDebugEnabled);
    }
}