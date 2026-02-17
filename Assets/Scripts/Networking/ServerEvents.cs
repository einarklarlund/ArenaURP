using System.Collections.Generic;
using FishNet.Demo.AdditiveScenes;

public struct KillEventData
{
    public Pawn VictimPawn;
    public NetworkPlayer VictimPlayer;
    
    public Pawn AttackerPawn; // Can be null (e.g., fall damage)
    public NetworkPlayer AttackerPlayer; // Can be null
}

public struct PlayerRegisteredData
{
    public NetworkPlayer Player;
}

public struct PlayerUnregisteredData
{
    public NetworkPlayer Player;
}

public struct MatchBeganData
{
    
}
