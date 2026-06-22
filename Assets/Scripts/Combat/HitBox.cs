using FishNet.Component.Prediction;
using FishNet.Object;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A collider that deals damage. When its collider intersects another collider that
/// has a <see cref="HurtBox"/>, builds a <see cref="DamageInfo"/> and forwards it to
/// that HurtBox. Both this HitBox and the receiving HurtBox broadcast OnHurt for
/// other components to listen to via inspector wiring.
///
/// Detection uses FishNet NetworkCollision/NetworkTrigger when present on the same
/// GameObject (same pattern as ProjectileHitbox). Add either component depending on
/// whether the collider is a trigger or a solid collider.
/// </summary>
[RequireComponent(typeof(NetworkCollision))]
public class HitBox : NetworkBehaviour
{
    [Header("Damage")]
    [SerializeField] private int amount = 1;
    [SerializeField] private DamageType damageType = DamageType.Bullet;
    [SerializeField] private NetworkPlayerOwnership ownership;

    [Header("Events")]
    [SerializeField] private DamageInfoEvent onHurt = new();

    [Header("Debug")]
    [Tooltip("Pause the editor the moment HandleHit accepts a hit, so colliders are still in their hit-frame positions.")]
    [SerializeField] private bool pauseOnHit;
    [Tooltip("Select and ping the other collider's GameObject in the hierarchy when a hit is accepted.")]
    [SerializeField] private bool selectOnHit;

    public DamageInfoEvent OnHurt => onHurt;

    public int Amount { get => amount; set => amount = value; }
    public DamageType DamageType { get => damageType; set => damageType = value; }
    public NetworkPlayer Attacker { get; private set; }
    public Vector3 Direction { get; private set; }

    /// <summary>
    /// UnityEvent-friendly setter for <see cref="Attacker"/>. Wire e.g.
    /// NetworkPlayerOwnership.OnAssignment → HitBox.AssignFirer in the inspector
    /// (dynamic NetworkPlayer parameter).
    /// </summary>
    public void AssignAttacker(NetworkPlayer attacker)
    {
        Attacker = attacker;
        if (attacker.ControlledPawn.Value != null)
        {
            if (attacker.ControlledPawn.Value.TryGetComponent<Collider>(out var attackerCol))
            {
                if (TryGetComponent<Collider>(out var selfCol))
                {
                    Physics.IgnoreCollision(attackerCol, selfCol);
                }
            }
        }
    }

    private NetworkCollision _networkCollision;
    private NetworkTrigger _networkTrigger;

    private void Awake()
    {
        _networkCollision = GetComponent<NetworkCollision>();
        _networkTrigger = GetComponent<NetworkTrigger>();
        if (_networkCollision != null) _networkCollision.OnEnter += OnNetworkCollisionEnter;
        if (_networkTrigger != null) _networkTrigger.OnEnter += OnNetworkTriggerEnter;
    }

    private void OnNetworkCollisionEnter(Collider other, uint tick) => HandleHit(other);
    private void OnNetworkTriggerEnter(Collider other, uint tick) => HandleHit(other);

    private void HandleHit(Collider other)
    {
        if (!other.TryGetComponent<HurtBox>(out var hurtBox))
            return;

        if (ownership != null)
        {
            if (other.TryGetComponent<NetworkPlayerOwnership>(out var otherOwner))
                if(ownership.NPOwner == otherOwner.NPOwner)
                    return;
        }

        var direction = Direction == Vector3.zero ? transform.forward : Direction;

        var info = new DamageInfo
        {
            Amount = amount,
            Attacker = Attacker,
            HitPoint = other.ClosestPoint(transform.position),
            Direction = direction.normalized,
            Type = damageType,
        };

        hurtBox.ReceiveDamage(info);
        onHurt?.Invoke(info);

#if UNITY_EDITOR
        if (selectOnHit)
        {
            Selection.objects = new Object[] { gameObject, other.gameObject };
            EditorGUIUtility.PingObject(other);
        }
        if (pauseOnHit) Debug.Break();
#endif
    }
}
