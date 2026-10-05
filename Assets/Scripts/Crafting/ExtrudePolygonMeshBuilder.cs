using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ExtrudePolygonMeshBuilder
{
	const float GeometryTolerance = 0.000001f;
	const float SquaredGeometryTolerance = GeometryTolerance * GeometryTolerance;
	const int MinimumSurfaceVertices = 2048;
	const int MaximumSurfaceVertices = 8192;
	const int VerticesPerBevelSegment = 1024;
	const int MaximumRefinementPasses = 8;
	const int MinimumCurveResolution = 8;
	const int MinimumGridResolution = 4;
	const int MinimumGrindSamples = 64;
	const int MaximumGrindSamples = 512;
	const int GridCellsAcrossBounds = 64;
	const int NormalSamplesAcrossBevel = 64;
	const float CoreClearanceFraction = 0.4f;
	const float CoreRadiusFraction = 0.25f;
	const float SignificantGrindFraction = 0.5f;
	const float MaximumProfileSlope = 1.5f;
	const float MinimumNormalStepFraction = 0.00001f;

	readonly struct GroundEdge
	{
		public Vector2 Start { get; }
		public Vector2 Vector { get; }
		public float Length { get; }
		public float StartGrind { get; }
		public float EndGrind { get; }
		public float ArcPosition { get; }

		public GroundEdge(Vector2 start, Vector2 vector, float length, float startGrind, float endGrind, float arcPosition)
		{
			Start = start;
			Vector = vector;
			Length = length;
			StartGrind = startGrind;
			EndGrind = endGrind;
			ArcPosition = arcPosition;
		}
	}

	readonly List<Vector3> vertices = new List<Vector3>();
	readonly List<Vector3> normals = new List<Vector3>();
	readonly List<Vector2> uvs = new List<Vector2>();
	readonly List<Color> colors = new List<Color>();
	readonly List<int> triangles = new List<int>();

	readonly List<Vector2> outline = new List<Vector2>();
	readonly List<int> capTriangles = new List<int>();
	readonly List<int> remainingIndices = new List<int>();
	readonly List<float> grindProgress = new List<float>();
	readonly List<float> edgeLengths = new List<float>();
	readonly List<float> boundaryDistances = new List<float>();
	readonly List<float> grindSamples = new List<float>();
	readonly List<float> smoothedGrindSamples = new List<float>();

	readonly List<int> fieldEdgeStarts = new List<int>();
	readonly List<Vector2> fieldEdgeVectors = new List<Vector2>();
	readonly List<GroundEdge> groundEdges = new List<GroundEdge>();
	readonly List<Vector2> significantGroundStarts = new List<Vector2>();
	readonly List<Vector2> significantGroundEnds = new List<Vector2>();
	readonly List<float> surfaceClearances = new List<float>();

	readonly List<Vector2> surfacePoints = new List<Vector2>();
	readonly List<float> surfaceHeights = new List<float>();
	readonly List<int> boundaryNext = new List<int>();
	readonly List<int> refinementTriangles = new List<int>();
	readonly List<int> wallIndices = new List<int>();
	readonly List<bool> usedBoundaryVertices = new List<bool>();
	readonly Dictionary<long, int> midpointLookup = new Dictionary<long, int>();

	float perimeter;
	float effectiveBevelWidth;
	float requestedBevelWidth;
	float bodyHalfThickness;
	float minimumHalfThickness;
	float maximumBevelSlope;
	float normalSampleWidth;
	Vector2 coreCenter;
	float coreRadius;
	int surfaceVertexLimit;

	public bool TryBuild(Mesh mesh, IReadOnlyList<Vector2> polygon, float thickness, Color color, float scale = 1f)
	{
		bool hasRequiredInputs = mesh != null && polygon != null;
		bool hasValidDimensions = IsPositiveFinite(thickness) && IsPositiveFinite(scale);
		if (!hasRequiredInputs || !hasValidDimensions)
			return false;

		outline.Clear();
		for (int i = 0; i < polygon.Count; i++)
		{
			Vector2 scaledPoint = polygon[i] * scale;
			outline.Add(scaledPoint);
		}

		bool triangulated = PolygonGeometry.Triangulate(outline, capTriangles, remainingIndices);
		if (!triangulated)
			return false;

		ClearMeshBuffers();
		BuildFlatCaps(thickness, color);
		BuildFlatWalls(thickness, color);
		ApplyMesh(mesh);
		return true;
	}

	public bool TryBuildGround(Mesh mesh, IReadOnlyList<Vector2> polygon, IReadOnlyList<float> grindAmounts, float thickness, Color color, float scale = 1f, float bevelWidth = 0.12f, float minEdgeThicknessRatio = 0.05f, int bevelSegments = 4, float grindForSharpEdge = 1f, float maximumBevelAngle = 30f)
	{
		if (mesh == null)
			return false;

		bool prepared = TryPrepareGroundGeometry(polygon, grindAmounts, thickness, color, scale, bevelWidth, minEdgeThicknessRatio, bevelSegments, grindForSharpEdge, maximumBevelAngle);
		if (!prepared)
			return false;

		ApplyMesh(mesh);
		return true;
	}

	void BuildFlatCaps(float thickness, Color color)
	{
		int outlineCount = outline.Count;
		float halfThickness = thickness * 0.5f;
		for (int i = 0; i < outlineCount; i++)
		{
			Vector2 point = outline[i];
			Vector3 position = new Vector3(point.x, halfThickness, point.y);
			AddVertex(position, Vector3.up, point, color);
		}

		for (int i = 0; i < outlineCount; i++)
		{
			Vector2 point = outline[i];
			Vector3 position = new Vector3(point.x, -halfThickness, point.y);
			AddVertex(position, Vector3.down, point, color);
		}

		for (int i = 0; i < capTriangles.Count; i += 3)
		{
			int firstVertex = capTriangles[i];
			int secondVertex = capTriangles[i + 1];
			int thirdVertex = capTriangles[i + 2];
			AddMirroredTriangle(firstVertex, secondVertex, thirdVertex, outlineCount);
		}
	}

	void BuildFlatWalls(float thickness, Color color)
	{
		int outlineCount = outline.Count;
		float halfThickness = thickness * 0.5f;
		bool counterClockwise = PolygonGeometry.SignedArea(outline) > 0f;
		for (int i = 0; i < outlineCount; i++)
		{
			int nextIndex = (i + 1) % outlineCount;
			Vector2 startPoint = outline[i];
			Vector2 endPoint = outline[nextIndex];
			if (!counterClockwise)
				(startPoint, endPoint) = (endPoint, startPoint);

			Vector2 edge = endPoint - startPoint;
			float edgeLength = edge.magnitude;
			float normalX = edge.y / edgeLength;
			float normalZ = -edge.x / edgeLength;
			Vector3 normal = new Vector3(normalX, 0f, normalZ);
			int firstVertex = vertices.Count;

			Vector3 bottomStart = new Vector3(startPoint.x, -halfThickness, startPoint.y);
			Vector3 topStart = new Vector3(startPoint.x, halfThickness, startPoint.y);
			Vector3 topEnd = new Vector3(endPoint.x, halfThickness, endPoint.y);
			Vector3 bottomEnd = new Vector3(endPoint.x, -halfThickness, endPoint.y);
			Vector2 bottomStartUv = new Vector2(0f, 0f);
			Vector2 topStartUv = new Vector2(0f, thickness);
			Vector2 topEndUv = new Vector2(edgeLength, thickness);
			Vector2 bottomEndUv = new Vector2(edgeLength, 0f);

			AddVertex(bottomStart, normal, bottomStartUv, color);
			AddVertex(topStart, normal, topStartUv, color);
			AddVertex(topEnd, normal, topEndUv, color);
			AddVertex(bottomEnd, normal, bottomEndUv, color);
			AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
			AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
		}
	}

	bool TryPrepareGroundGeometry(IReadOnlyList<Vector2> polygon, IReadOnlyList<float> grindAmounts, float thickness, Color color, float scale, float bevelWidth, float minEdgeThicknessRatio, int bevelSegments, float grindForSharpEdge, float maximumBevelAngle)
	{
		bool hasOutlineData = polygon != null && grindAmounts != null;
		if (!hasOutlineData)
			return false;

		bool hasMatchingAmounts = polygon.Count == grindAmounts.Count;
		bool hasValidDimensions = IsPositiveFinite(thickness) && IsPositiveFinite(scale);
		bool hasValidBevelWidth = IsPositiveFinite(bevelWidth);
		bool hasValidThicknessRatio = IsPositiveFinite(minEdgeThicknessRatio) && minEdgeThicknessRatio <= 1f;
		bool hasValidGrindThreshold = IsPositiveFinite(grindForSharpEdge);
		bool hasValidSegmentCount = bevelSegments >= 1 && bevelSegments <= 32;
		bool hasValidBevelSettings = hasValidBevelWidth && hasValidThicknessRatio && hasValidSegmentCount;
		bool hasValidBevelAngle = IsPositiveFinite(maximumBevelAngle) && maximumBevelAngle < 90f;
		if (!hasMatchingAmounts || !hasValidDimensions || !hasValidBevelSettings || !hasValidGrindThreshold || !hasValidBevelAngle)
			return false;

		int requestedVertexLimit = bevelSegments * VerticesPerBevelSegment;
		surfaceVertexLimit = Mathf.Clamp(requestedVertexLimit, MinimumSurfaceVertices, MaximumSurfaceVertices);

		bool loaded = TryLoadGroundOutline(polygon, grindAmounts, scale, grindForSharpEdge);
		if (!loaded)
			return false;

		bool triangulated = PolygonGeometry.Triangulate(outline, capTriangles, remainingIndices);
		if (!triangulated)
			return false;

		BuildOutlineMetrics();
		BuildBoundaryFieldEdges();
		BuildGroundEdges();
		InitializeSurfacePoints();
		if (!RestoreBoundaryVertices())
			return false;

		SeedSurfaceInterior();

		int curveResolution = Mathf.Max(MinimumCurveResolution, bevelSegments * 2);
		Vector3 boundsSize = PolygonGeometry.ComputeBounds(outline).size;
		float boundsSpan = Mathf.Max(boundsSize.x, boundsSize.y);
		float gridSpacing = CalculateGridSpacing(bevelWidth, bevelSegments, boundsSpan);
		int gridVertexLimit = Mathf.Max(surfacePoints.Count, surfaceVertexLimit / 2);
		RefineSurface(gridSpacing, 0f, false, gridVertexLimit);
		if (!TrySelectCore())
			return false;

		bodyHalfThickness = thickness * 0.5f;
		minimumHalfThickness = bodyHalfThickness * minEdgeThicknessRatio;
		float angleRadians = maximumBevelAngle * Mathf.Deg2Rad;
		maximumBevelSlope = Mathf.Tan(angleRadians);
		if (!IsPositiveFinite(maximumBevelSlope))
			return false;
		float heightDrop = bodyHalfThickness - minimumHalfThickness;
		float angleWidth = MaximumProfileSlope * heightDrop / maximumBevelSlope;
		requestedBevelWidth = bevelWidth;
		effectiveBevelWidth = Mathf.Max(bevelWidth, angleWidth);
		normalSampleWidth = MeasureMaximumTaperWidth();
		float blendRadius = Mathf.Min(normalSampleWidth * 0.25f, coreRadius * 3f);
		BuildSmoothGrindSamples(blendRadius);

		for (int i = 0; i < surfacePoints.Count; i++)
			surfaceHeights[i] = EvaluateHalfThickness(surfacePoints[i]);

		int squaredResolution = curveResolution * curveResolution;
		float toleranceDivisor = squaredResolution * 2f;
		float heightTolerance = bodyHalfThickness / toleranceDivisor;
		RefineSurface(0f, heightTolerance, true, surfaceVertexLimit);

		ClearMeshBuffers();
		BuildGroundCaps(color, boundsSpan);
		BuildGroundWalls(color);
		return true;
	}

	bool TryLoadGroundOutline(IReadOnlyList<Vector2> polygon, IReadOnlyList<float> grindAmounts, float scale, float grindForSharpEdge)
	{
		outline.Clear();
		grindProgress.Clear();
		edgeLengths.Clear();
		boundaryDistances.Clear();
		for (int i = 0; i < polygon.Count; i++)
		{
			float amount = grindAmounts[i];
			bool isFinite = !float.IsNaN(amount) && !float.IsInfinity(amount);
			if (!isFinite || amount < 0f)
				return false;

			Vector2 scaledPoint = polygon[i] * scale;
			float normalizedGrind = Mathf.Clamp01(amount / grindForSharpEdge);
			outline.Add(scaledPoint);
			grindProgress.Add(normalizedGrind);
		}
		return true;
	}

	void BuildOutlineMetrics()
	{
		perimeter = 0f;
		for (int i = 0; i < outline.Count; i++)
		{
			int nextIndex = (i + 1) % outline.Count;
			float edgeLength = Vector2.Distance(outline[i], outline[nextIndex]);
			boundaryDistances.Add(perimeter);
			edgeLengths.Add(edgeLength);
			perimeter += edgeLength;
		}
	}

	void InitializeSurfacePoints()
	{
		surfacePoints.Clear();
		surfaceHeights.Clear();
		boundaryNext.Clear();
		for (int i = 0; i < outline.Count; i++)
		{
			int nextIndex = (i + 1) % outline.Count;
			surfacePoints.Add(outline[i]);
			surfaceHeights.Add(0f);
			boundaryNext.Add(nextIndex);
		}
	}

	float CalculateGridSpacing(float bevelWidth, int bevelSegments, float boundsSpan)
	{
		float polygonArea = Mathf.Abs(PolygonGeometry.SignedArea(outline));
		float radiusEstimate = polygonArea / perimeter;
		float availableBevelWidth = Mathf.Min(bevelWidth, radiusEstimate);
		int gridResolution = Mathf.Max(MinimumGridResolution, bevelSegments);
		float bevelSpacing = availableBevelWidth / gridResolution;
		float boundsSpacing = boundsSpan / GridCellsAcrossBounds;
		return Mathf.Max(bevelSpacing, boundsSpacing);
	}

	float MeasureBoundaryDistance(Vector2 point)
	{
		float nearestSquaredDistance = float.PositiveInfinity;
		for (int i = 0; i < fieldEdgeStarts.Count; i++)
		{
			Vector2 start = outline[fieldEdgeStarts[i]];
			Vector2 end = start + fieldEdgeVectors[i];
			Vector2 closest = PolygonGeometry.ClosestOnSegment(point, start, end);
			float squaredDistance = (point - closest).sqrMagnitude;
			nearestSquaredDistance = Mathf.Min(nearestSquaredDistance, squaredDistance);
		}
		return Mathf.Sqrt(nearestSquaredDistance);
	}

	void BuildGroundCaps(Color color, float boundsSpan)
	{
		int surfaceCount = surfacePoints.Count;
		float bevelNormalStep = normalSampleWidth / NormalSamplesAcrossBevel;
		float minimumNormalStep = boundsSpan * MinimumNormalStepFraction;
		float normalStep = Mathf.Max(bevelNormalStep, minimumNormalStep);

		for (int i = 0; i < surfaceCount; i++)
		{
			Vector2 point = surfacePoints[i];
			Vector3 position = new Vector3(point.x, surfaceHeights[i], point.y);
			Vector3 normal = CalculateSurfaceNormal(point, normalStep);
			AddVertex(position, normal, point, color);
		}

		for (int i = 0; i < surfaceCount; i++)
		{
			Vector2 point = surfacePoints[i];
			Vector3 position = new Vector3(point.x, -surfaceHeights[i], point.y);
			Vector3 normal = normals[i];
			normal.y = -normal.y;
			AddVertex(position, normal, point, color);
		}

		for (int i = 0; i < capTriangles.Count; i += 3)
		{
			int firstVertex = capTriangles[i];
			int secondVertex = capTriangles[i + 1];
			int thirdVertex = capTriangles[i + 2];
			AddMirroredTriangle(firstVertex, secondVertex, thirdVertex, surfaceCount);
		}
	}

	Vector3 CalculateSurfaceNormal(Vector2 point, float normalStep)
	{
		Vector2 horizontalOffset = Vector2.right * normalStep;
		Vector2 verticalOffset = Vector2.up * normalStep;
		float rightHeight = EvaluateHalfThickness(point + horizontalOffset);
		float leftHeight = EvaluateHalfThickness(point - horizontalOffset);
		float upperHeight = EvaluateHalfThickness(point + verticalOffset);
		float lowerHeight = EvaluateHalfThickness(point - verticalOffset);
		float sampleDistance = normalStep * 2f;
		float horizontalGradient = (rightHeight - leftHeight) / sampleDistance;
		float verticalGradient = (upperHeight - lowerHeight) / sampleDistance;
		Vector3 normal = new Vector3(-horizontalGradient, 1f, -verticalGradient);
		return normal.normalized;
	}

	bool RestoreBoundaryVertices()
	{
		usedBoundaryVertices.Clear();
		for (int i = 0; i < outline.Count; i++)
			usedBoundaryVertices.Add(false);

		for (int i = 0; i < capTriangles.Count; i++)
			usedBoundaryVertices[capTriangles[i]] = true;

		for (int index = 0; index < outline.Count; index++)
		{
			if (usedBoundaryVertices[index])
				continue;

			int previous = (index - 1 + outline.Count) % outline.Count;
			int next = (index + 1) % outline.Count;
			while (!usedBoundaryVertices[previous])
				previous = (previous - 1 + outline.Count) % outline.Count;

			while (!usedBoundaryVertices[next])
				next = (next + 1) % outline.Count;

			bool restored = false;
			for (int triangle = 0; triangle < capTriangles.Count && !restored; triangle += 3)
			{
				for (int edge = 0; edge < 3; edge++)
				{
					int firstSlot = triangle + edge;
					int secondSlot = triangle + (edge + 1) % 3;
					int thirdSlot = triangle + (edge + 2) % 3;
					int firstVertex = capTriangles[firstSlot];
					int secondVertex = capTriangles[secondSlot];
					int thirdVertex = capTriangles[thirdSlot];
					bool matchesForwardEdge = firstVertex == previous && secondVertex == next;
					bool matchesReverseEdge = firstVertex == next && secondVertex == previous;
					if (!matchesForwardEdge && !matchesReverseEdge)
						continue;

					capTriangles[triangle] = firstVertex;
					capTriangles[triangle + 1] = index;
					capTriangles[triangle + 2] = thirdVertex;
					AddSurfaceTriangle(capTriangles, index, secondVertex, thirdVertex);
					usedBoundaryVertices[index] = true;
					restored = true;
					break;
				}
			}
			if (!restored)
				return false;
		}
		return true;
	}

	void SeedSurfaceInterior()
	{
		int originalIndexCount = capTriangles.Count;
		for (int i = 0; i < originalIndexCount; i += 3)
		{
			int firstVertex = capTriangles[i];
			int secondVertex = capTriangles[i + 1];
			int thirdVertex = capTriangles[i + 2];
			Vector2 positionSum = surfacePoints[firstVertex] + surfacePoints[secondVertex] + surfacePoints[thirdVertex];
			Vector2 centroid = positionSum / 3f;
			int centerVertex = AddSurfacePoint(centroid, 0f);

			capTriangles[i + 2] = centerVertex;
			AddSurfaceTriangle(capTriangles, secondVertex, thirdVertex, centerVertex);
			AddSurfaceTriangle(capTriangles, thirdVertex, firstVertex, centerVertex);
		}
	}

	void RefineSurface(float spacing, float heightTolerance, bool refineCurve, int vertexLimit)
	{
		for (int pass = 0; pass < MaximumRefinementPasses; pass++)
		{
			midpointLookup.Clear();
			refinementTriangles.Clear();
			int originalPointCount = surfacePoints.Count;

			for (int i = 0; i < capTriangles.Count; i += 3)
			{
				int firstVertex = capTriangles[i];
				int secondVertex = capTriangles[i + 1];
				int thirdVertex = capTriangles[i + 2];
				int firstMidpoint = RefineEdge(firstVertex, secondVertex, spacing, heightTolerance, refineCurve, vertexLimit);
				int secondMidpoint = RefineEdge(secondVertex, thirdVertex, spacing, heightTolerance, refineCurve, vertexLimit);
				int thirdMidpoint = RefineEdge(thirdVertex, firstVertex, spacing, heightTolerance, refineCurve, vertexLimit);
				int firstSplit = firstMidpoint >= 0 ? 1 : 0;
				int secondSplit = secondMidpoint >= 0 ? 1 : 0;
				int thirdSplit = thirdMidpoint >= 0 ? 1 : 0;
				int splitCount = firstSplit + secondSplit + thirdSplit;

				if (splitCount == 0)
				{
					AddSurfaceTriangle(refinementTriangles, firstVertex, secondVertex, thirdVertex);
				}
				else if (splitCount == 3)
				{
					AddSurfaceTriangle(refinementTriangles, firstVertex, firstMidpoint, thirdMidpoint);
					AddSurfaceTriangle(refinementTriangles, firstMidpoint, secondVertex, secondMidpoint);
					AddSurfaceTriangle(refinementTriangles, thirdMidpoint, secondMidpoint, thirdVertex);
					AddSurfaceTriangle(refinementTriangles, firstMidpoint, secondMidpoint, thirdMidpoint);
				}
				else if (splitCount == 1)
				{
					if (firstMidpoint >= 0)
						SplitOneEdge(firstVertex, secondVertex, thirdVertex, firstMidpoint);
					else if (secondMidpoint >= 0)
						SplitOneEdge(secondVertex, thirdVertex, firstVertex, secondMidpoint);
					else
						SplitOneEdge(thirdVertex, firstVertex, secondVertex, thirdMidpoint);
				}
				else
				{
					if (thirdMidpoint < 0)
						SplitTwoEdges(firstVertex, secondVertex, thirdVertex, firstMidpoint, secondMidpoint);
					else if (firstMidpoint < 0)
						SplitTwoEdges(secondVertex, thirdVertex, firstVertex, secondMidpoint, thirdMidpoint);
					else
						SplitTwoEdges(thirdVertex, firstVertex, secondVertex, thirdMidpoint, firstMidpoint);
				}
			}

			capTriangles.Clear();
			capTriangles.AddRange(refinementTriangles);
			bool noNewPoints = surfacePoints.Count == originalPointCount;
			bool reachedVertexLimit = surfacePoints.Count >= vertexLimit;
			if (noNewPoints || reachedVertexLimit)
				break;
		}
	}

	int RefineEdge(int firstVertex, int secondVertex, float spacing, float tolerance, bool refineCurve, int vertexLimit)
	{
		int lowerIndex = Mathf.Min(firstVertex, secondVertex);
		int upperIndex = Mathf.Max(firstVertex, secondVertex);
		long edgeKey = ((long)lowerIndex << 32) | (uint)upperIndex;
		if (midpointLookup.TryGetValue(edgeKey, out int midpoint))
			return midpoint;

		Vector2 startPoint = surfacePoints[firstVertex];
		Vector2 endPoint = surfacePoints[secondVertex];
		Vector2 midpointPosition = (startPoint + endPoint) * 0.5f;
		float midpointHeight = 0f;
		bool needsSplit;

		if (!refineCurve)
		{
			float squaredEdgeLength = (startPoint - endPoint).sqrMagnitude;
			float squaredSpacing = spacing * spacing;
			needsSplit = squaredEdgeLength > squaredSpacing;
		}
		else
		{
			midpointHeight = EvaluateHalfThickness(midpointPosition);
			float startHeight = surfaceHeights[firstVertex];
			float endHeight = surfaceHeights[secondVertex];
			float linearMidpointHeight = (startHeight + endHeight) * 0.5f;
			float midpointError = Mathf.Abs(midpointHeight - linearMidpointHeight);
			needsSplit = midpointError > tolerance;

			bool endpointsAreFlat = startHeight == bodyHalfThickness && endHeight == bodyHalfThickness;
			bool edgeIsFlat = endpointsAreFlat && midpointHeight == bodyHalfThickness;
			if (!needsSplit && !edgeIsFlat)
			{
				Vector2 quarterPosition = Vector2.Lerp(startPoint, endPoint, 0.25f);
				float quarterHeight = EvaluateHalfThickness(quarterPosition);
				float linearQuarterHeight = Mathf.Lerp(startHeight, endHeight, 0.25f);
				float quarterError = Mathf.Abs(quarterHeight - linearQuarterHeight);
				needsSplit = quarterError > tolerance;

				if (!needsSplit)
				{
					Vector2 threeQuarterPosition = Vector2.Lerp(startPoint, endPoint, 0.75f);
					float threeQuarterHeight = EvaluateHalfThickness(threeQuarterPosition);
					float linearThreeQuarterHeight = Mathf.Lerp(startHeight, endHeight, 0.75f);
					float threeQuarterError = Mathf.Abs(threeQuarterHeight - linearThreeQuarterHeight);
					needsSplit = threeQuarterError > tolerance;
				}
			}
		}

		midpoint = -1;
		bool hasVertexCapacity = surfacePoints.Count < vertexLimit;
		if (needsSplit && hasVertexCapacity)
		{
			midpoint = AddSurfacePoint(midpointPosition, midpointHeight);
			if (boundaryNext[firstVertex] == secondVertex)
			{
				boundaryNext[firstVertex] = midpoint;
				boundaryNext[midpoint] = secondVertex;
			}
			else if (boundaryNext[secondVertex] == firstVertex)
			{
				boundaryNext[secondVertex] = midpoint;
				boundaryNext[midpoint] = firstVertex;
			}
		}

		midpointLookup[edgeKey] = midpoint;
		return midpoint;
	}

	int AddSurfacePoint(Vector2 point, float height)
	{
		int index = surfacePoints.Count;
		surfacePoints.Add(point);
		surfaceHeights.Add(height);
		boundaryNext.Add(-1);
		return index;
	}

	void SplitOneEdge(int startVertex, int endVertex, int oppositeVertex, int midpoint)
	{
		AddSurfaceTriangle(refinementTriangles, startVertex, midpoint, oppositeVertex);
		AddSurfaceTriangle(refinementTriangles, midpoint, endVertex, oppositeVertex);
	}

	void SplitTwoEdges(int firstVertex, int sharedVertex, int lastVertex, int firstMidpoint, int secondMidpoint)
	{
		AddSurfaceTriangle(refinementTriangles, sharedVertex, secondMidpoint, firstMidpoint);
		AddSurfaceTriangle(refinementTriangles, firstVertex, firstMidpoint, lastVertex);
		AddSurfaceTriangle(refinementTriangles, firstMidpoint, secondMidpoint, lastVertex);
	}

	static void AddSurfaceTriangle(List<int> target, int firstVertex, int secondVertex, int thirdVertex)
	{
		target.Add(firstVertex);
		target.Add(secondVertex);
		target.Add(thirdVertex);
	}

	void BuildBoundaryFieldEdges()
	{
		fieldEdgeStarts.Clear();
		fieldEdgeVectors.Clear();

		for (int i = 0; i < outline.Count; i++)
		{
			int previousIndex = (i + outline.Count - 1) % outline.Count;
			int nextIndex = (i + 1) % outline.Count;
			Vector2 point = outline[i];
			Vector2 previous = outline[previousIndex];
			Vector2 next = outline[nextIndex];
			Vector2 closest = PolygonGeometry.ClosestOnSegment(point, previous, next);
			float squaredDeviation = (closest - point).sqrMagnitude;
			float directionAlignment = Vector2.Dot(point - previous, next - point);
			bool changesDirection = directionAlignment <= 0f;
			bool leavesStraightSegment = squaredDeviation > SquaredGeometryTolerance;
			if (leavesStraightSegment || changesDirection)
				fieldEdgeStarts.Add(i);
		}

		if (fieldEdgeStarts.Count < PolygonGeometry.MinimumVertexCount)
		{
			fieldEdgeStarts.Clear();
			for (int i = 0; i < outline.Count; i++)
				fieldEdgeStarts.Add(i);
		}

		for (int i = 0; i < fieldEdgeStarts.Count; i++)
		{
			int nextIndex = (i + 1) % fieldEdgeStarts.Count;
			int startVertex = fieldEdgeStarts[i];
			int endVertex = fieldEdgeStarts[nextIndex];
			Vector2 edge = outline[endVertex] - outline[startVertex];
			fieldEdgeVectors.Add(edge);
		}
	}

	void BuildSmoothGrindSamples(float radius)
	{
		float minimumRadius = GeometryTolerance * 4f;
		radius = Mathf.Max(radius, minimumRadius);
		float requestedSpacing = radius * 0.25f;
		int requestedSamples = Mathf.CeilToInt(perimeter / requestedSpacing);
		int sampleCount = Mathf.Clamp(requestedSamples, MinimumGrindSamples, MaximumGrindSamples);
		float sampleSpacing = perimeter / sampleCount;

		grindSamples.Clear();
		smoothedGrindSamples.Clear();
		int edgeIndex = 0;
		for (int i = 0; i < sampleCount; i++)
		{
			float arcPosition = i * sampleSpacing;
			while (edgeIndex + 1 < outline.Count)
			{
				float edgeEndDistance = boundaryDistances[edgeIndex] + edgeLengths[edgeIndex];
				bool sampleIsAfterEdge = arcPosition > edgeEndDistance;
				if (!sampleIsAfterEdge)
					break;
				edgeIndex++;
			}

			int nextVertex = (edgeIndex + 1) % outline.Count;
			float distanceAlongEdge = arcPosition - boundaryDistances[edgeIndex];
			float edgeFraction = distanceAlongEdge / edgeLengths[edgeIndex];
			float startGrind = grindProgress[edgeIndex];
			float endGrind = grindProgress[nextVertex];
			float interpolatedGrind = Mathf.Lerp(startGrind, endGrind, edgeFraction);
			grindSamples.Add(interpolatedGrind);
		}

		int requestedReach = Mathf.CeilToInt(radius / sampleSpacing);
		int maximumReach = sampleCount / 2;
		int sampleReach = Mathf.Min(requestedReach, maximumReach);
		for (int i = 0; i < sampleCount; i++)
		{
			float weightedGrindSum = 0f;
			float weightSum = 0f;
			for (int offset = -sampleReach; offset <= sampleReach; offset++)
			{
				float sampleDistance = Mathf.Abs(offset) * sampleSpacing;
				float radiusFraction = sampleDistance / radius;
				if (radiusFraction >= 1f)
					continue;

				float weight = 1f - radiusFraction * radiusFraction;
				weight *= weight;
				int wrappedIndex = (i + offset + sampleCount) % sampleCount;
				float weightedGrind = grindSamples[wrappedIndex] * weight;
				weightedGrindSum += weightedGrind;
				weightSum += weight;
			}
			float smoothedGrind = weightedGrindSum / weightSum;
			smoothedGrindSamples.Add(smoothedGrind);
		}
	}

	float SampleSmoothGrind(float arcPosition)
	{
		int sampleCount = smoothedGrindSamples.Count;
		float samplePosition = arcPosition / perimeter * sampleCount;
		int sampleIndex = Mathf.FloorToInt(samplePosition);
		float fraction = samplePosition - sampleIndex;
		sampleIndex %= sampleCount;

		int previousIndex = (sampleIndex + sampleCount - 1) % sampleCount;
		int nextIndex = (sampleIndex + 1) % sampleCount;
		int followingIndex = (sampleIndex + 2) % sampleCount;
		float previous = smoothedGrindSamples[previousIndex];
		float current = smoothedGrindSamples[sampleIndex];
		float next = smoothedGrindSamples[nextIndex];
		float following = smoothedGrindSamples[followingIndex];

		float constantTerm = 2f * current;
		float linearCoefficient = next - previous;
		float quadraticCoefficient = 2f * previous - 5f * current + 4f * next - following;
		float cubicCoefficient = 3f * current - previous - 3f * next + following;
		float linearTerm = linearCoefficient * fraction;
		float quadraticTerm = quadraticCoefficient * fraction * fraction;
		float cubicTerm = cubicCoefficient * fraction * fraction * fraction;
		float interpolatedGrind = 0.5f * (constantTerm + linearTerm + quadraticTerm + cubicTerm);
		return Mathf.Clamp01(interpolatedGrind);
	}

	void BuildGroundEdges()
	{
		groundEdges.Clear();
		significantGroundStarts.Clear();
		significantGroundEnds.Clear();

		float maximumGrind = 0f;
		for (int i = 0; i < grindProgress.Count; i++)
			maximumGrind = Mathf.Max(maximumGrind, grindProgress[i]);

		float significantThreshold = maximumGrind * SignificantGrindFraction;
		for (int i = 0; i < outline.Count; i++)
		{
			int nextIndex = (i + 1) % outline.Count;
			float startGrind = grindProgress[i];
			float endGrind = grindProgress[nextIndex];
			float strongestGrind = Mathf.Max(startGrind, endGrind);
			if (strongestGrind <= GeometryTolerance)
				continue;

			Vector2 startPoint = outline[i];
			Vector2 edgeVector = outline[nextIndex] - startPoint;
			GroundEdge edge = new GroundEdge(startPoint, edgeVector, edgeLengths[i], startGrind, endGrind, boundaryDistances[i]);
			groundEdges.Add(edge);
			if (strongestGrind < significantThreshold)
				continue;

			float startFraction = 0f;
			float endFraction = 1f;
			float grindChange = endGrind - startGrind;
			if (startGrind < significantThreshold)
				startFraction = (significantThreshold - startGrind) / grindChange;
			if (endGrind < significantThreshold)
				endFraction = (significantThreshold - startGrind) / grindChange;

			Vector2 significantStart = startPoint + edgeVector * startFraction;
			Vector2 significantEnd = startPoint + edgeVector * endFraction;
			significantGroundStarts.Add(significantStart);
			significantGroundEnds.Add(significantEnd);
		}
	}

	bool TrySelectCore()
	{
		surfaceClearances.Clear();
		float maximumClearance = 0f;
		for (int i = 0; i < surfacePoints.Count; i++)
		{
			float clearance = MeasureBoundaryDistance(surfacePoints[i]);
			surfaceClearances.Add(clearance);
			maximumClearance = Mathf.Max(maximumClearance, clearance);
		}
		if (!IsPositiveFinite(maximumClearance))
			return false;

		float minimumCoreClearance = maximumClearance * CoreClearanceFraction;
		float bestScore = float.NegativeInfinity;
		int coreVertex = 0;
		for (int i = 0; i < surfacePoints.Count; i++)
		{
			float clearance = surfaceClearances[i];
			if (clearance < minimumCoreClearance)
				continue;

			float groundDistance = MeasureSignificantGroundDistance(surfacePoints[i]);
			float clearancePreference = clearance * 0.05f;
			float score = groundDistance + clearancePreference;
			if (score <= bestScore)
				continue;

			bestScore = score;
			coreVertex = i;
		}

		coreCenter = surfacePoints[coreVertex];
		coreRadius = surfaceClearances[coreVertex] * CoreRadiusFraction;
		return true;
	}

	float MeasureSignificantGroundDistance(Vector2 point)
	{
		if (significantGroundStarts.Count == 0)
			return MeasureBoundaryDistance(point);

		float nearestSquaredDistance = float.PositiveInfinity;
		for (int i = 0; i < significantGroundStarts.Count; i++)
		{
			Vector2 closest = PolygonGeometry.ClosestOnSegment(point, significantGroundStarts[i], significantGroundEnds[i]);
			float squaredDistance = (point - closest).sqrMagnitude;
			nearestSquaredDistance = Mathf.Min(nearestSquaredDistance, squaredDistance);
		}
		return Mathf.Sqrt(nearestSquaredDistance);
	}

	float MeasureMaximumTaperWidth()
	{
		float maximumWidth = 0f;
		for (int i = 0; i < groundEdges.Count; i++)
		{
			GroundEdge edge = groundEdges[i];
			Vector2 endPoint = edge.Start + edge.Vector;
			float startRun = Vector2.Distance(edge.Start, coreCenter) - coreRadius;
			float endRun = Vector2.Distance(endPoint, coreCenter) - coreRadius;
			float availableRun = Mathf.Max(startRun, endRun);
			float taperWidth = Mathf.Min(effectiveBevelWidth, availableRun);
			maximumWidth = Mathf.Max(maximumWidth, taperWidth);
		}
		float minimumWidth = GeometryTolerance * 4f;
		return Mathf.Max(maximumWidth, minimumWidth);
	}

	float EvaluateGroundInfluence(Vector2 point, GroundEdge edge)
	{
		Vector2 offset = point - edge.Start;
		float squaredLength = edge.Length * edge.Length;
		float projectedFraction = Vector2.Dot(offset, edge.Vector) / squaredLength;
		float edgeFraction = Mathf.Clamp01(projectedFraction);
		Vector2 sourcePoint = edge.Start + edge.Vector * edgeFraction;
		float sourceDistance = Vector2.Distance(point, sourcePoint);
		float arcPosition = edge.ArcPosition + edgeFraction * edge.Length;
		float rawGrind = Mathf.Lerp(edge.StartGrind, edge.EndGrind, edgeFraction);
		float blendedGrind = SampleSmoothGrind(arcPosition);
		float sourceGrind = Mathf.Max(rawGrind, blendedGrind);

		float heightDrop = (bodyHalfThickness - minimumHalfThickness) * sourceGrind;
		float requiredWidth = MaximumProfileSlope * heightDrop / maximumBevelSlope;
		float desiredWidth = Mathf.Max(requestedBevelWidth, requiredWidth);
		float availableRun = Vector2.Distance(sourcePoint, coreCenter) - coreRadius;
		float taperWidth = Mathf.Min(desiredWidth, availableRun);
		if (taperWidth <= GeometryTolerance || sourceDistance >= taperWidth)
			return 0f;

		float taperFraction = sourceDistance / taperWidth;
		float thicknessRecovery = Mathf.SmoothStep(0f, 1f, taperFraction);
		return sourceGrind * (1f - thicknessRecovery);
	}

	float EvaluateHalfThickness(Vector2 point)
	{
		bool isInCore = (point - coreCenter).sqrMagnitude <= coreRadius * coreRadius;
		if (isInCore || groundEdges.Count == 0)
			return bodyHalfThickness;

		float strongestInfluence = 0f;
		for (int i = 0; i < groundEdges.Count; i++)
		{
			float influence = EvaluateGroundInfluence(point, groundEdges[i]);
			strongestInfluence = Mathf.Max(strongestInfluence, influence);
		}
		return Mathf.Lerp(bodyHalfThickness, minimumHalfThickness, strongestInfluence);
	}

	void BuildGroundWalls(Color color)
	{
		wallIndices.Clear();
		int boundaryVertex = 0;
		do
		{
			wallIndices.Add(boundaryVertex);
			boundaryVertex = boundaryNext[boundaryVertex];
		} while (boundaryVertex != 0);

		float winding = Mathf.Sign(PolygonGeometry.SignedArea(outline));
		int firstWallVertex = vertices.Count;
		float arcPosition = 0f;
		for (int i = 0; i < wallIndices.Count; i++)
		{
			int previousIndex = (i + wallIndices.Count - 1) % wallIndices.Count;
			int nextIndex = (i + 1) % wallIndices.Count;
			int surfaceVertex = wallIndices[i];
			Vector2 point = surfacePoints[surfaceVertex];
			Vector2 previous = surfacePoints[wallIndices[previousIndex]];
			Vector2 next = surfacePoints[wallIndices[nextIndex]];
			Vector3 normal = CalculateWallNormal(previous, point, next, winding);
			float halfThickness = surfaceHeights[surfaceVertex];
			float wallHeight = halfThickness * 2f;

			Vector3 bottomPosition = new Vector3(point.x, -halfThickness, point.y);
			Vector3 topPosition = new Vector3(point.x, halfThickness, point.y);
			Vector2 bottomUv = new Vector2(arcPosition, 0f);
			Vector2 topUv = new Vector2(arcPosition, wallHeight);
			AddVertex(bottomPosition, normal, bottomUv, color);
			AddVertex(topPosition, normal, topUv, color);
			arcPosition += Vector2.Distance(point, next);
		}

		for (int i = 0; i < wallIndices.Count; i++)
		{
			int nextIndex = (i + 1) % wallIndices.Count;
			int bottomStart = firstWallVertex + i * 2;
			int topStart = bottomStart + 1;
			int bottomEnd = firstWallVertex + nextIndex * 2;
			int topEnd = bottomEnd + 1;
			if (winding > 0f)
			{
				AddTriangle(bottomStart, topStart, topEnd);
				AddTriangle(bottomStart, topEnd, bottomEnd);
			}
			else
			{
				AddTriangle(bottomStart, topEnd, topStart);
				AddTriangle(bottomStart, bottomEnd, topEnd);
			}
		}
	}

	Vector3 CalculateWallNormal(Vector2 previous, Vector2 point, Vector2 next, float winding)
	{
		float incomingLength = Vector2.Distance(point, previous);
		float outgoingLength = Vector2.Distance(next, point);
		Vector2 incoming = (point - previous) / incomingLength;
		Vector2 outgoing = (next - point) / outgoingLength;
		Vector2 tangent = incoming + outgoing;
		bool directionsCancel = tangent.sqrMagnitude <= SquaredGeometryTolerance;
		if (directionsCancel)
			tangent = outgoing;

		Vector3 normal = new Vector3(tangent.y * winding, 0f, -tangent.x * winding);
		float normalLength = Mathf.Sqrt(normal.sqrMagnitude);
		return normal / normalLength;
	}

	void AddMirroredTriangle(int firstVertex, int secondVertex, int thirdVertex, int bottomOffset)
	{
		int bottomFirst = firstVertex + bottomOffset;
		int bottomSecond = secondVertex + bottomOffset;
		int bottomThird = thirdVertex + bottomOffset;
		AddTriangle(firstVertex, secondVertex, thirdVertex);
		AddTriangle(bottomFirst, bottomThird, bottomSecond);
	}

	void ClearMeshBuffers()
	{
		vertices.Clear();
		normals.Clear();
		uvs.Clear();
		colors.Clear();
		triangles.Clear();
	}

	void ApplyMesh(Mesh mesh)
	{
		bool needsLargeIndices = vertices.Count > ushort.MaxValue;
		mesh.Clear();
		mesh.indexFormat = needsLargeIndices ? IndexFormat.UInt32 : IndexFormat.UInt16;
		mesh.subMeshCount = 1;
		mesh.SetVertices(vertices);
		mesh.SetNormals(normals);
		mesh.SetUVs(0, uvs);
		mesh.SetColors(colors);
		mesh.SetTriangles(triangles, 0, calculateBounds: true);
	}

	void AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Color color)
	{
		vertices.Add(position);
		normals.Add(normal);
		uvs.Add(uv);
		colors.Add(color);
	}

	void AddTriangle(int firstVertex, int secondVertex, int thirdVertex)
	{
		triangles.Add(firstVertex);
		triangles.Add(secondVertex);
		triangles.Add(thirdVertex);
	}

	static bool IsPositiveFinite(float value)
	{
		bool isPositive = value > 0f;
		bool isFinite = !float.IsNaN(value) && !float.IsInfinity(value);
		return isPositive && isFinite;
	}
}
