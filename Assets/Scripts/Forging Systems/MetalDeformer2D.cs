using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local outline forging. Brush influence follows the boundary so nearby, opposite
/// walls stay independent. Inward strikes smooth displacement instead of flattening dents.
/// </summary>
public class MetalDeformer2D : MonoBehaviour
{
	const float MinimumWallClearance = 0.01f;
	public enum StartingShape
	{
		Oval,
		Rectangle
	}

	[Header("Starting Shape")]
	[SerializeField] StartingShape startingShape = StartingShape.Oval;
	[SerializeField] int vertexCount = 24;
	[SerializeField] Vector2 ovalRadii = new Vector2(1.4f, 0.7f);
	[SerializeField] Vector2 rectangleSize = new Vector2(2.4f, 1.1f);
	[SerializeField] Vector2 shapeCenter = Vector2.zero;

	[Header("Spatial Brush")]
	[SerializeField] float falloffExponent = 1.8f;
	[SerializeField] float minImpactRadius = 0.35f;
	[SerializeField] float maxImpactRadius = 0.75f;
	[SerializeField] float minStrikeStrength = 0.16f;
	[SerializeField] float maxStrikeStrength = 0.7f;
	[Range(0f, 0.25f)]
	[SerializeField] float minInfluence = 0.02f;
	[Range(0f, 1f)]
	[SerializeField] float inflateMix = 0.12f;

	[Header("Edge Split")]
	[SerializeField] bool splitEdgeUnderHammer = true;
	[SerializeField] float splitIfFartherThan = 0.08f;
	[SerializeField] int maxVertices = 48;

	[Header("Local Surface Tension (hot-metal feel)")]
	[Tooltip("Laplacian relax only near the hit. Smooths jagged spikes without dragging far edges by strike force.")]
	[Range(0f, 1f)]
	[SerializeField] float surfaceTension = 0.55f;
	[SerializeField] int tensionIterations = 4;
	[Tooltip("Also relax immediate neighbors of struck verts (smooth only, not strike push).")]
	[SerializeField] bool tensionIncludesAdjacency = true;
	[SerializeField] float sharpAngleDegrees = 55f;
	[Range(0f, 1f)]
	[SerializeField] float sharpCornerExtraSmooth = 0.65f;

	[Header("Local Outline Magnet")]
	[Tooltip("Outline alignment wins over smoothing. Magnet runs last; near-outline verts are protected from tension.")]
	[SerializeField] bool outlineMagnetEnabled = true;
	[SerializeField] float magnetRadius = 0.5f;
	[Range(0f, 1f)]
	[SerializeField] float magnetStrength = 0.365f;
	[SerializeField] float magnetFalloff = 1.4f;
	[Tooltip("Magnet strength fades smoothly with distance from the hammer (as a multiple of impact radius).")]
	[SerializeField] float magnetHitFalloffMultiplier = 2f;
	[Tooltip("Within this distance of the outline, surface tension is reduced/disabled so verts aren't pulled inward.")]
	[SerializeField] float outlineProtectDistance = 0.28f;

	[Header("Split Smoothing")]
	[Tooltip("Extra surface-tension strength when a strike inserts new geometry.")]
	[Range(0f, 1f)]
	[SerializeField] float splitTensionBoost = 0.35f;
	[SerializeField] int splitExtraTensionIterations = 3;

	[Header("Inward Handling")]
	[Tooltip("If strike aims inward past this dot threshold vs local outward, use inward handling.")]
	[Range(0f, 1f)]
	[SerializeField] float inwardDotThreshold = 0.2f;
	[Tooltip("Inward brush radius relative to the normal hammer footprint.")]
	[Range(0.25f, 1.5f)]
	[SerializeField] float creaseRadiusMultiplier = 1.35f;
	[Range(0.25f, 1.5f)]
	[SerializeField] float creaseStrengthScale = 0.75f;
	[SerializeField] bool disableSplitOnInward = false;
	[SerializeField] bool disableMagnetOnInward = false;

	[Header("Stability")]
	[SerializeField] float maxVertexTravelPerStrike = 0.95f;
	[SerializeField] float minEdgeLength = 0.06f;

	[Header("Metal Type")]
	[SerializeField] MetalType metalType;
	
	readonly List<Vector2> vertices = new List<Vector2>();
	readonly List<Vector2> initialVertices = new List<Vector2>();
	readonly List<Vector2> smoothBuffer = new List<Vector2>();
	readonly List<float> influenceBuffer = new List<float>();
	readonly List<float> tensionMask = new List<float>();
	readonly List<float> tensionScratch = new List<float>();
	readonly List<Vector2> verticesBeforeStrike = new List<Vector2>();
	readonly List<Vector2> strikeStart = new List<Vector2>();
	readonly List<Vector2> strikeDestination = new List<Vector2>();
	readonly List<float> boundaryDistances = new List<float>();
	
	Vector2[] targetOutline;
	bool splitThisStrike;
	float strikeMagnetMultiplier = 1f;
	float strikeTensionMultiplier = 1f;
	float heat = 0f;
	float impactRadius = 0f;
	
	public IReadOnlyList<Vector2> Vertices => vertices;
	public int VertexCount => vertices.Count;
	public float ImpactRadius(float charge01) => Mathf.Lerp(minImpactRadius, maxImpactRadius, charge01);
	public float ImpactRadius(Vector2 point, Vector2 direction, float charge01)
	{
		bool inward = vertices.Count >= 3 && Vector2.Dot(direction.normalized, EstimateOutwardAt(point)) <= -inwardDotThreshold;
		return ImpactRadius(charge01) * (inward ? Mathf.Clamp(creaseRadiusMultiplier, 0.25f, 1.5f) : 1f);
	}
	public bool LastStrikeLimited { get; private set; }
	public float MinStrikeStrength => minStrikeStrength;
	public float MaxStrikeStrength => maxStrikeStrength;
	public float FalloffExponent => falloffExponent;
	public float Heat { get => heat; set => heat = value; }
	public Vector2 ShapeCenter { get => shapeCenter; set => shapeCenter = value; }
	public MetalType MetalType => metalType;
	public string MetalDisplayName => metalType != null ? metalType.displayName : "Metal";
	public MetalFeel CurrentFeel => SampleFeel();

	public event System.Action VerticesChanged;
	public event System.Action<Vector2, float> Struck;
	public event System.Action MetalTypeChanged;
	
	void Awake()
	{
		if (vertices.Count == 0)
			InitializeShape();
	}

	public void SetTargetOutline(Vector2[] targetVertices)
	{
		targetOutline = targetVertices;
	}

	public void SetMetalType(MetalType type)
	{
		metalType = type;

		MetalTypeChanged?.Invoke();
	}

	public MetalFeel SampleFeel()
	{
		return metalType != null ? metalType.Sample(heat) : default;
	}

	public void InitializeShape()
	{
		vertices.Clear();
		initialVertices.Clear();
		vertexCount = Mathf.Clamp(vertexCount, 8, maxVertices);

		if (startingShape == StartingShape.Oval)
		{
			for (int i = 0; i < vertexCount; i++)
			{
				float t = (i / (float)vertexCount) * Mathf.PI * 2f;
				vertices.Add(shapeCenter + new Vector2(Mathf.Cos(t) * ovalRadii.x, Mathf.Sin(t) * ovalRadii.y));
			}
		}
		else
		{
			float w = rectangleSize.x;
			float h = rectangleSize.y;
			float perimeter = 2f * (w + h);
			for (int i = 0; i < vertexCount; i++)
			{
				float d = (i / (float)vertexCount) * perimeter;
				
				Vector2 local;
				
				if (d < w)
					local = new Vector2(-w * 0.5f + d, -h * 0.5f);
				else if (d < w + h)
					local = new Vector2(w * 0.5f, -h * 0.5f + (d - w));
				else if (d < w + h + w)
					local = new Vector2(w * 0.5f - (d - w - h), h * 0.5f);
				else
					local = new Vector2(-w * 0.5f, h * 0.5f - (d - w - h - w));
				
				vertices.Add(shapeCenter + local);
			}
		}

		initialVertices.AddRange(vertices);
		VerticesChanged?.Invoke();
	}

	public void LoadVertices(IReadOnlyList<Vector2> source, bool replaceInitialSnapshot = false)
	{
		if (!PolygonGeometry.IsSimple(source))
			return;
		
		vertices.Clear();
		
		for (int i = 0; i < source.Count; i++)
			vertices.Add(source[i]);
		
		if (replaceInitialSnapshot || initialVertices.Count == 0)
		{
			initialVertices.Clear();
			initialVertices.AddRange(vertices);
		}

		VerticesChanged?.Invoke();
	}

	public void CopyVerticesTo(List<Vector2> destination)
	{
		if (destination == null)
			return;
		
		destination.Clear();
		destination.AddRange(vertices);
	}

	public void CopyBrushPreview(Vector2 point, Vector2 direction, float charge, List<Vector2> destination)
	{
		destination.Clear();
		if (vertices.Count < 3 || direction.sqrMagnitude < 0.0001f || SampleFeel().mobility <= 0f)
			return;
		float radius = ImpactRadius(point, direction, charge);
		int edge = FindClosestEdge(point, out Vector2 closest);
		if (Vector2.Distance(point, closest) > radius)
			return;
		int next = (edge + 1) % vertices.Count;
		int focus = Vector2.Distance(point, vertices[edge]) <= Vector2.Distance(point, vertices[next]) ? edge : next;
		if (Vector2.Distance(point, vertices[focus]) > radius)
		{
			// A strike will insert a vertex here; preview just this long edge's local span.
			Vector2 tangent = (vertices[next] - vertices[edge]).normalized;
			destination.Add(PolygonGeometry.ClosestOnSegment(closest - tangent * radius, vertices[edge], vertices[next]));
			destination.Add(closest);
			destination.Add(PolygonGeometry.ClosestOnSegment(closest + tangent * radius, vertices[edge], vertices[next]));
			return;
		}
		bool inward = Vector2.Dot(direction.normalized, EstimateOutwardAt(point)) <= -inwardDotThreshold;
		BuildBrushInfluence(point, focus, radius, inward);
		int first = focus;
		for (int step = 0; step < vertices.Count - 1; step++)
		{
			int prev = (first - 1 + vertices.Count) % vertices.Count;
			if (influenceBuffer[prev] <= 0f)
				break;
			first = prev;
		}
		destination.Add(vertices[(first - 1 + vertices.Count) % vertices.Count]);
		for (int step = 0; step < vertices.Count; step++)
		{
			int index = (first + step) % vertices.Count;
			destination.Add(vertices[index]);
			if (influenceBuffer[index] <= 0f)
				break;
		}
	}

	public void ResetShape()
	{
		vertices.Clear();
		vertices.AddRange(initialVertices);
		VerticesChanged?.Invoke();
	}

	public bool TryStrike(Vector2 _impactPoint, Vector2 _direction, float _charge01)
	{
		LastStrikeLimited = false;
		if (vertices.Count < 3 || _direction.sqrMagnitude < 0.0001f)
			return false;
		
		_direction = _direction.normalized;
		_charge01 = Mathf.Clamp01(_charge01);
		MetalFeel feel = SampleFeel();
		strikeMagnetMultiplier = feel.magnet;
		strikeTensionMultiplier = feel.tension;
		impactRadius = ImpactRadius(_impactPoint, _direction, _charge01);
		
		float strength = Mathf.Lerp(minStrikeStrength, maxStrikeStrength, _charge01) * feel.mobility;
		if (strength <= 0.0001f)
			return false;
		
		splitThisStrike = false;
		
		
		Vector2 centroid = ComputeCentroid();
		Vector2 localOutward = EstimateOutwardAt(_impactPoint);
		float outwardDot = Vector2.Dot(_direction, localOutward);
		bool inwardStrike = outwardDot <= -inwardDotThreshold;

		bool allowSplit = splitEdgeUnderHammer && !(inwardStrike && disableSplitOnInward);
		
		FindClosestEdge(_impactPoint, out Vector2 closestPoint);
		if (Vector2.Distance(_impactPoint, closestPoint) > impactRadius)
			return false;
		
		verticesBeforeStrike.Clear();
		verticesBeforeStrike.AddRange(vertices);
		int focusIndex = EnsureVertexNearImpact(_impactPoint, allowSplit);
		if (Vector2.Distance(vertices[focusIndex], _impactPoint) > impactRadius)
		{
			vertices.Clear();
			vertices.AddRange(verticesBeforeStrike);
			return false;
		}

		// Keep matching vertex indices until all displacement, smoothing and assistance
		// have been validated. The earlier snapshot also rolls back inserted geometry.
		strikeStart.Clear();
		strikeStart.AddRange(vertices);
		BuildBrushInfluence(_impactPoint, focusIndex, impactRadius, inwardStrike);
		ApplyBrush(_direction, centroid, strength, inwardStrike);
		
		if (!inwardStrike)
		{
			BuildTensionMask(_impactPoint);
			float tensionScale = strikeTensionMultiplier + (splitThisStrike ? splitTensionBoost : 0f);
			int tensionIters = tensionIterations + (splitThisStrike ? splitExtraTensionIterations : 0);
			ApplySurfaceTension(tensionScale, tensionIters);
		}
		if (!(inwardStrike && disableMagnetOnInward))
			ApplyLocalOutlineMagnet(_impactPoint, _direction);
		
		if (!ApplySafeMovement())
		{
			vertices.Clear();
			vertices.AddRange(verticesBeforeStrike);
			return false;
		}
		CollapseTinyEdgesNear(_impactPoint);
		SubdivideStretchedEdges();

		VerticesChanged?.Invoke();
		Struck?.Invoke(_impactPoint, impactRadius);
		return true;
	}

	void BuildBrushInfluence(Vector2 impactPoint, int focusIndex, float radius, bool inward)
	{
		influenceBuffer.Clear();
		boundaryDistances.Clear();
		for (int i = 0; i < vertices.Count; i++)
		{
			influenceBuffer.Add(0f);
			boundaryDistances.Add(0f);
		}

		float perimeter = 0f;
		for (int step = 1; step <= vertices.Count; step++)
		{
			int i = (focusIndex + step) % vertices.Count;
			int prev = (i - 1 + vertices.Count) % vertices.Count;
			perimeter += Vector2.Distance(vertices[prev], vertices[i]);
			if (step < vertices.Count)
				boundaryDistances[i] = perimeter;
		}

		for (int i = 0; i < vertices.Count; i++)
		{
			float alongEdge = Mathf.Min(boundaryDistances[i], perimeter - boundaryDistances[i]);
			boundaryDistances[i] = alongEdge;
			float distance = Mathf.Max(Vector2.Distance(vertices[i], impactPoint), alongEdge / 1.5f);
			float t = 1f - Mathf.Clamp01(distance / radius);
			float smooth = t * t * (3f - 2f * t);
			float influence = Mathf.Pow(smooth, inward ? 2f : falloffExponent);
			influenceBuffer[i] = influence >= minInfluence * (inward ? 0.5f : 1f) ? influence : 0f;
		}
		influenceBuffer[focusIndex] = Mathf.Max(influenceBuffer[focusIndex], inward ? 0.55f : 0.65f);
	}

	void ApplyBrush(Vector2 direction, Vector2 centroid, float strength, bool inward)
	{
		for (int i = 0; i < vertices.Count; i++)
		{
			float influence = influenceBuffer[i];
			if (influence <= 0f)
				continue;

			Vector2 pushDirection = direction;
			if (inward)
			{
				// Soften the new displacement, never the existing indentation.
				float prev = influenceBuffer[(i - 1 + vertices.Count) % vertices.Count];
				float next = influenceBuffer[(i + 1) % vertices.Count];
				influence = Mathf.Lerp(influence, (prev + 2f * influence + next) * 0.25f, surfaceTension);
			}
			else
			{
				Vector2 outward = vertices[i] - centroid;
				outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : direction;
				pushDirection = Vector2.Lerp(direction, outward, inflateMix).normalized;
			}

			float move = strength * influence * (inward ? creaseStrengthScale : 1f);
			vertices[i] += pushDirection * Mathf.Min(move, maxVertexTravelPerStrike);
		}
	}

	bool ApplySafeMovement()
	{
		strikeDestination.Clear();
		strikeDestination.AddRange(vertices);
		float maxTravel = 0f;
		for (int i = 0; i < vertices.Count; i++)
			maxTravel = Mathf.Max(maxTravel, Vector2.Distance(strikeStart[i], vertices[i]));
		if (maxTravel < 0.0001f)
			return false;

		// Small steps also catch a hit that would pass entirely through a thin wall.
		int steps = Mathf.Max(1, Mathf.CeilToInt(maxTravel / (MinimumWallClearance * 0.5f)));
		float accepted = 0f;
		float winding = Mathf.Sign(PolygonGeometry.SignedArea(strikeStart));
		for (int step = 1; step <= steps; step++)
		{
			float amount = step / (float)steps;
			InterpolateStrike(amount);
			if (!IsValidStrikeShape(winding))
			{
				// Refine the last safe step instead of throwing away the whole hit.
				float blocked = amount;
				for (int refinement = 0; refinement < 8; refinement++)
				{
					float middle = (accepted + blocked) * 0.5f;
					InterpolateStrike(middle);
					if (IsValidStrikeShape(winding))
						accepted = middle;
					else
						blocked = middle;
				}
				LastStrikeLimited = true;
				break;
			}
			accepted = amount;
		}
		InterpolateStrike(accepted);
		return maxTravel * accepted >= 0.0001f;
	}

	void InterpolateStrike(float amount)
	{
		for (int i = 0; i < vertices.Count; i++)
			vertices[i] = Vector2.Lerp(strikeStart[i], strikeDestination[i], amount);
	}

	bool IsValidStrikeShape(float winding)
	{
		if (PolygonGeometry.SignedArea(vertices) * winding <= 0f || !PolygonGeometry.IsSimple(vertices))
			return false;
		for (int i = 0; i < vertices.Count; i++)
		{
			for (int edge = 0; edge < vertices.Count; edge++)
			{
				int next = (edge + 1) % vertices.Count;
				if (edge == i || next == i)
					continue;
				if (vertices[i].x < Mathf.Min(vertices[edge].x, vertices[next].x) - MinimumWallClearance || vertices[i].x > Mathf.Max(vertices[edge].x, vertices[next].x) + MinimumWallClearance ||
					vertices[i].y < Mathf.Min(vertices[edge].y, vertices[next].y) - MinimumWallClearance || vertices[i].y > Mathf.Max(vertices[edge].y, vertices[next].y) + MinimumWallClearance)
					continue;
				float distance = Vector2.Distance(vertices[i], PolygonGeometry.ClosestOnSegment(vertices[i], vertices[edge], vertices[next]));
				if (distance >= MinimumWallClearance)
					continue;
				// Existing fine tips remain valid; don't impose a new width on them.
				float originalDistance = Vector2.Distance(strikeStart[i], PolygonGeometry.ClosestOnSegment(strikeStart[i], strikeStart[edge], strikeStart[next]));
				if (distance + 0.000001f < Mathf.Min(MinimumWallClearance, originalDistance))
					return false;
			}
		}
		return true;
	}

	void SubdivideStretchedEdges()
	{
		float spacing = Mathf.Max(minEdgeLength * 2f, impactRadius * 0.75f);
		for (int i = vertices.Count - 1; i >= 0 && vertices.Count < maxVertices; i--)
		{
			int next = (i + 1) % vertices.Count;
			if (influenceBuffer[i] <= 0f && influenceBuffer[next] <= 0f)
				continue;
			int segments = Mathf.Min(Mathf.CeilToInt(Vector2.Distance(vertices[i], vertices[next]) / spacing), maxVertices - vertices.Count + 1);
			Vector2 end = vertices[next];
			for (int segment = segments - 1; segment > 0; segment--)
				vertices.Insert(i + 1, Vector2.Lerp(vertices[i], end, segment / (float)segments));
		}
	}

	int FindClosestEdge(Vector2 point, out Vector2 closest)
	{
		int edgeIndex = 0;
		closest = vertices[0];
		float bestDistance = float.MaxValue;
		for (int i = 0; i < vertices.Count; i++)
		{
			Vector2 candidate = PolygonGeometry.ClosestOnSegment(point, vertices[i], vertices[(i + 1) % vertices.Count]);
			float distance = (candidate - point).sqrMagnitude;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				closest = candidate;
				edgeIndex = i;
			}
		}
		return edgeIndex;
	}

	Vector2 EstimateOutwardAt(Vector2 point)
	{
		int index = FindClosestEdge(point, out _);
		Vector2 edge = vertices[(index + 1) % vertices.Count] - vertices[index];
		return new Vector2(edge.y, -edge.x).normalized * Mathf.Sign(PolygonGeometry.SignedArea(vertices));
	}

	int EnsureVertexNearImpact(Vector2 impactPoint, bool allowSplit)
	{
		int edgeIndex = FindClosestEdge(impactPoint, out Vector2 closestOnEdge);

		int indexA = edgeIndex;
		int indexB = (edgeIndex + 1) % vertices.Count;
		float distA = Vector2.Distance(closestOnEdge, vertices[indexA]);
		float distB = Vector2.Distance(closestOnEdge, vertices[indexB]);
		float splitDistance = Mathf.Min(splitIfFartherThan, impactRadius * 0.3f);
		if (distA <= splitDistance && distA <= distB)
			return indexA;

		if (distB <= splitDistance)
			return indexB;
		if (!allowSplit || vertices.Count >= maxVertices)
			return distA <= distB ? indexA : indexB;

		int insertAt = indexA + 1;
		vertices.Insert(insertAt, closestOnEdge);
		splitThisStrike = true;
		return insertAt;
	}

	void BuildTensionMask(Vector2 impactPoint)
	{
		tensionMask.Clear();
		int count = vertices.Count;
		for (int i = 0; i < count; i++)
		{
			float influence = i < influenceBuffer.Count ? influenceBuffer[i] : 0f;
			float dist = Vector2.Distance(vertices[i], impactPoint);
			float connectedDistance = Mathf.Max(dist, boundaryDistances[i] / 1.5f);
			float spatial = Mathf.Max(0f, 1f - connectedDistance / (impactRadius * 1.15f));
			tensionMask.Add(Mathf.Max(influence, spatial));
		}

		if (!tensionIncludesAdjacency)
			return;

		// Expand mask by one ring for smoothing only (does not add strike displacement).
		tensionScratch.Clear();
		for (int i = 0; i < count; i++)
		{
			float self = tensionMask[i];
			float prev = tensionMask[(i - 1 + count) % count];
			float next = tensionMask[(i + 1) % count];
			float expanded = Mathf.Max(self, Mathf.Max(prev, next) * 0.7f);
			tensionScratch.Add(boundaryDistances[i] <= impactRadius * 1.75f ? expanded : 0f);
		}

		for (int i = 0; i < count; i++)
			tensionMask[i] = tensionScratch[i];
	}

	void ApplySurfaceTension(float strengthScale = 1f, int iterationOverride = -1)
	{
		float tension = surfaceTension * Mathf.Max(0f, strengthScale);
		int iterations = iterationOverride >= 0 ? iterationOverride : tensionIterations;
		if (tension <= 0f || iterations <= 0)
			return;
		int count = vertices.Count;
		for (int iter = 0; iter < iterations; iter++)
		{
			smoothBuffer.Clear();
			for (int i = 0; i < count; i++)
			{
				Vector2 curr = vertices[i];
				float mask = i < tensionMask.Count ? tensionMask[i] : 0f;
				if (mask < 0.01f)
				{
					smoothBuffer.Add(curr);
					continue;
				}

				Vector2 prev = vertices[(i - 1 + count) % count];
				Vector2 next = vertices[(i + 1) % count];
				Vector2 averaged = (prev + curr + next) / 3f;

				float blend = tension * mask;

				// Extra smooth on very sharp corners inside the struck region.
				Vector2 toPrev = (prev - curr).normalized;
				Vector2 toNext = (next - curr).normalized;
				float ang = Vector2.Angle(toPrev, toNext);
				if (ang < sharpAngleDegrees)
				{
					float sharpness = 1f - (ang / sharpAngleDegrees);
					blend = Mathf.Max(blend, sharpCornerExtraSmooth * sharpness * mask * strengthScale);
				}

				// Extra polish on brand-new split geometry and its neighbors.
				if (splitThisStrike && influenceBuffer[i] > 0f)
					blend = Mathf.Max(blend, (surfaceTension + splitTensionBoost) * mask);

				// Outline priority: don't smooth verts off an outline they're already near.
				float outlineDist = DistanceToTargetOutline(curr, VertexOutward(strikeStart, i));
				if (outlineDist < outlineProtectDistance)
				{
					float protect = outlineDist / Mathf.Max(0.0001f, outlineProtectDistance);
					blend *= protect * protect;
				}

				blend = Mathf.Clamp01(blend);
				if (blend <= 0.001f)
				{
					smoothBuffer.Add(curr);
					continue;
				}

				smoothBuffer.Add(Vector2.Lerp(curr, averaged, blend));
			}

			vertices.Clear();
			vertices.AddRange(smoothBuffer);
		}
	}

	void ApplyLocalOutlineMagnet(Vector2 impactPoint, Vector2 direction)
	{
		bool hasTargetEdge = targetOutline != null && targetOutline.Length >= 2;
		if (!outlineMagnetEnabled || !hasTargetEdge)
			return;

		float hitFalloffRadius = Mathf.Max(0.01f, impactRadius * Mathf.Max(1f, magnetHitFalloffMultiplier));
		for (int i = 0; i < vertices.Count; i++)
		{
			// Only verts this strike actually pushed.
			if (influenceBuffer[i] <= 0f)
				continue;
			// Match surfaces by their pre-strike normal. Inner and outer band walls
			// can be close in space but face in opposite directions.
			if (!TryClosestPointOnOutline(vertices[i], VertexOutward(strikeStart, i), out Vector2 closest, out float outlineDist))
				continue;

			float reach = Mathf.Min(magnetRadius, impactRadius);
			if (outlineDist > reach || reach <= 0f)
				continue;

			// Soft falloff from the hammer — no hard cliff at the brush edge.
			float hitDist = Vector2.Distance(vertices[i], impactPoint);
			float hitT = 1f - Mathf.Clamp01(hitDist / hitFalloffRadius);
			float hitWeight = hitT * hitT * (3f - 2f * hitT);
			if (hitWeight <= 0.001f)
				continue;

			float outlineProximity = 1f - outlineDist / magnetRadius;
			float pull = Mathf.Pow(Mathf.Clamp01(outlineProximity), magnetFalloff) * magnetStrength * hitWeight * strikeMagnetMultiplier;
			if (outlineDist < outlineProtectDistance)
			{
				float lockAmount = 1f - outlineDist / Mathf.Max(0.0001f, outlineProtectDistance);
				pull = Mathf.Max(pull, magnetStrength * hitWeight * strikeMagnetMultiplier * Mathf.Lerp(1f, 1.35f, lockAmount));
			}

			if (pull <= 0.001f)
				continue;
			Vector2 assisted = Vector2.Lerp(vertices[i], closest, Mathf.Clamp01(pull));
			// Assistance may brake an overshoot, but must not reverse the aimed hit.
			float alongAim = Vector2.Dot(assisted - strikeStart[i], direction);
			if (alongAim < 0f)
				assisted -= direction * alongAim;
			vertices[i] = assisted;
		}
	}

	float DistanceToTargetOutline(Vector2 point, Vector2 outward)
	{
		if (targetOutline == null || targetOutline.Length < 2)
			return float.MaxValue;

		if (!TryClosestPointOnOutline(point, outward, out _, out float dist))
			return float.MaxValue;

		return dist;
	}

	void CollapseTinyEdgesNear(Vector2 impactPoint)
	{
		if (vertices.Count <= 8 || minEdgeLength <= 0f)
			return;

		// Merge only tiny edges near the impact so split spam doesn't create sawteeth.
		for (int pass = 0; pass < 3; pass++)
		{
			bool merged = false;
			for (int i = 0; i < vertices.Count && vertices.Count > 8; i++)
			{
				int next = (i + 1) % vertices.Count;
				Vector2 a = vertices[i];
				Vector2 b = vertices[next];
				if (influenceBuffer[i] <= 0f && influenceBuffer[next] <= 0f)
					continue;
				if (Vector2.Distance(a, impactPoint) > impactRadius * 1.25f && Vector2.Distance(b, impactPoint) > impactRadius * 1.25f)
					continue;

				if (Vector2.Distance(a, b) < minEdgeLength)
				{
					vertices[i] = (a + b) * 0.5f;
					vertices.RemoveAt(next);
					if (!PolygonGeometry.IsSimple(vertices))
					{
						vertices.Insert(next, b);
						vertices[i] = a;
						continue;
					}
					if (i < influenceBuffer.Count && next < influenceBuffer.Count)
					{
						influenceBuffer[i] = Mathf.Max(influenceBuffer[i], influenceBuffer[next]);
						influenceBuffer.RemoveAt(next);
					}

					if (i < tensionMask.Count && next < tensionMask.Count)
					{
						tensionMask[i] = Mathf.Max(tensionMask[i], tensionMask[next]);
						tensionMask.RemoveAt(next);
					}

					merged = true;
					break;
				}
			}

			if (!merged)
				break;
		}
	}

	public bool ContainsPoint(Vector2 point)
	{
		return PolygonGeometry.Contains(point, vertices);
	}

	public Bounds GetBounds()
	{
		if (vertices.Count == 0)
			return new Bounds(shapeCenter, Vector3.one);
		Vector2 min = vertices[0];
		Vector2 max = vertices[0];
		for (int i = 1; i < vertices.Count; i++)
		{
			min = Vector2.Min(min, vertices[i]);
			max = Vector2.Max(max, vertices[i]);
		}

		var center = (min + max) * 0.5f;
		var size = max - min;
		return new Bounds(center, new Vector3(size.x, size.y, 0.1f));
	}

	public Vector2 ComputeCentroid()
	{
		Vector2 c = Vector2.zero;
		for (int i = 0; i < vertices.Count; i++)
			c += vertices[i];
		return c / Mathf.Max(1, vertices.Count);
	}

	static Vector2 VertexOutward(IReadOnlyList<Vector2> polygon, int index)
	{
		Vector2 prev = polygon[index] - polygon[(index - 1 + polygon.Count) % polygon.Count];
		Vector2 next = polygon[(index + 1) % polygon.Count] - polygon[index];
		Vector2 tangent = prev.normalized + next.normalized;
		return new Vector2(tangent.y, -tangent.x).normalized * Mathf.Sign(PolygonGeometry.SignedArea(polygon));
	}

	bool TryClosestPointOnOutline(Vector2 point, Vector2 outward, out Vector2 closest, out float distance)
	{
		closest = point;
		distance = float.MaxValue;
		if (targetOutline == null || targetOutline.Length < 2)
			return false;
		float winding = Mathf.Sign(PolygonGeometry.SignedArea(targetOutline));
		for (int i = 0; i < targetOutline.Length; i++)
		{
			Vector2 a = targetOutline[i];
			Vector2 b = targetOutline[(i + 1) % targetOutline.Length];
			Vector2 edge = b - a;
			Vector2 targetOutward = new Vector2(edge.y, -edge.x).normalized * winding;
			if (Vector2.Dot(outward, targetOutward) < 0.25f)
				continue;
			Vector2 c = PolygonGeometry.ClosestOnSegment(point, a, b);
			float d = Vector2.Distance(point, c);
			if (d < distance)
			{
				distance = d;
				closest = c;
			}
		}

		return distance < float.MaxValue;
	}

}
