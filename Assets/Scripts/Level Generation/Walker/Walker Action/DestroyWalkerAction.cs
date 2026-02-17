using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Destroy Walker")]
public class DestroyWalkerAction : WalkerAction
{
    public override bool Perform(Walker walker, GenerationContext context)
    {
        // Simply tell the manager to unregister this instance
        context.Generator.WalkerManager.UnregisterWalker(walker);
        
        return true;
        // Optional: Leave a 'marker' or 'dead end' room here
        // context.Data.AddRoom(walker.GridPosition, new Vector2Int(1,1), context.Grid);
    }
}