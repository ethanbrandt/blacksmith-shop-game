using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class TransparentOutlineResources : ContextItem
{
    public TextureHandle normalDepth, metadata, outlineColor;
    public override void Reset()
    {
        normalDepth = metadata = outlineColor = TextureHandle.nullHandle;
    }
}

/// <summary>Captures the nearest visible liquid surface without modifying camera depth.</summary>
public sealed class TransparentOutlinePass : ScriptableRenderPass
{
    static readonly int NormalDepthId = Shader.PropertyToID("_LiquidNormalDepth");
    static readonly int MetadataId = Shader.PropertyToID("_LiquidMetadata");
    static readonly int ColorId = Shader.PropertyToID("_LiquidOutlineColor");

    public TransparentOutlinePass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    sealed class PassData { public RendererListHandle renderers; }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
    {
        var camera = frameData.Get<UniversalCameraData>();
        var rendering = frameData.Get<UniversalRenderingData>();
        var lights = frameData.Get<UniversalLightData>();
        var scene = frameData.Get<UniversalResourceData>();
        var resources = frameData.Create<TransparentOutlineResources>();
        var desc = new TextureDesc(camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height)
        {
            name = "Liquid Normal and Eye Depth",
            colorFormat = GraphicsFormat.R32G32B32A32_SFloat,
            msaaSamples = MSAASamples.None,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = true,
            clearColor = Color.clear
        };
        resources.normalDepth = graph.CreateTexture(desc);
        desc.name = "Liquid ID Opacity Outline Highlight";
        resources.metadata = graph.CreateTexture(desc);
        desc.name = "Liquid Outline Color";
        desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
        resources.outlineColor = graph.CreateTexture(desc);
        desc.name = "Liquid Private Depth";
        desc.colorFormat = GraphicsFormat.None;
        desc.depthBufferBits = DepthBits.Depth32;
        var depth = graph.CreateTexture(desc);

        var drawing = RenderingUtils.CreateDrawingSettings(new ShaderTagId("TransparentOutlineData"),
            rendering, camera, lights, SortingCriteria.CommonOpaque);
        // IDs are supplied through renderer property blocks, not instanced properties.
        drawing.enableDynamicBatching = false;
        drawing.enableInstancing = false;
        var filtering = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask);
        var parameters = new RendererListParams(rendering.cullResults, drawing, filtering);
        using (var builder = graph.AddRasterRenderPass<PassData>("Liquid Outline Data", out var data))
        {
            data.renderers = graph.CreateRendererList(parameters);
            builder.UseRendererList(data.renderers);
            builder.UseAllGlobalTextures(true);
            if (scene.cameraDepthTexture.IsValid()) builder.UseTexture(scene.cameraDepthTexture);
            builder.SetRenderAttachment(resources.normalDepth, 0);
            builder.SetRenderAttachment(resources.metadata, 1);
            builder.SetRenderAttachment(resources.outlineColor, 2);
            builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
            builder.SetGlobalTextureAfterPass(resources.normalDepth, NormalDepthId);
            builder.SetGlobalTextureAfterPass(resources.metadata, MetadataId);
            builder.SetGlobalTextureAfterPass(resources.outlineColor, ColorId);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((PassData pass, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(pass.renderers));
        }
    }
}
