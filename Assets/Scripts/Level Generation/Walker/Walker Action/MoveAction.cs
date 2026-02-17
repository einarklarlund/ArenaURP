using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Move")]
public class MoveAction : WalkerAction
{
    public int Distance = 1;

    public override bool Perform(Walker walker, GenerationContext context)
    {
        if(walker.Direction == Vector3Int.zero)
        {
            Debug.LogWarning("Walker tried to perform MoveAction with no direction");
            return false;
        }

        var nextPos = walker.GridPosition + walker.Direction * Distance;
        var grid = context.Grid;

        // reverse the walker's direction if it's about to go out of bounds
        if(nextPos.x >= grid.Width 
            || nextPos.x < 0
            || nextPos.z >= grid.Height
            || nextPos.z < 0)
        {
            walker.Direction *= -1;
        }

        walker.GridPosition += walker.Direction;

        return true;
    }
}