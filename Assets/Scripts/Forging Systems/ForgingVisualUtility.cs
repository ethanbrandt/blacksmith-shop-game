using UnityEngine;
using UnityEngine.Rendering;

public static class ForgingVisualUtility
{
	public const int ForgeLayer = 6;
	const int RoundedLineVertexCount = 4;
	static Material vertexColorMaterial;
	public static int ResolveForgeLayer()
	{
		int namedLayerIndex = LayerMask.NameToLayer("Forge");
		return namedLayerIndex >= 0 ? namedLayerIndex : ForgeLayer;
	}

	public static void ExcludeForgeLayer(Camera camera)
	{
		if (camera == null)
			return;

		camera.cullingMask &= ~(1 << ResolveForgeLayer());
	}

	public static void ApplyLayerRecursively(GameObject root, int layer)
	{
		if (root == null)
			return;

		root.layer = layer;
		Transform rootTransform = root.transform;
		for (int i = 0; i < rootTransform.childCount; i++)
			ApplyLayerRecursively(rootTransform.GetChild(i).gameObject, layer);
	}

	// Shared asset is immutable. Renderers tint through property blocks; factory clones
	// are reserved for callers (including editor rig builders) that own their lifetime.
	public static Material GetSpritesDefaultMaterial() => GetSharedVertexColorMaterial();

	public static Material GetSharedVertexColorMaterial()
	{
		if (vertexColorMaterial == null)
			vertexColorMaterial = Resources.Load<Material>("Crafting/Overlay");
		
		if (vertexColorMaterial == null)
			throw new System.InvalidOperationException("Missing Resources/Crafting/Overlay material.");
		
		return vertexColorMaterial;
	}

	public static Material CreateVertexColorMaterial(Color tint) => CreateColorMaterial(tint);

	public static Material CreateColorMaterial(Color color)
	{
		var material = new Material(GetSharedVertexColorMaterial())
		{
			name = "CraftingColor"
		};
		EnsureMeshFillMaterial(material, color);
		return material;
	}

	public static void DestroyGenerated(Object resource)
	{
		if (resource == null)
			return;
		if (Application.isPlaying)
			Object.Destroy(resource);
		else
			Object.DestroyImmediate(resource);
	}

	public static void SetTint(Renderer renderer, Color color)
	{
		if (renderer == null)
			return;
		var tintProperties = new MaterialPropertyBlock();
		renderer.GetPropertyBlock(tintProperties);
		tintProperties.SetColor("_BaseColor", color);
		tintProperties.SetColor("_Color", color);
		renderer.SetPropertyBlock(tintProperties);
	}

	public static void EnsureMeshFillMaterial(Material material, Color? color = null)
	{
		if (material == null)
			return;

		if (material.HasProperty("_MainTex") && material.mainTexture == null)
			material.mainTexture = Texture2D.whiteTexture;
		if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") == null)
			material.SetTexture("_BaseMap", Texture2D.whiteTexture);

		if (color.HasValue)
			ApplySolidColor(material, color.Value);
	}

	static void ApplySolidColor(Material material, Color color)
	{
		material.color = color;
		if (material.HasProperty("_BaseColor"))
			material.SetColor("_BaseColor", color);
		if (material.HasProperty("_Color"))
			material.SetColor("_Color", color);
		if (material.HasProperty("_RendererColor"))
			material.SetColor("_RendererColor", Color.white);
	}

	public static void ApplyLineRendererDefaults(LineRenderer line, Color color, float width, int sortingOrder)
	{
		if (line == null)
			return;
		line.sharedMaterial = GetSpritesDefaultMaterial();
		line.textureMode = LineTextureMode.Stretch;
		line.shadowCastingMode = ShadowCastingMode.Off;
		line.receiveShadows = false;
		line.allowOcclusionWhenDynamic = false;
		line.startColor = color;
		line.endColor = color;
		line.widthMultiplier = width;
		line.sortingOrder = sortingOrder;
		line.numCornerVertices = RoundedLineVertexCount;
		line.numCapVertices = RoundedLineVertexCount;
	}
}
