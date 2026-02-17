using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GridNetworkPlayer : MonoBehaviour
{
    public Camera DebugCamera;

    [Header("Settings")]
    public Color NewFloorColor = Color.cyan;
    public Color OldFloorColor = new Color(0.2f, 0.2f, 0.2f, 1.0f); // Dark Gray
    public int FadingSteps = 10; // How many steps until it reaches OldFloorColor

    private bool isDirty = true;
    private GenerationContext context;
    private LevelBoundary activeBoundary;

    // Since OnDrawGizmos runs every frame in the Editor, 
    // we use a "Cache" to store the visual data.
    private List<DebugTileData> cachedTiles = new List<DebugTileData>();

    // Simple struct to hold cache data
    public struct DebugTileData
    {
        public Vector3 WorldPos;
        public GridSpace Space;
        public Vector3 Size;
    }

    public void Setup(GenerationContext context)
    {
        this.context = context;

        // Subscribe to the grid's change event
        context.Grid.OnGridChanged += OnGridChanged;
        OnGridChanged(Vector3Int.zero);
        RebuildCache();
    }
    
    private void OnGridChanged(Vector3Int pos)
    {
        isDirty = true;

		// move camera to center of grid bounds
        var max = context.Grid.GridToWorld(new Vector3Int(context.Grid.MaxX, 0, context.Grid.MaxZ));
        var min = context.Grid.GridToWorld(new Vector3Int(context.Grid.MinX, 0, context.Grid.MinZ));
		Vector3 center = (max + min) / 2;
		DebugCamera.transform.position = center + Vector3.up * 50;
		DebugCamera.transform.rotation = Quaternion.LookRotation(Vector3.down);

        // include the new position in the camera
        IncludeInCameraView(context.Grid.GridToWorld(pos));
    }

    private void OnDrawGizmos()
    {
        if (context == null || context.Grid == null) return;


        // Accessing the registered boundary
        activeBoundary = context.Generator.ActiveBoundary;

        // Only rebuild the cache if the grid has changed
        if (isDirty)
        {
            RebuildCache();
            isDirty = false;
        }

        Grid grid = context.Grid;

        foreach (var tile in cachedTiles)
        {
            Gizmos.color = GetSpaceColor(tile.Space, context.Generator.WalkerManager.CurrentSimulationStep);
            Gizmos.DrawCube(tile.WorldPos, tile.Size);
        }

        // Draw active walkers as spheres
        Gizmos.color = Color.red;
        foreach (var walker in context.Generator.WalkerManager.ActiveWalkers)
        {
            Gizmos.DrawSphere(grid.GridToWorld(walker.GridPosition), grid.CellSize * 0.4f);

            IncludeInCameraView(grid.GridToWorld(walker.GridPosition));
        }
    }

    private void RebuildCache()
    {
        cachedTiles.Clear();
        var grid = context.Grid;

        for (int x = 0; x < grid.Width; x++)
        {
            for (int z = 0; z < grid.Height; z++)
            {
                var space = grid.GridSpaces[x, z];
                if (space == null || space is EmptySpace) continue;

                cachedTiles.Add(new DebugTileData {
                    WorldPos = grid.GridToWorld(x, z),
                    Space = space,
                    Size = Vector3.one * (grid.CellSize * 0.85f)
                });
            }
        }
    }

    private void OnDestroy()
    {
        // Important: Clean up the subscription to prevent memory leaks
        if (context != null && context.Grid != null)
            context.Grid.OnGridChanged -= OnGridChanged;
    }

    private void IncludeInCameraView(Vector3 worldPos)
    {
        int iteration = 0;

        // Zoom out the camera until the floor is visible
        while
        (
        	DebugCamera.WorldToViewportPoint(worldPos).x > 1
        	|| DebugCamera.WorldToViewportPoint(worldPos).y > 1
        	|| DebugCamera.WorldToViewportPoint(worldPos).x < 0
        	|| DebugCamera.WorldToViewportPoint(worldPos).y < 0
        )
        {        
        	DebugCamera.orthographicSize *= 1.15f;
        	if(iteration++ > 20)
            {
        		Debug.LogError("Can't find Floor in camera");
                break;
            }
        }
    }

    private Color GetSpaceColor(GridSpace space, int currentStep)
    {
        // If the tile is outside the boundary, make it look "darker" or red
        if (activeBoundary != null && !activeBoundary.IsInside(space.GridPosition.x, space.GridPosition.z, context.Grid))
        {
            return Color.red * 0.2f; // Dim red
        }
        
        if (space is Floor floor)
        {
            if(floor.ParentRoom is Hallway hallway)
            {
                if (floor.GridPosition == hallway.StartPoint)
                    return Color.red;
                if (floor.GridPosition == hallway.EndPoint)
                    return Color.yellow;
                return Color.Lerp(Color.yellow, Color.red, 0.5f);
            }

            // Calculate age: how many steps ago was this created?
            int age = currentStep - floor.StepNumber;
            
            // Convert age to a 0.0 to 1.0 percentage
            float t = Mathf.Clamp01((float)age / FadingSteps);
            
            // Blend from bright to dark
            return Color.Lerp(NewFloorColor, OldFloorColor, t);
        }

        if (space is Wall) return Color.black;
        if (space is LevelEdge) return Color.red;

        return Color.gray;
    }
}