using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Room Library Entry")]
public class RoomLibraryEntry : ScriptableObject
{
    public GameObject RoomPrefab; // The designer's visual prefab
    public float CellSize = 3f;
    
    [HideInInspector] // We hide these because they are populated via the Baker
    public List<Vector2Int> FloorOffsets = new List<Vector2Int>();
    [HideInInspector]
    public Vector2Int CalculatedSize;

    // Extensibility: Markers for enemies, loot, etc.
    [System.Serializable]
    public struct MarkerData {
        public string MarkerTag;
        public Vector2Int LocalPos;
    }
    public List<MarkerData> Markers = new List<MarkerData>();
}