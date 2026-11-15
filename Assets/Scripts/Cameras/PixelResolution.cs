using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders the game at <see cref="scale"/> times the window's width and height (0.5 = half) by setting the pipeline
/// asset's render scale; URP's final pass then upscales with the asset's point filter. Render scale is
/// relative to the camera's target size, so URP picks up window resizes on its own with no per-frame work here. Render
/// scale is global, so cameras in <see cref="fullResolutionCameras"/> (ones rendering into their own texture, like the
/// character preview) get a scale of 1 for their own render.
/// The asset's saved scale (what the Game tab uses in edit mode) is put back on disable, so play mode never changes it.
/// </summary>
public sealed class PixelResolution : MonoBehaviour
{
    // Fraction of the window's width and height to render at. URP truncates scaled sizes, so an odd dimension at 0.5
    // rounds down (1081 -> 540). URP clamps render scale to 0.1 at the low end.
    [SerializeField, Range(0.1f, 1f)] private float scale = 0.5f;
    [SerializeField] private List<Camera> fullResolutionCameras = new();

    private UniversalRenderPipelineAsset pipeline;
    private float savedScale;

    private void OnEnable()
    {
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
        {
            enabled = false;
            return;
        }
        savedScale = pipeline.renderScale;
        pipeline.renderScale = scale;
        if (fullResolutionCameras.Count > 0)
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
        }
    }

    private void OnDisable()
    {
        if (pipeline == null)
            return;
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
        pipeline.renderScale = savedScale;
    }

    // Lets the Inspector slider take effect immediately while playing.
    private void OnValidate()
    {
        if (isActiveAndEnabled && pipeline != null)
            pipeline.renderScale = scale;
    }

    // URP reads the render scale after this callback, separately for each camera.
    private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (fullResolutionCameras.Contains(camera))
            pipeline.renderScale = 1f;
    }

    private void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (fullResolutionCameras.Contains(camera))
            pipeline.renderScale = scale;
    }
}
