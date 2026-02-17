using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Place Room with Retry")]
public class PlaceRoomRetryAction : WalkerAction
{
    public Vector2Int RoomSize;
    public int MaxRetries = 5;
    public int OffsetDistance = 2;

    public override bool Perform(Walker walker, GenerationContext context)
    {
        bool placed = false;
        Vector3Int currentPos = walker.GridPosition;

        for (int i = 0; i < MaxRetries; i++)
        {
            // Check if we can place here
            if (!context.Data.DoesRoomOverlap(currentPos, RoomSize, 1))
            {
                Room room = new Room(RoomSize);
                room.Stamp(currentPos, context.Grid);
                placed = true;
                break;
            }

            // Retry logic: Move the attempt position slightly
            // You could also randomize this or rotate the RoomSize dimensions
            currentPos += new Vector3Int(
                UnityEngine.Random.Range(-OffsetDistance, OffsetDistance + 1),
                0,
                UnityEngine.Random.Range(-OffsetDistance, OffsetDistance + 1)
            );
        }

        return placed;
    }
}