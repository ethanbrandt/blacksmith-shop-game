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
	const float MinimumBevelDepth = 0.0001f;
	const float MinimumSquaredDirectionLength = 0.0001f;
	const float GeometrySquaredTolerance = 0.000001f;
	const float MinimumMiterProjection = 0.01f;
	const float MaximumOuterBandFraction = 0.85f;
	const float OutlineDarkening = 0.25f;
	const float MinimumGrindWidthRange = 0.05f;
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
	[Range(0.05f, 0.5f)]
	[SerializeField] float outerBandFraction = 0.28f;
	[Tooltip("Fraction of bevel width occupied by mid+outer bands together when fully ground.")]
	[Range(0.2f, 0.95f)]
	[SerializeField] float midBandFraction = 0.7f;
	[Tooltip("Caps how far a miter join extends at sharp corners (× bevel offset).")]
	[Min(1f)]
	[SerializeField] float bevelMiterLimit = 4f;
	[Tooltip("Vertices within this distance of an outward tip share one inner ring point.")]
	[Min(0.001f)]
	[SerializeField] float tipCollapseDistance = 0.22f;
	[Tooltip("Inward normals must diverge below this dot product to treat a vertex as an outward tip.")]
	[Range(-1f, 1f)]
	[SerializeField] float outwardTipNormalDot = 0.65f;

	[Header("Scene Objects")]
	[SerializeField] MeshFilter fillFilter;
	[SerializeField] MeshRenderer fillRenderer;
	[SerializeField] MeshFilter bevelFilter;
	[SerializeField] MeshRenderer bevelRenderer;
	[SerializeField] LineRenderer outline;
	[SerializeField] Transform sharpenOutlineRoot;
	[SerializeField] Material fillMaterial;
	[SerializeField] Material bevelMaterial;
	readonly List<Vector2> localVertices = new List<Vector2>();
	readonly List<Vector2> inwardNormals = new List<Vector2>();
	readonly List<Vector3> bevelVertexBuffer = new List<Vector3>();
	readonly List<Color> bevelColorBuffer = new List<Color>();
	readonly List<Vector2> bevelUvBuffer = new List<Vector2>();
	readonly List<int> bevelTriangleBuffer = new List<int>();
	readonly List<Vector2> ringOuterScratch = new List<Vector2>();
	readonly List<Vector2> ringInnerScratch = new List<Vector2>();
	readonly List<int> outerRingIndices = new List<int>();
	readonly List<int> innerRingIndices = new List<int>();
	readonly List<bool> ringLockedScratch = new List<bool>();
	readonly Dictionary<long, int> weldedVertexLookup = new Dictionary<long, int>();
	readonly List<LineRenderer> sharpenOutlineSegments = new List<LineRenderer>();

	Mesh fillMesh;
	readonly PolygonMeshBuilder fillBuilder = new PolygonMeshBuilder();
	Transform poseRoot;
	bool geometryDirty;

	void MarkDirty() => geometryDirty = true;

	void LateUpdate()
	{
		if (geometryDirty)
			Rebuild();
	}

	void OnDestroy()
	{
		if (fillMesh != null)
			ForgingVisualUtility.DestroyGenerated(fillMesh);
		if (bevelMesh != null)
			ForgingVisualUtility.DestroyGenerated(bevelMesh);
	}

	void UpdatePose()
	{
		if (poseRoot == null || blade == null)
			return;
		poseRoot.SetPositionAndRotation(new Vector3(blade.Position.x, blade.Position.y, transform.position.z), Quaternion.Euler(0f, 0f, blade.RotationDegrees));
	}

	Mesh bevelMesh;
	MaterialPropertyBlock tintBlock;
	bool isCounterClockwise;

	void Awake()
	{
		if (blade == null)
			blade = GetComponent<GrindBladeBody>();
		EnsureVisuals();
	}

	void OnEnable()
	{
		if (blade != null)
		{
			blade.VerticesChanged += MarkDirty;
			blade.PoseChanged += UpdatePose;
		}

		Rebuild();
	}

	void OnDisable()
	{
		if (blade != null)
		{
			blade.VerticesChanged -= MarkDirty;
			blade.PoseChanged -= UpdatePose;
		}
	}

	public void Configure(GrindBladeBody source, EdgeGrindEvaluator evaluator = null)
	{
		if (blade != null)
		{
			blade.VerticesChanged -= MarkDirty;
			blade.PoseChanged -= UpdatePose;
		}

		blade = source;
		if (evaluator != null)
			edgeEvaluator = evaluator;
		else if (edgeEvaluator == null)
			edgeEvaluator = GetComponent<EdgeGrindEvaluator>();

		EnsureVisuals();

		if (blade != null)
		{
			blade.VerticesChanged -= MarkDirty;
			blade.VerticesChanged += MarkDirty;
			blade.PoseChanged -= UpdatePose;
			blade.PoseChanged += UpdatePose;
		}

		Rebuild();
	}

	void EnsureVisuals()
	{
		EnsureMeshObject(ref fillFilter, ref fillRenderer, "GrindMetalFill", fillSortingOrder, ref fillMesh, "GrindMetalFill");
		EnsureMeshObject(ref bevelFilter, ref bevelRenderer, "GrindEdgeBevel", bevelSortingOrder, ref bevelMesh, "GrindEdgeBevel");
		fillMaterial = bevelMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
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
			outline.useWorldSpace = false;
			outline.loop = true;
			ForgingVisualUtility.ApplyLineRendererDefaults(outline, Color.white, outlineWidth, outlineSortingOrder);
		}

		EnsureSharpenOutlineRoot();
		if (poseRoot == null)
		{
			poseRoot = new GameObject("BladePose").transform;
			poseRoot.SetParent(transform, false);
			foreach (var child in new[]
			{
				fillFilter.transform,
				bevelFilter.transform,
				outline.transform,
				sharpenOutlineRoot
			}

			)
			{
				child.SetParent(poseRoot, false);
				child.localPosition = Vector3.zero;
				child.localRotation = Quaternion.identity;
				child.localScale = Vector3.one;
			}
		}

		outline.useWorldSpace = false;
		UpdatePose();
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
			line.useWorldSpace = false;
			line.loop = false;
			ForgingVisualUtility.ApplyLineRendererDefaults(line, sharpenOutlineColor, outlineWidth, outlineSortingOrder + 1);
			sharpenOutlineSegments.Add(line);
		}

		return sharpenOutlineSegments[index];
	}

	void ClearUnusedSharpenSegments(int usedCount)
	{
		for (int i = usedCount; i < sharpenOutlineSegments.Count; i++)
			sharpenOutlineSegments[i].positionCount = 0;
	}

	void EnsureMeshObject(ref MeshFilter filter, ref MeshRenderer renderer, string childName, int sortingOrder, ref Mesh mesh, string meshName)
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
			mesh = new Mesh
			{
				name = meshName
			};
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
		geometryDirty = false;
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

		blade.CopyLocalVerticesTo(localVertices);
		isCounterClockwise = SignedArea(localVertices) > 0f;
		RebuildFill();
		RebuildBevel();
		RebuildOutline();
		ApplyWhiteTint(fillRenderer, fillMaterial);
		ApplyWhiteTint(bevelRenderer, bevelMaterial);
	}

	void RebuildFill() => fillBuilder.Build(fillMesh, localVertices, fillZ, flatColor);

	void RebuildBevel()
	{
		bevelVertexBuffer.Clear();
		bevelColorBuffer.Clear();
		bevelUvBuffer.Clear();
		bevelTriangleBuffer.Clear();
		BuildInwardNormals(localVertices, inwardNormals);
		int vertexCount = localVertices.Count;
		float localDepth = bevelZ;
		EnsureRingScratchSize(vertexCount);
		BuildOuterBandStrip(localDepth, vertexCount);
		BuildMidBandStrip(localDepth, vertexCount);
		bevelMesh.Clear();
		if (bevelTriangleBuffer.Count == 0)
			return;
		bevelMesh.SetVertices(bevelVertexBuffer);
		bevelMesh.SetUVs(0, bevelUvBuffer);
		bevelMesh.SetColors(bevelColorBuffer);
		bevelMesh.SetTriangles(bevelTriangleBuffer, 0);
		bevelMesh.RecalculateBounds();
		bevelMesh.RecalculateNormals();
	}

	void EnsureRingScratchSize(int count)
	{
		while (ringOuterScratch.Count < count)
			ringOuterScratch.Add(default);
		while (ringInnerScratch.Count < count)
			ringInnerScratch.Add(default);
		while (outerRingIndices.Count < count)
			outerRingIndices.Add(-1);
		while (innerRingIndices.Count < count)
			innerRingIndices.Add(-1);
		while (ringLockedScratch.Count < count)
			ringLockedScratch.Add(false);
	}

	void BuildOuterBandStrip(float localDepth, int vertexCount)
	{
		for (int i = 0; i < vertexCount; i++)
		{
			float grindAmount = blade.GrindAmounts[i];
			GetBandDepths(grindAmount, out float outerDepth, out _);
			ringOuterScratch[i] = localVertices[i];
			ringInnerScratch[i] = grindAmount >= outerBandGrind ? RingPointAt(i, outerDepth) : localVertices[i];
		}

		CollapseInnerRingTowardTips(vertexCount, collapseOuter: false);
		BuildWeldedRingStrip(localDepth, vertexCount, outerBandColor, grindAmount => grindAmount >= outerBandGrind);
	}

	void BuildMidBandStrip(float localDepth, int vertexCount)
	{
		for (int i = 0; i < vertexCount; i++)
		{
			float grindAmount = blade.GrindAmounts[i];
			GetBandDepths(grindAmount, out float outerDepth, out float midDepth);
			float midStart = grindAmount >= outerBandGrind ? outerDepth : 0f;
			ringOuterScratch[i] = grindAmount >= midBandGrind ? RingPointAt(i, midStart) : localVertices[i];
			ringInnerScratch[i] = grindAmount >= midBandGrind ? RingPointAt(i, midDepth) : localVertices[i];
		}

		CollapseInnerRingTowardTips(vertexCount, collapseOuter: true);
		BuildWeldedRingStrip(localDepth, vertexCount, midBandColor, grindAmount => grindAmount >= midBandGrind);
	}

	void CollapseInnerRingTowardTips(int vertexCount, bool collapseOuter)
	{
		for (int i = 0; i < vertexCount; i++)
			ringLockedScratch[i] = false;
		for (int tip = 0; tip < vertexCount; tip++)
		{
			if (!IsOutwardMiterTip(tip))
				continue;

			Vector2 mergedInner = ringInnerScratch[tip];
			Vector2 mergedOuter = ringOuterScratch[tip];
			ringLockedScratch[tip] = true;
			CollapseInnerChain(tip, forward: false, vertexCount, mergedInner, mergedOuter, collapseOuter);
			CollapseInnerChain(tip, forward: true, vertexCount, mergedInner, mergedOuter, collapseOuter);
		}
	}

	void CollapseInnerChain(int tip, bool forward, int vertexCount, Vector2 mergedInner, Vector2 mergedOuter, bool collapseOuter)
	{
		float distanceAlongPerimeter = 0f;
		int currentIndex = forward ? (tip + 1) % vertexCount : (tip - 1 + vertexCount) % vertexCount;
		for (int visitedVertexCount = 0; visitedVertexCount < vertexCount && distanceAlongPerimeter < tipCollapseDistance; visitedVertexCount++)
		{
			if (!ringLockedScratch[currentIndex])
			{
				ringInnerScratch[currentIndex] = mergedInner;
				if (collapseOuter)
					ringOuterScratch[currentIndex] = mergedOuter;
				ringLockedScratch[currentIndex] = true;
			}

			int nextIndex = forward ? (currentIndex + 1) % vertexCount : (currentIndex - 1 + vertexCount) % vertexCount;
			if (nextIndex == tip)
				break;
			distanceAlongPerimeter += Vector2.Distance(localVertices[currentIndex], localVertices[nextIndex]);
			currentIndex = nextIndex;
		}
	}

	void BuildWeldedRingStrip(float localDepth, int vertexCount, Color color, System.Func<float, bool> includeEdgeByGrind)
	{
		weldedVertexLookup.Clear();
		for (int i = 0; i < vertexCount; i++)
		{
			outerRingIndices[i] = AddWeldedVertex(localDepth, ringOuterScratch[i], color);
			innerRingIndices[i] = AddWeldedVertex(localDepth, ringInnerScratch[i], color);
		}

		for (int i = 0; i < vertexCount; i++)
		{
			int j = (i + 1) % vertexCount;
			float startGrindAmount = blade.GrindAmounts[i];
			float endGrindAmount = blade.GrindAmounts[j];
			if (!includeEdgeByGrind(startGrindAmount) && !includeEdgeByGrind(endGrindAmount))
				continue;
			int outerStartIndex = outerRingIndices[i];
			int outerEndIndex = outerRingIndices[j];
			int innerStartIndex = innerRingIndices[i];
			int innerEndIndex = innerRingIndices[j];
			if (outerStartIndex == outerEndIndex && innerStartIndex == innerEndIndex)
				continue;
			if (innerStartIndex == innerEndIndex)
			{
				if (outerStartIndex != outerEndIndex)
					AddIndexedTriangle(outerStartIndex, outerEndIndex, innerStartIndex);
				continue;
			}

			if (outerStartIndex == outerEndIndex)
			{
				if (innerStartIndex != innerEndIndex)
					AddIndexedTriangle(outerStartIndex, innerEndIndex, innerStartIndex);
				continue;
			}

			AddIndexedQuad(outerStartIndex, outerEndIndex, innerEndIndex, innerStartIndex);
		}
	}

	Vector2 RingPointAt(int vertexIndex, float depth01)
	{
		if (depth01 <= MinimumBevelDepth)
			return localVertices[vertexIndex];
		float grind = blade.GrindAmounts[vertexIndex];
		float width = BevelWidth(grind);
		if (width <= 0f)
			return localVertices[vertexIndex];
		if (ShouldMiterJoin(vertexIndex))
			return ComputeMiterBevelPoint(vertexIndex, depth01, width);

		return FallbackBevelPoint(vertexIndex, depth01, width);
	}

	int AddWeldedVertex(float localDepth, Vector2 point, Color color)
	{
		long key = QuantizePointKey(point);
		if (weldedVertexLookup.TryGetValue(key, out int existing))
			return existing;
		int index = bevelVertexBuffer.Count;
		bevelVertexBuffer.Add(new Vector3(point.x, point.y, localDepth));
		bevelColorBuffer.Add(color);
		bevelUvBuffer.Add(Vector2.one * 0.5f);
		weldedVertexLookup[key] = index;
		return index;
	}

	static long QuantizePointKey(Vector2 point)
	{
		const float weldQuantizationScale = 10000f;
		const int coordinateBitCount = sizeof(int) * 8;
		int x = Mathf.RoundToInt(point.x * weldQuantizationScale);
		int y = Mathf.RoundToInt(point.y * weldQuantizationScale);
		return ((long)x << coordinateBitCount) ^ (uint)y;
	}

	void AddIndexedTriangle(int a, int b, int c)
	{
		bevelTriangleBuffer.Add(a);
		bevelTriangleBuffer.Add(b);
		bevelTriangleBuffer.Add(c);
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
			outerDepth = Mathf.Min(outerDepth, midDepth * MaximumOuterBandFraction);
		}
	}

	static Vector2 PointOnBevel(Vector2 outer, Vector2 inward, float width, float depth01)
	{
		return outer + inward * (width * Mathf.Clamp01(depth01));
	}

	Vector2 FallbackBevelPoint(int vertexIndex, float depth01, float width)
	{
		Vector2 inward = inwardNormals[vertexIndex];
		if (inward.sqrMagnitude < MinimumSquaredDirectionLength)
		{
			Vector2 centroid = Centroid(localVertices);
			inward = (centroid - localVertices[vertexIndex]).normalized;
		}

		return PointOnBevel(localVertices[vertexIndex], inward, width, depth01);
	}

	bool ShouldMiterJoin(int vertexIndex)
	{
		int vertexCount = localVertices.Count;
		if (vertexCount < 3)
			return false;
		int previousEdgeIndex = (vertexIndex - 1 + vertexCount) % vertexCount;
		return EdgeHasVisibleBevel(previousEdgeIndex) && EdgeHasVisibleBevel(vertexIndex);
	}

	bool IsOutwardMiterTip(int vertexIndex)
	{
		if (!ShouldMiterJoin(vertexIndex))
			return false;
		int vertexCount = localVertices.Count;
		int previousIndex = (vertexIndex - 1 + vertexCount) % vertexCount;
		int nextIndex = (vertexIndex + 1) % vertexCount;
		Vector2 v = localVertices[vertexIndex];
		Vector2 previousEdge = v - localVertices[previousIndex];
		Vector2 nextEdge = localVertices[nextIndex] - v;
		if (previousEdge.sqrMagnitude < GeometrySquaredTolerance || nextEdge.sqrMagnitude < GeometrySquaredTolerance)
			return false;
		Vector2 previousNormal = PerpInward(previousEdge, isCounterClockwise);
		Vector2 nextNormal = PerpInward(nextEdge, isCounterClockwise);
		if (previousNormal.sqrMagnitude < GeometrySquaredTolerance || nextNormal.sqrMagnitude < GeometrySquaredTolerance)
			return false;
		previousNormal.Normalize();
		nextNormal.Normalize();
		return Vector2.Dot(previousNormal, nextNormal) < outwardTipNormalDot;
	}

	bool EdgeHasVisibleBevel(int edgeIndex)
	{
		int j = (edgeIndex + 1) % localVertices.Count;
		float startGrindAmount = blade.GrindAmounts[edgeIndex];
		float endGrindAmount = blade.GrindAmounts[j];
		return startGrindAmount >= visibleGrindThreshold || endGrindAmount >= visibleGrindThreshold;
	}

	Vector2 ComputeMiterBevelPoint(int vertexIndex, float depth01, float width)
	{
		int vertexCount = localVertices.Count;
		int previousIndex = (vertexIndex - 1 + vertexCount) % vertexCount;
		int nextIndex = (vertexIndex + 1) % vertexCount;
		Vector2 v = localVertices[vertexIndex];
		Vector2 previousEdge = v - localVertices[previousIndex];
		Vector2 nextEdge = localVertices[nextIndex] - v;
		if (previousEdge.sqrMagnitude < GeometrySquaredTolerance || nextEdge.sqrMagnitude < GeometrySquaredTolerance)
			return FallbackBevelPoint(vertexIndex, depth01, width);
		Vector2 previousNormal = PerpInward(previousEdge, isCounterClockwise);
		Vector2 nextNormal = PerpInward(nextEdge, isCounterClockwise);
		float offset = width * Mathf.Clamp01(depth01);
		if (offset <= 0f)
			return v;
		Vector2 miterDirection = previousNormal + nextNormal;
		if (miterDirection.sqrMagnitude < GeometrySquaredTolerance)
			return FallbackBevelPoint(vertexIndex, depth01, width);
		miterDirection.Normalize();
		Vector2 toCentroid = Centroid(localVertices) - v;
		if (toCentroid.sqrMagnitude > GeometrySquaredTolerance && Vector2.Dot(miterDirection, toCentroid) <= 0f)
			return FallbackBevelPoint(vertexIndex, depth01, width);
		float normalProjection = Vector2.Dot(miterDirection, previousNormal.normalized);
		if (Mathf.Abs(normalProjection) < MinimumMiterProjection)
			return FallbackBevelPoint(vertexIndex, depth01, width);
		float miterLength = Mathf.Min(offset / normalProjection, offset * bevelMiterLimit);
		return v + miterDirection * miterLength;
	}

	void RebuildOutline()
	{
		int count = localVertices.Count;
		if (outline == null || count < 2)
		{
			if (outline != null)
				outline.positionCount = 0;
			ClearUnusedSharpenSegments(0);
			return;
		}

		Color defaultEdge = Color.Lerp(flatColor, Color.black, OutlineDarkening);
		outline.positionCount = count;
		for (int i = 0; i < count; i++)
		{
			Vector2 v = localVertices[i];
			outline.SetPosition(i, new Vector3(v.x, v.y, outlineZ));
		}

		outline.startColor = defaultEdge;
		outline.endColor = defaultEdge;

		RebuildSharpenOutlineSegments(count);
	}

	void RebuildSharpenOutlineSegments(int count)
	{
		IReadOnlyList<bool> edgeFlags = edgeEvaluator != null ? edgeEvaluator.SilhouetteEdgeNeedsSharpening : null;
		int segmentIndex = 0;
		if (edgeFlags != null && edgeFlags.Count == count)
		{
			for (int i = 0; i < count; i++)
			{
				if (!edgeFlags[i])
					continue;

				int j = (i + 1) % count;
				Vector2 a = localVertices[i];
				Vector2 b = localVertices[j];
				if ((b - a).sqrMagnitude < GeometrySquaredTolerance)
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
		float t = Mathf.Clamp01(grind / Mathf.Max(MinimumGrindWidthRange, grindForFullWidth));
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

	static void BuildInwardNormals(List<Vector2> verts, List<Vector2> destination)
	{
		destination.Clear();
		int vertexCount = verts.Count;
		bool isCounterClockwise = SignedArea(verts) > 0f;
		for (int i = 0; i < vertexCount; i++)
		{
			Vector2 previousIndex = verts[(i - 1 + vertexCount) % vertexCount];
			Vector2 currentIndex = verts[i];
			Vector2 nextIndex = verts[(i + 1) % vertexCount];
			Vector2 previousEdge = currentIndex - previousIndex;
			Vector2 nextEdge = nextIndex - currentIndex;
			Vector2 previousNormal = PerpInward(previousEdge, isCounterClockwise);
			Vector2 nextNormal = PerpInward(nextEdge, isCounterClockwise);
			Vector2 combined = previousNormal + nextNormal;
			destination.Add(combined.sqrMagnitude > MinimumSquaredDirectionLength ? combined.normalized : Vector2.zero);
		}
	}

	static Vector2 PerpInward(Vector2 edge, bool isCounterClockwise)
	{
		if (edge.sqrMagnitude < GeometrySquaredTolerance)
			return Vector2.zero;

		// CCW polygon: inward is rotate edge 90° CCW (-y, x). CW: opposite.
		Vector2 vertexCount = isCounterClockwise ? new Vector2(-edge.y, edge.x) : new Vector2(edge.y, -edge.x);
		return vertexCount.normalized;
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
