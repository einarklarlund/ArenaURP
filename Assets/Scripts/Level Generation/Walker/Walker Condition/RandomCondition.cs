using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Conditions/Random Chance")]
public class RandomCondition : WalkerCondition
{
    public float Chance;

    public override bool Check(Walker walker, GenerationContext context)
    {
        return Random.value < Chance;
    }
}