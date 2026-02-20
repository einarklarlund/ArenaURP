using FishNet.Object;
using UnityEngine;

public struct PawnMovementData 
{
    public Vector2 Move;
    public bool Jump;
    public Vector2 Look;
}

public sealed class PawnMovementInput : NetworkBehaviour 
{
    public PawnMovementData Data;
    public float Sensitivity = 1f;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner) InputManager.PawnControls.Enable();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (IsOwner) InputManager.PawnControls.Disable();
    }

    private void Update() 
    {
        if (!IsOwner) return;

        float sensitivity = SettingsManager.Instance.Settings.MouseSensitivity;

        Data = new PawnMovementData
        {
            Move = InputManager.PawnControls.Move.ReadValue<Vector2>(),
            Jump = InputManager.PawnControls.Jump.IsPressed(),
            Look = InputManager.PawnControls.Look.ReadValue<Vector2>() * sensitivity
        };
    }
}