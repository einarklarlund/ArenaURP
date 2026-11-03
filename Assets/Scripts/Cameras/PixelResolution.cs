using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Keeps the game rendering <see cref="targetHeight"/> pixels tall, whatever the window size, by driving the pipeline
/// asset's render scale; URP's final pass then upscales with the asset's point filter, so the dither stays one cell per
/// low-res pixel. Render scale is global, so cameras in <see cref="fullResolutionCameras"/> (ones rendering into their
/// own texture, like the character preview) get a scale of 1 for their own render.
/// The asset's saved scale (what the Game tab uses in edit mode) is put back on disable, so play mode never changes it.
/// </summary>
public sealed class PixelResolution : MonoBehaviour
{
    [SerializeField] private int targetHeight = 360;
    [SerializeField] private List<Camera> fullResolutionCameras = new();

    private UniversalRenderPipelineAsset pipeline;
    private float savedScale;
    private float scale;
    private int appliedForHeight;

    private void OnEnable()
    {
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
        {
            enabled = false;
            return;
        }
        savedScale = pipeline.renderScale;
        appliedForHeight = 0;
        Apply();
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        if (pipeline == null)
            return;
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
        pipeline.renderScale = savedScale;
    }

    private void Update()
    {
        Apply();
    }

    private void Apply()
    {
        if (Screen.height == appliedForHeight)
            return;
        appliedForHeight = Screen.height;
        // URP truncates scaled sizes, so the half pixel keeps 360 from landing on 359. Never render above native size.
        scale = Mathf.Min(1f, (targetHeight + 0.5f) / Screen.height);
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
