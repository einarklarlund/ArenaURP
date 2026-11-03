using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Managing.Timing;

/// <summary>
/// All logic + data for frying pans, in one class.
/// Includes logic + data for following the owner, detecting targets, swinging, and damage.
///
/// The pan hovers over its owner's head until another pawn enters the detection volume,
/// then swings down to land flat in front of the owner and damages every pawn in the hitbox.
/// The owner decides when the swing begins; the tick and position of the swing are relayed
/// to the server and observers, and every peer animates the swing kinematically from them.
///
/// Prefab convention: at identity rotation the pan lies flat, face up (+Y), with its handle
/// pointing back toward the owner (-Z).
/// </summary>
public class FryingPan : NetworkBehaviour
{
    [Header("Hover")]
    [Tooltip("Height above the owning pawn's position at which the pan hovers before swinging.")]
    public float hoverHeight = 2.5f;

    [Header("Volumes")]
    [Tooltip("Center of the detection volume and hitbox, relative to the owning pawn's position and yaw.")]
    public Vector3 volumeCenterOffset = new Vector3(0f, 0f, 2f);
    public Vector3 detectionSize = new Vector3(4f, 0.2f, 4f);
    public float hitboxRadius = 4f;
    public LayerMask includeLayers;

    [Header("Swing")]
    public float swingDuration = 0.5f;

    [Header("Damage")]
    public int damage = 1;

    [Header("Lifetime")]
    [Tooltip("How long the pan stays on the ground after landing before it is despawned.")]
    public float lingerDuration = 0.5f;

    [Header("Visuals")]
    public GameObject hitVfx;


    // Spawn payload
    public Pawn owningPawn { get; set; }

    // Swing state is set once, by the owner, and relayed to the server and observers.
    // Movement and damage after the swing begins are calculated kinematically from it.
    private bool _hasSwung;
    private PreciseTick _swingTick;
    private Vector3 _swingOrigin;
    private Quaternion _swingYaw;

    // Tracking whether the hitbox has been cast allows us to deal damage only once.
    private bool _hasLanded;
    private bool _isAlive = true;

    private readonly Collider[] _overlapResults = new Collider[32];


    // ------------------------------ OBJECT CREATION -----------------------------------


    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        owningPawn = (Pawn) reader.ReadNetworkBehaviour();
    }

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.Write(owningPawn);
    }


    // ------------------------------ OBJECT REMOVAL ------------------------------


    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);

        // Reset stateful values to support object pooling.
        _hasSwung = false;
        _hasLanded = false;
        _isAlive = true;
        owningPawn = null;
    }


    // ------------------------------ TICK ------------------------------


    private void Update()
    {
        // Update may run before network lifecycle events -- guard against it specifically
        if (!IsSpawned || !_isAlive)
            return;

        if (!_hasSwung)
        {
            if (owningPawn == null || !owningPawn.IsSpawned)
            {
                Die();
                return;
            }

            Hover();
            return;
        }

        Swing();

        double elapsed = TimeManager.TimePassed(_swingTick);
        if (!_hasLanded && elapsed >= swingDuration)
            Land();

        if (elapsed >= swingDuration + lingerDuration)
            Die();
    }

    private void FixedUpdate()
    {
        // Only the owner decides when the swing begins.
        if (!IsSpawned || !_isAlive || _hasSwung || !IsOwner)
            return;

        if (owningPawn == null || !owningPawn.IsSpawned)
            return;

        if (IsTargetInDetectionVolume())
            OwnerBeginSwing();
    }


    // ------------------------------ MOVEMENT/POSITION ------------------------------


    private void Hover()
    {
        Quaternion yaw = GetYaw(owningPawn.transform);
        transform.position = GetHoverPosition(owningPawn.transform.position);
        transform.rotation = GetUprightRotation(yaw);
    }

    private void Swing()
    {
        float t = Mathf.Clamp01((float) (TimeManager.TimePassed(_swingTick) / swingDuration));
        // Ease in so the pan accelerates into the ground.
        float eased = t * t;

        Vector3 from = GetHoverPosition(_swingOrigin);
        Vector3 to = GetVolumeCenter(_swingOrigin, _swingYaw);
        transform.position = Vector3.Lerp(from, to, eased);
        transform.rotation = Quaternion.Slerp(GetUprightRotation(_swingYaw), _swingYaw, eased);
    }

    private Vector3 GetHoverPosition(Vector3 pawnPosition)
    {
        return pawnPosition + hoverHeight * Vector3.up;
    }

    private Vector3 GetVolumeCenter(Vector3 pawnPosition, Quaternion yaw)
    {
        return pawnPosition + yaw * volumeCenterOffset;
    }

    private static Quaternion GetUprightRotation(Quaternion yaw)
    {
        // Stand the pan up with its face toward the owner and its handle pointing down,
        // so that swinging to the flat rotation tips it forward onto the ground.
        return yaw * Quaternion.Euler(-90f, 0f, 0f);
    }

    private static Quaternion GetYaw(Transform t)
    {
        return Quaternion.Euler(0f, t.eulerAngles.y, 0f);
    }


    // ------------------------------ SWING ------------------------------


    private void OwnerBeginSwing()
    {
        PreciseTick tick = TimeManager.GetPreciseTick(TickType.Tick);
        Vector3 origin = owningPawn.transform.position;
        Quaternion yaw = GetYaw(owningPawn.transform);

        BeginSwing(tick, origin, yaw);
        ServerBeginSwing(tick, origin, yaw);
    }

    [ServerRpc]
    private void ServerBeginSwing(PreciseTick tick, Vector3 origin, Quaternion yaw)
    {
        BeginSwing(tick, origin, yaw);
        ObserversBeginSwing(tick, origin, yaw);
    }

    [ObserversRpc(ExcludeOwner = true, BufferLast = true)]
    private void ObserversBeginSwing(PreciseTick tick, Vector3 origin, Quaternion yaw)
    {
        BeginSwing(tick, origin, yaw);
    }

    private void BeginSwing(PreciseTick tick, Vector3 origin, Quaternion yaw)
    {
        // Guard against running twice on a host, which is both owner and server.
        if (_hasSwung)
            return;

        _hasSwung = true;
        _swingTick = tick;
        _swingOrigin = origin;
        _swingYaw = yaw;
    }


    // ------------------------------ COLLISION ------------------------------


    private bool IsTargetInDetectionVolume()
    {
        Quaternion yaw = GetYaw(owningPawn.transform);
        int count = Physics.OverlapBoxNonAlloc(
            GetVolumeCenter(owningPawn.transform.position, yaw),
            detectionSize,
            _overlapResults,
            yaw,
            includeLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < count; i++)
        {
            if (_overlapResults[i].TryGetComponent(out Pawn pawn) && pawn != owningPawn)
                return true;
        }

        return false;
    }

    private void Land()
    {
        _hasLanded = true;

        Vector3 center = GetVolumeCenter(_swingOrigin, _swingYaw);
        int count = Physics.OverlapSphereNonAlloc(
            center,
            hitboxRadius,
            _overlapResults,
            includeLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < count; i++)
            HandleCollision(_overlapResults[i], center);

        PlayHitVfx(center);
    }

    private void HandleCollision(Collider hit, Vector3 center)
    {
        if (hit == null || !hit.TryGetComponent(out Pawn pawn))
            return;

        if (pawn == owningPawn)
            return;

        // Pawn.TakeDamage applies damage on the server and plays VFX on every peer.
        Vector3 hitPoint = hit.ClosestPoint(center);
        pawn.TakeDamage(new DamageInfo()
        {
            Amount = damage,
            Attacker = null,
            HitPoint = hitPoint,
            Direction = Vector3.down
        });
    }


    // ------------------------------ DAMAGE HANDLING ------------------------------


    private void Die()
    {
        if (!_isAlive)
            return;

        if (IsServerStarted && IsSpawned)
            Despawn(DespawnType.Pool);

        _isAlive = false;
    }


    // ------------------------------ VFX ------------------------------


    private void PlayHitVfx(Vector3 center)
    {
        // Only run VFX client-side
        if (IsServerStarted && !IsClientStarted)
            return;

        if (hitVfx == null)
            return;

        // Assumes the VFX prefab is a unit-diameter sphere, so it scales to the hitbox.
        GameObject vfx = Instantiate(hitVfx, center, _swingYaw);
        vfx.transform.localScale = 2f * hitboxRadius * Vector3.one;
    }


    // ------------------------------ GIZMOS ------------------------------


    private void OnDrawGizmos()
    {
        // The detection volume follows the owning pawn, so it only exists during gameplay.
        if (!Application.isPlaying || owningPawn == null)
            return;

        Vector3 pawnPosition = _hasSwung ? _swingOrigin : owningPawn.transform.position;
        Quaternion yaw = _hasSwung ? _swingYaw : GetYaw(owningPawn.transform);

        Gizmos.color = Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(GetVolumeCenter(pawnPosition, yaw), yaw, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, detectionSize * 2);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
