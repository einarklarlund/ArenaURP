using UnityEngine;

/// <summary>
/// Identity of a projectile kind (bullet, grenade, laser, arc, homing): the <see cref="ScriptableTypeObject.InstancePrefab"/>
/// to spawn plus any data that CANNOT live on the prefab (future: display name, icon, SFX — the UI-facing
/// identity). ALL gameplay config lives on the prefab's components (prefab-per-kind): motion strategy + speed
/// on <c>NetworkedMotion</c>, radius/lifetime/hit-operation lists on <c>ServerCollision</c>, max HP on
/// <c>Health</c>, damage on <c>Damage</c>, spawn/VFX prefab lists on <c>SpawnableObjects</c> /
/// <c>InstantiableObjects</c>. Every peer instantiates the same prefab, so config is present everywhere with
/// nothing on the wire — and being plain component data, it is tunable at runtime per instance.
/// New kinds = a new prefab composing the generic components + a new asset of this type pointing at it.
/// </summary>
[CreateAssetMenu(menuName = "Projectiles/Type", fileName = "ProjectileType")]
public sealed class ProjectileType : ScriptableTypeObject
{
}
