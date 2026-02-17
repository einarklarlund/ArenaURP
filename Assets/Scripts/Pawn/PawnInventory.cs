using System.Linq;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public struct WeaponSlot
{
    public int WeaponID;
    public bool IsOccupied;
}

public class PawnInventory : NetworkBehaviour
{
    [SerializeField] private WeaponDataProcessor weaponEngine;
    [SerializeField] private WeaponData defaultWeapon;

    public readonly SyncList<WeaponSlot> Slots = new() { new(), new() };
    private readonly SyncVar<int> _equippedIndex = new(0);

    public int EquippedIndex => _equippedIndex.Value;
    public WeaponData CurrentWeaponData =>
        WeaponDatabase.Instance.GetWeapon(Slots[EquippedIndex].WeaponID);

    public override void OnStartServer()
    {
        if (defaultWeapon != null) Slots[0] = new()
        {
            WeaponID = WeaponDatabase.Instance.GetID(defaultWeapon),
            IsOccupied = true,
        };
    }

    public bool CanEquip(int index) => index >= 0 && index < Slots.Count;

    [Server]
    public void ServerSwap()
    {
        if (!Slots[1].IsOccupied) return;
        (Slots[1], Slots[0]) = (Slots[0], Slots[1]);
    }

    [Server]
    public void ServerPickup(WeaponPickup pickup) 
    {
        if (pickup == null) return;
        if (Vector3.Distance(transform.position, pickup.transform.position) > 5f) return;

        int newWeaponID = pickup.WeaponDataId.Value;

        int targetIndex = Slots.FindIndex(slot => !slot.IsOccupied);

        if (targetIndex == -1) targetIndex = EquippedIndex;

        WeaponSlot oldSlot = Slots[targetIndex];
        WeaponData oldWeaponData = oldSlot.IsOccupied ? WeaponDatabase.Instance.GetWeapon(oldSlot.WeaponID) : null;

        // SyncList must be updated by index
        Slots[targetIndex] = new WeaponSlot { WeaponID = newWeaponID, IsOccupied = true };

        if (oldWeaponData != null) 
        {
            pickup.ServerSwitchWeaponID(WeaponDatabase.Instance.GetID(oldWeaponData));
        } 
        else
        {
            pickup.Despawn();
        }
    }
}