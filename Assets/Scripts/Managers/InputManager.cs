using UnityEngine;

public class InputManager : MonoBehaviour
{
    private static InputManager instance;

    public static PlayerControls.PawnControlsActions PawnControls => instance.pawnControls;

    private PlayerControls.PawnControlsActions pawnControls;

    void Awake()
    {
        var playerControls = new PlayerControls();
        pawnControls = playerControls.PawnControls;

        instance = this;
    }
}