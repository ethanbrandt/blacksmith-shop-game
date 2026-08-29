using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
	[Tooltip("Caps how far a miter join extends at sharp corners (× bevel offset).")]
	[SerializeField, Min(1f)] float bevelMiterLimit = 4f;
	[Tooltip("Vertices within this distance of an outward tip share one inner ring point.")]
	[SerializeField, Min(0.001f)] float tipCollapseDistance = 0.22f;
	[Tooltip("Inward normals must diverge below this dot product to treat a vertex as an outward tip.")]
	[SerializeField, Range(-1f, 1f)] float outwardTipNormalDot = 0.65f;

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
	readonly List<Vector2> ringOuterScratch = new List<Vector2>();
	readonly List<Vector2> ringInnerScratch = new List<Vector2>();
	readonly List<int> ringOuterIdxScratch = new List<int>();
	readonly List<int> ringInnerIdxScratch = new List<int>();
	readonly List<bool> ringLockedScratch = new List<bool>();
	readonly Dictionary<long, int> weldedVertexLookup = new Dictionary<long, int>();
	readonly List<LineRenderer> sharpenOutlineSegments = new List<LineRenderer>();

	Mesh fillMesh;
	Mesh bevelMesh;
	MaterialPropertyBlock tintBlock;
	bool worldVertsCcw;

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
		worldVertsCcw = SignedArea(worldVerts) > 0f;
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

		EnsureRingScratchSize(n);

		BuildOuterBandStrip(bevelTx, worldZ, n);
		BuildMidBandStrip(bevelTx, worldZ, n);

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

	void EnsureRingScratchSize(int count)
	{
		while (ringOuterScratch.Count < count) ringOuterScratch.Add(default);
		while (ringInnerScratch.Count < count) ringInnerScratch.Add(default);
		while (ringOuterIdxScratch.Count < count) ringOuterIdxScratch.Add(-1);
		while (ringInnerIdxScratch.Count < count) ringInnerIdxScratch.Add(-1);
		while (ringLockedScratch.Count < count) ringLockedScratch.Add(false);
	}

	void BuildOuterBandStrip(Transform bevelTx, float worldZ, int n)
	{
		for (int i = 0; i < n; i++)
		{
			float g = blade.GrindAmounts[i];
			GetBandDepths(g, out float outerD, out _);
			ringOuterScratch[i] = worldVerts[i];
			ringInnerScratch[i] = g >= outerBandGrind
				? RingPointAt(i, outerD)
				: worldVerts[i];
		}

		CollapseInnerRingTowardTips(n, collapseOuter: false);
		BuildWeldedRingStrip(bevelTx, worldZ, n, outerBandColor, g => g >= outerBandGrind);
	}

	void BuildMidBandStrip(Transform bevelTx, float worldZ, int n)
	{
		for (int i = 0; i < n; i++)
		{
			float g = blade.GrindAmounts[i];
			GetBandDepths(g, out float outerD, out float midD);
			float midStart = g >= outerBandGrind ? outerD : 0f;
			ringOuterScratch[i] = g >= midBandGrind
				? RingPointAt(i, midStart)
				: worldVerts[i];
			ringInnerScratch[i] = g >= midBandGrind
				? RingPointAt(i, midD)
				: worldVerts[i];
		}

		CollapseInnerRingTowardTips(n, collapseOuter: true);
		BuildWeldedRingStrip(bevelTx, worldZ, n, midBandColor, g => g >= midBandGrind);
	}

	void CollapseInnerRingTowardTips(int n, bool collapseOuter)
	{
		for (int i = 0; i < n; i++)
			ringLockedScratch[i] = false;

		for (int tip = 0; tip < n; tip++)
		{
			if (!IsOutwardMiterTip(tip))
				continue;

			Vector2 mergedInner = ringInnerScratch[tip];
			Vector2 mergedOuter = ringOuterScratch[tip];
			ringLockedScratch[tip] = true;
			CollapseInnerChain(tip, forward: false, n, mergedInner, mergedOuter, collapseOuter);
			CollapseInnerChain(tip, forward: true, n, mergedInner, mergedOuter, collapseOuter);
		}
	}

	void CollapseInnerChain(
		int tip,
		bool forward,
		int n,
		Vector2 mergedInner,
		Vector2 mergedOuter,
		bool collapseOuter)
	{
		float walked = 0f;
		int cur = forward ? (tip + 1) % n : (tip - 1 + n) % n;
		for (int safety = 0; safety < n && walked < tipCollapseDistance; safety++)
		{
			if (!ringLockedScratch[cur])
			{
				ringInnerScratch[cur] = mergedInner;
				if (collapseOuter)
					ringOuterScratch[cur] = mergedOuter;
				ringLockedScratch[cur] = true;
			}

			int next = forward ? (cur + 1) % n : (cur - 1 + n) % n;
			if (next == tip)
				break;

			walked += Vector2.Distance(worldVerts[cur], worldVerts[next]);
			cur = next;
		}
	}

	void BuildWeldedRingStrip(
		Transform bevelTx,
		float worldZ,
		int n,
		Color color,
		System.Func<float, bool> includeEdgeByGrind)
	{
		weldedVertexLookup.Clear();

		for (int i = 0; i < n; i++)
		{
			ringOuterIdxScratch[i] = AddWeldedVertex(bevelTx, worldZ, ringOuterScratch[i], color);
			ringInnerIdxScratch[i] = AddWeldedVertex(bevelTx, worldZ, ringInnerScratch[i], color);
		}

		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			float g0 = blade.GrindAmounts[i];
			float g1 = blade.GrindAmounts[j];
			if (!includeEdgeByGrind(g0) && !includeEdgeByGrind(g1))
				continue;

			int o0 = ringOuterIdxScratch[i];
			int o1 = ringOuterIdxScratch[j];
			int in0 = ringInnerIdxScratch[i];
			int in1 = ringInnerIdxScratch[j];

			if (o0 == o1 && in0 == in1)
				continue;

			if (in0 == in1)
			{
				if (o0 != o1)
					AddIndexedTriangle(o0, o1, in0);
				continue;
			}

			if (o0 == o1)
			{
				if (in0 != in1)
					AddIndexedTriangle(o0, in1, in0);
				continue;
			}

			AddIndexedQuad(o0, o1, in1, in0);
		}
	}

	Vector2 RingPointAt(int vertexIndex, float depth01)
	{
		if (depth01 <= 0.0001f)
			return worldVerts[vertexIndex];

		float grind = blade.GrindAmounts[vertexIndex];
		float width = BevelWidth(grind);
		if (width <= 0f)
			return worldVerts[vertexIndex];

		if (ShouldMiterJoin(vertexIndex))
			return ComputeMiterBevelPoint(vertexIndex, depth01, width);

		return FallbackBevelPoint(vertexIndex, depth01, width);
	}

	int AddWeldedVertex(Transform space, float worldZ, Vector2 point, Color color)
	{
		long key = QuantizePointKey(point);
		if (weldedVertexLookup.TryGetValue(key, out int existing))
			return existing;

		int index = bevelVertBuffer.Count;
		bevelVertBuffer.Add(space.InverseTransformPoint(new Vector3(point.x, point.y, worldZ)));
		bevelColorBuffer.Add(color);
		bevelUvBuffer.Add(Vector2.one * 0.5f);
		weldedVertexLookup[key] = index;
		return index;
	}

	static long QuantizePointKey(Vector2 point)
	{
		const float scale = 10000f;
		int x = Mathf.RoundToInt(point.x * scale);
		int y = Mathf.RoundToInt(point.y * scale);
		return ((long)x << 32) ^ (uint)y;
	}

	void AddIndexedTriangle(int a, int b, int c)
	{
		bevelTriBuffer.Add(a);
		bevelTriBuffer.Add(b);
		bevelTriBuffer.Add(c);
	}

	void AddIndexedQuad(int a, int b, int c, int d)
	{
		AddIndexedTriangle(a, b, c);
		AddIndexedTriangle(a, c, d);
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

	Vector2 FallbackBevelPoint(int vertexIndex, float depth01, float width)
	{
		Vector2 inward = inwardNormals[vertexIndex];
		if (inward.sqrMagnitude < 0.0001f)
		{
			Vector2 centroid = Centroid(worldVerts);
			inward = (centroid - worldVerts[vertexIndex]).normalized;
		}

		return PointOnBevel(worldVerts[vertexIndex], inward, width, depth01);
	}

	bool ShouldMiterJoin(int vertexIndex)
	{
		int n = worldVerts.Count;
		if (n < 3)
			return false;

		int prevEdge = (vertexIndex - 1 + n) % n;
		return EdgeHasVisibleBevel(prevEdge) && EdgeHasVisibleBevel(vertexIndex);
	}

	bool IsOutwardMiterTip(int vertexIndex)
	{
		if (!ShouldMiterJoin(vertexIndex))
			return false;

		int n = worldVerts.Count;
		int prev = (vertexIndex - 1 + n) % n;
		int next = (vertexIndex + 1) % n;
		Vector2 v = worldVerts[vertexIndex];
		Vector2 e0 = v - worldVerts[prev];
		Vector2 e1 = worldVerts[next] - v;
		if (e0.sqrMagnitude < 0.000001f || e1.sqrMagnitude < 0.000001f)
			return false;

		Vector2 n0 = PerpInward(e0, worldVertsCcw);
		Vector2 n1 = PerpInward(e1, worldVertsCcw);
		if (n0.sqrMagnitude < 0.000001f || n1.sqrMagnitude < 0.000001f)
			return false;

		n0.Normalize();
		n1.Normalize();
		return Vector2.Dot(n0, n1) < outwardTipNormalDot;
	}

	bool EdgeHasVisibleBevel(int edgeIndex)
	{
		int j = (edgeIndex + 1) % worldVerts.Count;
		float g0 = blade.GrindAmounts[edgeIndex];
		float g1 = blade.GrindAmounts[j];
		return g0 >= visibleGrindThreshold || g1 >= visibleGrindThreshold;
	}

	Vector2 ComputeMiterBevelPoint(int vertexIndex, float depth01, float width)
	{
		int n = worldVerts.Count;
		int prev = (vertexIndex - 1 + n) % n;
		int next = (vertexIndex + 1) % n;

		Vector2 v = worldVerts[vertexIndex];
		Vector2 e0 = v - worldVerts[prev];
		Vector2 e1 = worldVerts[next] - v;
		if (e0.sqrMagnitude < 0.000001f || e1.sqrMagnitude < 0.000001f)
			return FallbackBevelPoint(vertexIndex, depth01, width);

		Vector2 n0 = PerpInward(e0, worldVertsCcw);
		Vector2 n1 = PerpInward(e1, worldVertsCcw);
		float offset = width * Mathf.Clamp01(depth01);
		if (offset <= 0f)
			return v;

		Vector2 miterDir = n0 + n1;
		if (miterDir.sqrMagnitude < 0.000001f)
			return FallbackBevelPoint(vertexIndex, depth01, width);

		miterDir.Normalize();
		Vector2 toCentroid = Centroid(worldVerts) - v;
		if (toCentroid.sqrMagnitude > 0.000001f && Vector2.Dot(miterDir, toCentroid) <= 0f)
			return FallbackBevelPoint(vertexIndex, depth01, width);

		float denom = Vector2.Dot(miterDir, n0.normalized);
		if (Mathf.Abs(denom) < 0.01f)
			return FallbackBevelPoint(vertexIndex, depth01, width);

		float miterLen = Mathf.Min(offset / denom, offset * bevelMiterLimit);
		return v + miterDir * miterLen;
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
