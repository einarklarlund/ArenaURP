using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Conditions/Room Overlap Check")]
public class NoRoomOverlapCondition : WalkerCondition
{
    public Vector2Int RoomSize;
    public int Padding = 1;

    public override bool Check(Walker walker, GenerationContext context)
    {
        // Ask the Data layer if the space is clear
        bool isOverlap = context.Data.DoesRoomOverlap(
            walker.GridPosition, 
            RoomSize, 
            Padding
        );

        // We want to return TRUE if there is NO overlap
        return !isOverlap;
    }
}