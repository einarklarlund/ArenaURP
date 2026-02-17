

using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Conditions/Population Limit")]
public class PopulationCondition : WalkerCondition
{
    public int MaxWalkers = 5;

    public override bool Check(Walker walker, GenerationContext context)
    {
        // Only allow the action if we haven't hit the cap
        return context.Generator.WalkerManager.ActiveWalkers.Count < MaxWalkers;
    }
}