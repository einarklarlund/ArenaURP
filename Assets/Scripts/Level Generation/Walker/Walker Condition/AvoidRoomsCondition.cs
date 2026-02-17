using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Conditions/Avoid Rooms")]
public class ConditionAvoidRooms : WalkerCondition
{
    public int LookAheadDistance = 2;

    public override bool Check(Walker walker, GenerationContext context)
    {
        // Calculate the position the walker is about to move into
        Vector3Int targetPos = walker.GridPosition + (walker.Direction * LookAheadDistance);

        // Return true if the target position is NOT in a room
        return !context.Data.IsPositionInsideAnyRoom(targetPos);
    }
}