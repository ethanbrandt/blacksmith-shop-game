using UnityEngine;
using UnityEngine.Rendering;

namespace ForgingPrototype
{
	public static class ForgingVisualUtility
	{
		public const int ForgeLayer = 6;

		static Material spritesMaterial;
		static Shader cachedUnlitShader;

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

		public static Material GetSpritesDefaultMaterial()
		{
			if (spritesMaterial != null)
				return spritesMaterial;

			Material shared = Resources.GetBuiltinResource<Material>("Sprites-Default.mat");
			if (shared != null)
			{
				spritesMaterial = new Material(shared) { name = "ForgeSpritesDefault" };
				return spritesMaterial;
			}

			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
			{
				spritesMaterial = new Material(shader) { name = "ForgeSpritesDefault" };
				return spritesMaterial;
			}

			return CreateColorMaterial(Color.white);
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

			// Prefer URP Unlit for MeshRenderer fills; Sprites/Default needs a white _MainTex.
			cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
			if (cachedUnlitShader == null)
				cachedUnlitShader = Shader.Find("Unlit/Color");
			if (cachedUnlitShader == null)
				cachedUnlitShader = Shader.Find("Sprites/Default");
			if (cachedUnlitShader == null)
				cachedUnlitShader = Shader.Find("Hidden/InternalErrorShader");

			return cachedUnlitShader;
		}
	}
}
