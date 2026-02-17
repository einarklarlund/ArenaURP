

using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Conditions/Position Based Random")]
public class PositionCondition : WalkerCondition
{
    public Vector3Int Origin;
    public float MinDistanceFromOrigin;
    public float MaxDistanceFromOrigin;
    public float ChanceAtMin;
    public float ChanceAtMax;

    public override bool Check(Walker walker, GenerationContext context)
    {
        // ensure that distance between origin and walker is within range
        float distanceFromOrigin = Vector3Int.Distance(walker.GridPosition, Origin);
        if(distanceFromOrigin < MinDistanceFromOrigin || distanceFromOrigin > MaxDistanceFromOrigin)
        {
            return false;
        }
        
        // calculate the displacement of the walker within the range
        float range = MaxDistanceFromOrigin - MinDistanceFromOrigin;
        float displacement = distanceFromOrigin - MinDistanceFromOrigin;

        // calculate final chance threshold
        float chanceThreshold = Mathf.Lerp(ChanceAtMin, ChanceAtMax, displacement / range);
        return UnityEngine.Random.value < chanceThreshold;
    }
}