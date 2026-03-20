using FishNet.Object;
using UnityEngine;

/// <summary>
/// Controls camera components on a human pawn.
/// </summary>
public class HumanPawnCamera : NetworkBehaviour
{
    public Transform cameraTransform;
    public HumanPawnInput humanPawnInput;

    public override void OnStartClient() 
    {
        base.OnStartClient();
        if (CameraIsActive())
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

    private bool CameraIsActive()
    {
        return humanPawnInput.IsActive;
    }
}