using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public enum AmmoType { Bullet, Shell, Energy, Bolt, Explosive }

public class PawnAmmo : NetworkBehaviour {
    public readonly SyncDictionary<AmmoType, int> AmmoPools = new();
    public static readonly Dictionary<AmmoType, int> AmmoMaximums = new()
    {
        { AmmoType.Bullet, 255 },
        { AmmoType.Shell, 55 },
        { AmmoType.Bolt, 55 },
        { AmmoType.Explosive, 55 },
        { AmmoType.Energy, 55 },
    };

    public override void OnStartServer()
    {
        base.OnStartServer();
        AmmoPools[AmmoType.Bullet] = 100;
        AmmoPools[AmmoType.Shell] = 0;
        AmmoPools[AmmoType.Bolt] = 0;
        AmmoPools[AmmoType.Energy] = 0;
    }
    
    [Server]
    public void AddAmmo(AmmoType type, int amount)
    {
        AmmoPools[type] += Mathf.Min(amount, AmmoMaximums[type]);
    }

    public bool ConsumeAmmo(AmmoType type, int amount) {
        if (AmmoPools[type] >= amount)
        {
            AmmoPools[type] -= amount;
            return true;
        }
        return false;
    }
}