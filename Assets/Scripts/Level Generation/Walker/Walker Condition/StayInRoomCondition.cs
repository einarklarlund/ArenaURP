using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Conditions/Stay In Room")]
public class ConditionStayInRoom : WalkerCondition
{
    public int LookAheadDistance = 2;

    public override bool Check(Walker walker, GenerationContext context)
    {
        Vector3Int currentPos = walker.GridPosition;
        Vector3Int targetPos = walker.GridPosition + walker.Direction * LookAheadDistance;

        // 1. Identify the room we are currently standing in
        Room currentRoom = context.Data.GetRoomAt(currentPos);
        
        // 2. Identify the room we are trying to move into
        Room targetRoom = context.Data.GetRoomAt(targetPos);

        // 3. Logic: We can only move if we stay within the same room instance
        // This handles the "null" case too (if you start outside a room, 
        // you must stay outside)
        return currentRoom == targetRoom && currentRoom != null;
    }
}