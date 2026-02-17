using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Spawn Twin")]
public class SpawnTwinAction : WalkerAction
{
    public override bool Perform(Walker walker, GenerationContext context)
    {
        // Clone the current walker logic but give it a new direction
        Walker newWalker = new Walker(walker.Profile, walker.GridPosition, walker.Direction); 
        context.Generator.WalkerManager.RegisterWalker(newWalker);

        return true;
    }
}