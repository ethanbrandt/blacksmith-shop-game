using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ExtrudePolygonMeshBuilder
{
	const float GeometryTolerance = 0.000001f;
	const float SquaredGeometryTolerance = GeometryTolerance * GeometryTolerance;
	const int MinimumSurfaceVertices = 192;
	const int MaximumSurfaceVertices = 256;
	const int VerticesPerBevelSegment = 64;
	const int MaximumRefinementPasses = 4;
	const int MinimumCurveResolution = 4;
	const int MinimumGridResolution = 4;
	const int MinimumGrindSamples = 32;
	const int MaximumGrindSamples = 128;
	const int GridCellsAcrossBounds = 32;
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
		public float InverseSquaredLength { get; }
		public float MaximumGrind { get; }
		public float MaximumInfluenceDistance { get; }
		public float MaximumSquaredInfluenceDistance { get; }

		public GroundEdge(Vector2 start, Vector2 vector, float length, float startGrind, float endGrind, float arcPosition, float maximumInfluenceDistance = float.PositiveInfinity, float maximumGrind = 1f)
		{
			Start = start;
			Vector = vector;
			Length = length;
			StartGrind = startGrind;
			EndGrind = endGrind;
			ArcPosition = arcPosition;
			InverseSquaredLength = 1f / (length * length);
			MaximumGrind = maximumGrind;
			MaximumInfluenceDistance = maximumInfluenceDistance;
			MaximumSquaredInfluenceDistance = maximumInfluenceDistance * maximumInfluenceDistance;
		}
	}

	readonly struct GrindInterpolation
	{
		public float ConstantTerm { get; }
		public float LinearCoefficient { get; }
		public float QuadraticCoefficient { get; }
		public float CubicCoefficient { get; }
		public float MaximumValue { get; }

		public GrindInterpolation(float previous, float current, float next, float following)
		{
			ConstantTerm = 2f * current;
			LinearCoefficient = next - previous;
			QuadraticCoefficient = 2f * previous - 5f * current + 4f * next - following;
			CubicCoefficient = 3f * current - previous - 3f * next + following;
			float firstControl = current + LinearCoefficient / 6f;
			float secondControl = current + LinearCoefficient / 3f + QuadraticCoefficient / 6f;
			float endpointMaximum = Mathf.Max(current, next);
			float controlMaximum = Mathf.Max(firstControl, secondControl);
			MaximumValue = Mathf.Clamp01(Mathf.Max(endpointMaximum, controlMaximum) + GeometryTolerance);
		}

		public float Evaluate(float fraction)
		{
			float linearTerm = LinearCoefficient * fraction;
			float quadraticTerm = QuadraticCoefficient * fraction * fraction;
			float cubicTerm = CubicCoefficient * fraction * fraction * fraction;
			return 0.5f * (ConstantTerm + linearTerm + quadraticTerm + cubicTerm);
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
	readonly List<GrindInterpolation> grindInterpolations = new List<GrindInterpolation>();

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
	readonly Dictionary<Vector2, float> thicknessLookup = new Dictionary<Vector2, float>();

	float perimeter;
	float effectiveBevelWidth;
	float requestedBevelWidth;
	float bodyHalfThickness;
	float minimumHalfThickness;
	float maximumBevelSlope;
	float normalSampleWidth;
	float bevelWidthPerGrind;
	float grindSamplePositionScale;
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
		bevelWidthPerGrind = angleWidth;
		requestedBevelWidth = bevelWidth;
		effectiveBevelWidth = Mathf.Max(bevelWidth, angleWidth);
		normalSampleWidth = MeasureMaximumTaperWidth();
		float blendRadius = Mathf.Min(normalSampleWidth * 0.25f, coreRadius * 3f);
		BuildSmoothGrindSamples(blendRadius);
		PrepareGroundInfluences();

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
		Vector2 gradient = CalculateThicknessGradient(point, normalStep);
		Vector3 normal = new Vector3(-gradient.x, 1f, -gradient.y);
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
		if (surfacePoints.Count >= vertexLimit)
		{
			midpointLookup[edgeKey] = -1;
			return -1;
		}

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
		BuildGrindInterpolations();
	}

	void BuildGrindInterpolations()
	{
		grindInterpolations.Clear();
		int sampleCount = smoothedGrindSamples.Count;
		grindSamplePositionScale = sampleCount / perimeter;
		for (int i = 0; i < sampleCount; i++)
		{
			int previousIndex = (i + sampleCount - 1) % sampleCount;
			int nextIndex = (i + 1) % sampleCount;
			int followingIndex = (i + 2) % sampleCount;
			float previous = smoothedGrindSamples[previousIndex];
			float current = smoothedGrindSamples[i];
			float next = smoothedGrindSamples[nextIndex];
			float following = smoothedGrindSamples[followingIndex];
			GrindInterpolation interpolation = new GrindInterpolation(previous, current, next, following);
			grindInterpolations.Add(interpolation);
		}
	}

	GrindInterpolation FindGrindInterpolation(float arcPosition, out float fraction)
	{
		float samplePosition = arcPosition * grindSamplePositionScale;
		int sampleIndex = Mathf.FloorToInt(samplePosition);
		fraction = samplePosition - sampleIndex;
		sampleIndex %= grindInterpolations.Count;
		return grindInterpolations[sampleIndex];
	}

	float SampleSmoothGrind(float arcPosition)
	{
		GrindInterpolation interpolation = FindGrindInterpolation(arcPosition, out float fraction);
		float interpolatedGrind = interpolation.Evaluate(fraction);
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

	void PrepareGroundInfluences()
	{
		thicknessLookup.Clear();
		for (int i = 0; i < groundEdges.Count; i++)
		{
			GroundEdge edge = groundEdges[i];
			float maximumGrind = MeasureMaximumEdgeGrind(edge);
			Vector2 endPoint = edge.Start + edge.Vector;
			float startRun = Vector2.Distance(edge.Start, coreCenter) - coreRadius;
			float endRun = Vector2.Distance(endPoint, coreCenter) - coreRadius;
			float maximumRun = Mathf.Max(startRun, endRun);
			float desiredWidth = Mathf.Max(requestedBevelWidth, bevelWidthPerGrind * maximumGrind);
			float maximumWidth = Mathf.Max(0f, Mathf.Min(desiredWidth, maximumRun));
			float conservativeWidth = maximumWidth + GeometryTolerance;
			GroundEdge preparedEdge = new GroundEdge(edge.Start, edge.Vector, edge.Length, edge.StartGrind, edge.EndGrind, edge.ArcPosition, conservativeWidth, maximumGrind);
			groundEdges[i] = preparedEdge;
		}
	}

	float MeasureMaximumEdgeGrind(GroundEdge edge)
	{
		float maximumGrind = Mathf.Max(edge.StartGrind, edge.EndGrind);
		float startPosition = edge.ArcPosition * grindSamplePositionScale;
		float endPosition = (edge.ArcPosition + edge.Length) * grindSamplePositionScale;
		int firstSample = Mathf.FloorToInt(startPosition);
		int lastSample = Mathf.FloorToInt(endPosition);
		for (int i = firstSample; i <= lastSample; i++)
		{
			int sampleIndex = i % grindInterpolations.Count;
			maximumGrind = Mathf.Max(maximumGrind, grindInterpolations[sampleIndex].MaximumValue);
		}
		return maximumGrind;
	}

	float CalculateInfluenceUpperBound(float distance, GroundEdge edge)
	{
		float taperFraction = distance / edge.MaximumInfluenceDistance;
		float thicknessRecovery = Mathf.SmoothStep(0f, 1f, taperFraction);
		return edge.MaximumGrind * (1f - thicknessRecovery);
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

	float EvaluateGroundInfluence(Vector2 point, GroundEdge edge, float minimumInfluence = 0f)
	{
		Vector2 offset = point - edge.Start;
		float projectedFraction = Vector2.Dot(offset, edge.Vector) * edge.InverseSquaredLength;
		float edgeFraction = Mathf.Clamp01(projectedFraction);
		Vector2 sourcePoint = edge.Start + edge.Vector * edgeFraction;
		float squaredSourceDistance = (point - sourcePoint).sqrMagnitude;
		if (squaredSourceDistance >= edge.MaximumSquaredInfluenceDistance)
			return 0f;
		float sourceDistance = Mathf.Sqrt(squaredSourceDistance);
		if (minimumInfluence > 0f)
		{
			float upperBound = CalculateInfluenceUpperBound(sourceDistance, edge);
			if (upperBound + GeometryTolerance <= minimumInfluence)
				return 0f;
		}

		float arcPosition = edge.ArcPosition + edgeFraction * edge.Length;
		float rawGrind = Mathf.Lerp(edge.StartGrind, edge.EndGrind, edgeFraction);
		float blendedGrind = rawGrind >= 1f ? 1f : SampleSmoothGrind(arcPosition);
		float sourceGrind = Mathf.Max(rawGrind, blendedGrind);

		float requiredWidth = bevelWidthPerGrind * sourceGrind;
		float desiredWidth = Mathf.Max(requestedBevelWidth, requiredWidth);
		float availableRun = Vector2.Distance(sourcePoint, coreCenter) - coreRadius;
		float taperWidth = Mathf.Min(desiredWidth, availableRun);
		float squaredTaperWidth = taperWidth * taperWidth;
		if (taperWidth <= GeometryTolerance || squaredSourceDistance >= squaredTaperWidth)
			return 0f;

		float taperFraction = sourceDistance / taperWidth;
		float thicknessRecovery = Mathf.SmoothStep(0f, 1f, taperFraction);
		return sourceGrind * (1f - thicknessRecovery);
	}

	Vector2 CalculateThicknessGradient(Vector2 point, float normalStep)
	{
		Vector2 horizontalOffset = Vector2.right * normalStep;
		Vector2 verticalOffset = Vector2.up * normalStep;
		Vector2 rightPoint = point + horizontalOffset;
		Vector2 leftPoint = point - horizontalOffset;
		Vector2 upperPoint = point + verticalOffset;
		Vector2 lowerPoint = point - verticalOffset;
		float squaredCoreRadius = coreRadius * coreRadius;
		bool rightInCore = (rightPoint - coreCenter).sqrMagnitude <= squaredCoreRadius;
		bool leftInCore = (leftPoint - coreCenter).sqrMagnitude <= squaredCoreRadius;
		bool upperInCore = (upperPoint - coreCenter).sqrMagnitude <= squaredCoreRadius;
		bool lowerInCore = (lowerPoint - coreCenter).sqrMagnitude <= squaredCoreRadius;
		if (rightInCore && leftInCore && upperInCore && lowerInCore)
			return Vector2.zero;

		float rightInfluence = rightInCore ? 1f : 0f;
		float leftInfluence = leftInCore ? 1f : 0f;
		float upperInfluence = upperInCore ? 1f : 0f;
		float lowerInfluence = lowerInCore ? 1f : 0f;
		for (int i = 0; i < groundEdges.Count; i++)
		{
			GroundEdge edge = groundEdges[i];
			float horizontalMinimum = Mathf.Min(rightInfluence, leftInfluence);
			float verticalMinimum = Mathf.Min(upperInfluence, lowerInfluence);
			float minimumInfluence = Mathf.Min(horizontalMinimum, verticalMinimum);
			if (edge.MaximumGrind <= minimumInfluence)
				continue;

			Vector2 offset = point - edge.Start;
			float projectedFraction = Vector2.Dot(offset, edge.Vector) * edge.InverseSquaredLength;
			Vector2 sourcePoint = edge.Start + edge.Vector * Mathf.Clamp01(projectedFraction);
			float squaredDistance = (point - sourcePoint).sqrMagnitude;
			float maximumSampleDistance = edge.MaximumInfluenceDistance + normalStep;
			if (squaredDistance >= maximumSampleDistance * maximumSampleDistance)
				continue;

			float minimumSampleDistance = Mathf.Max(0f, Mathf.Sqrt(squaredDistance) - normalStep);
			float upperBound = CalculateInfluenceUpperBound(minimumSampleDistance, edge);
			if (upperBound + GeometryTolerance <= minimumInfluence)
				continue;

			if (!rightInCore)
				rightInfluence = Mathf.Max(rightInfluence, EvaluateGroundInfluence(rightPoint, edge, rightInfluence));
			if (!leftInCore)
				leftInfluence = Mathf.Max(leftInfluence, EvaluateGroundInfluence(leftPoint, edge, leftInfluence));
			if (!upperInCore)
				upperInfluence = Mathf.Max(upperInfluence, EvaluateGroundInfluence(upperPoint, edge, upperInfluence));
			if (!lowerInCore)
				lowerInfluence = Mathf.Max(lowerInfluence, EvaluateGroundInfluence(lowerPoint, edge, lowerInfluence));
		}

		float rightHeight = rightInCore ? bodyHalfThickness : Mathf.Lerp(bodyHalfThickness, minimumHalfThickness, rightInfluence);
		float leftHeight = leftInCore ? bodyHalfThickness : Mathf.Lerp(bodyHalfThickness, minimumHalfThickness, leftInfluence);
		float upperHeight = upperInCore ? bodyHalfThickness : Mathf.Lerp(bodyHalfThickness, minimumHalfThickness, upperInfluence);
		float lowerHeight = lowerInCore ? bodyHalfThickness : Mathf.Lerp(bodyHalfThickness, minimumHalfThickness, lowerInfluence);
		float sampleDistance = normalStep * 2f;
		float horizontalGradient = (rightHeight - leftHeight) / sampleDistance;
		float verticalGradient = (upperHeight - lowerHeight) / sampleDistance;
		return new Vector2(horizontalGradient, verticalGradient);
	}

	float EvaluateHalfThickness(Vector2 point)
	{
		bool isInCore = (point - coreCenter).sqrMagnitude <= coreRadius * coreRadius;
		if (isInCore || groundEdges.Count == 0)
			return bodyHalfThickness;
		if (thicknessLookup.TryGetValue(point, out float cachedThickness))
			return cachedThickness;

		float strongestInfluence = 0f;
		for (int i = 0; i < groundEdges.Count; i++)
		{
			GroundEdge edge = groundEdges[i];
			if (edge.MaximumGrind <= strongestInfluence)
				continue;
			float influence = EvaluateGroundInfluence(point, edge, strongestInfluence);
			strongestInfluence = Mathf.Max(strongestInfluence, influence);
			if (strongestInfluence >= 1f)
			{
				thicknessLookup[point] = minimumHalfThickness;
				return minimumHalfThickness;
			}
		}
		float halfThickness = Mathf.Lerp(bodyHalfThickness, minimumHalfThickness, strongestInfluence);
		thicknessLookup[point] = halfThickness;
		return halfThickness;
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
