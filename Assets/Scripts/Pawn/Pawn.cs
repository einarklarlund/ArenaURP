using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.CodeGenerating;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// All data and logic for Pawns besides input.
/// </summary>
public class Pawn : NetworkBehaviour
{
    public readonly SyncVar<NetworkPlayer> ControllingPlayer = new();

    [SerializeField] private CharacterController controller;

    [Header("Health")]

    [AllowMutableSyncType]
    public SyncVar<int> CurrentHealth = new(1, new SyncTypeSettings(WritePermission.ServerOnly));

    [AllowMutableSyncType]
    public SyncVar<int> MaxHealth = new(21, new SyncTypeSettings(WritePermission.ServerOnly));

    [Tooltip("Seconds without taking damage before health regeneration (from upgrades) starts.")]
    [SerializeField] private float regenDelay = 3f;

    [Header("Weapon")]

    [Tooltip("Spawned into the pawn's slots when it spawns. Either may be empty. The primary is in hand if there is one.")]
    [SerializeField] private Weapon startingPrimaryWeapon;

    [SerializeField] private Weapon startingSecondaryWeapon;

    [Tooltip("Where a held weapon's origin (the top of its grip) goes. Both slots hold here; the stowed one is hidden.")]
    [FormerlySerializedAs("primaryHoldPoint")]
    [SerializeField] private Transform holdPoint;

    [Tooltip("Scrolling counts as a new weapon switch only after this many seconds without scrolling, " +
             "since one flick of a wheel or trackpad scrolls for many frames.")]
    [SerializeField] private float scrollSwitchPause = 0.15f;

    [Tooltip("How far ahead of the camera a weapon can be picked up or a chest opened.")]
    [SerializeField] private float pickupRange = 3f;

    [Header("Interaction")]

    [Tooltip("Layers the interact raycast hits: weapons, chests, and the geometry that hides them.")]
    [SerializeField] private LayerMask pickupLayers;

    [Header("Movement")]

    [SerializeField] private MovementConfig config = MovementConfig.Default;

    [Header("Rotation")]
    [SerializeField] private Transform characterControllerTransform;
    
    [SerializeField] Transform cameraTransform;

    // VFX events (predicted client-side)
    public event Action<DamageInfo> OnDamageTaken;
    public event Action<DamageInfo> OnDeath;

    private bool _isReceivingUserInput;
    private float _horizontalRotation = 0f;
    private float _verticalRotation = 0f;
    private JumpState _jumpState = JumpState.Default;
    private bool _isPaused = false;
    // Server only: this life's despawn has dropped its weapons (see OnDespawnServer).
    private bool _despawnHandled;
    // Knockback received since the last Move(), consumed by MovementProcessor as one impulse. Owner only.
    private Vector3 _pendingImpulse;
    // What MovementProcessor asked the CharacterController for last tick. Owner only.
    private Vector3 _requestedVelocity;
    // What MovementProcessor asked for last tick to pull the character onto the ground. Owner only.
    private Vector3 _snapVelocity;
    // Follow each weapon's synced hold; see Weapon.ApplyHold.
    private Weapon _primaryWeapon;
    private Weapon _secondaryWeapon;
    // Which slot is in hand. The owner decides, client-authoritative like firing, and keeps its own copy
    // in _ownerHeldSlot; the server carries it to everyone else in _syncedHeldSlot.
    private readonly SyncVar<Weapon.Slot> _syncedHeldSlot =
        new(Weapon.Slot.Primary, new SyncTypeSettings(WritePermission.ServerOnly));
    private Weapon.Slot _ownerHeldSlot = Weapon.Slot.Primary;
    // The free weapon this pawn's owner asked to pick up; it goes in hand when its hold arrives. Owner only.
    private Weapon _pendingPickup;
    private float _lastScrollAt = float.NegativeInfinity;
    // What the local player is aiming at to interact with. Compared by reference: a despawned target still differs from null.
    private Component _interactTarget;

    /// <summary>The center of the pawn's body, in world space.</summary>
    public Vector3 Center => controller.transform.TransformPoint(controller.center);

    // --------------------- API - HEALTH ---------------------------

    public void TakeDamage(DamageInfo damageInfo)
    {
        if (CurrentHealth.Value <= 0) return;

        OnDamageTaken?.Invoke(damageInfo);

        if (!IsServerInitialized)
            return;

        if (damageInfo.Amount > 0)
        {
            CurrentHealth.Value = Mathf.Max(0, CurrentHealth.Value - damageInfo.Amount);
            _lastDamagedAt = Time.time;
            _regenProgress = 0f;
        }

        if (CurrentHealth.Value <= 0)
        {
            // Read before OnDeath, which takes the drop off the victim's reported experience.
            _deathDropSize = DeathDropSize();
            OnDeath?.Invoke(damageInfo);
            Despawn(DespawnType.Pool);
        }
    }


    // --------------------- EXPERIENCE DROP (stream D: progression) ---------------------

    [Header("Experience drop")]
    [Tooltip("Instantiated locally on every client when this pawn dies, not spawned over the network.")]
    [SerializeField] private Pickup experiencePickup;
    [SerializeField] private float experienceLaunchSpeed = 15f;

    // Server only: the death drop, read before the server takes it off the victim's reported experience.
    private int _deathDropSize;

    private int DeathDropSize()
    {
        NetworkPlayer victim = ControllingPlayer.Value;
        return victim != null ? Levels.DeathDrop(victim.ReportedExperience.Value) : 0;
    }

    /// <summary>
    /// Bursts this pawn's death drop where it died: half its player's experience, at least 15. Every client does this
    /// once, when the dead pawn despawns. Nobody is given the pickups: each client's copies go to whoever comes near
    /// them there, and give experience only on that player's own client. The victim's experience goes down on its own
    /// client (NetworkPlayer), not here.
    /// </summary>
    private void InstantiateExperience(int amount)
    {
        if (experiencePickup == null) return;

        for (int i = 0; i < amount; i++)
        {
            Vector3 direction = new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(0.5f, 1f),
                UnityEngine.Random.Range(-1f, 1f)
            ).normalized;

            Pickup pickup = Instantiate(experiencePickup, Center, Quaternion.LookRotation(direction));
            pickup.Launch(null, experienceLaunchSpeed * direction);
        }
    }


    // --------------------- API - HEAL (stream C: supply pickups) ---------------------

    /// <summary>
    /// Client-authoritative, like ammo and experience: the owner's <see cref="Pickup"/> asks, and the
    /// server trusts it. Clamps to <see cref="MaxHealth"/>.
    /// </summary>
    [ServerRpc]
    public void ServerHeal(int amount)
    {
        if (CurrentHealth.Value <= 0 || amount <= 0) return;

        CurrentHealth.Value = Mathf.Min(CurrentHealth.Value + amount, MaxHealth.Value);
    }


    // --------------------- UPGRADE FIELDS (stream G2: upgrades) ---------------------
    // Written by Upgrade hooks, which the player pushes when it gets this pawn and when it picks one mid-life.
    // Upgrade values start neutral every life (ResetState); the base values are the prefab's.

    /// <summary>Scales max speed on the ground and in the air. Read by the owner, which moves the pawn.</summary>
    [NonSerialized] public float MoveSpeedMultiplier = 1f;

    /// <summary>Health per second once <see cref="regenDelay"/> passes without damage. Read by the server, which ticks it.</summary>
    [NonSerialized] public float RegenPerSecond;

    /// <summary>Scales <see cref="basePickupRadius"/>. Read on every peer, since each one claims its own pickups.</summary>
    [NonSerialized] public float PickupRadiusMultiplier = 1f;

    [Header("Pickups")]
    [Tooltip("A waiting pickup homes in on this pawn once it is this close.")]
    [SerializeField] private float basePickupRadius = 5f;

    /// <summary>How close a waiting <see cref="Pickup"/> has to be to home in on this pawn.</summary>
    public float PickupRadius => basePickupRadius * PickupRadiusMultiplier;


    // --------------------- REGENERATION ---------------------

    // Server only. Health regenerated but not yet added, since health is whole numbers.
    private float _regenProgress;
    // Server only. Time.time of the last damage taken.
    private float _lastDamagedAt = float.NegativeInfinity;

    [Server]
    private void Regenerate(float deltaTime)
    {
        int health = CurrentHealth.Value;
        if (RegenPerSecond <= 0f || health <= 0 || Time.time - _lastDamagedAt < regenDelay)
            return;

        if (health >= MaxHealth.Value)
        {
            _regenProgress = 0f;
            return;
        }

        _regenProgress += RegenPerSecond * deltaTime;
        int whole = Mathf.FloorToInt(_regenProgress);
        if (whole <= 0)
            return;

        _regenProgress -= whole;
        CurrentHealth.Value = Mathf.Min(health + whole, MaxHealth.Value);
    }


    // --------------------- API - KNOCKBACK ---------------------

    /// <summary>
    /// Adds a velocity change (m/s) to this pawn. Called on every peer that resolves the cause
    /// (e.g. an <see cref="Explosion"/>); only the owner moves the pawn, and the client-authoritative
    /// NetworkTransform carries the result to everyone else.
    /// </summary>
    public void ApplyKnockback(Vector3 impulse)
    {
        if (IsOwner)
        {
            _pendingImpulse += impulse;
        }
        else
        {
            // Observers: no-op for now. Step 2 predicts the knockback on the pawn's visuals here.
        }
    }


    // --------------------- API - WEAPONS ---------------------

    /// <summary>The weapon in hand, from whichever slot that is: the only one that fires. Null when that slot is empty.</summary>
    public Weapon PrimaryWeapon => WeaponIn(HeldSlot);

    /// <summary>The weapon in the slot that isn't in hand, or null.</summary>
    public Weapon StowedWeapon => WeaponIn(OtherSlot(HeldSlot));

    /// <summary>Records a weapon in a slot and returns the hold point to parent it to.</summary>
    public Transform Equip(Weapon weapon, Weapon.Slot slot)
    {
        if (slot == Weapon.Slot.Primary)
            _primaryWeapon = weapon;
        else
            _secondaryWeapon = weapon;

        if (IsOwner && weapon == _pendingPickup)
        {
            _pendingPickup = null;
            _ownerHeldSlot = slot;
            weapon.GivePickupAmmo(ControllingPlayer.Value);
        }

        RefreshHeldWeapon();
        return holdPoint;
    }

    /// <summary>Forgets a weapon that left this pawn's hands.</summary>
    public void Release(Weapon weapon)
    {
        if (_primaryWeapon == weapon)
            _primaryWeapon = null;
        if (_secondaryWeapon == weapon)
            _secondaryWeapon = null;

        RefreshHeldWeapon();
    }


    // --------------------- OBJECT CREATION ---------------------

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _syncedHeldSlot.OnChange += OnSyncedHeldSlotChanged;

        // Weapons whose holds arrived before this pawn spawned here.
        Weapon.ApplyHoldsFor(this);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        _despawnHandled = false;

        _syncedHeldSlot.Value = startingPrimaryWeapon == null && startingSecondaryWeapon != null
            ? Weapon.Slot.Secondary
            : Weapon.Slot.Primary;
        ServerSpawnStartingWeapon(startingPrimaryWeapon, Weapon.Slot.Primary);
        ServerSpawnStartingWeapon(startingSecondaryWeapon, Weapon.Slot.Secondary);
    }

    public override void OnStartClient() 
    {
        base.OnStartClient();

        // The owner takes over the slot the server spawned it holding.
        if (IsOwner)
        {
            _ownerHeldSlot = _syncedHeldSlot.Value;
            RefreshHeldWeapon();
        }

        bool isNotReceivingUserInput = !IsOwner || (IsOwner && ControllingPlayer.Value != null && ControllingPlayer.Value.IsBot);
        _isReceivingUserInput = !isNotReceivingUserInput;
        if (_isReceivingUserInput)
        {
            InputManager.PawnControls.Enable();

            Cursor.lockState = CursorLockMode.Locked;
            if(cameraTransform.TryGetComponent<Camera>(out var cam)) cam.enabled = true;
            if(cameraTransform.TryGetComponent<AudioListener>(out var listener)) listener.enabled = true;
            cameraTransform.tag = "MainCamera";

            // Spawned goes first: the view it brings up subscribes to health during this call.
            NetworkUIEvents.OnLocalPawnSpawned?.Invoke(this);
            CurrentHealth.OnChange += ClientOnHealthChanged;
            NetworkUIEvents.OnLocalHealthChanged?.Invoke(CurrentHealth.Value);
            NetworkUIEvents.OnLocalWeaponsChanged?.Invoke(PrimaryWeapon, StowedWeapon);
        }
        else
        {
            if(cameraTransform.TryGetComponent<Camera>(out var cam)) cam.enabled = false;
            if(cameraTransform.TryGetComponent<AudioListener>(out var listener)) listener.enabled = false;
        }

        // NetworkTransform's CharacterController configuration disables the CharacterController on observers :/
        // So we're working around that here by adding a CapsuleCollider for client-side collision prediction.
        if (!IsOwner)
        {
            var collider = gameObject.AddComponent<CapsuleCollider>();
            collider.radius = controller.radius;
            collider.height = controller.height;
            collider.center = controller.center;
            collider.includeLayers = controller.includeLayers;
            collider.excludeLayers = controller.excludeLayers;
        }

        LocalUIEvents.OnPause += OnPause;
        LocalUIEvents.OnUnpause += OnUnpause;
    }


    // --------------------- OBJECT DELETION ---------------------

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        _syncedHeldSlot.OnChange -= OnSyncedHeldSlotChanged;

        // The pawn may be pooled before its weapons' drops arrive; it never takes a weapon with it.
        if (_primaryWeapon != null)
            _primaryWeapon.DetachFromHolder();
        if (_secondaryWeapon != null)
            _secondaryWeapon.DetachFromHolder();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        LocalUIEvents.OnPause -= OnPause;
        LocalUIEvents.OnUnpause -= OnUnpause;
        _isPaused = false;

        // FishNet sends a despawning object's pending SyncVars before the despawn, so a pawn that died gets here
        // with no health; one despawned for any other reason (match end, new level, its player left) has some.
        // A client's view of the victim's reported experience still predates the death: the server's cut is on
        // another object, sent after the despawn. The host's isn't, so it uses the size the server read.
        if (CurrentHealth.Value <= 0)
            InstantiateExperience(IsServerStarted ? _deathDropSize : DeathDropSize());
        if (_isReceivingUserInput)
        {
            InputManager.PawnControls.Disable();
            CurrentHealth.OnChange -= ClientOnHealthChanged;
            SetInteractTarget(null);
        }
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);

        // Pawns are pooled: don't carry knockback or movement into the next life.
        _pendingImpulse = Vector3.zero;
        _requestedVelocity = Vector3.zero;
        _snapVelocity = Vector3.zero;
        _ownerHeldSlot = Weapon.Slot.Primary;
        _pendingPickup = null;
        _lastScrollAt = float.NegativeInfinity;
        _interactTarget = null;
        // Upgrades are applied again to each new life.
        MoveSpeedMultiplier = 1f;
        RegenPerSecond = 0f;
        PickupRadiusMultiplier = 1f;
        _regenProgress = 0f;
        _lastDamagedAt = float.NegativeInfinity;
    }

    // FishNet calls this once per observer, and not only on despawn: also whenever one stops observing, as a client
    // that leaves does. Only a despawn leaves the connection among the observers. That first call comes before
    // FishNet tears the pawn down; OnStopServer comes after, too late to hand off weapon ownership.
    public override void OnDespawnServer(NetworkConnection connection)
    {
        base.OnDespawnServer(connection);

        if (_despawnHandled || !Observers.Contains(connection))
            return;
        _despawnHandled = true;

        // Held weapons drop where they are.
        ServerDropWeapon(_primaryWeapon);
        ServerDropWeapon(_secondaryWeapon);

        // Guard: ControllingPlayer may not be set if the pawn is despawned before initialization.
        if (ControllingPlayer.Value != null)
            ControllingPlayer.Value.ControlledPawn.Value = null;
    }


    // --------------------- TICK ---------------------

    private void Update()
    {
        if (IsServerInitialized)
            Regenerate(Time.deltaTime);

        if (_isReceivingUserInput)
        {
            bool canAttack = false;
            if (InputManager.PawnControls.Pause.WasPressedThisFrame())
            {
                HandlePauseInput();
            }
            else if (!_isPaused && InputManager.PawnControls.Reload.IsPressed())
            {
                HandleReload(TimeManager.GetPreciseTick(TickType.Tick));
            }
            else
            {
                canAttack = !_isPaused;
            }

            if (!_isPaused && InputManager.PawnControls.Interact.WasPressedThisFrame())
                TryInteract();

            SetInteractTarget(_isPaused ? null : FindInteractable());

            if (!_isPaused)
                HandleWeaponSwitchInput();

            // Weapons are ticked every frame, even while attacking is blocked, so they can tell
            // a fresh click from a held button.
            HandleTriggers(canAttack);
        }

        var movementData = GetMovementInputData();

        Rotate(movementData);
        Move(movementData);
    }

    // --------------------- NETWORKED HEALTH PROCESSING ---------------------

    private void ClientOnHealthChanged(int prev, int next, bool asServer)
    {
        // A host keeps one copy of the value, so its client-side callback arrives with prev == next.
        // The host reacts to the server-side callback instead; remote clients to the client-side one.
        if (asServer != IsHostStarted) return;

        NetworkUIEvents.OnLocalHealthChanged?.Invoke(next);
        if (next < prev)
            NetworkUIEvents.OnLocalDamageTaken?.Invoke(prev - next);
        if (next <= 0)
            NetworkUIEvents.OnLocalDeath?.Invoke();
        else if (next > prev)
            NetworkUIEvents.OnLocalHeal?.Invoke(next - prev);
    }


    // --------------------- INPUT PROCESSING ---------------------

    private MovementInputData GetMovementInputData()
    {
        if (!_isReceivingUserInput || _isPaused)
            return new();

        float sensitivity = SettingsManager.Instance.Settings.MouseSensitivity;
        Vector2 rawLook = InputManager.PawnControls.Look.ReadValue<Vector2>();
        Vector2 look = rawLook * sensitivity;

        #if UNITY_WEBGL && !UNITY_EDITOR
            // Mouse-look delta in WebGL is ~an order of magnitude larger than editor/standalone, so scale it down.
            const float webGLLookDivisor = 10f;
            look /= webGLLookDivisor;
        #endif

        return new MovementInputData
        {
            Move = InputManager.PawnControls.Move.ReadValue<Vector2>(),
            Jump = InputManager.PawnControls.Jump.IsPressed(),
            Look = look,
        };
    }

    private void HandlePauseInput()
    {
        if (!_isReceivingUserInput)
            return;

        if (_isPaused)
        {
            LocalUIEvents.OnUnpause?.Invoke();
        }
        else
        {
            LocalUIEvents.OnPause?.Invoke();
        }
    }

    private void OnPause()
    {
        if(_isPaused)
            return;

        _isPaused = true;
        InputManager.PawnControls.Disable();
        // The pause key still closes the menu.
        InputManager.PawnControls.Pause.Enable();
    }

    private void OnUnpause()
    {
        if(!_isPaused)
            return;

        _isPaused = false;
        InputManager.PawnControls.Enable();
    }


    // --------------------- WEAPON ---------------------

    private Weapon.Slot HeldSlot => IsOwner ? _ownerHeldSlot : _syncedHeldSlot.Value;

    private Weapon WeaponIn(Weapon.Slot slot) => slot switch
    {
        Weapon.Slot.Primary => _primaryWeapon,
        Weapon.Slot.Secondary => _secondaryWeapon,
        _ => null,
    };

    private static Weapon.Slot OtherSlot(Weapon.Slot slot) =>
        slot == Weapon.Slot.Primary ? Weapon.Slot.Secondary : Weapon.Slot.Primary;

    /// <summary>Shows the weapon in hand, stows the other, and tells the local player's HUD.</summary>
    private void RefreshHeldWeapon()
    {
        Weapon.Slot held = HeldSlot;
        if (_primaryWeapon != null)
            _primaryWeapon.SetStowed(held != Weapon.Slot.Primary);
        if (_secondaryWeapon != null)
            _secondaryWeapon.SetStowed(held != Weapon.Slot.Secondary);

        if (_isReceivingUserInput)
            NetworkUIEvents.OnLocalWeaponsChanged?.Invoke(PrimaryWeapon, StowedWeapon);
    }

    private void OnSyncedHeldSlotChanged(Weapon.Slot prev, Weapon.Slot next, bool asServer)
    {
        // The owner already shows its own choice.
        if (!IsOwner)
            RefreshHeldWeapon();
    }

    private void HandleTriggers(bool canAttack)
    {
        var attack = InputManager.PawnControls.Attack;

        Weapon held = PrimaryWeapon;
        if (held != null)
            held.TickTrigger(this, canAttack && attack.IsPressed(), canAttack && attack.WasPressedThisFrame());
    }

    private void HandleReload(PreciseTick tick)
    {
        Weapon held = PrimaryWeapon;
        if (held != null)
            held.TryReload(this, tick);
    }

    private void HandleWeaponSwitchInput()
    {
        var controls = InputManager.PawnControls;
        bool switchPressed = controls.Next.WasPressedThisFrame() || controls.Previous.WasPressedThisFrame();

        if (controls.WeaponScroll.ReadValue<float>() != 0f)
        {
            if (Time.time - _lastScrollAt >= scrollSwitchPause)
                switchPressed = true;
            _lastScrollAt = Time.time;
        }

        // With two slots, next and previous are the same switch.
        if (switchPressed && WeaponIn(OtherSlot(HeldSlot)) != null)
            SetHeldSlot(OtherSlot(HeldSlot));
    }

    /// <summary>Owner: switches hands at once, then tells everyone else.</summary>
    private void SetHeldSlot(Weapon.Slot slot)
    {
        _ownerHeldSlot = slot;
        RefreshHeldWeapon();
        ServerSetHeldSlot(slot);
    }

    [ServerRpc]
    private void ServerSetHeldSlot(Weapon.Slot slot)
    {
        if (slot is Weapon.Slot.Primary or Weapon.Slot.Secondary)
            _syncedHeldSlot.Value = slot;
    }

    // --------------------- INTERACTION ---------------------

    /// <summary>The chest or free weapon in front of the camera and within reach, or null.</summary>
    private Component FindInteractable()
    {
        if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit,
                pickupRange, pickupLayers, QueryTriggerInteraction.Ignore))
            return null;

        if (hit.collider.TryGetComponent(out SupplyChest chest))
            return chest;
        if (hit.collider.TryGetComponent(out WeaponChest weaponChest))
            return weaponChest;

        Rigidbody body = hit.collider.attachedRigidbody;
        if (body != null && body.TryGetComponent(out Weapon weapon) && weapon.IsFree)
            return weapon;

        return null;
    }

    /// <summary>Opens the chest or picks up the weapon in front of the camera.</summary>
    private void TryInteract()
    {
        switch (FindInteractable())
        {
            case SupplyChest chest:
                chest.Open(this);
                break;
            case WeaponChest weaponChest:
                weaponChest.Open(this);
                break;
            case Weapon weapon:
                PickUp(weapon);
                break;
        }
    }

    private void SetInteractTarget(Component target)
    {
        if (ReferenceEquals(target, _interactTarget))
            return;

        _interactTarget = target;
        NetworkUIEvents.OnLocalInteractTargetChanged?.Invoke(target);
    }

    private void PickUp(Weapon weapon)
    {
        // An empty slot first, so no weapon is thrown away; with both full, the one in hand.
        // Either way the new weapon goes in hand, once its hold arrives (see Equip).
        Weapon.Slot slot = _primaryWeapon == null ? Weapon.Slot.Primary
            : _secondaryWeapon == null ? Weapon.Slot.Secondary
            : HeldSlot;
        _pendingPickup = weapon;
        ServerPickUpWeapon(weapon, slot);
    }

    /// <summary>
    /// Client-authoritative: the server trusts that the weapon was in reach and the owner's choice of slot.
    /// A weapon already in that slot drops where the new weapon lay.
    /// </summary>
    [ServerRpc]
    private void ServerPickUpWeapon(Weapon weapon, Weapon.Slot slot)
    {
        // Another pawn may have claimed it first.
        if (weapon == null || !weapon.IsFree)
            return;
        if (slot is not (Weapon.Slot.Primary or Weapon.Slot.Secondary))
            return;

        // Hold the new weapon first, so its collider is off before the old one lands in its place.
        Weapon replaced = WeaponIn(slot);
        Vector3 position = weapon.transform.position;
        Quaternion rotation = weapon.transform.rotation;
        weapon.ServerHold(this, slot);
        if (replaced != null)
            replaced.ServerDrop(position, rotation);

        _syncedHeldSlot.Value = slot;
    }

    [Server]
    private void ServerSpawnStartingWeapon(Weapon prefab, Weapon.Slot slot)
    {
        if (prefab == null)
            return;

        Weapon weapon = Instantiate(prefab, holdPoint.position, holdPoint.rotation);
        Spawn(weapon.gameObject, Owner);
        weapon.ServerHold(this, slot);
    }

    [Server]
    private void ServerDropWeapon(Weapon weapon)
    {
        // A leaving player's weapons are theirs, so FishNet despawns them with the player, before or after this pawn.
        if (weapon != null && weapon.IsSpawned && !weapon.Owner.Disconnecting)
            weapon.ServerDrop(weapon.transform.position, weapon.transform.rotation);
    }


    // --------------------- MOVEMENT ---------------------

    private void Move(MovementInputData inputData)
    {
        // We're using NetworkTransform to sync Pawns' movement over the network,
        // which requires the the CharacterController to be disabled.
        if (!IsOwner)
            return;
        
        MovementState state = new MovementState
        {
            Velocity = controller.velocity,
            RequestedVelocity = _requestedVelocity,
            SnapVelocity = _snapVelocity,
            Position = controller.transform.TransformPoint(controller.center) - Vector3.up * (controller.height / 2f),
            Radius = controller.radius,
            SkinWidth = controller.skinWidth,
            SlopeLimit = controller.slopeLimit,
            Rotation = transform.rotation,
        };

        _requestedVelocity = MovementProcessor.Process
        (
            in state,
            in inputData,
            in config,
            ref _jumpState,
            _pendingImpulse,
            MoveSpeedMultiplier,
            Time.deltaTime,
            out _snapVelocity
        );
        _pendingImpulse = Vector3.zero;

        controller.Move((_requestedVelocity + _snapVelocity) * Time.deltaTime);
    }


    // --------------------- ROTATION -----------------------

    void Rotate(MovementInputData inputData)
    {
        // this should eventually use a stream of inputs and reconciliations to turn the pawn
        // and camera
        if (!_isReceivingUserInput) return;

        float mouseX = inputData.Look.x;
        float mouseY = inputData.Look.y;

        // Rotate Body (Yaw)
        _horizontalRotation += mouseX;
        characterControllerTransform.rotation = Quaternion.Euler(0f, _horizontalRotation, 0f);

        // Rotate Camera / Aim Point (Pitch)
        _verticalRotation -= mouseY;
        _verticalRotation = Mathf.Clamp(_verticalRotation, -90f, 90f);
        cameraTransform.rotation = Quaternion.Euler(_verticalRotation, _horizontalRotation, 0f);
    }
}
