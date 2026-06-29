using UnityEngine;

/// <summary>
/// A decoded animated gif: an ordered list of frame Sprites with a per-frame delay (seconds). Produced by
/// <see cref="GifImporter"/> when you drop a .gif into the project ??? the gif asset IS a GifClip. Played by
/// <see cref="BillboardGif"/>. Holds no runtime/per-instance state, so one clip is safely shared by many
/// billboards at once.
/// </summary>
public class GifClip : ScriptableObject
{
    [Tooltip("One Sprite per gif frame, in playback order.")]
    public Sprite[] frames;

    [Tooltip("Seconds to hold each frame (parallel to frames).")]
    public float[] frameDelays;

    [Tooltip("Whether the source gif declared itself as looping (informational; BillboardGif decides).")]
    public bool loops;

    public int FrameCount => frames != null ? frames.Length : 0;

    public Sprite GetFrame(int i) => frames[i];

    public float GetDelay(int i) => (frameDelays != null && i >= 0 && i < frameDelays.Length) ? frameDelays[i] : 0.1f;
}
