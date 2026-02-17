using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

public class AmmoChest : NetworkBehaviour, IInteractable
{
    [SerializeField] private AmmoPickup ammoPickupPrefab;
    [SerializeField] private int numPickups = 2;
    

    [ServerRpc(RequireOwnership = false)]
    public void RequestInteract(Pawn pawn)
    {
        ServerOpen(pawn);
    }

    [Server]
    private void ServerOpen(Pawn pawn)
    {
        var currentAmmoType = pawn.Inventory.CurrentWeaponData.AmmoType;

        for (int i = 0; i < numPickups; i++)
        {
            var ammoPickup = Instantiate(ammoPickupPrefab, transform.position, transform.rotation);
            ammoPickup.AmmoType = currentAmmoType;
            Spawn(ammoPickup);
        }

        Despawn();
    }
}