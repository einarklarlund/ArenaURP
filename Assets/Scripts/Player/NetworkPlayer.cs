using System;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Represents the network identity and persistent data of an individual participant
/// in a match. Used for both human players and bots.
/// </summary>
public sealed class NetworkPlayer : NetworkBehaviour
{
    public static NetworkPlayer LocalInstance { get; private set; }

    public readonly SyncVar<string> Username = new();
    public readonly SyncVar<string> ID = new(Guid.NewGuid().ToString());
    public readonly SyncVar<bool> IsReady = new();
    public readonly SyncVar<Pawn> ControlledPawn = new();
    public readonly SyncVar<double> RespawnTimeEnd = new();
    public readonly SyncVar<int> Score = new();
    public readonly SyncVar<Pawn> CharacterPrefab = new();

    /// <summary>The experience the owner last reported. Every peer reads it to size this player's death drop.</summary>
    public readonly SyncVar<int> ReportedExperience = new();
    /// <summary>The upgrades picked this life, in order. An id that appears twice is two stacks.</summary>
    public readonly SyncList<byte> Upgrades = new();
    /// <summary>Upgrades still to pick: from level-ups while alive, or before spawning.</summary>
    public readonly SyncVar<int> UpgradePicksPending = new(new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));
    /// <summary>The two catalogue ids on offer; -1 where there is nothing.</summary>
    public readonly SyncVar<Vector2Int> UpgradeOffer = new(NoOffer, new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

    [Serializable]
    private struct AmmoCapacity
    {
        public AmmoType type;
        public int max;
        public int starting;
    }

    [Header("Ammo")]
    [SerializeField] private AmmoCapacity[] ammoCapacities =
    {
        new() { type = AmmoType.Bullet, max = 255, starting = 100 },
        new() { type = AmmoType.Shell, max = 55, starting = 0 },
        new() { type = AmmoType.Bolt, max = 55, starting = 0 },
        new() { type = AmmoType.Explosive, max = 55, starting = 0 },
        new() { type = AmmoType.Energy, max = 55, starting = 0 },
    };

    // Ammo counts are client-authoritative: only the owner spends or adds ammo, and the counts
    // aren't synced. Bots are owned by the host, so the host keeps their counts.
    private readonly int[] _ammo = new int[Enum.GetValues(typeof(AmmoType)).Length];

    [Header("Upgrades")]
    [SerializeField] private UpgradeCatalogue upgradeCatalogue;
    [Tooltip("Picks before every spawn, on top of one per level.")]
    [SerializeField] private int baseRespawnPicks = 2;
    [Tooltip("Seconds a dead player has to pick before the rest are picked for them.")]
    [SerializeField] private float respawnPickTimeout = 15f;

    // Experience is client-authoritative like ammo: only the owner counts it, and reports it to the server.
    private int _experience;

    // Raised by AmmoCapacityUpgrade; back to 1 when the upgrades are cleared.
    private float _ammoCapacityMultiplier = 1f;

    // Server only: the pawn whose death this player listens for, and whether it is dead and picking.
    private Pawn _watchedPawn;
    private bool _waitingToSpawn;

    // Owner only: the pawn whose death takes this player's death drop off its experience.
    private Pawn _ownedPawn;

    private static readonly Vector2Int NoOffer = new(-1, -1);

    [NonSerialized] public bool IsBot;

    public Action<NetworkPlayer> OnDespawn;

    public UpgradeCatalogue UpgradeCatalogue => upgradeCatalogue;

    /// <summary>Server only: dead (or not yet spawned) and picking upgrades. Spawns itself when done.</summary>
    public bool IsPickingBeforeSpawn => _waitingToSpawn;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        Upgrades.OnChange += OnUpgradesChanged;
        ControlledPawn.OnChange += OnPawnAcquired;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        Upgrades.OnChange -= OnUpgradesChanged;
        ControlledPawn.OnChange -= OnPawnAcquired;
        OwnerWatchPawn(null);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        ServerWatchPawn(null);
        OnDespawn?.Invoke(this);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsBot)
            GiveOwnership(InstanceFinder.ClientManager.Connection);

        // Filled on every peer because a bot's ownership may still be moving to the host here;
        // only the owner's copy is ever spent.
        ResetAmmo();
        ControlledPawn.OnChange += OnControlledPawnChanged;

        if (!IsOwner || IsBot) return;

        LocalInstance = this;
        NotifyAllAmmoChanged(); // the fill above happened before this was the local player
        NotifyExperienceChanged();

        RespawnTimeEnd.OnChange += ClientOnRespawnTimeEndChanged;
        UpgradePicksPending.OnChange += ClientOnOfferChanged;
        UpgradeOffer.OnChange += ClientOnOfferChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        ControlledPawn.OnChange -= OnControlledPawnChanged;

        if (LocalInstance != this) return;

        RespawnTimeEnd.OnChange -= ClientOnRespawnTimeEndChanged;
        UpgradePicksPending.OnChange -= ClientOnOfferChanged;
        UpgradeOffer.OnChange -= ClientOnOfferChanged;
        LocalInstance = null;
    }

    private void ClientOnOfferChanged<T>(T prev, T next, bool asServer)
    {
        // A host gets both the server- and client-side callback; react to one.
        if (asServer != IsHostStarted) return;
        NetworkUIEvents.OnLocalUpgradeOfferChanged?.Invoke();
    }

    private void ClientOnRespawnTimeEndChanged(double prev, double next, bool asServer)
    {
        if (asServer) return;
        if (next > 0) NetworkUIEvents.OnRespawnTimerStarted?.Invoke(next);
    }

    private void OnControlledPawnChanged(Pawn prev, Pawn next, bool asServer)
    {
        // A host gets both the server- and client-side callback; react to one.
        if (asServer != IsHostStarted) return;
        if (!IsOwner || next == null) return;

        // Each new life starts with fresh ammo.
        ResetAmmo();
    }

    // -------------------- AMMO --------------------

    public int GetAmmo(AmmoType type) => _ammo[(int)type];

    public int GetMaxAmmo(AmmoType type) => Mathf.RoundToInt(FindAmmoCapacity(type).max * _ammoCapacityMultiplier);

    /// <summary>Raises every ammo capacity by <paramref name="bonus"/> of its base: 0.4 is +40%.</summary>
    public void AddAmmoCapacity(float bonus) => _ammoCapacityMultiplier += bonus;

    public bool TryConsumeAmmo(AmmoType type, int amount)
    {
        if (_ammo[(int)type] < amount)
            return false;

        _ammo[(int)type] -= amount;
        NotifyAmmoChanged(type);
        return true;
    }
    public void AddAmmo(AmmoType type, int amount)
    {
        _ammo[(int)type] = Mathf.Min(_ammo[(int)type] + amount, GetMaxAmmo(type));
        NotifyAmmoChanged(type);
    }

    public void ResetAmmo()
    {
        Array.Clear(_ammo, 0, _ammo.Length);
        foreach (AmmoCapacity capacity in ammoCapacities)
            _ammo[(int)capacity.type] = Mathf.Min(capacity.starting, GetMaxAmmo(capacity.type));

        NotifyAllAmmoChanged();
    }

    private void NotifyAllAmmoChanged()
    {
        for (int i = 0; i < _ammo.Length; i++)
            NotifyAmmoChanged((AmmoType)i);
    }

    private void NotifyAmmoChanged(AmmoType type)
    {
        // Only the local human player drives the HUD; bots share the host's ownership.
        if (LocalInstance != this)
            return;

        NetworkUIEvents.OnLocalAmmoChanged?.Invoke(type, _ammo[(int)type]);
    }

    private AmmoCapacity FindAmmoCapacity(AmmoType type)
    {
        foreach (AmmoCapacity capacity in ammoCapacities)
        {
            if (capacity.type == type)
                return capacity;
        }

        return default;
    }

    // -------------------- EXPERIENCE --------------------

    public int Experience => _experience;

    public int Level => Levels.ForExperience(_experience);

    /// <summary>Owner only. Experience never goes below 0.</summary>
    public void AddExperience(int amount)
    {
        _experience = Mathf.Max(0, _experience + amount);
        NotifyExperienceChanged();
        ServerReportExperience(_experience);
    }

    /// <summary>
    /// Owner only: the experience this player's pawn drops on death. Not reported: the server takes the same
    /// amount off its own copy when the death is confirmed (ServerOnPawnDied), so a report would take it twice.
    /// </summary>
    private void LoseExperience()
    {
        _experience = Mathf.Max(0, _experience - Levels.DeathDrop(_experience));
        NotifyExperienceChanged();
    }

    [ServerRpc]
    private void ServerReportExperience(int experience)
    {
        int levelBefore = Levels.ForExperience(ReportedExperience.Value);
        ReportedExperience.Value = experience;

        // A level-up while alive is one pick each. Picks before spawning were counted at death.
        int gained = Levels.ForExperience(experience) - levelBefore;
        if (gained > 0 && !_waitingToSpawn && ControlledPawn.Value != null)
            ServerAddPicks(gained);
    }

    [TargetRpc]
    private void TargetResetExperience(NetworkConnection connection)
    {
        _experience = 0;
        NotifyExperienceChanged();
    }

    private void NotifyExperienceChanged()
    {
        // Only the local human player drives the HUD; bots share the host's ownership.
        if (LocalInstance != this)
            return;

        NetworkUIEvents.OnLocalExperienceChanged?.Invoke(_experience);
    }

    // -------------------- UPGRADES --------------------

    /// <summary>Owner: takes the upgrade in offer slot 0 or 1.</summary>
    [ServerRpc]
    public void PickUpgrade(int slot) => ServerPick(slot);

    /// <summary>
    /// One more pick, e.g. from RerollUpgrade. Called while the pick that granted it is being applied (the server's
    /// SyncList callback runs inside ServerPick), so that pick's own decrement and new offer follow.
    /// </summary>
    [Server]
    public void ServerGrantPick() => UpgradePicksPending.Value++;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>Debug menu: grants an upgrade outright, skipping offers and pending picks. Respects MaxStacks.</summary>
    [ServerRpc]
    public void DebugGrantUpgrade(int id)
    {
        if (id < 0 || id >= upgradeCatalogue.Count || upgradeCatalogue[id] == null) return;
        Upgrade upgrade = upgradeCatalogue[id];
        if (upgrade.MaxStacks > 0 && UpgradeCatalogue.StacksOf(id, Upgrades) >= upgrade.MaxStacks) return;

        Upgrades.Add((byte)id); // applied on every peer by OnUpgradesChanged, like a pick

        // RerollUpgrade grants a pick from inside its apply; outside a pick nothing would roll its offer.
        if (UpgradePicksPending.Value > 0 && UpgradeOffer.Value == NoOffer)
            ServerRollOffer();
    }

    /// <summary>Debug menu: one more pick, offered the normal way.</summary>
    [ServerRpc]
    public void DebugGrantPick() => ServerAddPicks(1);
#endif

    /// <summary>Applies every upgrade picked this life to <paramref name="pawn"/>, once per stack.</summary>
    public void ApplyUpgradesTo(Pawn pawn)
    {
        foreach (byte id in Upgrades)
            upgradeCatalogue[id].ApplyTo(pawn);
    }

    /// <summary>For a weapon this player's pawn just equipped.</summary>
    public void ApplyUpgradesTo(Weapon weapon)
    {
        foreach (byte id in Upgrades)
            upgradeCatalogue[id].ApplyTo(weapon);
    }

    /// <summary>For a projectile this player's pawn just fired.</summary>
    public void ApplyUpgradesTo(Projectile projectile)
    {
        foreach (byte id in Upgrades)
            upgradeCatalogue[id].ApplyTo(projectile);
    }

    /// <summary>For a supply chest this player is opening.</summary>
    public void ApplyUpgradesTo(SupplyChest chest)
    {
        foreach (byte id in Upgrades)
            upgradeCatalogue[id].ApplyTo(chest);
    }

    /// <summary>For a weapon chest this player opened.</summary>
    public void ApplyUpgradesTo(WeaponChest chest)
    {
        foreach (byte id in Upgrades)
            upgradeCatalogue[id].ApplyTo(chest);
    }

    /// <summary>For a chest spawned for this player.</summary>
    public void ApplyUpgradesTo(ChestTimer chest)
    {
        foreach (byte id in Upgrades)
            upgradeCatalogue[id].ApplyTo(chest);
    }

    /// <summary>The player is dead (or about to spawn for the first time): it rebuilds its upgrades before spawning.</summary>
    [Server]
    public void ServerBeginRespawn()
    {
        Upgrades.Clear();
        UpgradePicksPending.Value = 0;
        _waitingToSpawn = true;
        RespawnTimeEnd.Value = TimeManager.TicksToTime() + respawnPickTimeout;
        ServerAddPicks(baseRespawnPicks + Levels.ForExperience(ReportedExperience.Value));

        if (IsBot)
            ServerAutoPickRemaining();
    }

    /// <summary>Takes a random side of every offer still open.</summary>
    [Server]
    public void ServerAutoPickRemaining()
    {
        while (UpgradePicksPending.Value > 0 && UpgradeOffer.Value != NoOffer)
            ServerPick(UnityEngine.Random.Range(0, 2));

        // Nothing left to offer: the rest of the picks are forfeit.
        if (UpgradePicksPending.Value > 0)
        {
            UpgradePicksPending.Value = 0;
            ServerFinishPicks();
        }
    }

    /// <summary>A new match: no experience, no upgrades, nothing pending.</summary>
    [Server]
    public void ServerResetProgression()
    {
        Upgrades.Clear();
        UpgradePicksPending.Value = 0;
        UpgradeOffer.Value = NoOffer;
        ReportedExperience.Value = 0;
        RespawnTimeEnd.Value = -1;
        _waitingToSpawn = false;
        TargetResetExperience(Owner);
    }

    [Server]
    private void ServerAddPicks(int count)
    {
        UpgradePicksPending.Value += count;
        if (UpgradeOffer.Value == NoOffer)
            ServerRollOffer();
    }

    [Server]
    private void ServerPick(int slot)
    {
        if (UpgradePicksPending.Value <= 0) return;

        Vector2Int offer = UpgradeOffer.Value;
        int id = slot == 0 ? offer.x : offer.y;
        if (id < 0) return;

        Upgrades.Add((byte)id); // applied on every peer by OnUpgradesChanged
        UpgradePicksPending.Value--;

        if (UpgradePicksPending.Value > 0)
            ServerRollOffer();
        else
        {
            UpgradeOffer.Value = NoOffer;
            ServerFinishPicks();
        }
    }

    [Server]
    private void ServerRollOffer()
    {
        (int first, int second) = upgradeCatalogue.RollOffer(Upgrades);
        if (first < 0)
            UpgradeOffer.Value = NoOffer;
        else
            UpgradeOffer.Value = new Vector2Int(first, second);
    }

    [Server]
    private void ServerFinishPicks()
    {
        if (!_waitingToSpawn) return;

        _waitingToSpawn = false;
        RespawnTimeEnd.Value = -1;
        MatchManager.Instance.ServerSpawn(this);
    }

    private void Update()
    {
        // The pick timeout: whatever is left is picked at random.
        if (IsServerInitialized && _waitingToSpawn && TimeManager.TicksToTime() >= RespawnTimeEnd.Value)
            ServerAutoPickRemaining();
    }

    /// <summary>Every peer applies a new pick to this player and its live pawn, and undoes everything on a clear.</summary>
    private void OnUpgradesChanged(SyncListOperation op, int index, byte previous, byte next, bool asServer)
    {
        // The server reacts to its own callback (a host's client side would repeat it); clients to theirs.
        if (!asServer && IsServerStarted) return;

        switch (op)
        {
            case SyncListOperation.Add:
                Upgrade upgrade = upgradeCatalogue[next];
                upgrade.ApplyTo(this);

                Pawn pawn = ControlledPawn.Value;
                if (pawn != null)
                {
                    upgrade.ApplyTo(pawn);
                    if (pawn.PrimaryWeapon != null) upgrade.ApplyTo(pawn.PrimaryWeapon);
                    if (pawn.StowedWeapon != null) upgrade.ApplyTo(pawn.StowedWeapon);
                }
                break;

            case SyncListOperation.Clear:
                // Upgrades only clear while dead or between matches, so there is no live pawn to undo.
                _ammoCapacityMultiplier = 1f;
                if (asServer)
                {
                    // The directors' per-player records (ExtraChestsUpgrade) are server-only.
                    ChestDirector.Instance?.ServerResetRecord(this);
                    ExperienceCapsuleDirector.Instance?.ServerResetRecord(this);
                }
                break;
        }
    }

    /// <summary>Every peer applies this life's upgrades to a new pawn. The server also listens for its death.</summary>
    private void OnPawnAcquired(Pawn prev, Pawn next, bool asServer)
    {
        if (!asServer && IsServerStarted) return;

        if (next != null)
            ApplyUpgradesTo(next);

        if (asServer)
            ServerWatchPawn(next);
        if (IsOwner)
            OwnerWatchPawn(next);
    }

    // The owner sees its pawn's death in the synced health, whether or not it predicted the killing hit.
    private void OwnerWatchPawn(Pawn pawn)
    {
        if (_ownedPawn != null)
            _ownedPawn.CurrentHealth.OnChange -= OwnerOnPawnHealthChanged;
        _ownedPawn = pawn;
        if (_ownedPawn != null)
            _ownedPawn.CurrentHealth.OnChange += OwnerOnPawnHealthChanged;
    }

    private void OwnerOnPawnHealthChanged(int prev, int next, bool asServer)
    {
        // A host keeps one copy of the value: it reacts to the server-side callback, remote clients to theirs.
        if (asServer != IsServerStarted) return;

        if (prev > 0 && next <= 0)
            LoseExperience();
    }

    [Server]
    private void ServerWatchPawn(Pawn pawn)
    {
        if (_watchedPawn != null)
            _watchedPawn.OnDeath -= ServerOnPawnDied;
        _watchedPawn = pawn;
        if (_watchedPawn != null)
            _watchedPawn.OnDeath += ServerOnPawnDied;
    }

    /// <summary>
    /// The pawn drops experience on every client when it despawns (see Pawn), and the owner takes it
    /// off its own count without reporting (OwnerOnPawnHealthChanged). The server takes the same amount off here.
    /// </summary>
    private void ServerOnPawnDied(DamageInfo damageInfo)
    {
        int experience = ReportedExperience.Value;
        ReportedExperience.Value = Mathf.Max(0, experience - Levels.DeathDrop(experience));

        if (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.During)
            ServerBeginRespawn();
    }

    // -------------------- RPCS --------------------

    [ServerRpc]
    public void ServerSetIsReady(bool value)
    {
        IsReady.Value = value;
    }

    [ServerRpc]
    public void ServerSetUsername(string input)
    {
        Username.Value = input;
    }

    [ServerRpc]
    public void ServerSetCharacter(Pawn character)
    {
        // A character is a prefab to spawn from, never a pawn that is already in the world.
        if (character != null && character.IsSpawned) return;

        CharacterPrefab.Value = character;
    }
}