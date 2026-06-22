using UnityEngine;
using UnityEngine.Events;

public enum DamageType
{
    Bullet,
    Environment,
    Laser,
}

[System.Serializable]
public struct DamageInfo
{
    public int Amount;
    public NetworkPlayer Attacker;
    public Vector3 HitPoint;
    public Vector3 Direction;
    public DamageType Type;
}

[System.Serializable]
public class DamageInfoEvent : UnityEvent<DamageInfo> { }

public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}
