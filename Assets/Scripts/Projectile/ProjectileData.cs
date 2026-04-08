using FishNet.Object;
using UnityEngine;
using System.Collections.Generic;

public class ProjectileData : NetworkBehaviour
{
    [Header("Kinematics")]
    public float speed = 50f;
    public float acceleration = 0f;

    [Header("Damage")]
    public int damage = 10;

    [Header("Lifetime")]
    public float lifetime = 5f;
    public List<ProjectileData> spawnOnDeath;

    [Header("Collision interactions")]
    public LayerMask hitLayers = Physics.AllLayers;
    public bool deflectOtherBullets = false;

    [Header("Health")]
    public int MaxHealth = 1;

    [Header("Collision spawning")]
    public List<GameObject> impactVfx;
    public List<NetworkObject> spawnOnAnyImpact;

    [Header("Capsule Dimensions")]
    public float radius = 0.05f;
    public float height = 0.2f;
    public int directionAxis = 2;

    public float Lifetime => lifetime;
}
