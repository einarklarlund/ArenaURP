using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

public class PawnInteractionHandler : NetworkBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private Transform cameraTransform;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner) return;

        InputManager.PawnControls.Interact.performed += TryInteract;
    }

    private void OnDestroy()
    {
        InputManager.PawnControls.Interact.performed -= TryInteract;
    }

    private void TryInteract(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // Raycast from the center of the screen
        Ray ray = new(cameraTransform.position, cameraTransform.forward);
        
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            if (hit.collider.TryGetComponent<IInteractable>(out var interactable))
            {
                interactable.RequestInteract(pawn);
            }
        }
    }
}