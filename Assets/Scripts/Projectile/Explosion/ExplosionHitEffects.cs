public class ExplosionHitEffects : ProjectileDamageableImpact
{
    protected override bool ShouldAvoidDamage(IDamageable damageable)
    {
        return false;
    }
}
