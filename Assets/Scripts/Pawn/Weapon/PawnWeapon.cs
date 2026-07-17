using System;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Thin NetworkBehaviour that wires the pure WeaponProcessor into Unity and FishNet.
/// Owns the fire point, reads input, feeds data to the processor, and spawns bullets.
/// </summary>
public class PawnWeapon : NetworkBehaviour
{
    // Configuration
    [SerializeField] private Pawn pawn;
    [SerializeField] private Transform firePoint;
    [SerializeField] private WeaponConfig config;
    [Tooltip("The projectile type this weapon fires; it owns the instance prefab + component defaults.")]
    [SerializeField] private ProjectileType projectile;
    [SerializeField] private Vector3 spawnPositionOffset;
    [SerializeField] private PawnInput input;
    [SerializeField] private Collider pawnCollider;
    [Tooltip("Optional. While this receiver is stunned, the weapon may not start a new fire event.")]
    [SerializeField] private HitstunReceiver hitstun;

    // API
    public readonly List<WeaponModifier> Modifiers = new();

    /// <summary>Whether the weapon may START a new fire event (an already-committed burst plays out). Plain data
    /// this weapon owns: the sibling HitstunController drives it from the replicated stun window on whichever
    /// peer simulates this pawn — the weapon never reaches into hitstun state.</summary>
    public bool CanFire { get; private set; } = true;

    public void SetCanFire(bool value) => CanFire = value;

    // State
    private WeaponState _state = WeaponState.Default;
    private bool _fireHeldLastFrame;

    private void Update()
    {
        if (!input.IsActive) return;

        bool fireHeld = input.Data.Fire;
        bool firePressed = fireHeld && !_fireHeldLastFrame;
        _fireHeldLastFrame = fireHeld;

        // Read the stun window locally. This Update runs on whichever peer simulates the pawn (the owning client
        // for a human, the server for a bot), and the receiver's expiry tick is a SyncVar, so both reach the same
        // verdict — the owner never predicts a shot the server would reject.
        var weaponInput = new WeaponInput
        {
            FireHeld = fireHeld,
            FirePressed = firePressed,
            HasAmmo = true,
            Stunned = hitstun != null && hitstun.IsStunned,
            Modifiers = Modifiers,
        };

        _state = WeaponProcessor.Process(
            _state, in config, in weaponInput, Time.deltaTime, out int shotsToFire);

        if (shotsToFire > 0)
        {
            float effectiveSpread = WeaponProcessor.GetEffectiveSpreadAngle(in config, in weaponInput);

            for (int shot = 0; shot < shotsToFire; shot++)
            {
                ExecuteShot(effectiveSpread);
            }
        }
    }

    private void ExecuteShot(float effectiveSpread)
    {
        if (projectile == null)
            return;

        int count = config.ProjectilesPerShot;

        for (int i = 0; i < count; i++)
        {
            // effectiveSpread is post-modifier (overheat widens, accuracy buffs tighten); i is this pellet's
            // slot in the fan. The 4-arg overload read the raw SpreadAngle at index 0 — every pellet identical.
            var fireDir = WeaponHelper.CalculateSpread(
                config.SpreadType, count, effectiveSpread, firePoint, i, count);
            Vector3 spawnPos = firePoint.TransformPoint(spawnPositionOffset);

            // Instantiate at the spawn pose and network-spawn; the instance's components self-configure
            // (TypeObjectRoot at Awake, NetworkedMotion's anchor at OnStartNetwork from this pose + tick).
            // Use THIS pawn's own NetworkManager (not InstanceFinder): on the owning client that is the
            // client's manager, so the spawn takes FishNet's predicted-spawn path and the bullet appears
            // locally on the SAME frame; on the server/host it spawns authoritatively. Owner = this pawn's
            // connection so the predicting client owns its bullet. The firer id is the one per-spawn value a
            // component cannot self-derive (bots have no owning connection), so it is stamped here.
            Quaternion rot = fireDir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fireDir) : Quaternion.identity;
            NetworkObject nob = NetworkManager.GetPooledInstantiated(projectile.InstancePrefab, spawnPos, rot, asServer: false);
            if (nob.TryGetComponent<NetPlayerOwnership>(out var ownership))
                ownership.SpawnOwnerId = pawn.ControllingPlayer.Value != null ? pawn.ControllingPlayer.Value.ObjectId : -1;
            NetworkManager.ServerManager.Spawn(nob, Owner);
        }
    }
}
