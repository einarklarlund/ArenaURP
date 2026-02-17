using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Boundaries/Rectangle")]
public class RectangleBoundary : LevelBoundary
{
    [Range(0.1f, 1f)] public float WidthPercentage = 0.8f;
    [Range(0.1f, 1f)] public float HeightPercentage = 0.3f;

    public override bool IsInside(int x, int z, Grid grid)
    {
        float centerX = grid.Width / 2f;
        float centerZ = grid.Height / 2f;
        
        float halfW = (grid.Width * WidthPercentage) / 2f;
        float halfH = (grid.Height * HeightPercentage) / 2f;

        return x >= centerX - halfW && x <= centerX + halfW &&
               z >= centerZ - halfH && z <= centerZ + halfH;
    }
}