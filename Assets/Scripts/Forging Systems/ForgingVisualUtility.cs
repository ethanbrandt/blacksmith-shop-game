using UnityEngine;
using UnityEngine.Rendering;

public static class ForgingVisualUtility
{
	public const int ForgeLayer = 6;

	static Material spritesMaterial;
	static Material vertexColorMaterial;
	static Shader cachedUnlitShader;
	static Shader cachedVertexColorShader;

	public static int ResolveForgeLayer()
	{
		int named = LayerMask.NameToLayer("Forge");
		return named >= 0 ? named : ForgeLayer;
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
		Transform t = root.transform;
		for (int i = 0; i < t.childCount; i++)
			ApplyLayerRecursively(t.GetChild(i).gameObject, layer);
	}

	/// <summary>
	/// LineRenderer / particle friendly material. Avoids Resources.GetBuiltinResource
	/// ("Sprites-Default.mat"), which errors on newer Unity versions.
	/// </summary>
	public static Material GetSpritesDefaultMaterial()
	{
		if (spritesMaterial != null)
			return spritesMaterial;

		Shader shader = Shader.Find("Sprites/Default");
		if (shader == null)
			shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
		if (shader == null)
			return CreateColorMaterial(Color.white);

		spritesMaterial = new Material(shader) { name = "ForgeSpritesDefault" };
		EnsureMeshFillMaterial(spritesMaterial, Color.white);
		return spritesMaterial;
	}

	/// <summary>
	/// Mesh material that multiplies vertex colors (grind bands / highlights).
	/// </summary>
	public static Material CreateVertexColorMaterial(Color tint)
	{
		if (vertexColorMaterial != null)
		{
			var clone = new Material(vertexColorMaterial) { name = "ForgeVertexColor" };
			ApplySolidColor(clone, tint);
			return clone;
		}

		Shader shader = ResolveVertexColorShader();
		var material = new Material(shader) { name = "ForgeVertexColor" };
		EnsureMeshFillMaterial(material, tint);
		material.renderQueue = (int)RenderQueue.Transparent;
		vertexColorMaterial = material;
		return new Material(material) { name = "ForgeVertexColor" };
	}

	public static Material GetSharedVertexColorMaterial()
	{
		if (vertexColorMaterial != null)
			return vertexColorMaterial;

		Shader shader = ResolveVertexColorShader();
		vertexColorMaterial = new Material(shader) { name = "ForgeVertexColorShared" };
		EnsureMeshFillMaterial(vertexColorMaterial, Color.white);
		vertexColorMaterial.renderQueue = (int)RenderQueue.Transparent;
		return vertexColorMaterial;
	}

	public static Material CreateColorMaterial(Color color)
	{
		Shader shader = ResolveUnlitShader();
		var material = new Material(shader) { name = "ForgeColor" };
		ApplySolidColor(material, color);
		material.renderQueue = (int)RenderQueue.Transparent;
		return material;
	}

	/// <summary>
	/// Sprites/Default samples _MainTex; a null texture makes MeshRenderer fills invisible
	/// (LineRenderers still draw because they supply UVs/geometry differently).
	/// </summary>
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

		line.material = GetSpritesDefaultMaterial();
		line.textureMode = LineTextureMode.Stretch;
		line.shadowCastingMode = ShadowCastingMode.Off;
		line.receiveShadows = false;
		line.allowOcclusionWhenDynamic = false;
		line.startColor = color;
		line.endColor = color;
		line.widthMultiplier = width;
		line.sortingOrder = sortingOrder;
		line.numCornerVertices = 4;
		line.numCapVertices = 4;
	}

	static Shader ResolveUnlitShader()
	{
		if (cachedUnlitShader != null)
			return cachedUnlitShader;

		cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
		if (cachedUnlitShader == null)
			cachedUnlitShader = Shader.Find("Unlit/Color");
		if (cachedUnlitShader == null)
			cachedUnlitShader = Shader.Find("Sprites/Default");
		if (cachedUnlitShader == null)
			cachedUnlitShader = Shader.Find("Hidden/InternalErrorShader");

		return cachedUnlitShader;
	}

	static Shader ResolveVertexColorShader()
	{
		if (cachedVertexColorShader != null)
			return cachedVertexColorShader;

		cachedVertexColorShader = Shader.Find("ForgingPrototype/UnlitVertexColor");
		if (cachedVertexColorShader == null)
			cachedVertexColorShader = Shader.Find("Sprites/Default");
		if (cachedVertexColorShader == null)
			cachedVertexColorShader = ResolveUnlitShader();

		return cachedVertexColorShader;
	}
}
