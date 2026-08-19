using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Override that makes child renderers share one object id.
/// By default every renderer gets its own id.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Crease Group")]
public class CreaseGroup : MonoBehaviour
{
    [Tooltip("Leave 0 to share an id only among children of this object. Set the same non-zero value on multiple Crease Groups to share across hierarchies.")]
    public int sharedId;

    public int GetId()
    {
        int id = sharedId != 0 ? sharedId : GetInstanceID();
        return id == 0 ? 1 : id;
    }
}

public class ObjectIdResources : ContextItem
{
    public static readonly int TextureId = Shader.PropertyToID("_ObjectIdTexture");
    public TextureHandle idTexture;

    public override void Reset()
    {
        idTexture = TextureHandle.nullHandle;
    }
}

public static class ObjectIdBinding
{
    static readonly int ObjectIdProp = Shader.PropertyToID("_ObjectId");
    static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();
    static readonly Dictionary<int, int> PackedIds = new Dictionary<int, int>();
    static readonly Dictionary<int, int> LastBound = new Dictionary<int, int>();
    static int _nextPackedId = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PackedIds.Clear();
        LastBound.Clear();
        _nextPackedId = 1;
    }

    public static void BindAll()
    {
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
            Bind(renderers[i]);
    }

    public static void Bind(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled)
            return;

        int packed = Pack(ResolveKey(renderer));
        int rendererId = renderer.GetInstanceID();
        if (LastBound.TryGetValue(rendererId, out int previous) && previous == packed)
            return;

        LastBound[rendererId] = packed;
        renderer.GetPropertyBlock(Mpb);
        Mpb.SetFloat(ObjectIdProp, packed);
        renderer.SetPropertyBlock(Mpb);
    }

    static int ResolveKey(Renderer renderer)
    {
        var group = renderer.GetComponentInParent<CreaseGroup>();
        if (group != null && group.isActiveAndEnabled)
            return group.GetId();

        int id = renderer.GetInstanceID();
        return id == 0 ? 1 : id;
    }

    static int Pack(int key)
    {
        if (PackedIds.TryGetValue(key, out int packed))
            return packed;

        packed = _nextPackedId++;
        PackedIds[key] = packed;
        return packed;
    }
}

public class ObjectIdPass : ScriptableRenderPass
{
    private Material _material;

    public ObjectIdPass(Material material)
    {
        _material = material;
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public void SetMaterial(Material material)
    {
        _material = material;
    }

    private class PassData
    {
        public RendererListHandle rendererListHandle;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null)
            return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalLightData lightData = frameData.Get<UniversalLightData>();

        if (!resourceData.activeDepthTexture.IsValid())
            return;

        TextureDesc idDesc = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
        {
            name = "_ObjectIdTexture",
            colorFormat = GraphicsFormat.R32_SFloat,
            depthBufferBits = DepthBits.None,
            msaaSamples = (MSAASamples)Mathf.Max(1, cameraData.cameraTargetDescriptor.msaaSamples),
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = true,
            clearColor = Color.clear
        };
        TextureHandle idTexture = renderGraph.CreateTexture(idDesc);

        var idResources = frameData.Create<ObjectIdResources>();
        idResources.idTexture = idTexture;

        ShaderTagId shadersToOverride = new ShaderTagId("UniversalForward");
        DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
            shadersToOverride, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
        drawSettings.overrideMaterial = _material;
        drawSettings.overrideMaterialPassIndex = 0;
        drawSettings.SetShaderPassName(1, new ShaderTagId("UniversalForwardOnly"));
        drawSettings.SetShaderPassName(2, new ShaderTagId("SRPDefaultUnlit"));

        FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.opaque, cameraData.camera.cullingMask);
        var rendererListParameters = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("ObjectId", out var passData))
        {
            passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParameters);

            builder.UseRendererList(passData.rendererListHandle);
            builder.SetRenderAttachment(idTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
            builder.SetGlobalTextureAfterPass(idTexture, ObjectIdResources.TextureId);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.rendererListHandle);
            });
        }
    }
}
