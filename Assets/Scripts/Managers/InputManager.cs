using UnityEngine;

public class InputManager : MonoBehaviour
{
    private static InputManager instance;

    /// <summary>True when an InputManager exists in the scene. Lets input-driven components
    /// skip subscribing in headless/test contexts where no InputManager is present.</summary>
    public static bool HasInstance => instance != null;

    public static PlayerControls.PawnControlsActions PawnControls => instance.pawnControls;

    private PlayerControls.PawnControlsActions pawnControls;

    void Awake()
    {
        var playerControls = new PlayerControls();
        pawnControls = playerControls.PawnControls;

        instance = this;
    }
}