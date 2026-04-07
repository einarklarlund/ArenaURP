using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using FishNet.Managing.Timing;
using FishNet;
using System;

[RequireComponent(typeof(ProjectileLifecycle))]
public class ProjectileState : NetworkBehaviour
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
    public int Health;

    private ProjectileLifecycle _projectile;
    private bool isPayloadRead = false;

    private void Awake()
    {
        _projectile = GetComponent<ProjectileLifecycle>();
    }

    private void OnDisable()
    {
        isPayloadRead = false;
    }

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WriteString(ID);
        writer.WritePreciseTick(PreciseTick);
        writer.WriteVector3(StartDirection);
        writer.WriteVector3(StartPosition);
        writer.WriteNetworkBehaviour(Firer);
        writer.WriteInt32(Health);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        ID             = reader.ReadStringAllocated();
        PreciseTick    = reader.ReadPreciseTick();
        StartDirection = reader.ReadVector3();
        StartPosition  = reader.ReadVector3();
        Firer          = reader.ReadNetworkBehaviour() as NetworkPlayer;
        Health         = reader.ReadInt32();

        isPayloadRead = true;
        _projectile.Initialize();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _projectile.Initialize();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner || isPayloadRead)
            return;

        // Predict bullet state
        ID             = Guid.NewGuid().ToString();
        PreciseTick    = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
        StartDirection = transform.forward;
        StartPosition  = transform.position;
        Firer          = NetworkPlayer.LocalInstance;
        Health         = GetComponent<ProjectileData>().MaxHealth;

        _projectile.Initialize();
    }
}
