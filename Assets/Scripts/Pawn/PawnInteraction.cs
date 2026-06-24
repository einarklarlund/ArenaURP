using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

public class PawnInteraction : NetworkBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private Transform cameraTransform;

    private bool _subscribed;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner || !InputManager.HasInstance) return;

        InputManager.PawnControls.Interact.performed += TryInteract;
        _subscribed = true;
    }

    private void OnDestroy()
    {
        // Only unsubscribe what we actually subscribed, and only if the InputManager is still
        // around. Guards the previous unconditional deref, which NRE'd for non-owners and during
        // teardown when the InputManager was already gone (e.g. headless tests, scene unload).
        if (_subscribed && InputManager.HasInstance)
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