using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// All data + logic for handling weapon inputs and passing them to the right handler.
/// </summary>
public class PawnWeaponController : NetworkBehaviour
{
    // Configuration
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnWeapon primaryWeapon;

    public void FireWeapon(PreciseTick tick)
    {
        primaryWeapon.ProcessFireInput(pawn, tick);
    }
}
