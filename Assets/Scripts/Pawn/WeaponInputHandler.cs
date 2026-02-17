using FishNet.Object;
using UnityEngine;

public class WeaponInputHandler : NetworkBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnInventory inventory;
    [SerializeField] private WeaponDataProcessor weaponEngine;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner) return;

        pawn.Input.OnWeaponSwapPressed += RequestSwap;
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (pawn.Input.Data.Fire)
        {
            weaponEngine.TryFireAuto(inventory.CurrentWeaponData);
        }
    }

    [ServerRpc]
    private void RequestSwap()
    {
        inventory.ServerSwap();
    }

    // Handle Semi-Auto via the event from PawnInput
    private void OnEnable()
    {
        if (pawn.Input != null) pawn.Input.OnFirePressed += HandleSemiAuto;
    }

    private void OnDisable()
    {
        if (pawn.Input != null) pawn.Input.OnFirePressed -= HandleSemiAuto;
    }

    private void HandleSemiAuto()
    {
        if (!IsOwner) return;
        weaponEngine.TryFireSemiAuto(inventory.CurrentWeaponData);
    }
}