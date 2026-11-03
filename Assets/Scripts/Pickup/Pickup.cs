using System;
using UnityEngine;

/// <summary>
/// All logic + data for client-side pickups, in one class: small health, small ammo, and experience.
/// A pickup is instantiated locally, never spawned over the network. A launched pickup tumbles as a
/// physics body for a moment, then homes in on the pawn of the player it was launched at.
/// An unlaunched pickup is kinematic and waits for any player's pawn to come within that pawn's <see cref="Pawn.PickupRadius"/>,
/// then claims the nearest. Every peer decides that for its own copy (see <see cref="PickupDirector"/>), so
/// two players in a close race may both get it.
/// A claim is final: the pickup flies to that player only.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Pickup : MonoBehaviour
{
    public enum Kind
    {
        Health,
        Ammo,
        Experience,
    }

    [Serializable]
    private struct AmmoReward
    {
        public AmmoType type;
        public int amount;
    }

    [Header("Kind")]
    public Kind kind = Kind.Health;

    [Header("Health")]
    [SerializeField] private int healthAmount = 3;

    [Header("Ammo")]
    [Tooltip("Ammo given for the held weapon's ammo type, read when the pickup is collected.")]
    [SerializeField] private AmmoReward[] ammoRewards =
    {
        new() { type = AmmoType.Bullet, amount = 8 },
        new() { type = AmmoType.Shell, amount = 4 },
        new() { type = AmmoType.Bolt, amount = 3 },
        new() { type = AmmoType.Explosive, amount = 3 },
        new() { type = AmmoType.Energy, amount = 5 },
    };

    [Header("Experience")]
    [SerializeField] private int experienceAmount = 1;

    [Header("Tumble")]
    [Tooltip("How long a launched pickup tumbles as a non-kinematic rigidbody before it homes in.")]
    [SerializeField] private float tumbleDuration = 0.5f;

    [Header("Homing")]
    [Tooltip("How fast the pickup flies towards the collecting pawn, in units per second.")]
    [SerializeField] private float homingSpeed = 20f;
    [Tooltip("How close to the pawn's center the pickup must get to be collected.")]
    [SerializeField] private float collectDistance = 0.75f;

    private Rigidbody _rb;
    private NetworkPlayer _claimant;
    private float _homingStartTime;


    // -------------------- OBJECT CREATION --------------------

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        // Only a launched pickup is ever a dynamic body, and only for its tumble.
        _rb.isKinematic = true;
    }


    // -------------------- API --------------------

    /// <summary>Whether a player has claimed it on this peer.</summary>
    public bool Claimed => _claimant != null;

    /// <summary>Claims the pickup for <paramref name="collector"/> and tumbles it out before it homes in.</summary>
    public void Launch(NetworkPlayer collector, Vector3 velocity)
    {
        Claim(collector);
        _homingStartTime = Time.fixedTime + tumbleDuration;

        _rb.isKinematic = false;
        _rb.detectCollisions = true;
        _rb.AddForce(velocity, ForceMode.VelocityChange);
    }

    /// <summary>Gives the pickup to one player for good. Later claims are ignored.</summary>
    public void Claim(NetworkPlayer player)
    {
        if (_claimant == null)
            _claimant = player;
    }


    // -------------------- TICK --------------------

    private void FixedUpdate()
    {
        if (Time.fixedTime < _homingStartTime)
            return;

        if (_claimant == null)
        {
            TryClaimPlayer();
            return;
        }

        Pawn pawn = _claimant.ControlledPawn.Value;
        if (pawn == null)
        {
            // Nobody to fly to (they're dead or gone).
            Destroy(gameObject);
            return;
        }

        Home(pawn);
    }


    // -------------------- MOVEMENT --------------------

    private void Home(Pawn pawn)
    {
        if (_rb.detectCollisions)
        {
            _rb.isKinematic = true;
            _rb.detectCollisions = false; // fly through walls and pawns
        }

        Vector3 target = pawn.Center;
        Vector3 next = Vector3.MoveTowards(_rb.position, target, homingSpeed * Time.fixedDeltaTime);
        _rb.MovePosition(next);

        if (Vector3.Distance(next, target) <= collectDistance)
            Collect(pawn);
    }


    // -------------------- CLAIMING --------------------

    /// <summary>
    /// Claims the nearest player whose spawned pawn is within its own <see cref="Pawn.PickupRadius"/>, as this
    /// peer sees it. Launched pickups are already claimed.
    /// </summary>
    private void TryClaimPlayer()
    {
        MatchManager match = MatchManager.Instance;
        if (match == null)
            return;

        NetworkPlayer nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (NetworkPlayer player in match.ActivePlayers)
        {
            Pawn pawn = player != null ? player.ControlledPawn.Value : null;
            if (pawn == null || !pawn.IsSpawned)
                continue;

            float distance = Vector3.Distance(_rb.position, pawn.Center);
            if (distance <= pawn.PickupRadius && distance < nearestDistance)
            {
                nearest = player;
                nearestDistance = distance;
            }
        }

        if (nearest != null)
            Claim(nearest);
    }


    // -------------------- COLLECTION --------------------

    /// <summary>Always consumed, even when it gives nothing (full health, full ammo, no held weapon).</summary>
    private void Collect(Pawn pawn)
    {
        // Ammo and experience are client-authoritative, and the heal is an owner's ServerRpc:
        // only the claimant's owner applies them.
        if (_claimant.IsOwner)
        {
            switch (kind)
            {
                case Kind.Health:
                    pawn.ServerHeal(healthAmount);
                    break;
                case Kind.Ammo:
                    GiveAmmo(pawn);
                    break;
                case Kind.Experience:
                    _claimant.AddExperience(experienceAmount);
                    break;
            }
        }

        Destroy(gameObject);
    }

    private void GiveAmmo(Pawn pawn)
    {
        Weapon weapon = pawn.PrimaryWeapon;
        if (weapon == null)
            return;

        foreach (AmmoReward reward in ammoRewards)
        {
            if (reward.type == weapon.ammoType)
            {
                _claimant.AddAmmo(reward.type, reward.amount);
                return;
            }
        }
    }
}
