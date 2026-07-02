using FishNet.Object;
using UnityEngine;

/// <summary>
/// Base for a Type Object that names a kind and owns its networked INSTANCE prefab. A type is IDENTITY plus
/// whatever cannot live on the prefab (display name, icon, SFX — UI-facing data); ALL gameplay config is
/// authored on the prefab's own components, so every peer has it baked with nothing on the wire. It does NOT
/// instantiate or spawn — spawn sites pooled-instantiate <see cref="InstancePrefab"/> themselves.
/// </summary>
public abstract class ScriptableTypeObject : ScriptableObject
{
    [Header("Instance")]
    [Tooltip("The networked prefab instantiated for this type. Its TypeObjectRoot holds this same type baked in.")]
    [SerializeField] private NetworkObject instancePrefab;

    public NetworkObject InstancePrefab => instancePrefab;

    /// <summary>
    /// Optional hook to push type data into a freshly-instantiated instance, called by the instance's
    /// <see cref="TypeObjectRoot"/> at Awake on every peer. The default is a no-op: gameplay config lives on
    /// the prefab's components, so most types have nothing to push. Override only for data that genuinely
    /// cannot be baked into the prefab.
    /// </summary>
    public virtual void Initialize(GameObject instance) { }
}
