using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tuning for an explosion. A null <see cref="VfxPrefab"/> means "no explosion".
/// </summary>
[System.Serializable]
public class ExplosionSettings
{
    [Tooltip("Visual-only prefab spawned at the explosion point. Leave empty to disable the explosion.")]
    public GameObject VfxPrefab;

    [Tooltip("Radius in world units. Pawns whose colliders overlap this sphere are affected.")]
    public float Radius = 4f;

    [Tooltip("Damage dealt at the center of the explosion, scaled down linearly to 0 at the edge.")]
    public int Damage = 1;

    [Tooltip("Velocity change (m/s) at the center of the explosion, scaled down linearly to 0 at the edge.")]
    public float KnockbackSpeed = 15f;

    [Tooltip("Extra upward velocity change (m/s) at the center, scaled by the same falloff. Lets ground hits launch pawns.")]
    public float UpwardBias = 5f;

    [Tooltip("Layers searched for pawns.")]
    public LayerMask PawnLayers = ~0;
}

/// <summary>
/// Resolves explosions locally on every peer. Explosions are not networked objects:
/// each peer that sees the triggering collision resolves the same explosion itself.
/// <list type="bullet">
/// <item>Damage goes through <see cref="Pawn.TakeDamage"/>, which only changes health on the server.</item>
/// <item>Knockback goes through <see cref="Pawn.ApplyKnockback"/>, which only moves the pawn on its owner.</item>
/// </list>
/// </summary>
public static class Explosion
{
    /// <summary>
    /// Plays the explosion's VFX and applies its damage and knockback to every pawn in range.
    /// </summary>
    /// <param name="settings">Explosion tuning. Does nothing when <see cref="ExplosionSettings.VfxPrefab"/> is null.</param>
    /// <param name="point">World-space center of the explosion.</param>
    /// <param name="normal">Normal of the surface that was hit. Used as the push direction for pawns centered on <paramref name="point"/>.</param>
    /// <param name="owningPawn">Pawn that caused the explosion. Takes knockback but no damage.</param>
    /// <param name="playVfx">Whether this peer renders (false on a dedicated server).</param>
    /// <param name="bonusDamage">Added to the center damage, e.g. by the projectile's upgrades.</param>
    public static void Resolve(
        ExplosionSettings settings,
        Vector3 point,
        Vector3 normal,
        Pawn owningPawn,
        bool playVfx,
        int bonusDamage = 0)
    {
        if (settings == null || settings.VfxPrefab == null)
            return;

        if (playVfx)
            Object.Instantiate(settings.VfxPrefab, point, Quaternion.LookRotation(normal));

        foreach (var (pawn, center) in GatherPawns(point, settings.Radius, settings.PawnLayers))
        {
            float falloff = GetFalloff(point, center, settings.Radius);
            if (falloff <= 0f)
                continue;

            Vector3 impulse = GetImpulse(point, center, normal, falloff, settings.KnockbackSpeed, settings.UpwardBias);

            // Knockback before damage: on the server, lethal damage despawns the pawn.
            pawn.ApplyKnockback(impulse);

            if (pawn == owningPawn)
                continue;

            int damage = Mathf.Max(Mathf.RoundToInt((settings.Damage + bonusDamage) * falloff), 1);

            pawn.TakeDamage(new DamageInfo()
            {
                Amount = damage,
                // The kill (score and death drop) goes to whoever caused the explosion.
                Attacker = owningPawn != null ? owningPawn.ControllingPlayer.Value : null,
                HitPoint = center,
                Direction = impulse.normalized
            });
        }
    }


    // ------------------------------ PURE HELPERS ------------------------------


    /// <summary>
    /// Linear falloff: 1 at the explosion's center, 0 at <paramref name="radius"/> and beyond.
    /// </summary>
    public static float GetFalloff(Vector3 point, Vector3 target, float radius)
    {
        if (radius <= 0f)
            return 0f;

        return 1f - Mathf.Clamp01(Vector3.Distance(point, target) / radius);
    }

    /// <summary>
    /// Velocity change for a target at <paramref name="target"/>: away from <paramref name="point"/>
    /// (or along <paramref name="normal"/> when they overlap), plus an upward bias, both scaled by <paramref name="falloff"/>.
    /// </summary>
    public static Vector3 GetImpulse(
        Vector3 point,
        Vector3 target,
        Vector3 normal,
        float falloff,
        float knockbackSpeed,
        float upwardBias)
    {
        Vector3 direction = target - point;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : normal.normalized;

        return falloff * (knockbackSpeed * direction + upwardBias * Vector3.up);
    }


    // ------------------------------ QUERIES ------------------------------


    /// <summary>
    /// Every unique pawn with a collider overlapping the sphere, paired with the center of its first such collider.
    /// </summary>
    private static List<(Pawn pawn, Vector3 center)> GatherPawns(Vector3 point, float radius, LayerMask layers)
    {
        var hits = Physics.OverlapSphere(point, radius, layers, QueryTriggerInteraction.Collide);
        var pawns = new List<(Pawn pawn, Vector3 center)>(hits.Length);

        foreach (var hit in hits)
        {
            // A pawn can have several colliders (CharacterController, observer CapsuleCollider, hitboxes).
            var pawn = hit.GetComponentInParent<Pawn>();
            if (pawn == null || pawns.Exists(entry => entry.pawn == pawn))
                continue;

            pawns.Add((pawn, hit.bounds.center));
        }

        return pawns;
    }
}
