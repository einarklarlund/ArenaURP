using UnityEngine;

/// <summary>
/// Marks a <c>[SerializeReference]</c> <see cref="IEffect{TContext}"/> field (or a list of them) for the
/// VARIANCE-AWARE effect picker (see the EffectSelectorDrawer in the Editor assembly).
///
/// MackySoft's <c>[SubclassSelector]</c> lists only the EXACT closed-generic implementers of the field's type
/// (<c>TypeCache.GetTypesDerivedFrom(IEffect&lt;ProjectilePlayerCollision&gt;)</c>), which ignores the
/// contravariance the runtime already honours. This picker instead offers every effect whose context is
/// SATISFIED by the field's context <c>T</c>: an <c>IEffect&lt;IAimContext&gt;</c> shows up in an
/// <c>IEffect&lt;ProjectilePlayerCollision&gt;</c> list because a ProjectilePlayerCollision IS an IAimContext.
/// This lets an operation be typed to the leanest capability it needs and authored on every compatible site.
/// </summary>
public sealed class EffectSelectorAttribute : PropertyAttribute { }
