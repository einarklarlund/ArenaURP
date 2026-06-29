using UnityEngine;

/// <summary>
/// The one generic composition root for any <see cref="ScriptableTypeObject"/> instance prefab. It is DUMB
/// about which kind it is: it holds a BASE-typed reference to the type baked into this prefab variant
/// (prefab-per-kind) and does nothing but trigger that type's <see cref="ScriptableTypeObject.Initialize"/>
/// on itself, exactly once, at <see cref="Awake"/>.
///
/// Why the instance self-inits instead of the spawner doing it: a spawner's code runs only on the spawning
/// peer, so with client-predicted spawns the server and the other observers instantiate the prefab and
/// would never configure it. <see cref="Awake"/> runs on EVERY peer — synchronously inside FishNet's
/// instantiation, before the per-spawn payload is applied and before any component's OnStartNetwork — so
/// every peer configures locally from the baked SO with nothing on the wire.
///
/// The leaf components stay generic and type-agnostic; this root is the ONLY component that references the
/// type. The deliberate bidirectional prefab→SO reference (SO's InstancePrefab ⇄ this root's type) is the
/// trade that buys correct predicted spawn; <see cref="OnValidate"/> guards it from drifting out of sync.
/// </summary>
public sealed class TypeObjectRoot : MonoBehaviour
{
    [Tooltip("The type baked into this prefab variant. Resolved locally on every peer; never sent over the network.")]
    [SerializeField] private ScriptableTypeObject type;

    private bool _initialized;

    private void Awake() => EnsureInitialized();

    /// <summary>Configure this instance's components from its type, once. Idempotent and safe to re-call.</summary>
    public void EnsureInitialized()
    {
        if (_initialized)
            return;
        if (type == null)
        {
            Debug.LogError($"{name}: TypeObjectRoot has no 'type' assigned — this instance cannot configure itself. " +
                "Assign the ScriptableTypeObject baked into this prefab.", this);
            return;
        }
        _initialized = true;
        type.Initialize(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (type == null)
            return;
        // Consistency guard for the deliberate bidirectional prefab↔SO ref: the type this root points at
        // must point its InstancePrefab back at a prefab whose root points at the SAME type. A mismatch
        // would silently configure the wrong kind under prefab-per-kind.
        var prefab = type.InstancePrefab;
        var backRoot = prefab != null ? prefab.GetComponent<TypeObjectRoot>() : null;
        if (backRoot != null && backRoot.type != type)
            Debug.LogWarning(
                $"{name}: TypeObjectRoot.type ('{type.name}') disagrees with its InstancePrefab's root " +
                $"('{(backRoot.type != null ? backRoot.type.name : "null")}') — the wrong kind would be configured.",
                this);
    }
#endif
}
