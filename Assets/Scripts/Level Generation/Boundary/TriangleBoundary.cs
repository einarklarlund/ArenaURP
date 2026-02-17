using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Boundaries/Triangle")]
public class TriangleBoundary : LevelBoundary
{
    [Range(0.1f, 1f)] public float SizePercentage = 0.8f;

    public override bool IsInside(int x, int z, Grid grid)
    {
        float size = Mathf.Min(grid.Width, grid.Height) * SizePercentage;
        Vector2 center = new Vector2(grid.Width / 2f, grid.Height / 2f);

        // Define the three vertices of the triangle
        float height = size * Mathf.Sqrt(3) / 2f;
        Vector2 p1 = center + new Vector2(0, height / 2f);
        Vector2 p2 = center + new Vector2(-size / 2f, -height / 2f);
        Vector2 p3 = center + new Vector2(size / 2f, -height / 2f);

        // Helper function: Cross product check to see if point is on one side of a line
        return IsPointInTriangle(new Vector2(x, z), p1, p2, p3);
    }

    private bool IsPointInTriangle(Vector2 p, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        float d1, d2, d3;
        bool has_neg, has_pos;

        d1 = Sign(p, p1, p2);
        d2 = Sign(p, p2, p3);
        d3 = Sign(p, p3, p1);

        has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(has_neg && has_pos);
    }

    private float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}