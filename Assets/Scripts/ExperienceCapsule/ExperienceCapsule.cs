using FishNet.CodeGenerating;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;


// All data and logic for a shoot-able experience capsule that
// spawns a bunch of experience pick-ups after being shot a few times
public class ExperienceCapsule : NetworkBehaviour
{

    [Header("Health")]

    // im pretty sure that this doesn't need to be synced and can be tracked separately on server/client
    [AllowMutableSyncType]
    public SyncVar<int> CurrentHealth = new(4, new SyncTypeSettings(WritePermission.ServerOnly));

    [Header("Drops")]
    public int numberOfDrops = 15;
    [Tooltip("Instantiated locally on every observing client, not spawned over the network.")]
    public Pickup drop;
    public float dropLaunchSpeed = 15f;

    [Header("VFX Objects")]
    [SerializeField] private MeshRenderer bodyRenderer;
    [SerializeField] private GameObject damageVfx;
    [SerializeField] private GameObject deathVfx;

    [Header("Color effects")]
    [Tooltip("The color that the body's material changes to when damaged.")]
    [SerializeField] private Color damagedColor = Color.red;

    [Tooltip("The default albedo value of the body's material.")]
    [SerializeField] private Color defaultAlbedo = Color.white;

    [Tooltip("The default emission value of the body's material.")]
    [SerializeField] private Color defaultEmission = Color.black;

    [Tooltip("The amount of time before the body renderer returns to its original color")]
    [SerializeField] private float damageColorDuration = 0.75f;

    // State for color changing
    private float? _damageDealtTime;

    // Guards against spawning drops twice when more hits land before the server's despawn arrives
    private bool _dropsSpawned;

    // This peer's guess at the health: the synced health less the hits seen here that the server hasn't
    // reported yet. A shooter's own hits land well before the synced health counts them.
    private int _predictedHealth;

    public override void OnStartClient()
    {
        base.OnStartClient();
        _dropsSpawned = false; // capsules are pooled
        _predictedHealth = int.MaxValue;
    }

    // ----------------------- API - DAMAGE -----------------------

    public void TakeDamage(DamageInfo damageInfo)
    {
        if (CurrentHealth.Value <= 0) return;

        // Play VFX client-side
        if (IsClientInitialized)
        {
            PlayVfx(damageVfx, damageInfo.HitPoint, damageInfo.Direction);
            _damageDealtTime = Time.time;

            // Both values have already taken off every hit they know of, so the lower one is the closer
            // guess, and a hit the synced health has caught up on isn't counted twice.
            _predictedHealth = Mathf.Min(_predictedHealth, CurrentHealth.Value) - damageInfo.Amount;
            if (_predictedHealth <= 0 && !_dropsSpawned)
            {
                PlayVfx(deathVfx, damageInfo.HitPoint, damageInfo.Direction);
                SpawnDrops();
            }
        }

        // Track state server-size
        if (IsServerInitialized)
        {
            if (damageInfo.Amount > 0)
                CurrentHealth.Value = Mathf.Max(0, CurrentHealth.Value - damageInfo.Amount);

            if (CurrentHealth.Value <= 0)
                Despawn(DespawnType.Pool);
        }
    }

    // ----------------------- TICK -----------------------

    private void Update()
    {
        // Handle color changing
        if (_damageDealtTime != null)
            if (_damageDealtTime <= Time.time + damageColorDuration)
                ApplyDamageColor();
            else
                ResetDamageColor();
    }

    // ----------------------- SPAWNING -----------------------

    /// <summary>
    /// Nobody is given the drops: each client's copies go to whoever comes near them, and give experience only on
    /// that player's own client.
    /// </summary>
    private void SpawnDrops()
    {
        _dropsSpawned = true;

        for (int i = 0; i < numberOfDrops; i++)
            SpawnDrop();
    }

    private void SpawnDrop()
    {
        Vector3 direction = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(0.5f, 1f),
            Random.Range(-1f, 1f)
        ).normalized;

        Pickup instance = Instantiate(drop, transform.position, Quaternion.LookRotation(direction));
        instance.Launch(null, dropLaunchSpeed * direction);
    }

    // ----------------------- VFX -----------------------

    private void ApplyDamageColor()
    {
        if (_damageDealtTime == null)
            return;

        float damageDealtTime = (float) _damageDealtTime;
        float elapsedTimeSinceDamage = Time.time - damageDealtTime;
        float damageColorLerpFactor = elapsedTimeSinceDamage / damageColorDuration;

        Color damageAlbedo = Color.Lerp(damagedColor, defaultAlbedo, damageColorLerpFactor);
        Color damageEmission = Color.Lerp(damagedColor, defaultEmission, damageColorLerpFactor);

        SetBodyColors(damageAlbedo, damageEmission);
    }

    private void ResetDamageColor()
    {
        SetBodyColors(defaultAlbedo, defaultEmission);
        _damageDealtTime = null;
    }

    private void SetBodyColors(Color albedo, Color emission)
    {
        bodyRenderer.material.SetColor("_BaseColor", albedo);
        if (bodyRenderer.material.HasColor("_Emission"))
            bodyRenderer.material.SetColor("_Emission", emission);
        else
            bodyRenderer.material.SetColor("_EmissionColor", emission);
    }

    private void PlayVfx(GameObject gameObject, Vector3 atPoint, Vector3 inDirection)
    {
        var effect = Instantiate(gameObject, atPoint, Quaternion.LookRotation(inDirection));
    }
}
