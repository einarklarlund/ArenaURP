using FishNet.Object;
using UnityEngine;

public class PawnInteractionHandler : NetworkBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnInventory inventory;
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private Transform cameraTransform;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner) return;
        pawn.Input.OnInteractPressed += TryInteract;
    }

    private void TryInteract()
    {
        if (!IsOwner) return;

        // Raycast from the center of the screen
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            if (hit.collider.TryGetComponent<IInteractable>(out var interactable))
            {
                interactable.RequestInteract(pawn);
            }
        }
    }
}