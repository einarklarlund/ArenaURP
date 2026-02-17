using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Trace Perimeter")]
public class TracePerimeterAction : WalkerAction
{
    public override bool Perform(Walker walker, GenerationContext context)
    {
        Room myRoom = context.Data.GetRoomAt(walker.GridPosition);
        if (myRoom == null) return false;

        // 1. Check if moving forward stays inside the room
        Vector3Int nextPos = walker.GridPosition + walker.Direction;
        
        // Use the Bounds check directly for precision
        if (!myRoom.GetBounds().Contains(new Vector2Int(nextPos.x, nextPos.z)))
        {
            // 2. If hitting the edge, rotate 90 degrees clockwise
            walker.Direction = new Vector3Int(-walker.Direction.z, 0, walker.Direction.x);
        }

        // 3. Move and place the shell tile
        walker.GridPosition += walker.Direction;
        context.Grid.SetSpace<BuildingWall>(walker.GridPosition);

        return true;
    }
}