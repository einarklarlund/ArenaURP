// using System.Linq;
// using UnityEngine;

// [CreateAssetMenu(menuName = "Walker/Actions/Lasso Building")]
// public class LassoBuildingAction : WalkerAction
// {
//     public override bool Perform(Walker walker, GenerationContext context)
//     {
//         // 1. Move the walker using its standard movement (e.g., Random or Perlin)
//         // You can call another Action here or just move forward
//         Vector3Int nextPos = walker.GridPosition + walker.Direction;

//         // Check for Overlap (Cycle Detection)
//         if (walker.PathHistory.Contains(nextPos) && walker.PathHistory.Distinct().Count() > 10)
//         {
//             // Check if the path from nextPos to the overlapping space has a length of x
            
//             // We found a loop!
//             SolidifyLoop(walker, nextPos, context);
            
//             // Clear history so we can start a new building or stop
//             walker.PathHistory.Clear();
//             context.Generator.WalkerManager.ActiveWalkers.Remove(walker); 
//             return true;
//         }

//         return false;
//     }

//     private void SolidifyLoop(Walker walker, Vector3Int intersectionPoint, GenerationContext context)
//     {
//         bool startTracing = false;

//         // Trace the history from the beginning
//         foreach (var point in walker.PathHistory)
//         {
//             // Once we hit the point where the walker crossed its own path...
//             if (point == intersectionPoint) startTracing = true;

//             if (startTracing)
//             {
//                 // ...every point from here until the current position becomes a wall
//                 context.Grid.SetSpace<BuildingWall>(point);
//             }
//         }
        
//         // Don't forget to set the final closing point
//         context.Grid.SetSpace<BuildingWall>(walker.GridPosition);
//     }
// }