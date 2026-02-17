using FishNet.Object;
using UnityEngine;

public enum FireMode { SemiAuto, Automatic }
public enum SpreadType { Random, Even }

[CreateAssetMenu(fileName = "New Weapon", menuName = "FPS/Weapon Data")]
public class WeaponData : ScriptableObject
{
    public AmmoType AmmoType;

    [Header("Visuals")]
    public string WeaponName;
    public GameObject WeaponPrefab;
    public NetworkObject BulletPrefab;
    
    [Header("Firing Logic")]
    public FireMode FireMode = FireMode.Automatic;
    public float FireRate = 0.1f;
    public int AmmoPerFire = 1;

    [Header("Burst Settings")]
    public int ShotsPerFire = 1; // Total times it "pulses" per click
    public float FireDuration = 0f; // Time over which ShotsPerFire are spread

    [Header("Spread & Projectiles")]
    public int ProjectilesPerShot = 1; // Multi-pellet (like a shotgun)
    public float SpreadAngle = 5f; // In degrees
    public SpreadType SpreadType = SpreadType.Random;
}