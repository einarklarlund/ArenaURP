// using FishNet.Connection;
// using FishNet.Component.Ownership;
// using FishNet.Object;
// using System;
// using UnityEngine;
// using FishNet;

// public class BulletPredictedSpawn : PredictedSpawn
// {
//     public override bool OnTrySpawnServer(NetworkConnection spawner, NetworkConnection owner = null)
//     {
//         // Is predicted spawning even allowed for this prefab?
//         if (!GetAllowSpawning()) return false;

//         // Locate the WeaponBase on the connection's primary object (the Player)
//         // Note: owner is usually the same as spawner in this context
//         NetworkObject playerObject = spawner.FirstObject;
//         if (playerObject == null) return false;

//         // Locate the Pawn on the player object
//         NetworkPlayer player = GetComponent<NetworkPlayer>();
//         if (player == null) return false;

//         Pawn pawn = player.ControlledPawn.Value;
//         if (pawn == null) return false;

//         // Locate the Weapon engine on the pawn
//         WeaponEngine weapon = pawn.WeaponBase;
        
//         // 3. Authority Validation
//         // We ask the weapon if the fire-rate allows this bullet to be created.
//         if (weapon != null && weapon.CanFireServer())
//         {
//             // inject state into Bullet class
//             var bullet = GetComponent<Bullet>();
//             var spawnState = BulletHelper.GetSpawnState(player, weapon);
//             // bullet.Initialize(spawnState);
//             return true;
//         }

//         return false; // Server rejects spawn; client's bullet will be destroyed
//     }
// }