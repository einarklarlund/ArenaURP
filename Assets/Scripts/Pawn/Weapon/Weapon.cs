using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// All data and logic for an individual weapon.
/// A weapon is either free, lying in the world with its own physics until a pawn picks it up,
/// or held by a pawn. Free weapons have no network owner; held weapons are owned by their pawn's owner.
/// To make implementation simple for now, firing logic only runs on the side of the weapon's owner.
/// </summary>
public class Weapon : NetworkBehaviour
{
    public enum Slot : byte
    {
        None,
        Primary,
        Secondary,
    }

    /// <summary>
    /// Who holds a weapon, and in which slot. The holder is a pawn's ObjectId rather than a Pawn
    /// reference, because a reference that arrives before its pawn has spawned reads as null.
    /// </summary>
    public struct Hold
    {
        public int HolderId;
        public Slot Slot;
        /// <summary>This is the weapon's first hold, which pays out its pickup ammo.</summary>
        public bool PaysPickupAmmo;
    }

    [System.Serializable]
    private struct AmmoReward
    {
        public AmmoType type;
        public int amount;
    }

    public enum FireMode
    {
        [Tooltip("One trigger pull per click.")]
        SingleShot,
        [Tooltip("Keeps pulling the trigger while the button is held.")]
        Automatic,
        [Tooltip("One burst per click.")]
        Burst,
        [Tooltip("Keeps firing bursts while the button is held.")]
        BurstAutomatic,
    }

    private static readonly int ReloadedHash = Animator.StringToHash("Reloaded");
    private static readonly int FiredHash = Animator.StringToHash("Fired");

    [Header("Weapon Stats")]

    public FireMode fireMode = FireMode.Automatic;

    [Tooltip("Seconds between trigger pulls.")]
    [Min(0)]
    public float cooldown = 0.2f;

    [Tooltip("Random spread: full cone width in degrees, applied to every projectile.")]
    public float spreadAngle = 5f;

    [Tooltip("Fixed spread: total horizontal arc in degrees across which a shot's projectiles are spaced evenly, edge to edge. 0 = all on the aim line.")]
    public float fixedSpreadAngle = 0f;

    [Tooltip("0 = no clip: the weapon never reloads, and spends the player's ammo of Ammo Type instead.")]
    public int clipSize = 30;

    [Tooltip("Ammo used per trigger pull. A whole burst counts as one pull.")]
    [Min(0)]
    public int ammoPerShot = 1;

    [Tooltip("Which of the player's ammo counts this weapon spends: per shot when it has no clip, per reload when it does.")]
    public AmmoType ammoType = AmmoType.Bullet;

    [Tooltip("Ammo given for this weapon's ammo type the first time it is picked up.")]
    [SerializeField] private AmmoReward[] pickupAmmo =
    {
        new() { type = AmmoType.Bullet, amount = 32 },
        new() { type = AmmoType.Shell, amount = 8 },
        new() { type = AmmoType.Bolt, amount = 7 },
        new() { type = AmmoType.Explosive, amount = 6 },
        new() { type = AmmoType.Energy, amount = 10 },
    };

    public int projectilesPerShot = 1;

    [Tooltip("Fire rate multiplier while hasted, e.g. after a hit with the haste upgrade.")]
    [Min(1)]
    public float hasteFireRate = 1.5f;

    [Header("Upgrades")]

    [Tooltip("Written by the holder's upgrades; reset whenever the weapon changes hands.")]
    public float spreadMultiplier = 1f;

    [Header("Burst")]

    [Tooltip("Shots fired per trigger pull. Used by the Burst and Burst Automatic fire modes.")]
    [Min(1)]
    public int burstCount = 1;

    [Tooltip("Seconds between shots within a burst.")]
    [Min(0)]
    public float burstInterval = 0.067f; // 2 frames at Nuclear Throne's 30 fps

    [Header("Projectile Spawning")]

    [Tooltip("Transform that keeps track of where projectiles are spawned.")]
    public Transform spawnPoint;

    [Tooltip("Transform that keeps track of where projectiles appear visually when spawned.")]
    public Transform muzzlePoint;

    [Tooltip("The projectile prefab to spawn.")]
    public Projectile projectilePrefab;

    [Header("Animation")]
    public Animator animator;

    [Header("VFX")]
    [Tooltip("Procedural fire animation and muzzle flash sizing. Used only when there is no Animator; with one, firing sets its Fired trigger instead.")]
    public WeaponKick kick;
    public GameObject smokeEffects;
    public ParticleSystem muzzleFlash;
    public Rigidbody ragdoll;
    public float ragdollLaunchSpeed = 14f;
    public float ragdollRotationalLaunchSpeed = 20f;

    [Header("World")]
    [Tooltip("Simulates while the weapon is free. Not networked: every peer simulates its own copy.")]
    [SerializeField] private Rigidbody body;
    [Tooltip("Lets a free weapon land and be found by pickup raycasts. Off while held.")]
    [SerializeField] private BoxCollider worldCollider;

    private readonly SyncVar<Hold> _hold = new(new SyncTypeSettings(WritePermission.ServerOnly));

    // Spawned weapons, so a pawn that starts after its weapons' holds arrived can claim them.
    private static readonly List<Weapon> Spawned = new();

    private float _nextPullAt; // earliest Time.time the trigger can be pulled again
    private int _burstShotsLeft;
    private float _nextBurstShotAt;
    private int _currentAmmo; // ammo counts are client-authoritative for now
    private bool _isReloading;
    private NetworkPlayer _reloadingPlayer; // pays for the reload in progress
    private bool _isStowed; // held, but in the slot that isn't in hand
    private bool _animatorNeedsReset; // a reload was cut off by stowing; its clip may still be mid-play
    private float _hasteUntil; // Time.time until which the weapon fires at hasteFireRate
    private Pawn _upgradedBy; // the holder whose player's upgrades this weapon carries
    private bool _wasHeld; // server only: a weapon pays out its pickup ammo on its first hold only
    private Renderer[] _renderers;
    private bool[] _rendererDefaults; // which renderers the prefab has enabled

    /// <summary>The pawn this weapon is parented to on this peer. Null while free or while its holder hasn't spawned here yet.</summary>
    public Pawn Holder { get; private set; }

    /// <summary>A free weapon has no owner and no holder: its physics runs and it can be picked up.</summary>
    public bool IsFree => !Owner.IsValid && _hold.Value.Slot == Slot.None;

    private bool HasClip => clipSize > 0;
    private bool IsBursting => _burstShotsLeft > 0;
    private float FireRate => Time.time < _hasteUntil ? hasteFireRate : 1f;
    private int ShotsPerPull => fireMode is FireMode.Burst or FireMode.BurstAutomatic ? burstCount : 1;

    // -------------------- API - INPUT --------------------

    /// <summary>
    /// Called by the pawn every frame with the state of this weapon's fire button.
    /// </summary>
    public void TickTrigger(Pawn pawnOwner, bool held, bool pressedThisFrame)
    {
        if (!IsOwner)
            return;

        // A burst keeps going after the button is released, and nothing else happens until it ends.
        if (IsBursting)
        {
            AdvanceBurst(pawnOwner);
            return;
        }

        bool isAutomatic = fireMode is FireMode.Automatic or FireMode.BurstAutomatic;
        bool wantsPull = isAutomatic ? held : pressedThisFrame;
        if (!wantsPull)
            return;

        TryPullTrigger(pawnOwner);
    }

    public void TryReload(Pawn pawnOwner, PreciseTick tick)
    {
        if (!IsOwner)
            return;

        if (!HasClip)
            return;

        if (_currentAmmo == clipSize)
            return;

        if (_isReloading)
            return;

        if (IsBursting)
            return;

        // A reload needs a full clip's worth of the player's ammo.
        NetworkPlayer player = pawnOwner.ControllingPlayer.Value;
        if (player == null || player.GetAmmo(ammoType) < clipSize)
            return;

        _currentAmmo = 0;
        _isReloading = true;
        _reloadingPlayer = player;

        PlayReloadAnimation();
    }

    /// <summary>Fires at <see cref="hasteFireRate"/> for the next <paramref name="duration"/> seconds.</summary>
    public void ApplyHaste(float duration)
    {
        _hasteUntil = Time.time + duration;
    }

    // -------------------- API - ANIMATION --------------------

    public void ThrowRagdoll()
    {
        // Called from animation events
        PlayRagdollVfx(transform.position, ragdollLaunchSpeed * transform.forward);
    }

    public void RefillAmmo()
    {
        // Called from animation events
        if (!IsOwner)
            return;

        // The player may have spent this ammo type elsewhere since the reload started;
        // if so, the clip stays empty.
        if (_reloadingPlayer != null && _reloadingPlayer.TryConsumeAmmo(ammoType, clipSize))
            _currentAmmo = clipSize;

        _isReloading = false;
        _reloadingPlayer = null;
    }

    // -------------------- API - HOLDING --------------------

    /// <summary>Gives this weapon to a pawn's owner and puts it in one of the pawn's slots.</summary>
    [Server]
    public void ServerHold(Pawn holder, Slot slot)
    {
        GiveOwnership(holder.Owner);
        // A starting weapon's first hold uses up its payout too, though no pickup collects it.
        _hold.Value = new Hold { HolderId = holder.ObjectId, Slot = slot, PaysPickupAmmo = !_wasHeld };
        _wasHeld = true;
        // The holder may still be starting, before the server can look it up by id.
        ApplyHold(holder);
    }

    /// <summary>
    /// Called by the owner of the pawn that picked this weapon up, once its hold arrives. Ammo is
    /// client-authoritative, so the owner adds it; the server decided whether this hold pays out.
    /// </summary>
    public void GivePickupAmmo(NetworkPlayer player)
    {
        if (player == null || !_hold.Value.PaysPickupAmmo)
            return;

        foreach (AmmoReward reward in pickupAmmo)
        {
            if (reward.type == ammoType)
            {
                player.AddAmmo(reward.type, reward.amount);
                return;
            }
        }
    }

    /// <summary>Frees this weapon at a pose. Every peer then simulates its fall on its own.</summary>
    [Server]
    public void ServerDrop(Vector3 position, Quaternion rotation)
    {
        RemoveOwnership();
        _hold.Value = default;
        ObserversPlace(position, rotation);
    }

    /// <summary>
    /// Called by the holder: a stowed weapon is hidden and stops what its trigger started. Ammo already
    /// spent stays spent, and its cooldown keeps running, so switching away and back never skips it.
    /// </summary>
    public void SetStowed(bool stowed)
    {
        if (_isStowed == stowed)
            return;

        _isStowed = stowed;
        if (stowed)
        {
            _burstShotsLeft = 0;
            if (_isReloading)
            {
                // Nothing was paid yet; a late RefillAmmo event finds no player and leaves the clip empty.
                _isReloading = false;
                _reloadingPlayer = null;
                _animatorNeedsReset = true;
            }
        }
        else if (_animatorNeedsReset)
        {
            _animatorNeedsReset = false;
            RecordAnimatorDefaults();
        }

        RefreshPhysics();
    }

    /// <summary>Called by a pawn that starts: claims the weapons whose holds name it.</summary>
    public static void ApplyHoldsFor(Pawn pawn)
    {
        foreach (Weapon weapon in Spawned)
        {
            if (weapon._hold.Value.Slot != Slot.None && weapon._hold.Value.HolderId == pawn.ObjectId)
                weapon.ApplyHold(pawn);
        }
    }

    /// <summary>Called by a pawn that stops: it may be pooled before its weapons' drops arrive.</summary>
    public void DetachFromHolder()
    {
        if (Holder == null)
            return;

        Holder.Release(this);
        Holder = null;
        transform.SetParent(null, worldPositionStays: true);
        RefreshUpgrades();
        RefreshPhysics();
    }

    // -------------------- OBJECT CREATION --------------------

    private void Awake()
    {
        _hold.OnChange += OnHoldChanged;
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _rendererDefaults = new bool[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
            _rendererDefaults[i] = _renderers[i].enabled;
        RefreshPhysics();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Spawned.Add(this);
        ApplyHold();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
            ResetFiring();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        Spawned.Remove(this);
        DetachFromHolder();
    }

    public override void OnOwnershipServer(NetworkConnection prevOwner)
    {
        base.OnOwnershipServer(prevOwner);
        RefreshPhysics();
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        RefreshPhysics();

        if (IsOwner)
            ResetFiring();
    }

    private void ResetFiring()
    {
        _nextPullAt = 0;
        _burstShotsLeft = 0;
        _currentAmmo = clipSize;
        _isReloading = false;
        _reloadingPlayer = null;
    }

    // -------------------- HOLDING --------------------

    private void OnHoldChanged(Hold prev, Hold next, bool asServer)
    {
        // A host gets both the server- and client-side callback for the same object; react once.
        if (!asServer && IsServerStarted)
            return;

        ApplyHold();
    }

    /// <summary>
    /// Parents this weapon to its holder's hold point, or releases it into the world.
    /// </summary>
    /// <param name="knownHolder">The holder, when the caller has it and a lookup by id may not work yet.</param>
    private void ApplyHold(Pawn knownHolder = null)
    {
        Hold hold = _hold.Value;
        Pawn holder = null;
        if (hold.Slot != Slot.None)
            holder = knownHolder != null && knownHolder.ObjectId == hold.HolderId ? knownHolder : FindPawn(hold.HolderId);

        Pawn previous = Holder;
        if (previous != null && previous != holder)
            previous.Release(this);

        Holder = holder;
        if (holder != null)
        {
            transform.SetParent(holder.Equip(this, hold.Slot), worldPositionStays: false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (holder != previous)
                RecordAnimatorDefaults();
        }
        else if (previous != null)
        {
            // Free now, or held by a pawn that hasn't spawned here yet (it claims the weapon when it does).
            transform.SetParent(null, worldPositionStays: true);
        }

        if (holder != previous)
            RefreshUpgrades();
        RefreshPhysics();
    }

    // -------------------- UPGRADES --------------------

    /// <summary>
    /// Drops the previous holder's upgrades and haste, then has the new holder's player push its own.
    /// Runs on every peer: the fields are local, and only the owner fires.
    /// </summary>
    private void RefreshUpgrades()
    {
        // Starting weapons are held before their pawn's ControllingPlayer is set, so wait for it.
        if (_upgradedBy != Holder)
        {
            if (_upgradedBy != null)
                _upgradedBy.ControllingPlayer.OnChange -= OnHolderPlayerChanged;
            _upgradedBy = Holder;
            if (_upgradedBy != null)
                _upgradedBy.ControllingPlayer.OnChange += OnHolderPlayerChanged;
        }

        spreadMultiplier = 1f;
        _hasteUntil = 0f;

        NetworkPlayer player = Holder != null ? Holder.ControllingPlayer.Value : null;
        if (player != null)
            player.ApplyUpgradesTo(this);
    }

    private void OnHolderPlayerChanged(NetworkPlayer prev, NetworkPlayer next, bool asServer)
    {
        // A host gets both the server- and client-side callback; reapplying is idempotent but once is enough.
        if (!asServer && IsServerStarted)
            return;

        RefreshUpgrades();
    }

    /// <summary>
    /// An Animator writes every property its clips animate on every frame, even in states that don't
    /// animate it, starting from values it records when its GameObject activates. A clip that animates
    /// the root would otherwise keep writing the pose the weapon had before it was held. Neither
    /// Rebind nor re-enabling the Animator records them again; reactivating the GameObject does.
    /// Safe on a spawned NetworkObject: its OnDisable only acts while it's deinitializing.
    /// </summary>
    private void RecordAnimatorDefaults()
    {
        if (animator == null)
            return;

        animator.enabled = true;
        gameObject.SetActive(false);
        gameObject.SetActive(true);
    }

    private Pawn FindPawn(int objectId)
    {
        NetworkObject nob = null;
        if (IsClientStarted)
            ClientManager.Objects.Spawned.TryGetValue(objectId, out nob);
        if (nob == null && IsServerStarted)
            ServerManager.Objects.Spawned.TryGetValue(objectId, out nob);
        return nob != null ? nob.GetComponent<Pawn>() : null;
    }

    [ObserversRpc(RunLocally = true)]
    private void ObserversPlace(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    private void RefreshPhysics()
    {
        bool simulate = IsFree && Holder == null;
        body.isKinematic = !simulate;
        worldCollider.enabled = simulate;
        // A free weapon is always shown; it forgets having been stowed.
        if (Holder == null)
            _isStowed = false;

        // Only the weapon in hand animates: clips that animate the root would pin a free weapon in the air,
        // and clips that animate renderers (the Uzi's reload) would show a stowed one.
        if (animator != null)
            animator.enabled = Holder != null && !_isStowed;
        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].enabled = _rendererDefaults[i] && !_isStowed;
    }

    // -------------------- FIRING --------------------

    private void TryPullTrigger(Pawn pawnOwner)
    {
        if (HasClip && _currentAmmo < ammoPerShot)
        {
            TryReload(pawnOwner, TimeManager.GetPreciseTick(TickType.Tick));
            return;
        }

        if (Time.time < _nextPullAt)
            return;

        // Ammo is paid for the whole pull up front, so a burst always finishes once started.
        if (!TrySpendAmmo(pawnOwner))
            return;

        _nextPullAt = Time.time + cooldown / FireRate;
        _burstShotsLeft = ShotsPerPull;
        _nextBurstShotAt = Time.time;
        AdvanceBurst(pawnOwner);
    }

    /// <summary>
    /// Fires every shot of the current pull that is due. A single shot is a burst of one.
    /// </summary>
    private void AdvanceBurst(Pawn pawnOwner)
    {
        // Several shots may be due in one frame when burstInterval is shorter than a frame;
        // firing them all keeps the burst's length the same at any frame rate.
        while (IsBursting && Time.time >= _nextBurstShotAt)
        {
            // Each shot carries its own spawn tick, not the tick of the click that started the burst.
            Fire(pawnOwner, TimeManager.GetPreciseTick(TickType.Tick));
            _burstShotsLeft--;
            _nextBurstShotAt += burstInterval / FireRate;
        }
    }

    private void Fire(Pawn pawnOwner, PreciseTick tick)
    {
        for (int i = 0; i < projectilesPerShot; i++)
        {
            LaunchProjectile(pawnOwner, tick, i);
        }

        PlayVfx();
    }

    /// <summary>
    /// Pays for one trigger pull: from the clip, or from the controlling player's ammo when clipless.
    /// </summary>
    private bool TrySpendAmmo(Pawn pawnOwner)
    {
        if (HasClip)
        {
            _currentAmmo -= ammoPerShot;
            return true;
        }

        NetworkPlayer player = pawnOwner.ControllingPlayer.Value;
        return player != null && player.TryConsumeAmmo(ammoType, ammoPerShot);
    }

    private void LaunchProjectile(Pawn pawnOwner, PreciseTick tick, int projectileIndex)
    {
        var fireRot = CalculateSpread(projectileIndex);
        var firePos = muzzlePoint.position;

        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(
            projectilePrefab, firePos, fireRot, asServer: false);
        
        if (nob.TryGetComponent<Projectile>(out var p))
        {
            p.owningPawn = pawnOwner;
            p.spawnTick = tick;
            // Send the muzzle point to the *client only* so that projectiles
            // always appear at the right spot during client-side predicted spawn.
            p.visualPredictionPoint = muzzlePoint;
            // TODO: spawn position should be set here and be calculated based on where
            // the pawn was at spawnTick.
        }
        nob.transform.localScale = pawnOwner.transform.localScale.x * pawnOwner.transform.localScale;
        InstanceFinder.NetworkManager.ServerManager.Spawn(nob, Owner);
    }

    private Quaternion CalculateSpread(int projectileIndex)
    {
        // Fixed spread: fan the shot's projectiles evenly across the arc, edge to edge
        float fixedYaw = 0f;
        if (projectilesPerShot > 1)
            fixedYaw = fixedSpreadAngle * ((float)projectileIndex / (projectilesPerShot - 1) - 0.5f);

        // Random spread within a cone
        float spread = spreadAngle * spreadMultiplier;
        float randomYaw = Random.Range(-spread / 2, spread / 2);
        float randomPitch = Random.Range(-spread / 6, spread / 6);
        return muzzlePoint.rotation * Quaternion.Euler(randomPitch, fixedYaw + randomYaw, 0);
    }

    // -------------------- ANIMATION --------------------

    private void PlayReloadAnimation()
    {
        if (animator != null)
            animator.SetTrigger(ReloadedHash);
    }

    // -------------------- VFX --------------------

    private void PlayVfx()
    {
        Instantiate(smokeEffects, muzzlePoint.position, muzzlePoint.rotation);
        // Hand-made fire clips win over the procedural kick. The kick also sizes the muzzle flash, so it goes first.
        if (animator != null)
            animator.SetTrigger(FiredHash);
        else if (kick != null)
            kick.Play();
        if(muzzleFlash.isPlaying)
            muzzleFlash.Stop();
        muzzleFlash.Play();
    }

    private void PlayRagdollVfx(Vector3 spawnPoint, Vector3 launchVelocity)
    {
        if (IsServerStarted && !IsClientStarted)
            return;

        Rigidbody rd = Instantiate(ragdoll, spawnPoint, transform.rotation);
        rd.AddForce(launchVelocity, ForceMode.VelocityChange);
        rd.AddTorque(ragdollRotationalLaunchSpeed * transform.right, ForceMode.VelocityChange);
    }
}
