using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Make Floor")]
public class MakeFloorAction : WalkerAction
{
    public int TileSize = 1;

    public override bool Perform(Walker walker, GenerationContext context)
    {
        var floors = context.Grid.SetSpaces<Floor>(walker.GridPosition, new Vector2Int(TileSize, TileSize));
        
        if(floors.Count == 0)
            return false;

        foreach(Floor floor in floors)
        {
            floor.StepNumber = context.Generator.WalkerManager.CurrentSimulationStep;        
        }

        return true;
    }
}