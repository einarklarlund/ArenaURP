using FishNet.Object;
using UnityEngine.InputSystem;

public class PawnUIHandler : NetworkBehaviour
{
    private bool paused = false;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner) return;
        InputManager.PawnControls.Pause.performed += HandlePause;
    }

    private void OnDestroy()
    {
        InputManager.PawnControls.Pause.performed -= HandlePause;
    }

    private void HandlePause(InputAction.CallbackContext context)
    {
        paused = !paused;
        if (paused)
        {
            LocalUIEvents.OnPause?.Invoke();
        }
        else
        {
            LocalUIEvents.OnUnpause?.Invoke();
        }
    }
}