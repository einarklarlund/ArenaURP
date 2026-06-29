using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Plays a <see cref="GifClip"/> on a SpriteRenderer and keeps the sprite turned toward the active camera.
/// Drop a gif into the project (it imports as a GifClip via <see cref="GifImporter"/>) and drag it onto Clip.
///
/// - Loop: toggle Loop on/off. When off, the gif plays once then runs On Complete.
/// - Speed: Playback Speed scales the gif's own timing (2 = twice as fast, 0.5 = half).
/// - Camera: faces Camera.main and re-resolves it automatically when it changes or disappears. Assign
///   Camera Override to pin a specific camera instead.
/// - Editor preview: the inspector has a "Preview In Scene View" button that animates the gif in the Scene
///   view without entering play mode (see BillboardGifEditor). That preview is in-memory only.
///
/// No external packages required.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BillboardGif : MonoBehaviour
{
    public enum BillboardMode
    {
        ScreenAligned,      // sprite stays parallel to the screen (no perspective tilt) ??? the usual look
        FaceCameraPosition, // sprite normal points straight at the camera's position
        VerticalBillboard   // rotate around Y only, staying upright (trees / pickups)
    }

    public enum CompleteAction { None, Disable, Destroy }

    [Header("Source")]
    [Tooltip("The gif to play. Drop a .gif into the project, then drag the resulting asset here.")]
    public GifClip clip;

    [Header("Playback")]
    [Tooltip("Loop forever, or play once and run On Complete.")]
    public bool loop = true;

    [Min(0f)]
    [Tooltip("Scales the gif's own frame timing. 1 = native speed, 2 = double speed, 0.5 = half.")]
    public float playbackSpeed = 1f;

    [Tooltip("Start playing automatically when the object is enabled.")]
    public bool playOnEnable = true;

    [Tooltip("What to do after a non-looping gif finishes its last frame.")]
    public CompleteAction onComplete = CompleteAction.None;

    [Tooltip("Fired when a non-looping gif reaches its last frame.")]
    public UnityEvent completed;

    [Header("Billboard")]
    public BillboardMode billboard = BillboardMode.ScreenAligned;

    [Tooltip("Optional: face this camera instead of Camera.main.")]
    public Camera cameraOverride;

    [Min(0f)]
    [Tooltip("How often (seconds) to re-check Camera.main in case the active camera changed. 0 = every frame.")]
    public float cameraRefreshInterval = 0.5f;

    SpriteRenderer _renderer;
    Camera _camera;
    float _cameraCheck;
    int _frame;
    float _timer;
    bool _playing;

    void Awake() => EnsureRenderer();

    void EnsureRenderer()
    {
        if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        if (playOnEnable) Play();
    }

    /// <summary>(Re)start the clip from the first frame.</summary>
    public void Play()
    {
        EnsureRenderer();
        _frame = 0;
        _timer = 0f;
        _playing = clip != null && clip.FrameCount > 0;
        if (_playing) _renderer.sprite = clip.GetFrame(0);
    }

    /// <summary>Freeze on the current frame.</summary>
    public void Stop() => _playing = false;

    void LateUpdate()
    {
        // Edit-mode animation is driven separately (see BillboardGifEditor); this only runs in play mode.
        ApplyBillboard(ResolveCamera());

        if (!_playing) return;
        if (clip == null || clip.FrameCount == 0) { _playing = false; return; }

        _timer += Time.deltaTime * Mathf.Max(0f, playbackSpeed);

        // Advance across however many frames the elapsed time covers (handles fast speed / frame hitches).
        float delay = Mathf.Max(0.0001f, clip.GetDelay(_frame));
        while (_timer >= delay)
        {
            _timer -= delay;
            _frame++;
            if (_frame >= clip.FrameCount)
            {
                if (loop)
                {
                    _frame = 0;
                }
                else
                {
                    _frame = clip.FrameCount - 1;
                    _renderer.sprite = clip.GetFrame(_frame);
                    Finish();
                    return;
                }
            }
            delay = Mathf.Max(0.0001f, clip.GetDelay(_frame));
        }

        _renderer.sprite = clip.GetFrame(_frame);
    }

    void Finish()
    {
        _playing = false;
        completed?.Invoke();
        switch (onComplete)
        {
            case CompleteAction.Disable: gameObject.SetActive(false); break;
            case CompleteAction.Destroy: Destroy(gameObject); break;
        }
    }

    Camera ResolveCamera()
    {
        if (cameraOverride != null) return cameraOverride;

        // Re-resolve when the cached camera is gone/inactive, or periodically in case the active main
        // camera was swapped for another one that is still enabled.
        _cameraCheck -= Time.deltaTime;
        if (_camera == null || !_camera.isActiveAndEnabled || _cameraCheck <= 0f)
        {
            _camera = Camera.main;
            _cameraCheck = cameraRefreshInterval;
        }
        return _camera;
    }

    void ApplyBillboard(Camera cam)
    {
        if (cam == null) return;

        Transform camTransform = cam.transform;
        switch (billboard)
        {
            case BillboardMode.ScreenAligned:
                transform.rotation = Quaternion.LookRotation(camTransform.forward, camTransform.up);
                break;
            case BillboardMode.FaceCameraPosition:
                transform.rotation = Quaternion.LookRotation(transform.position - camTransform.position, camTransform.up);
                break;
            case BillboardMode.VerticalBillboard:
                Vector3 dir = transform.position - camTransform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                break;
        }
    }

#if UNITY_EDITOR
    // ---- Editor-only Scene-view preview (no play mode). Driven by BillboardGifEditor's update loop.
    // previewInEditor is intentionally NOT serialized: toggling it never modifies/dirties the scene, and it
    // resets on domain reload. All frame/rotation changes it causes are in-memory and restored on stop/save.
    [System.NonSerialized] public bool previewInEditor;
    [System.NonSerialized] int _previewFrame;
    [System.NonSerialized] float _previewTimer;

    /// <summary>Advance the edit-mode preview by dt and face the given (Scene view) camera.</summary>
    public void EditorPreviewTick(float dt, Camera cam)
    {
        EnsureRenderer();
        ApplyBillboard(cam);
        if (clip == null || clip.FrameCount == 0) return;

        _previewTimer += dt * Mathf.Max(0f, playbackSpeed);
        float delay = Mathf.Max(0.0001f, clip.GetDelay(_previewFrame));
        while (_previewTimer >= delay)
        {
            _previewTimer -= delay;
            _previewFrame = (_previewFrame + 1) % clip.FrameCount; // preview always loops
            delay = Mathf.Max(0.0001f, clip.GetDelay(_previewFrame));
        }
        if (_renderer != null) _renderer.sprite = clip.GetFrame(_previewFrame);
    }

    /// <summary>Return the preview to a resting state (first frame).</summary>
    public void EditorPreviewReset()
    {
        EnsureRenderer();
        _previewFrame = 0;
        _previewTimer = 0f;
        if (clip != null && clip.FrameCount > 0 && _renderer != null) _renderer.sprite = clip.GetFrame(0);
    }
#endif
}
