using UnityEngine;

/// <summary>
/// Hands this camera to <see cref="CameraDirector"/> while enabled. Drop a camera into a scene with this on it and it
/// takes the screen in play mode (at the default Override priority); disable the component to give the screen back.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class DirectedCamera : MonoBehaviour
{
    [Tooltip("Higher wins. See CameraDirector.Priority: Scene -10, Fallback 0, Pawn 10, Menu 20, Override 100.")]
    [SerializeField] private int priority = CameraDirector.Priority.Override;

    private Camera directedCamera;

    private void Awake()
    {
        directedCamera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        CameraDirector.Request(directedCamera, priority);
    }

    private void OnDisable()
    {
        CameraDirector.Release(directedCamera);
    }
}
