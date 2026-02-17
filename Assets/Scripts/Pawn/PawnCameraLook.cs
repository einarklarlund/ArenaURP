using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Handles the visual orientation and rotational logic of the Pawn.
/// Responsibilities:
/// - Rotates the Pawn's root transform horizontally (Yaw) based on mouse input.
/// - Rotates the CameraTarget vertically (Pitch) with clamped constraints.
/// - Manages cursor locking and hides camera/audio listeners for non-owning players.
/// </summary>
public sealed class PawnCameraLook : NetworkBehaviour 
{
    [SerializeField] private Pawn pawn;
    public Transform CameraTarget;
    
    private float _verticalRotation = 0f;

    public override void OnStartClient() 
    {
        base.OnStartClient();
        if (IsOwner) 
        {
            Cursor.lockState = CursorLockMode.Locked;
            // Clean up for other players so we don't see through their eyes
            if(CameraTarget.TryGetComponent<Camera>(out var cam)) cam.enabled = true;
            if(CameraTarget.TryGetComponent<AudioListener>(out var listener)) listener.enabled = true;
        } 
        else 
        {
            // Clean up for other players so we don't see through their eyes
            if(CameraTarget.TryGetComponent<Camera>(out var cam)) cam.enabled = false;
            if(CameraTarget.TryGetComponent<AudioListener>(out var listener)) listener.enabled = false;
        }
    }

    private void Update() 
    {
        if (!IsOwner) return;

        // Consume data from our Input script
        float mouseX = pawn.Input.Data.MouseX;
        float mouseY = pawn.Input.Data.MouseY;

        // Rotate Body (Yaw)
        transform.Rotate(Vector3.up * mouseX);

        // Rotate Camera (Pitch)
        _verticalRotation -= mouseY;
        _verticalRotation = Mathf.Clamp(_verticalRotation, -90f, 90f);
        CameraTarget.localRotation = Quaternion.Euler(_verticalRotation, 0f, 0f);
    }
}