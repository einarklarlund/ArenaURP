using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Make Room")]
public class MakeRoomAction : WalkerAction
{
    public Vector2Int Size;

    public override bool Perform(Walker walker, GenerationContext context)
    {
        Room room = new Room(Size);
        room.Stamp(walker.GridPosition, context.Grid);

        foreach(var floor in room.MemberTiles)
        {
            floor.StepNumber = context.Generator.WalkerManager.CurrentSimulationStep;
        }

        return true;
    }
}