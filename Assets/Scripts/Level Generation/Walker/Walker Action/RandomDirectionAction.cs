using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Pick Random Direction")]
public class RandomDirectionAction : WalkerAction
{
    public override bool Perform(Walker walker, GenerationContext context)
    {
        List<Vector3Int> options = new List<Vector3Int>();
        var grid = context.Grid;
        var gridPos = walker.GridPosition;

        if(gridPos.x + 1 < grid.Width)
            options.Add(Vector3Int.right);
        if(gridPos.x - 1 >= 0)
            options.Add(Vector3Int.left);
        if(gridPos.z + 1 < grid.Height)
            options.Add(Vector3Int.forward);
        if(gridPos.z - 1 >= 0)
            options.Add(Vector3Int.back);

        if(options.Count == 0)
            return false;

        walker.Direction = options[UnityEngine.Random.Range(0, options.Count)];
        
        return true;
    }
}