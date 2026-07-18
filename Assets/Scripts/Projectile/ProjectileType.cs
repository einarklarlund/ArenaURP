using UnityEngine;

/// <summary>
/// Identity of a projectile kind (bullet, grenade, laser, arc, homing): the <see cref="ScriptableTypeObject.InstancePrefab"/>
/// to spawn plus any data that CANNOT live on the prefab (future: display name, icon, SFX — the UI-facing
/// identity). ALL gameplay config lives on the prefab's components (prefab-per-kind): motion strategy + speed
/// on <c>NetworkedMotion</c>, radius/lifetime on <c>ServerCollision</c>, max HP on <c>HealthController</c>, damage
/// on <c>DamageController</c>, hit reactions as <c>IHitMessageHandler</c> components (knockback/hitstun/redirect/
/// instantiate), spawn/VFX prefab lists on <c>SpawnNetworkObjects</c> / <c>InstantiationController</c>. Every peer
/// instantiates the same prefab, so config is present everywhere with
/// nothing on the wire — and being plain component data, it is tunable at runtime per instance.
/// New kinds = a new prefab composing the generic components + a new asset of this type pointing at it.
/// </summary>
[CreateAssetMenu(menuName = "Projectiles/Type", fileName = "ProjectileType")]
public sealed class ProjectileType : ScriptableTypeObject
{
}
