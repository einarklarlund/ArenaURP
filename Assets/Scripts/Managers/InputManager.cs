using UnityEngine;

public class InputManager : MonoBehaviour
{
    private static InputManager instance;

    /// <summary>True when an InputManager exists in the scene. Lets input-driven components
    /// skip subscribing in headless/test contexts where no InputManager is present.</summary>
    public static bool HasInstance => instance != null;

    public static PlayerControls.PawnControlsActions PawnControls => instance.pawnControls;

    /// <summary>Upgrade picks. Always enabled: picks are made while alive and while waiting to respawn,
    /// when PawnControls is off. Readers check their own panel is showing.</summary>
    public static PlayerControls.ProgressionControlsActions ProgressionControls => instance.progressionControls;

    private PlayerControls.PawnControlsActions pawnControls;
    private PlayerControls.ProgressionControlsActions progressionControls;

    void Awake()
    {
        var playerControls = new PlayerControls();
        pawnControls = playerControls.PawnControls;
        progressionControls = playerControls.ProgressionControls;
        progressionControls.Enable();

        instance = this;
    }
}
