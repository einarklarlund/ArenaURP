using UnityEngine;

/// <summary>
/// Handles all VFX for a pawn taking damage, including particle effects
/// and color changes.
/// </summary>
public class PawnDamageVfx : MonoBehaviour
{
    public Pawn pawn;
    [SerializeField] private MeshRenderer bodyRenderer;

    [Header("VFX Prefabs")]
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

    private static GameObjectPool damageVfxPool;
    private static GameObjectPool deathVfxPool;

    // Color changing state
    private float? _damageDealtTime;

    private void Awake()
    {
        // Initialize effect prefab pools.
        damageVfxPool ??= new GameObjectPool(damageVfx);
        deathVfxPool ??= new GameObjectPool(deathVfx);
    }

    private void Start()
    {
        pawn.OnDamageTaken += OnDamageTaken;
    }

    private void Update()
    {
        // Handle color changing
        if (_damageDealtTime != null)
            if (_damageDealtTime <= Time.time + damageColorDuration)
                ApplyDamageColor();
            else
                ResetDamageColor();
    }

    private void OnDestroy()
    {
        pawn.OnDamageTaken -= OnDamageTaken;
    }

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

    private void OnDamageTaken(DamageInfo damageInfo)
    {
        PlayVfx(damageVfxPool, damageInfo.HitPoint, damageInfo.Direction);
        // TODO - test if this works as intended by predicting death
        // if (healthController.CurrentHealth.Value - damageInfo.Amount <= 0)
        //     PlayVfx(deathVfxPool);
        _damageDealtTime = Time.time;
    }

    private void PlayVfx(GameObjectPool pool, Vector3 atPoint, Vector3 inDirection)
    {
        var effect = pool.Get(atPoint, Quaternion.LookRotation(inDirection));
        effect.GetComponent<ParticleSystem>().Play();
        if (effect.TryGetComponent<PooledLifetime>(out var lifetime))
            lifetime.Initialize(pool);
    }
}
