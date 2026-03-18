using FishNet.Object;
using UnityEngine;

/// <summary>
/// Handles the visual orientation and rotational logic of the Pawn.
/// Manages cursor locking and hides camera/audio listeners for non-owning players.
/// </summary>
public sealed class PawnCameraRotation : NetworkBehaviour 
{
    [SerializeField] private PawnInput input;
    [SerializeField] private Transform characterControllerTransform;
    [SerializeField] Transform cameraTransform;
    
    private float horizontalRotation = 0f;
    private float verticalRotation = 0f;

    public override void OnStartClient() 
    {
        base.OnStartClient();
        if (IsOwner) 
        {
            Cursor.lockState = CursorLockMode.Locked;
            if(cameraTransform.TryGetComponent<Camera>(out var cam)) cam.enabled = true;
            if(cameraTransform.TryGetComponent<AudioListener>(out var listener)) listener.enabled = true;
        } 
        else 
        {
            if(cameraTransform.TryGetComponent<Camera>(out var cam)) cam.enabled = false;
            if(cameraTransform.TryGetComponent<AudioListener>(out var listener)) listener.enabled = false;
        }
    }

    private void Update() 
    {
        if (!input.IsActive) return;

        float mouseX = input.Data.Look.x;
        float mouseY = input.Data.Look.y;

        // Rotate Body (Yaw)
        horizontalRotation += mouseX;
        characterControllerTransform.rotation = Quaternion.Euler(0f, horizontalRotation, 0f);

        // Rotate Camera / Aim Point (Pitch)
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);
        cameraTransform.rotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }
}
