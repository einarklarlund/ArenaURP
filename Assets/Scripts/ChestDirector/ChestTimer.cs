using System;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Gives a chest a limited life: the server despawns it once <see cref="ExpireTime"/> passes.
/// <see cref="ChestDirector"/> spawns the chest and starts its life. Opening the chest is the job
/// of its <see cref="SupplyChest"/> or <see cref="WeaponChest"/>; if the server's expiry lands first,
/// the open doesn't happen.
/// On clients it shows the life running out on a bar above the chest, and a beam that marks
/// where the chest appeared. Both run off the synced times, so a late joiner sees them where
/// everyone else does. Only the player the chest was spawned for sees its beam, for as long as
/// that player's upgrades say; anyone can open it.
/// </summary>
public class ChestTimer : NetworkBehaviour
{
    /// <summary>
    /// When the chest's life began, on the network clock. A slot's first chest starts partway
    /// through its life, so this can be earlier than the spawn.
    /// </summary>
    public readonly SyncVar<double> StartTime = new();
    /// <summary>When the chest spawned, on the network clock: its spawn beam plays from here.</summary>
    public readonly SyncVar<double> SpawnTime = new();
    /// <summary>When the server despawns the chest, on the network clock. 0 until the life starts.</summary>
    public readonly SyncVar<double> ExpireTime = new();
    /// <summary>The player whose chest slot this fills. Null once they leave.</summary>
    public readonly SyncVar<NetworkPlayer> ForPlayer = new();

    [Header("Timer bar")]
    [Tooltip("Turned to face the camera. Holds the bar's background and fill.")]
    [SerializeField] private Transform timerBar;
    [Tooltip("Pivoted at its left end; its X scale at full life is the bar's width.")]
    [SerializeField] private Transform timerFill;
    [Tooltip("Beyond this distance from the camera the bar grows with distance, so it keeps the size on screen it has here.")]
    [SerializeField] private float timerBarFullSizeDistance = 10f;

    [Header("Spawn beam")]
    [Tooltip("Vertical, with its widthCurve thick at the bottom and thin at the top.")]
    [SerializeField] private LineRenderer beam;
    [Tooltip("Seconds, before upgrades.")]
    [SerializeField] private float beamDuration = 2.5f;

    /// <summary>Written on each client by <see cref="ForPlayer"/>'s upgrades; scales <see cref="beamDuration"/>.</summary>
    [NonSerialized] public float BeamDurationMultiplier = 1f;

    private float fullFillWidth;
    private Gradient beamColor;
    private float beamAlpha;
    private float beamWidth;

    // The server writes the times from its tick. A client's Tick is its estimate of the server's;
    // its LocalTick counts from when it connected, so it would put every chest out by that much.
    private double Now => TimeManager.TicksToTime(TickType.Tick);

    private void Awake()
    {
        fullFillWidth = timerFill.localScale.x;
        beamColor = beam.colorGradient;
        beamAlpha = beamColor.alphaKeys[0].alpha;
        beamWidth = beam.widthMultiplier;

        // Hidden until the life's times arrive.
        timerBar.gameObject.SetActive(false);
        beam.enabled = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // The player may arrive after the spawn, with the life's times.
        ForPlayer.OnChange += OnForPlayerChanged;
        ClientApplyUpgrades();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        ForPlayer.OnChange -= OnForPlayerChanged;
    }

    [Server]
    public void ServerStartLife(NetworkPlayer forPlayer, double startTime, double expireTime)
    {
        ForPlayer.Value = forPlayer;
        StartTime.Value = startTime;
        SpawnTime.Value = Now;
        ExpireTime.Value = expireTime;
    }

    private void Update()
    {
        if (ExpireTime.Value <= 0)
            return;

        if (IsServerInitialized && Now >= ExpireTime.Value)
        {
            Despawn();
            return;
        }

        if (IsClientInitialized)
        {
            UpdateTimerBar();
            UpdateBeam();
        }
    }

    private void LateUpdate()
    {
        if (!timerBar.gameObject.activeSelf)
            return;

        // Upright, and square to the view: turn only about Y.
        Camera camera = Camera.main;
        if (camera == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
            timerBar.rotation = Quaternion.LookRotation(forward);

        float distance = Vector3.Distance(camera.transform.position, timerBar.position);
        timerBar.localScale = Vector3.one * Mathf.Max(1f, distance / timerBarFullSizeDistance);
    }

    // -------------------- CLIENT --------------------

    private void OnForPlayerChanged(NetworkPlayer prev, NetworkPlayer next, bool asServer)
    {
        if (!asServer)
            ClientApplyUpgrades();
    }

    private void ClientApplyUpgrades()
    {
        BeamDurationMultiplier = 1f;
        if (ForPlayer.Value != null)
            ForPlayer.Value.ApplyUpgradesTo(this);
    }

    private void UpdateTimerBar()
    {
        double life = ExpireTime.Value - StartTime.Value;
        float left = life > 0 ? Mathf.Clamp01((float)((ExpireTime.Value - Now) / life)) : 0f;

        timerBar.gameObject.SetActive(true);
        Vector3 scale = timerFill.localScale;
        scale.x = fullFillWidth * left;
        timerFill.localScale = scale;
    }

    /// <summary>For its own player only: flashes up at spawn, then thins and fades out over the beam's duration.</summary>
    private void UpdateBeam()
    {
        bool mine = ForPlayer.Value != null && ForPlayer.Value == NetworkPlayer.LocalInstance;
        float t = (float)((Now - SpawnTime.Value) / (beamDuration * BeamDurationMultiplier));
        if (!mine || SpawnTime.Value <= 0 || t >= 1f || t < 0f)
        {
            beam.enabled = false;
            return;
        }

        float fade = 1f - t;
        beam.enabled = true;
        beam.widthMultiplier = beamWidth * fade * fade;

        GradientAlphaKey[] alphas = beamColor.alphaKeys;
        for (int i = 0; i < alphas.Length; i++)
            alphas[i].alpha = beamAlpha * fade;
        beamColor.SetKeys(beamColor.colorKeys, alphas);
        beam.colorGradient = beamColor;
    }
}
