using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Boundaries/Ellipsoid")]
public class EllipsoidBoundary : LevelBoundary
{
    [Range(0.1f, 1f)] public float WidthPercentage = 0.4f;
    [Range(0.1f, 1f)] public float HeightPercentage = 0.7f;

    public override bool IsInside(int x, int z, Grid grid)
    {
        float centerX = grid.Width / 2f;
        float centerZ = grid.Height / 2f;
        float radiusX = grid.Width * WidthPercentage;
        float radiusZ = grid.Height * HeightPercentage;

        // Standard Ellipse Equation: (x-h)^2 / a^2 + (z-k)^2 / b^2 <= 1
        float normalizedX = Mathf.Pow(x - centerX, 2) / Mathf.Pow(radiusX, 2);
        float normalizedZ = Mathf.Pow(z - centerZ, 2) / Mathf.Pow(radiusZ, 2);

        return (normalizedX + normalizedZ) <= 1.0f;
    }
}