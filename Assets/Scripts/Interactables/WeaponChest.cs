using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

public class WeaponChest : NetworkBehaviour, IInteractable
{
    public int NumWeaponSpawns = 2;
    [SerializeField] private List<WeaponData> weaponPool;
    [SerializeField] WeaponPickup emptyPickupPrefab;

    [ServerRpc(RequireOwnership = false)]
    public void RequestInteract(Pawn pawn)
    {
        ServerOpen();
    }

    [Server]
    private void ServerOpen()
    {
        var weapons = new List<WeaponData>(weaponPool);
        
        for (int i = 0; i < NumWeaponSpawns; i++)
        {
            var weapon = weapons[Random.Range(0, weapons.Count)];

            var weaponPickup = Instantiate(emptyPickupPrefab, transform.position, transform.rotation);
            weaponPickup.InitialWeaponData = weapon;

            Spawn(weaponPickup);

            weapons.Remove(weapon);
        }

        Despawn();
    }
}