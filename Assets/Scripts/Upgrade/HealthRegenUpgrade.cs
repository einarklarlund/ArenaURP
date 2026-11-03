using UnityEngine;

/// <summary>Regenerates health once the pawn has gone a while without taking damage (see Pawn's regen delay).</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Health Regen")]
public sealed class HealthRegenUpgrade : Upgrade
{
    [Tooltip("Health per second added per stack.")]
    [SerializeField] private float perSecond = 3f;

    public override void ApplyTo(Pawn pawn)
    {
        // Server only: health is server-authoritative, and the server ticks the regen.
        if (!pawn.IsServerInitialized) return;

        pawn.RegenPerSecond += perSecond;
    }
}
