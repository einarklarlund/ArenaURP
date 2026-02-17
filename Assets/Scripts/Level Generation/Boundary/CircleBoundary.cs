using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Boundaries/Circle")]
public class CircleBoundary : LevelBoundary
{
    [Range(0.1f, 1f)] public float RadiusPercentage = 0.45f;

    public override bool IsInside(int x, int z, Grid grid)
    {
        Vector2 center = new Vector2(grid.Width / 2f, grid.Height / 2f);
        float radius = Mathf.Min(grid.Width, grid.Height) * RadiusPercentage;
        return Vector2.Distance(new Vector2(x, z), center) < radius;
    }
}