

using FishNet.Connection;
using UnityEngine;

public enum DamageType
{
    Bullet,
    Environment,
}

[System.Serializable]
public struct DamageInfo
{
    public int Amount;
    public NetworkConnection Attacker;
    public Vector3 HitPoint;
    public Vector3 Direction;
    public DamageType Type;
}

public interface IDamageable
{
    void ServerTakeDamage(DamageInfo info);
    void ObserversTakeDamage(DamageInfo info);
}