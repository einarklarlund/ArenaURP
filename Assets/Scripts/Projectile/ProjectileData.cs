using FishNet;
using FishNet.Object;
using System;
using FishNet.Managing.Timing;
using UnityEngine;
using FishNet.Connection;
using FishNet.Serializing;

[Serializable]
public struct BulletSpawnState
{
    public string ID;
    public PreciseTick PreciseTick;
    public Vector3 StartDirection;
    public Vector3 StartPosition;

    /// <summary>
    /// The NetworkPlayer who fired this bullet (the controlling player of the
    /// firing pawn). Null for environment damage. Serialized via the FishNet
    /// NetworkBehaviour payload pattern so clients receive the reference on spawn.
    /// </summary>
    public NetworkPlayer Firer;
}

[RequireComponent(typeof(Projectile))]
public class ProjectileData : NetworkBehaviour
{
    [Header("Projectile Settings")]
    public float speed = 50f;
    public float acceleration = 0f;
    public int damage = 10;
    public float lifetime = 5f;
    public LayerMask hitLayers = Physics.AllLayers;
    public bool deflectOtherBullets = false;

    [Header("Capsule Dimensions")]
    public float radius = 0.05f;
    public float height = 0.2f;
    public int directionAxis = 2;

    public BulletSpawnState spawnState;

    public float Lifetime => lifetime;

    private Projectile _projectile;

    private void Awake()
    {
        _projectile = GetComponent<Projectile>();
    }

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WriteString(spawnState.ID);
        writer.WritePreciseTick(spawnState.PreciseTick);
        writer.WriteVector3(spawnState.StartDirection);
        writer.WriteVector3(spawnState.StartPosition);
        writer.WriteNetworkBehaviour(spawnState.Firer);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        var id             = reader.ReadStringAllocated();
        var preciseTick    = reader.ReadPreciseTick();
        var startDirection = reader.ReadVector3();
        var startPosition  = reader.ReadVector3();
        var firer          = reader.ReadNetworkBehaviour() as NetworkPlayer;
        spawnState = new()
        {
            ID             = id,
            PreciseTick    = preciseTick,
            StartDirection = startDirection,
            StartPosition  = startPosition,
            Firer          = firer,
        };
        _projectile.Initialize();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _projectile.Initialize();
    }
}
