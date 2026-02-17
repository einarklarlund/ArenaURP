using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Spawn Walker")]
public class SpawnWalkerAction : WalkerAction
{
    public WalkerProfile WalkerProfile; // Drag a Walker prefab here
    public bool InheritDirection = false;

    public override bool Perform(Walker walker, GenerationContext context)
    {
        // Choose the new Walker's direction
        var direction = walker.Direction;
        if (!InheritDirection)
            direction = WalkerHelpers.GetRandomDirection();

        // Create the new Walker object
        Walker newWalker = new Walker(WalkerProfile, walker.GridPosition, direction);

        // Register it so it starts stepping in the next simulation cycle
        context.Generator.WalkerManager.RegisterWalker(newWalker);

        return true;
    }
}