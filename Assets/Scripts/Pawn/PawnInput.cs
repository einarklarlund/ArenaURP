using System;
using FishNet.Object;
using UnityEngine;

public struct PawnInputData 
{
    public float Horizontal;
    public float Vertical;
    public bool Jump;
    public bool Fire;
    public bool Interact;
    public int SelectedSlot;
    public float MouseX;
    public float MouseY;
}

/// <summary>
/// Responsible for harvesting raw hardware input and translating it into a unified data structure.
/// Responsibilities:
/// - Captures Keyboard, Mouse, or Controller states during the Update loop.
/// - Stores input in a 'PawnInputData' struct to ensure a single source of truth for other components.
/// - Restricts input collection to the 'Owner' client only.
/// - Fires events for button presses that other components can subscribe to
/// </summary>
public sealed class PawnInput : NetworkBehaviour 
{
    // Events that other components can subscribe to
    public event Action OnFirePressed;
    public event Action OnInteractPressed;
    public event Action OnWeaponSwapPressed;

    public PawnInputData Data;
    public float Sensitivity = 2f;
    private int _currentSlot = 0;

    private void Update() 
    {
        if (!IsOwner) return;
        
        if (Input.GetKeyDown(KeyCode.Tab)) OnWeaponSwapPressed?.Invoke();

        if (Input.GetButtonDown("Fire1")) OnFirePressed?.Invoke();

        if (Input.GetKeyDown(KeyCode.E)) OnInteractPressed?.Invoke();

        // Maintain the struct for continuous data (like movement)
        Data = new PawnInputData
        {
            Horizontal = Input.GetAxisRaw("Horizontal"),
            Vertical = Input.GetAxisRaw("Vertical"),
            Jump = Input.GetButton("Jump"),
            Fire = Input.GetButton("Fire1"),
            Interact = Input.GetKey(KeyCode.E),
            SelectedSlot = _currentSlot,
            MouseX = Input.GetAxis("Mouse X") * Sensitivity,
            MouseY = Input.GetAxis("Mouse Y") * Sensitivity
        };
    }
}