using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Steps/Generate Library Rooms")]
public class GenerateLibraryRoomStep : LevelGenerationStep
{
	[Serializable]
    public struct LibraryRoomSpawningRule
    {
        public RoomLibraryEntry Entry;
        public int RoomCount;
    }

    public List<LibraryRoomSpawningRule> RoomLibrary;

    public override IEnumerator Execute(GenerationContext context)
    {
        foreach(var entry in RoomLibrary)
        {
            for(int i = 0; i < entry.RoomCount; i++)
            {
                LibraryRoom room = new(entry.Entry);
                context.Data.UnplacedRooms.Add(room);
            }
        }

        yield break;
    }
}