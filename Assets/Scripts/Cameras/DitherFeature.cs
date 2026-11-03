using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Dithers the camera's image to the palette (see Fullscreen/Dithered) at the render-scaled size, into a point-filtered
/// texture that becomes the camera colour. URP's final blit upscales from that texture, so the low-res pixels stay hard.
/// The point filter has to live on the texture: WebGL ignores a shader's inline sampler states and samples with the
/// texture's own filter mode, and URP's camera colour texture is bilinear, so URP's Point upscaling blurs there.
/// </summary>
public sealed class DitherFeature : ScriptableRendererFeature
{
    [SerializeField] private Material material;

    private DitherPass pass;

    public override void Create()
    {
        pass = new DitherPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (material == null || cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;
        pass.Material = material;
        renderer.EnqueuePass(pass);
    }

    private sealed class DitherPass : ScriptableRenderPass
    {
        public Material Material;

        public DitherPass()
        {
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_DitheredColor";
            desc.filterMode = FilterMode.Point;
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, Material, 0), "Dither");
            resourceData.cameraColor = destination;
        }
    }
}
