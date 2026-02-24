using UnityEngine;

public class InputManager : MonoBehaviour
{
    private static InputManager instance;

    [HideInInspector] public static PlayerControls.PawnControlsActions PawnControls => instance.controls;

    private PlayerControls.PawnControlsActions controls;

    void Awake()
    {
        controls = new PlayerControls().PawnControls;
        instance = this;
    }
}