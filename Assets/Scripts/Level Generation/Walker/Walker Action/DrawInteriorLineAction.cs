using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Draw Interior Line")]
public class DrawActionInteriorLine : WalkerAction
{
    public override bool Perform(Walker walker, GenerationContext context)
    {
        // Move forward
        walker.GridPosition += walker.Direction;

        // Check what we hit
        GridSpace currentSpace = context.Grid.GridSpaces[walker.GridPosition.x, walker.GridPosition.z];

        if (currentSpace is BuildingWall)
        {
            // We hit the shell! Time to stop or turn.
            // We can mark this walker as "dead" by removing it from the Manager
            context.Generator.WalkerManager.ActiveWalkers.Remove(walker);
            return false;
        }

        // Otherwise, place an interior wall
        context.Grid.SetSpace<InteriorWall>(walker.GridPosition);
        return true;
    }
}