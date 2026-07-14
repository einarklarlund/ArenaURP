using UnityEngine;

/// <summary>
/// Instantiate the non-networked prefab(s) configured on a referenced <see cref="InstantiableObjects"/>
/// component at the hit point, oriented to the surface normal (e.g. impact VFX). Reads point + normal, so it is
/// typed to <see cref="ProjectileImpact"/> — authorable on every collision context, but not on an ability aim
/// (no normal).
///
/// This is a LOCAL instantiate (runs in the server-authoritative collision context). For host/editor play the
/// host sees it; to guarantee it on remote clients, make the prefab networked and use
/// <see cref="SpawnNetworkObjects"/> instead. The prefab list is data on an <see cref="InstantiableObjects"/>
/// component (a plain data-holder, not a first-class system), resolved via GetComponent on the context's
/// <see cref="EffectContext.Source"/> by default; set <see cref="source"/> (the one allowed reference on an
/// otherwise stateless effect) only to disambiguate when the source carries several.
/// </summary>
[System.Serializable]
public sealed class InstantiateGameObjects : IEffect<ProjectileImpact>
{
    [SerializeField, Tooltip("Optional explicit InstantiableObjects. Leave null to resolve via GetComponent on the context's Source; set it only to disambiguate when the source carries several.")]
    private InstantiableObjects source;

    public InstantiateGameObjects() { }

    public InstantiateGameObjects(InstantiableObjects source) => this.source = source;

    public void Apply(ProjectileImpact ctx)
    {
        InstantiableObjects resolved = source;
        if (resolved == null && (ctx.Source == null || !ctx.Source.TryGetComponent(out resolved)))
            return;

        Quaternion rot = ctx.Normal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(ctx.Normal) : Quaternion.identity;
        var prefabs = resolved.Prefabs;
        for (int i = 0; i < prefabs.Count; i++)
            if (prefabs[i] != null)
                Object.Instantiate(prefabs[i], ctx.Point, rot);
    }
}
