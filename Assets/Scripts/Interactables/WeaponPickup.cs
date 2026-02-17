using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class WeaponPickup : NetworkBehaviour, IInteractable
{
    public WeaponData InitialWeaponData;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private float lifeTime = 5f;
    public WeaponData Data => WeaponDatabase.Instance.GetWeapon(WeaponDataId.Value);

    public readonly SyncVar<int> WeaponDataId = new();

    private readonly Dictionary<AmmoType, int> ammoValues = new()
    {
        { AmmoType.Bullet, 32 },
        { AmmoType.Shell, 8 },
        { AmmoType.Bolt, 7 },
        { AmmoType.Explosive, 6 },
        { AmmoType.Energy, 5 },
    };

    private float elapsedLife;
    private int ammo;

    public override void OnStartServer()
    {
        base.OnStartServer();
        WeaponDataId.Value = WeaponDatabase.Instance.GetID(InitialWeaponData);
        elapsedLife = 0;
        ammo = ammoValues[InitialWeaponData.AmmoType];

        float randomYaw = Random.Range(-25, 25);
        float randomPitch = Random.Range(-25, 25);
        var spreadRotation = Quaternion.Euler(randomPitch, randomYaw, 0);
        var dir = spreadRotation * Vector3.up;
        rb.AddForce(dir * 5, ForceMode.VelocityChange);
    }

    void Update()
    {
        if (!IsServerInitialized) return;

        if (elapsedLife >= lifeTime)
            Despawn();

        elapsedLife += Time.deltaTime;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestInteract(Pawn pawn)
    {
        pawn.Ammo.AddAmmo(Data.AmmoType, ammo);
        pawn.Inventory.ServerPickup(this);
        ammo = 0;
    }

    [Server]
    public void ServerSwitchWeaponID(int newID)
    {
        rb.AddForce(Vector3.up * 3, ForceMode.VelocityChange);
        WeaponDataId.Value = newID;
        elapsedLife = 0;
    }
}