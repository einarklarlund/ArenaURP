using UnityEngine;
using FishNet.Object;

/// <summary>
/// All data + logic for pickups in one class.
/// </summary>
public class PowerupPickup : NetworkBehaviour
{
     public float pickupRadius = 3f;
     public PowerUps.Type type;

     [Header("Collision")]
     [Tooltip("Layer mask for physics objects that can pick up this pickup.")]
     public LayerMask pickupLayers;
     public SphereCollider collider;

     [Header("Visuals")]
     public MeshRenderer meshRenderer;
     public GameObject pickupEffects;

     private bool _isPickedUp = false;

    // ---------------------------- OBJECT CREATION ----------------------------
    public override void OnStartServer()
    {
        base.OnStartServer();
        _isPickedUp = false;
    }

     // ---------------------------- TICK ----------------------------

     private void FixedUpdate()
     {
          if (!IsServerStarted && !IsClientStarted)
               return;

          var collisions = GatherCollisions();
          HandleCollisions(collisions);
     }

     // ---------------------------- COLLISION ----------------------------

     private RaycastHit[] GatherCollisions()
     {
          var hits = Physics.SphereCastAll(
              transform.position,
              collider.radius,
              Vector3.up,
              0,
              pickupLayers,
              QueryTriggerInteraction.Collide
          );

          return hits;
     }

     private void HandleCollisions(RaycastHit[] hits)
     {
          foreach (var hit in hits)
               HandleCollision(hit);
     }

     private void HandleCollision(RaycastHit hit)
     {
          if (hit.collider == null || hit.collider == collider)
               return;

          if (hit.collider.TryGetComponent(out Pawn pawn))
          {
               if (_isPickedUp)
                    return;

               GiveToPawn(pawn);
          }

          // Play vfx
          PlayPickupVfx(transform.position);
     }

     private void GiveToPawn(Pawn pawn)
     {
          PowerUps.Apply(pawn, type);
          _isPickedUp = true;

          // client-side predicted despawn
          if (IsClientInitialized)
               Despawn();
     }

     // ---------------------------- VFX ----------------------------


     private void PlayPickupVfx(Vector3 position)
     {
        // Only run VFX client-side
        if (IsServerStarted && !IsClientStarted)
            return;

        meshRenderer.enabled = false;
        Quaternion rotation = Quaternion.LookRotation(Vector3.up);
        Instantiate(pickupEffects, position, rotation);
     }
}
