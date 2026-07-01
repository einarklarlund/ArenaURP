using UnityEngine;

/// <summary>
/// Static factory for constructing operation commands IN CODE — used by tests and by any runtime code that
/// composes operations (e.g. a powerup contributing a bonus <see cref="DealDamage"/>). Human-authored
/// operations are instead selected as data via <c>[SerializeReference, SubclassSelector]</c> in the inspector;
/// this is the code counterpart, giving named, discoverable constructors for the same command classes so a
/// caller doesn't sprinkle <c>new DealDamage(...)</c> across the codebase.
/// </summary>
public static class Commands
{
    public static DealDamage DealDamage(bool useInvokerDamage = true, int amount = 10, bool despawnAfter = true)
        => new(useInvokerDamage, amount, despawnAfter);

    public static LoseHealth LoseHealth(int amount = 1) => new(amount);

    public static Ricochet Ricochet() => new();

    public static DeflectProjectile Deflect() => new();

    public static SpawnProjectiles SpawnProjectiles(ProjectileType type, int count = 1, float spreadDegrees = 0f)
        => new(type, count, spreadDegrees);

    public static SpawnVfx SpawnVfx(GameObject prefab) => new(prefab);

    public static ApplyDebuff ApplyDebuff(DebuffType debuff = DebuffType.Slow) => new(debuff);
}
