using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ForgingPrototype
{
	/// <summary>
	/// Body fill stays the forged silhouette (dark flats). Grind shows only as a
	/// multi-ring bevel strip along the perimeter so different edge sections can
	/// band at different sharpness levels.
	/// </summary>
	[RequireComponent(typeof(GrindBladeBody))]
	public class GrindMetalView : MonoBehaviour
	{
		[SerializeField] GrindBladeBody blade;
		[SerializeField] float fillZ = -0.01f;
		[SerializeField] float bevelZ = -0.015f;
		[SerializeField] float outlineZ = -0.03f;
		[SerializeField] float outlineWidth = 0.03f;
		[SerializeField] int fillSortingOrder = 8;
		[SerializeField] int bevelSortingOrder = 10;
		[SerializeField] int outlineSortingOrder = 11;

		[Header("Flat / Oxide")]
		[SerializeField] Color flatColor = new Color(0.2f, 0.22f, 0.28f, 1f);

		[Header("Edge Bands (hard steps, outer → inner)")]
		[Tooltip("Outermost tip when that section is keen.")]
		[SerializeField] Color outerBandColor = new Color(0.78f, 0.78f, 0.8f, 1f);
		[Tooltip("Mid grind band under the bright tip.")]
		[SerializeField] Color midBandColor = new Color(0.38f, 0.38f, 0.4f, 1f);

		[Header("Outline")]
		[SerializeField] Color sharpenOutlineColor = new Color(0.75f, 0.92f, 1f, 1f);
		[SerializeField] EdgeGrindEvaluator edgeEvaluator;

		[Header("Bevel")]
		[SerializeField] float minBevelWidth = 0.04f;
		[SerializeField] float maxBevelWidth = 0.32f;
		[SerializeField] float grindForFullWidth = 1f;
		[SerializeField] float visibleGrindThreshold = 0.03f;
		[Tooltip("Grind amount needed before the mid (dark grey) band appears.")]
		[SerializeField] float midBandGrind = 0.2f;
		[Tooltip("Grind amount needed before the outer (light) tip band appears.")]
		[SerializeField] float outerBandGrind = 0.55f;
		[Tooltip("Fraction of bevel width occupied by the outer light band when fully keen.")]
		[SerializeField, Range(0.05f, 0.5f)] float outerBandFraction = 0.28f;
		[Tooltip("Fraction of bevel width occupied by mid+outer bands together when fully ground.")]
		[SerializeField, Range(0.2f, 0.95f)] float midBandFraction = 0.7f;

		[Header("Scene Objects")]
		[SerializeField] MeshFilter fillFilter;
		[SerializeField] MeshRenderer fillRenderer;
		[SerializeField] MeshFilter bevelFilter;
		[SerializeField] MeshRenderer bevelRenderer;
		[SerializeField] LineRenderer outline;
		[SerializeField] Transform sharpenOutlineRoot;
		[SerializeField] Material fillMaterial;
		[SerializeField] Material bevelMaterial;

		readonly List<Vector2> worldVerts = new List<Vector2>();
		readonly List<Vector2> inwardNormals = new List<Vector2>();
		readonly List<Vector3> bevelVertBuffer = new List<Vector3>();
		readonly List<Color> bevelColorBuffer = new List<Color>();
		readonly List<Vector2> bevelUvBuffer = new List<Vector2>();
		readonly List<int> bevelTriBuffer = new List<int>();
		readonly List<LineRenderer> sharpenOutlineSegments = new List<LineRenderer>();

		Mesh fillMesh;
		Mesh bevelMesh;
		MaterialPropertyBlock tintBlock;

		void Awake()
		{
			if (blade == null)
				blade = GetComponent<GrindBladeBody>();
			EnsureVisuals();
		}

		void OnEnable()
		{
			if (blade != null)
				blade.VerticesChanged += Rebuild;
			Rebuild();
		}

		void OnDisable()
		{
			if (blade != null)
				blade.VerticesChanged -= Rebuild;
		}

		public void Configure(GrindBladeBody source, EdgeGrindEvaluator evaluator = null)
		{
			if (blade != null)
				blade.VerticesChanged -= Rebuild;

			blade = source;
			if (evaluator != null)
				edgeEvaluator = evaluator;
			else if (edgeEvaluator == null)
				edgeEvaluator = GetComponent<EdgeGrindEvaluator>();

			EnsureVisuals();

			if (blade != null)
			{
				blade.VerticesChanged -= Rebuild;
				blade.VerticesChanged += Rebuild;
			}

			Rebuild();
		}

		void EnsureVisuals()
		{
			EnsureMeshObject(ref fillFilter, ref fillRenderer, "GrindMetalFill", fillSortingOrder, ref fillMesh, "GrindMetalFill");
			EnsureMeshObject(ref bevelFilter, ref bevelRenderer, "GrindEdgeBevel", bevelSortingOrder, ref bevelMesh, "GrindEdgeBevel");

			// Same double-sided vertex-color shader as bevel — URP Unlit culls the
			// silhouette when outline winding faces away from the grind camera.
			if (fillMaterial == null
				|| fillMaterial.shader == null
				|| fillMaterial.shader.name != "ForgingPrototype/UnlitVertexColor")
				fillMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
			if (bevelMaterial == null
				|| bevelMaterial.shader == null
				|| bevelMaterial.shader.name != "ForgingPrototype/UnlitVertexColor")
				bevelMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();

			ForgingVisualUtility.EnsureMeshFillMaterial(fillMaterial, Color.white);
			ForgingVisualUtility.EnsureMeshFillMaterial(bevelMaterial, Color.white);

			ApplyRenderer(fillRenderer, fillMaterial, fillSortingOrder);
			ApplyRenderer(bevelRenderer, bevelMaterial, bevelSortingOrder);

			if (outline == null)
			{
				Transform existing = transform.Find("GrindMetalOutline");
				if (existing != null)
					outline = existing.GetComponent<LineRenderer>();
			}

			if (outline == null)
			{
				var outlineGo = new GameObject("GrindMetalOutline");
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

			EnsureSharpenOutlineRoot();

			ForgingVisualUtility.ApplyLayerRecursively(gameObject, gameObject.layer);
		}

		void EnsureSharpenOutlineRoot()
		{
			if (sharpenOutlineRoot == null)
			{
				Transform existing = transform.Find("SharpenOutline");
				if (existing != null)
					sharpenOutlineRoot = existing;
			}

			if (sharpenOutlineRoot == null)
			{
				var rootGo = new GameObject("SharpenOutline");
				rootGo.transform.SetParent(transform, false);
				sharpenOutlineRoot = rootGo.transform;
			}
		}

		LineRenderer GetOrCreateSharpenSegment(int index)
		{
			EnsureSharpenOutlineRoot();
			while (sharpenOutlineSegments.Count <= index)
			{
				var go = new GameObject($"Seg{sharpenOutlineSegments.Count}");
				go.transform.SetParent(sharpenOutlineRoot, false);
				var line = go.AddComponent<LineRenderer>();
				line.useWorldSpace = true;
				line.loop = false;
				ForgingVisualUtility.ApplyLineRendererDefaults(
					line,
					sharpenOutlineColor,
					outlineWidth,
					outlineSortingOrder + 1);
				sharpenOutlineSegments.Add(line);
			}

			return sharpenOutlineSegments[index];
		}

		void ClearUnusedSharpenSegments(int usedCount)
		{
			for (int i = usedCount; i < sharpenOutlineSegments.Count; i++)
				sharpenOutlineSegments[i].positionCount = 0;
		}

		void EnsureMeshObject(
			ref MeshFilter filter,
			ref MeshRenderer renderer,
			string childName,
			int sortingOrder,
			ref Mesh mesh,
			string meshName)
		{
			if (filter == null)
			{
				Transform existing = transform.Find(childName);
				if (existing != null)
				{
					filter = existing.GetComponent<MeshFilter>();
					renderer = existing.GetComponent<MeshRenderer>();
				}
			}

			if (filter == null)
			{
				var go = new GameObject(childName);
				go.transform.SetParent(transform, false);
				filter = go.AddComponent<MeshFilter>();
				renderer = go.AddComponent<MeshRenderer>();
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				renderer.sortingOrder = sortingOrder;
			}

			if (mesh == null)
			{
				mesh = filter.sharedMesh != null && filter.sharedMesh.name == meshName
					? filter.sharedMesh
					: new Mesh { name = meshName };
				mesh.MarkDynamic();
			}

			filter.sharedMesh = mesh;
		}

		static void ApplyRenderer(MeshRenderer renderer, Material material, int sortingOrder)
		{
			if (renderer == null)
				return;

			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.sortingOrder = sortingOrder;
			renderer.enabled = true;
		}

		void Rebuild()
		{
			EnsureVisuals();
			if (blade == null || blade.VertexCount < 3)
			{
				if (fillMesh != null)
					fillMesh.Clear();
				if (bevelMesh != null)
					bevelMesh.Clear();
				if (outline != null)
					outline.positionCount = 0;
				ClearUnusedSharpenSegments(0);
				return;
			}

			blade.GetWorldVertices(worldVerts);
			RebuildFill();
			RebuildBevel();
			RebuildOutline();
			ApplyWhiteTint(fillRenderer, fillMaterial);
			ApplyWhiteTint(bevelRenderer, bevelMaterial);
		}

		void RebuildFill()
		{
			int count = worldVerts.Count;
			var verts = new Vector3[count + 1];
			var uvs = new Vector2[count + 1];
			var colors = new Color[count + 1];

			Vector2 centroid = Centroid(worldVerts);
			Transform fillTx = fillFilter.transform;
			float worldZ = fillTx.position.z + fillZ;

			verts[0] = fillTx.InverseTransformPoint(new Vector3(centroid.x, centroid.y, worldZ));
			uvs[0] = Vector2.one * 0.5f;
			colors[0] = flatColor;

			for (int i = 0; i < count; i++)
			{
				Vector2 v = worldVerts[i];
				verts[i + 1] = fillTx.InverseTransformPoint(new Vector3(v.x, v.y, worldZ));
				uvs[i + 1] = Vector2.one * 0.5f;
				colors[i + 1] = flatColor;
			}

			// Grind camera looks +Z, so it needs -Z-facing triangles.
			// CCW in XY produces +Z normals — flip those.
			bool ccw = SignedArea(worldVerts) > 0f;
			var tris = new int[count * 3];
			for (int i = 0; i < count; i++)
			{
				int t = i * 3;
				int a = i + 1;
				int b = (i + 1) % count + 1;
				tris[t] = 0;
				if (ccw)
				{
					tris[t + 1] = b;
					tris[t + 2] = a;
				}
				else
				{
					tris[t + 1] = a;
					tris[t + 2] = b;
				}
			}

			fillMesh.Clear();
			fillMesh.SetVertices(verts);
			fillMesh.SetUVs(0, uvs);
			fillMesh.SetColors(colors);
			fillMesh.SetTriangles(tris, 0);
			fillMesh.RecalculateBounds();
			fillMesh.RecalculateNormals();
		}

		void RebuildBevel()
		{
			bevelVertBuffer.Clear();
			bevelColorBuffer.Clear();
			bevelUvBuffer.Clear();
			bevelTriBuffer.Clear();
			BuildInwardNormals(worldVerts, inwardNormals);

			int n = worldVerts.Count;
			Transform bevelTx = bevelFilter.transform;
			float worldZ = bevelTx.position.z + bevelZ;
			Vector2 centroid = Centroid(worldVerts);

			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				float g0 = blade.GrindAmounts[i];
				float g1 = blade.GrindAmounts[j];
				if (g0 < visibleGrindThreshold && g1 < visibleGrindThreshold)
					continue;

				Vector2 outer0 = worldVerts[i];
				Vector2 outer1 = worldVerts[j];
				Vector2 in0 = inwardNormals[i];
				Vector2 in1 = inwardNormals[j];
				if (in0.sqrMagnitude < 0.0001f)
					in0 = (centroid - outer0).normalized;
				if (in1.sqrMagnitude < 0.0001f)
					in1 = (centroid - outer1).normalized;

				float w0 = BevelWidth(g0);
				float w1 = BevelWidth(g1);
				GetBandDepths(g0, out float outerD0, out float midD0);
				GetBandDepths(g1, out float outerD1, out float midD1);

				bool mid0 = g0 >= midBandGrind;
				bool mid1 = g1 >= midBandGrind;
				bool tip0 = g0 >= outerBandGrind;
				bool tip1 = g1 >= outerBandGrind;

				// Mid (dark grey) sits under the tip — hard strip, no color blend.
				if (mid0 || mid1)
				{
					float midStart0 = tip0 ? outerD0 : 0f;
					float midStart1 = tip1 ? outerD1 : 0f;
					AddSolidBand(
						bevelTx, worldZ,
						PointOnBevel(outer0, in0, w0, midStart0),
						PointOnBevel(outer1, in1, w1, midStart1),
						PointOnBevel(outer1, in1, w1, midD1),
						PointOnBevel(outer0, in0, w0, midD0),
						midBandColor);
				}

				// Outer tip (light grey) — separate verts so the boundary stays stepped.
				if (tip0 || tip1)
				{
					AddSolidBand(
						bevelTx, worldZ,
						PointOnBevel(outer0, in0, w0, 0f),
						PointOnBevel(outer1, in1, w1, 0f),
						PointOnBevel(outer1, in1, w1, outerD1),
						PointOnBevel(outer0, in0, w0, outerD0),
						outerBandColor);
				}
			}

			bevelMesh.Clear();
			if (bevelTriBuffer.Count == 0)
				return;

			bevelMesh.SetVertices(bevelVertBuffer);
			bevelMesh.SetUVs(0, bevelUvBuffer);
			bevelMesh.SetColors(bevelColorBuffer);
			bevelMesh.SetTriangles(bevelTriBuffer, 0);
			bevelMesh.RecalculateBounds();
			bevelMesh.RecalculateNormals();
		}

		void GetBandDepths(float grind, out float outerDepth, out float midDepth)
		{
			outerDepth = 0f;
			midDepth = 0f;
			if (grind < visibleGrindThreshold)
				return;

			if (grind >= midBandGrind)
			{
				float midProgress = Mathf.InverseLerp(midBandGrind, grindForFullWidth, grind);
				midDepth = midBandFraction * Mathf.Clamp01(midProgress);
			}

			if (grind >= outerBandGrind)
			{
				float outerProgress = Mathf.InverseLerp(outerBandGrind, grindForFullWidth, grind);
				outerDepth = outerBandFraction * Mathf.Clamp01(outerProgress);
				outerDepth = Mathf.Min(outerDepth, midDepth * 0.85f);
			}
		}

		static Vector2 PointOnBevel(Vector2 outer, Vector2 inward, float width, float depth01)
		{
			return outer + inward * (width * Mathf.Clamp01(depth01));
		}

		void AddSolidBand(
			Transform space,
			float worldZ,
			Vector2 a,
			Vector2 b,
			Vector2 c,
			Vector2 d,
			Color color)
		{
			int start = bevelVertBuffer.Count;
			bevelVertBuffer.Add(space.InverseTransformPoint(new Vector3(a.x, a.y, worldZ)));
			bevelVertBuffer.Add(space.InverseTransformPoint(new Vector3(b.x, b.y, worldZ)));
			bevelVertBuffer.Add(space.InverseTransformPoint(new Vector3(c.x, c.y, worldZ)));
			bevelVertBuffer.Add(space.InverseTransformPoint(new Vector3(d.x, d.y, worldZ)));
			for (int i = 0; i < 4; i++)
			{
				bevelColorBuffer.Add(color);
				bevelUvBuffer.Add(Vector2.one * 0.5f);
			}

			bevelTriBuffer.Add(start);
			bevelTriBuffer.Add(start + 1);
			bevelTriBuffer.Add(start + 2);
			bevelTriBuffer.Add(start);
			bevelTriBuffer.Add(start + 2);
			bevelTriBuffer.Add(start + 3);
		}

		void RebuildOutline()
		{
			int count = worldVerts.Count;
			if (outline == null || count < 2)
			{
				if (outline != null)
					outline.positionCount = 0;
				ClearUnusedSharpenSegments(0);
				return;
			}

			Color defaultEdge = Color.Lerp(flatColor, Color.black, 0.25f);
			outline.positionCount = count;
			for (int i = 0; i < count; i++)
			{
				Vector2 v = worldVerts[i];
				outline.SetPosition(i, new Vector3(v.x, v.y, outlineZ));
			}

			outline.startColor = defaultEdge;
			outline.endColor = defaultEdge;

			RebuildSharpenOutlineSegments(count);
		}

		void RebuildSharpenOutlineSegments(int count)
		{
			IReadOnlyList<bool> edgeFlags = edgeEvaluator != null
				? edgeEvaluator.SilhouetteEdgeNeedsSharpening
				: null;

			int segmentIndex = 0;
			if (edgeFlags != null && edgeFlags.Count == count)
			{
				for (int i = 0; i < count; i++)
				{
					if (!edgeFlags[i])
						continue;

					int j = (i + 1) % count;
					Vector2 a = worldVerts[i];
					Vector2 b = worldVerts[j];
					if ((b - a).sqrMagnitude < 0.000001f)
						continue;

					LineRenderer segment = GetOrCreateSharpenSegment(segmentIndex++);
					segment.positionCount = 2;
					segment.SetPosition(0, new Vector3(a.x, a.y, outlineZ));
					segment.SetPosition(1, new Vector3(b.x, b.y, outlineZ));
					segment.startColor = sharpenOutlineColor;
					segment.endColor = sharpenOutlineColor;
					segment.widthMultiplier = outlineWidth;
					segment.sortingOrder = outlineSortingOrder + 1;
				}
			}

			ClearUnusedSharpenSegments(segmentIndex);
		}

		float BevelWidth(float grind)
		{
			if (grind < visibleGrindThreshold)
				return 0f;

			float t = Mathf.Clamp01(grind / Mathf.Max(0.05f, grindForFullWidth));
			t = Mathf.SmoothStep(0f, 1f, t);
			return Mathf.Lerp(minBevelWidth, maxBevelWidth, t);
		}

		void ApplyWhiteTint(MeshRenderer renderer, Material material)
		{
			if (renderer == null)
				return;

			tintBlock ??= new MaterialPropertyBlock();
			renderer.GetPropertyBlock(tintBlock);
			tintBlock.SetColor("_Color", Color.white);
			tintBlock.SetColor("_BaseColor", Color.white);
			tintBlock.SetColor("_RendererColor", Color.white);
			if (material != null)
			{
				if (material.HasProperty("_MainTex"))
					tintBlock.SetTexture("_MainTex", Texture2D.whiteTexture);
				if (material.HasProperty("_BaseMap"))
					tintBlock.SetTexture("_BaseMap", Texture2D.whiteTexture);
			}

			renderer.SetPropertyBlock(tintBlock);
		}

		static Vector2 Centroid(List<Vector2> verts)
		{
			Vector2 c = Vector2.zero;
			for (int i = 0; i < verts.Count; i++)
				c += verts[i];
			return c / Mathf.Max(1, verts.Count);
		}

		static void BuildInwardNormals(List<Vector2> verts, List<Vector2> dst)
		{
			dst.Clear();
			int n = verts.Count;
			bool ccw = SignedArea(verts) > 0f;
			for (int i = 0; i < n; i++)
			{
				Vector2 prev = verts[(i - 1 + n) % n];
				Vector2 cur = verts[i];
				Vector2 next = verts[(i + 1) % n];
				Vector2 e0 = cur - prev;
				Vector2 e1 = next - cur;
				Vector2 n0 = PerpInward(e0, ccw);
				Vector2 n1 = PerpInward(e1, ccw);
				Vector2 combined = n0 + n1;
				dst.Add(combined.sqrMagnitude > 0.0001f ? combined.normalized : Vector2.zero);
			}
		}

		static Vector2 PerpInward(Vector2 edge, bool ccw)
		{
			if (edge.sqrMagnitude < 0.000001f)
				return Vector2.zero;

			// CCW polygon: inward is rotate edge 90° CCW (-y, x). CW: opposite.
			Vector2 n = ccw ? new Vector2(-edge.y, edge.x) : new Vector2(edge.y, -edge.x);
			return n.normalized;
		}

		static float SignedArea(List<Vector2> verts)
		{
			float area = 0f;
			for (int i = 0; i < verts.Count; i++)
			{
				Vector2 a = verts[i];
				Vector2 b = verts[(i + 1) % verts.Count];
				area += a.x * b.y - b.x * a.y;
			}

			return area * 0.5f;
		}
	}
}
