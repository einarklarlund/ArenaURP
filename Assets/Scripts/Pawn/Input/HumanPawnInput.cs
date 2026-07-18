using UnityEngine;

/// <summary>
/// Human player implementation of PawnInputProvider.
/// Reads hardware input each frame (move, look, jump, fire) and writes it
/// into Data for the pawn's handler components to consume.
/// Only active on the owning client.
/// </summary>
public sealed class HumanPawnInput : PawnInput
{
    public float Sensitivity = 1f;

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

        Data = new PawnInputData
        {
            Move = InputManager.PawnControls.Move.ReadValue<Vector2>(),
            Jump = InputManager.PawnControls.Jump.IsPressed(),
            Look = look,
            Fire = InputManager.PawnControls.Attack.IsPressed(),
            CycleAbilityPrev = InputManager.PawnControls.CycleAbilityPrev.WasPressedThisFrame(),
            CycleAbilityNext = InputManager.PawnControls.CycleAbilityNext.WasPressedThisFrame(),
            UseAbility = InputManager.PawnControls.UseAbility.WasPressedThisFrame(),
        };
    }
}
