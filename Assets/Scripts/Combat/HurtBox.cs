using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A collider that receives damage. Exposes a <see cref="ReceiveDamage"/> method
/// that <see cref="HitBox"/> calls when it hits this collider. Broadcasts the
/// incoming <see cref="DamageInfo"/> via the OnHurt UnityEvent so other components
/// (Health, Pawn.TakeDamage, VFX) can react via inspector wiring.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HurtBox : MonoBehaviour
{
    [SerializeField] private DamageInfoEvent onHurt = new();

    public DamageInfoEvent OnHurt => onHurt;

    public void ReceiveDamage(DamageInfo info)
    {
        onHurt?.Invoke(info);
    }
}
