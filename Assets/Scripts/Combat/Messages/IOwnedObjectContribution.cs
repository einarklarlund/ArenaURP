using UnityEngine;

/// <summary>
/// A per-effect component on a passive-ability INSTANCE prefab that contributes on-hit behaviour to the objects
/// the owner spawns (ADR 0005). At spawn, <see cref="SpawnContributionReceiver"/> resolves the owner's passives and calls
/// <see cref="Apply"/> on each contribution, which ADDS + configures the corresponding
/// <see cref="IHitMessageHandler"/> onto the spawned object (the SpawnContributionReceiver model — no wire payload, every
/// peer runs it the same way). By hit time the handler is an ordinary component on the source, so
/// <see cref="HitMessage.Dispatch"/> reaches it without knowing anything about the ability system.
///
/// The contribution and the handler it adds are usually the SAME class (self-cloning: <see cref="Apply"/> does
/// <c>target.AddComponent&lt;ThisType&gt;()</c> if absent and copies its own config across a self-contained public
/// setter). Discovered by presence — <c>GetComponents&lt;IOwnedObjectContribution&gt;()</c> on the passive
/// instance — never a serialized list. Contributions are plain (non-networked) MonoBehaviours: adding a
/// NetworkBehaviour to an already-spawned NetworkObject would break FishNet's component index. (Part of the
/// acknowledged passive-system debt; see the passives-convoluted memory.)
/// </summary>
public interface IOwnedObjectContribution
{
    /// <summary>Add + configure this contribution's handler onto <paramref name="target"/> (the spawned object),
    /// idempotently — a re-apply on the same target must not stack a second copy. Returns the component this call
    /// ADDED, or null if an equivalent handler was already present, so the caller can tear down on pooled reuse
    /// exactly what it introduced (and never a handler the target was authored with).</summary>
    Component Apply(GameObject target);
}
