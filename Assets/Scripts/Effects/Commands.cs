/// <summary>
/// Static factory for constructing operation commands IN CODE — used by tests and by any runtime code that
/// composes operations. Human-authored operations are instead selected as data via
/// <c>[SerializeReference, EffectSelector]</c> in the inspector; this is the code counterpart, giving named,
/// discoverable constructors for the same command classes so a caller doesn't sprinkle <c>new DealDamage()</c>
/// across the codebase.
///
/// Most commands are now stateless and parameterless (their config lives on components read from the context).
/// The spawn commands take the component that holds their prefab list (<see cref="SpawnableObjects"/> /
/// <see cref="InstantiableObjects"/>).
/// </summary>
public static class Commands
{
    public static DealDamage DealDamage() => new();

    public static LoseHealth LoseHealth() => new();

    public static Ricochet Ricochet() => new();

    public static DeflectProjectile Deflect() => new();

    public static SpawnNetworkObjects SpawnNetworkObjects(SpawnableObjects source) => new(source);

    public static InstantiateGameObjects InstantiateGameObjects(InstantiableObjects source) => new(source);

    public static ApplyDebuff ApplyDebuff() => new();
}
