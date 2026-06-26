using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Base for a Type Object that owns a networked INSTANCE prefab and knows how to build + initialize one
/// (Type Object + Flyweight). A type holds the per-kind config; <see cref="Create"/> pooled-instantiates
/// the instance prefab and lets the concrete type assign its components' defaults from that config — so
/// the thing that spawns an instance does NOT need to know the instance's component composition, and the
/// components stay decoupled from the type. (The networked Spawn is left to the caller so it can choose
/// the predicting vs. authoritative path.)
/// </summary>
public abstract class ScriptableTypeObject : ScriptableObject
{
    [Header("Instance")]
    [Tooltip("The networked prefab instantiated for this type.")]
    [SerializeField] private NetworkObject instancePrefab;

    public NetworkObject InstancePrefab => instancePrefab;

    /// <summary>
    /// Pooled-instantiate the instance prefab and initialize its components from this type's data. Returns
    /// the configured, NOT-yet-network-spawned instance — the caller calls <c>ServerManager.Spawn</c>.
    /// </summary>
    public NetworkObject Create(NetworkManager manager, Vector3 position, Quaternion rotation, bool asServer)
    {
        NetworkObject nob = manager.GetPooledInstantiated(instancePrefab, position, rotation, asServer);
        Initialize(nob.gameObject);
        return nob;
    }

    /// <summary>Assign default values to the freshly-instantiated instance's components from this type's data.</summary>
    protected abstract void Initialize(GameObject instance);
}
