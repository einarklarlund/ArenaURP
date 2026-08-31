using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// All data + logic for handling human input on a pawn that the owner of the connection owns.
/// </summary>
public sealed class HumanPawnInput : PawnInput
{
    public float Sensitivity = 1f;
    public PawnWeaponController weaponController;

    public override bool IsActive => IsOwner;

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
        Vector2 rawLook = InputManager.PawnControls.Look.ReadValue<Vector2>();

        Vector2 look = rawLook * sensitivity;
        #if UNITY_WEBGL && !UNITY_EDITOR
            // Mouse-look delta in WebGL is ~an order of magnitude larger than editor/standalone, so scale it down.
            const float webGLLookDivisor = 10f;
            look /= webGLLookDivisor;
        #endif

        MovementData = new MovementInputData
        {
            Move = InputManager.PawnControls.Move.ReadValue<Vector2>(),
            Jump = InputManager.PawnControls.Jump.IsPressed(),
            Look = look,
        };

        if (InputManager.PawnControls.Attack.IsPressed())
        {
            FireWeapon(TimeManager.GetPreciseTick(TickType.Tick));
        }
    }

    public override void FireWeapon(PreciseTick pt)
    {
        weaponController.FireWeapon(pt);
    }
}
