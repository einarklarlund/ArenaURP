using UnityEngine;
using System.Collections;
using System.Linq;

[CreateAssetMenu(menuName = "Generation/Steps/Spawn Player")]
public class PlacePlayerStep : LevelGenerationStep
{
    public NetworkPlayer PlayerPrefab;
    public bool PlacePlayer = true;

    public override IEnumerator Execute(GenerationContext context)
    {
        if(!PlacePlayer || !Application.isPlaying)
            yield break;

        // find the top-most room
        var rooms = context.Data.PlacedRooms;
        Room topMostRoom = context.Data.PlacedRooms.First();
        foreach(var room in rooms)
        {
            if(topMostRoom.GetBounds().max.y > topMostRoom.GetBounds().max.y)
                topMostRoom = room;
        }

        // find the top-most floor in that room
        var topMostTile = topMostRoom.MemberTiles.First();
        foreach(var tile in topMostRoom.MemberTiles)
        {
            if(tile.GridPosition.y > topMostTile.GridPosition.y)
                topMostTile = tile;
        }

        // spawn the player in the top-most floor of the top-most room
        var spawnPos = context.Grid.GridToWorld(topMostTile.GridPosition);
        Instantiate(PlayerPrefab, spawnPos, Quaternion.LookRotation(Vector3.back));

        yield break;
    }
}