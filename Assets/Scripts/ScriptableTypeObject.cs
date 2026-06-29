using FishNet.Object;
using UnityEngine;

/// <summary>
/// Base for a Type Object that owns a networked INSTANCE prefab and knows how to configure one (Type Object
/// + Flyweight). A type is data: the per-kind config plus the <see cref="InstancePrefab"/> template. It does
/// NOT instantiate or spawn — the spawner pooled-instantiates <see cref="InstancePrefab"/>, and the prefab's
/// generic <see cref="TypeObjectRoot"/> calls <see cref="Initialize"/> on itself at Awake (on every peer).
/// The components stay decoupled from the type; only the root references it.
/// </summary>
public abstract class ScriptableTypeObject : ScriptableObject
{
    [Header("Instance")]
    [Tooltip("The networked prefab instantiated for this type. Its TypeObjectRoot holds this same type baked in.")]
    [SerializeField] private NetworkObject instancePrefab;

    public NetworkObject InstancePrefab => instancePrefab;

    /// <summary>
    /// Push this type's data into the freshly-instantiated instance's components — the composition lives
    /// here, not in any component. Called by the instance's <see cref="TypeObjectRoot"/> at Awake; public so
    /// the root can invoke it. Runs on every peer, so it must be safe off-server (server-only components gate
    /// their own behaviour) and idempotent enough that the root's once-guard is the only call it needs.
    /// </summary>
    public abstract void Initialize(GameObject instance);
}
