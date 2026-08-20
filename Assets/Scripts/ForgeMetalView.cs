using UnityEngine;
using UnityEngine.Rendering;

namespace ForgingPrototype
{
	[RequireComponent(typeof(MetalDeformer2D))]
	public class ForgeMetalView : MonoBehaviour
	{
		[SerializeField] MetalDeformer2D deformer;
		[SerializeField] float fillZ;
		[SerializeField] float outlineZ = -0.02f;
		[SerializeField] float outlineWidth = 0.045f;
		[SerializeField] int fillSortingOrder = 8;
		[SerializeField] int outlineSortingOrder = 9;
		[SerializeField] Color coldTint = new Color(0.45f, 0.48f, 0.55f, 1f);
		[SerializeField] Color hotTint = new Color(1f, 0.45f, 0.12f, 1f);

		[Header("Scene Objects")]
		[SerializeField] MeshFilter fillFilter;
		[SerializeField] MeshRenderer fillRenderer;
		[SerializeField] LineRenderer outline;
		[SerializeField] Material fillMaterial;

		Mesh fillMesh;
		MaterialPropertyBlock tintBlock;

		public MetalDeformer2D Deformer => deformer;

		void Awake()
		{
			if (deformer == null)
				deformer = GetComponent<MetalDeformer2D>();

			EnsureVisuals();
		}

		void OnEnable()
		{
			if (deformer != null)
				deformer.VerticesChanged += Rebuild;
			Rebuild();
		}

		void OnDisable()
		{
			if (deformer != null)
				deformer.VerticesChanged -= Rebuild;
		}

		void LateUpdate()
		{
			RefreshTint();
		}

		public void Configure(MetalDeformer2D source)
		{
			if (deformer != null)
				deformer.VerticesChanged -= Rebuild;

			deformer = source;
			EnsureVisuals();

			if (deformer != null)
			{
				deformer.VerticesChanged -= Rebuild;
				deformer.VerticesChanged += Rebuild;
			}

			Rebuild();
		}

		void EnsureVisuals()
		{
			if (fillFilter == null)
			{
				Transform existing = transform.Find("MetalFill");
				if (existing != null)
				{
					fillFilter = existing.GetComponent<MeshFilter>();
					fillRenderer = existing.GetComponent<MeshRenderer>();
				}
			}

			if (fillFilter == null)
			{
				var fillGo = new GameObject("MetalFill");
				fillGo.transform.SetParent(transform, false);
				fillFilter = fillGo.AddComponent<MeshFilter>();
				fillRenderer = fillGo.AddComponent<MeshRenderer>();
				fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
				fillRenderer.receiveShadows = false;
				fillRenderer.sortingOrder = fillSortingOrder;
			}

			if (fillMesh == null)
			{
				fillMesh = fillFilter.sharedMesh != null && fillFilter.sharedMesh.name == "ForgeMetalFill"
					? fillFilter.sharedMesh
					: new Mesh { name = "ForgeMetalFill" };
				fillMesh.MarkDynamic();
				fillFilter.sharedMesh = fillMesh;
			}

			if (fillMaterial == null)
				fillMaterial = ForgingVisualUtility.CreateColorMaterial(Color.white);
			if (fillRenderer != null && fillRenderer.sharedMaterial == null)
				fillRenderer.sharedMaterial = fillMaterial;

			if (outline == null)
			{
				Transform existing = transform.Find("MetalOutline");
				if (existing != null)
					outline = existing.GetComponent<LineRenderer>();
			}

			if (outline == null)
			{
				var outlineGo = new GameObject("MetalOutline");
				outlineGo.transform.SetParent(transform, false);
				outline = outlineGo.AddComponent<LineRenderer>();
				outline.useWorldSpace = true;
				outline.loop = true;
				ForgingVisualUtility.ApplyLineRendererDefaults(
					outline,
					Color.white,
					outlineWidth,
					outlineSortingOrder);
			}

			ForgingVisualUtility.ApplyLayerRecursively(gameObject, gameObject.layer);
		}

		void Rebuild()
		{
			EnsureVisuals();
			if (deformer == null || deformer.VertexCount < 3)
			{
				if (fillMesh != null)
					fillMesh.Clear();
				if (outline != null)
					outline.positionCount = 0;
				return;
			}

			var source = deformer.Vertices;
			int count = source.Count;
			var verts = new Vector3[count + 1];
			Vector2 centroid = Vector2.zero;
			for (int i = 0; i < count; i++)
				centroid += source[i];
			centroid /= count;

			verts[0] = new Vector3(centroid.x, centroid.y, fillZ);
			for (int i = 0; i < count; i++)
			{
				Vector2 v = source[i];
				verts[i + 1] = new Vector3(v.x, v.y, fillZ);
			}

			var tris = new int[count * 3];
			for (int i = 0; i < count; i++)
			{
				int t = i * 3;
				tris[t] = 0;
				tris[t + 1] = (i + 1) % count + 1;
				tris[t + 2] = i + 1;
			}

			fillMesh.Clear();
			fillMesh.SetVertices(verts);
			fillMesh.SetTriangles(tris, 0);
			fillMesh.RecalculateBounds();

			outline.positionCount = count;
			for (int i = 0; i < count; i++)
			{
				Vector2 v = source[i];
				outline.SetPosition(i, new Vector3(v.x, v.y, outlineZ));
			}

			RefreshTint();
		}

		void RefreshTint()
		{
			if (deformer == null)
				return;

			Color baseColor = deformer.MetalType != null ? deformer.MetalType.metalColor : Color.white;
			float heat01 = deformer.Heat;
			Color heated = Color.Lerp(Color.Lerp(coldTint, baseColor, 0.65f), hotTint, heat01);

			if (fillRenderer != null)
			{
				tintBlock ??= new MaterialPropertyBlock();
				fillRenderer.GetPropertyBlock(tintBlock);
				tintBlock.SetColor("_Color", heated);
				tintBlock.SetColor("_BaseColor", heated);
				fillRenderer.SetPropertyBlock(tintBlock);
			}

			if (outline != null)
			{
				Color edge = Color.Lerp(heated, Color.black, 0.35f);
				outline.startColor = edge;
				outline.endColor = edge;
			}
		}
	}
}
