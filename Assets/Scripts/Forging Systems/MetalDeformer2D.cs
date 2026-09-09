using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Feel v3: spatial brush + local surface tension.
/// Moves metal where you hit, then relaxes only the struck region so edges stay soft like hot metal
/// (without ring-spreading strike force that dragged distant parts).
/// </summary>
public class MetalDeformer2D : MonoBehaviour
{
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
	[Tooltip("For Blend mode: 0 = pure lateral flow, 1 = pure crease dent.")]
	[SerializeField] float creaseRadiusMultiplier = 1.35f;
	[Range(0.25f, 1.5f)]
	[SerializeField] float creaseStrengthScale = 0.75f;
	[Range(0f, 1f)]
	[SerializeField] float creaseTensionBoost = 0.45f;
	[SerializeField] int creaseExtraTensionIterations = 3;
	[Tooltip("Inflate mix used for lateral / blend lateral portion (usually 0).")]
	[SerializeField] bool disableSplitOnInward = true;
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
	readonly List<bool> movedThisStrike = new List<bool>();
	readonly List<Vector2> verticesBeforeStrike = new List<Vector2>();
	
	Vector2[] targetOutline;
	bool splitThisStrike;
	bool inwardSpecialThisStrike;
	bool inwardCreaseThisStrike;
	float strikeMagnetMultiplier = 1f;
	float strikeTensionMultiplier = 1f;
	float heat = 0f;
	float impactRadius = 0f;
	
	public IReadOnlyList<Vector2> Vertices => vertices;
	public int VertexCount => vertices.Count;
	public float ImpactRadius(float charge01) => Mathf.Lerp(minImpactRadius, maxImpactRadius, charge01);
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

	public void ResetShape()
	{
		vertices.Clear();
		vertices.AddRange(initialVertices);
		VerticesChanged?.Invoke();
	}

	public bool TryStrike(Vector2 _impactPoint, Vector2 _direction, float _charge01)
	{
		if (vertices.Count < 3)
			return false;
		
		_direction = _direction.normalized;
		_charge01 = Mathf.Clamp01(_charge01);
		MetalFeel feel = SampleFeel();
		strikeMagnetMultiplier = feel.magnet;
		strikeTensionMultiplier = feel.tension;
		impactRadius = Mathf.Lerp(minImpactRadius, maxImpactRadius, _charge01);
		
		float strength = Mathf.Lerp(minStrikeStrength, maxStrikeStrength, _charge01) * feel.mobility;
		if (strength <= 0.0001f)
		{
			Debug.Log("REJECTED: TOO LOW STRENGTH");
			return false;
		}
		
		splitThisStrike = false;
		inwardSpecialThisStrike = false;
		inwardCreaseThisStrike = false;
		
		
		Vector2 centroid = ComputeCentroid();
		Vector2 localOutward = EstimateOutwardAt(_impactPoint, centroid);
		float outwardDot = Vector2.Dot(_direction, localOutward);
		bool inwardStrike = outwardDot <= -inwardDotThreshold;

		bool allowSplit = splitEdgeUnderHammer && !(inwardStrike && disableSplitOnInward);
		
		float boundaryDistance = float.MaxValue;

		for (int i = 0; i < vertices.Count; i++)
		{
			Vector2 closestPoint = PolygonGeometry.ClosestOnSegment(_impactPoint, vertices[i], vertices[(i + 1) % vertices.Count]);
			boundaryDistance = Mathf.Min(boundaryDistance, Vector2.Distance(_impactPoint, closestPoint));
		}

		if (boundaryDistance > impactRadius)
		{
			Debug.Log("REJECTED: BOUNDARY IS OUT OF IMPACT RADIUS");
			return false;
		}
		
		verticesBeforeStrike.Clear();
		verticesBeforeStrike.AddRange(vertices);
		int focusIndex = EnsureVertexNearImpact(_impactPoint, allowSplit);
		if (Vector2.Distance(vertices[focusIndex], _impactPoint) > impactRadius)
		{
			vertices.Clear();
			vertices.AddRange(verticesBeforeStrike);
			Debug.Log("REJECTED: DISTANCE OF VERTEX AND IMPACT POINT TOO HIGH");
			return false;
		}

		if (inwardStrike)
		{
			inwardSpecialThisStrike = true;
			ApplyInwardCrease(_impactPoint, localOutward, focusIndex, strength);
		}
		else
			ApplyOutwardBrush(_impactPoint, _direction, focusIndex, centroid, strength, inflateMix);
		
		BuildTensionMask(_impactPoint);
		
		float tensionScale = strikeTensionMultiplier;
		int tensionIters = tensionIterations;
		if (splitThisStrike)
		{
			tensionScale += splitTensionBoost;
			tensionIters += splitExtraTensionIterations;
		}

		if (inwardCreaseThisStrike)
		{
			tensionScale += creaseTensionBoost;
			tensionIters += Mathf.RoundToInt(creaseExtraTensionIterations);
		}

		ApplySurfaceTension(tensionScale, tensionIters);
		CollapseTinyEdgesNear(_impactPoint);
		if (!(inwardSpecialThisStrike && disableMagnetOnInward))
			ApplyLocalOutlineMagnet(_impactPoint);
		
		if (!PolygonGeometry.IsSimple(vertices))
		{
			vertices.Clear();
			vertices.AddRange(verticesBeforeStrike);
			Debug.Log("REJECTED: POLYGON NOT SIMPLE");
			return false;
		}

		VerticesChanged?.Invoke();
		Struck?.Invoke(_impactPoint, impactRadius);
		return true;
	}

	void ApplyOutwardBrush(Vector2 impactPoint, Vector2 direction, int focusIndex, Vector2 centroid, float strength, float inflateAmount)
	{
		influenceBuffer.Clear();
		movedThisStrike.Clear();
		bool anyMoved = false;
		for (int i = 0; i < vertices.Count; i++)
		{
			float dist = Vector2.Distance(vertices[i], impactPoint);
			float influence = 0f;
			if (dist <= impactRadius)
			{
				float t = 1f - dist / impactRadius;
				float smooth = t * t * (3f - 2f * t);
				influence = Mathf.Pow(smooth, falloffExponent);
				if (influence < minInfluence)
					influence = 0f;
			}

			if (i == focusIndex)
				influence = Mathf.Max(influence, 0.65f);
			influenceBuffer.Add(influence);
			if (influence <= 0f)
			{
				movedThisStrike.Add(false);
				continue;
			}

			Vector2 outward = vertices[i] - centroid;
			if (outward.sqrMagnitude < 0.0001f)
				outward = direction;
			else
				outward.Normalize();

			Vector2 pushDir = Vector2.Lerp(direction, outward, inflateAmount).normalized;
			float move = Mathf.Min(strength * influence, maxVertexTravelPerStrike);
			vertices[i] += pushDir * move;
			movedThisStrike.Add(true);
			anyMoved = true;
		}

		if (!anyMoved)
		{
			vertices[focusIndex] += direction * strength;
			if (focusIndex < influenceBuffer.Count)
				influenceBuffer[focusIndex] = 1f;
			while (movedThisStrike.Count < vertices.Count)
				movedThisStrike.Add(false);
			movedThisStrike[focusIndex] = true;
		}
	}

	void ApplyInwardCrease(Vector2 impactPoint, Vector2 localOutward, int focusIndex, float strength)
	{
		inwardCreaseThisStrike = true;
		Vector2 inward = -localOutward;
		Vector2 centroid = ComputeCentroid();
		float creaseRadius = impactRadius * Mathf.Max(1f, creaseRadiusMultiplier);
		float creaseStrength = strength * creaseStrengthScale;
		influenceBuffer.Clear();
		movedThisStrike.Clear();
		bool anyMoved = false;
		for (int i = 0; i < vertices.Count; i++)
		{
			float dist = Vector2.Distance(vertices[i], impactPoint);
			float influence = 0f;
			if (dist <= creaseRadius)
			{
				float t = 1f - dist / creaseRadius;
				// Wider, softer lobe than the outward brush — dent instead of stab.
				float smooth = t * t * (3f - 2f * t);
				influence = smooth * smooth;
				if (influence < minInfluence * 0.5f)
					influence = 0f;
			}

			if (i == focusIndex)
				influence = Mathf.Max(influence, 0.55f);
			influenceBuffer.Add(influence);
			if (influence <= 0f)
			{
				movedThisStrike.Add(false);
				continue;
			}

			// Shared inward from the hit plus a little local surface inward — dent, not stab.
			Vector2 vertOutward = vertices[i] - centroid;
			Vector2 vertInward = vertOutward.sqrMagnitude > 0.0001f ? -vertOutward.normalized : inward;
			Vector2 creaseDir = Vector2.Lerp(inward, vertInward, 0.35f).normalized;

			float move = Mathf.Min(creaseStrength * influence, maxVertexTravelPerStrike * 0.85f);
			vertices[i] += creaseDir * move;
			movedThisStrike.Add(true);
			anyMoved = true;
		}

		if (!anyMoved)
		{
			vertices[focusIndex] += inward * creaseStrength;
			if (focusIndex < influenceBuffer.Count)
				influenceBuffer[focusIndex] = 1f;
			while (movedThisStrike.Count < vertices.Count)
				movedThisStrike.Add(false);
			movedThisStrike[focusIndex] = true;
		}
	}

	Vector2 EstimateOutwardAt(Vector2 point, Vector2 centroid)
	{
		// Prefer boundary normal at the closest edge; fall back to centroid ray.
		int edgeIndex = 0;
		Vector2 closest = vertices[0];
		float bestDist = float.MaxValue;
		for (int i = 0; i < vertices.Count; i++)
		{
			Vector2 a = vertices[i];
			Vector2 b = vertices[(i + 1) % vertices.Count];
			Vector2 c = ClosestPointOnSegment(point, a, b);
			float d = Vector2.Distance(point, c);
			if (d < bestDist)
			{
				bestDist = d;
				closest = c;
				edgeIndex = i;
			}
		}

		Vector2 a0 = vertices[edgeIndex];
		Vector2 b0 = vertices[(edgeIndex + 1) % vertices.Count];
		Vector2 edge = b0 - a0;
		if (edge.sqrMagnitude > 0.0001f)
		{
			// CCW boundary → outward is right normal (edge.y, -edge.x).
			// If winding is CW, flip using signed area.
			Vector2 outward = new Vector2(edge.y, -edge.x).normalized;
			if (ComputeSignedArea() < 0f)
				outward = -outward;

			return outward;
		}

		Vector2 fromCenter = point - centroid;
		return fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized : Vector2.up;
	}

	float ComputeSignedArea() => PolygonGeometry.SignedArea(vertices);
	int EnsureVertexNearImpact(Vector2 impactPoint, bool allowSplit)
	{
		int edgeIndex = 0;
		Vector2 closestOnEdge = vertices[0];
		float bestDist = float.MaxValue;
		for (int i = 0; i < vertices.Count; i++)
		{
			Vector2 a = vertices[i];
			Vector2 b = vertices[(i + 1) % vertices.Count];
			Vector2 c = ClosestPointOnSegment(impactPoint, a, b);
			float d = Vector2.Distance(impactPoint, c);
			if (d < bestDist)
			{
				bestDist = d;
				closestOnEdge = c;
				edgeIndex = i;
			}
		}

		int indexA = edgeIndex;
		int indexB = (edgeIndex + 1) % vertices.Count;
		float distA = Vector2.Distance(closestOnEdge, vertices[indexA]);
		float distB = Vector2.Distance(closestOnEdge, vertices[indexB]);
		if (distA <= splitIfFartherThan && distA <= distB)
			return indexA;

		if (distB <= splitIfFartherThan)
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
			float spatial = dist <= impactRadius * 1.15f ? 1f - dist / (impactRadius * 1.15f) : 0f;
			tensionMask.Add(Mathf.Max(influence, spatial));
		}

		if (!tensionIncludesAdjacency)
			return;

		// Expand mask by one ring for smoothing only (does not add strike displacement).
		var expanded = new float[count];
		for (int i = 0; i < count; i++)
		{
			float self = tensionMask[i];
			float prev = tensionMask[(i - 1 + count) % count];
			float next = tensionMask[(i + 1) % count];
			expanded[i] = Mathf.Max(self, Mathf.Max(prev, next) * 0.7f);
		}

		for (int i = 0; i < count; i++)
			tensionMask[i] = expanded[i];
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
				bool hasMovementRecord = i < movedThisStrike.Count;
				bool wasMoved = hasMovementRecord && movedThisStrike[i];
				if (splitThisStrike && wasMoved)
					blend = Mathf.Max(blend, (surfaceTension + splitTensionBoost) * mask);

				// Outline priority: don't smooth verts off an outline they're already near.
				float outlineDist = DistanceToTargetOutline(curr);
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

	void ApplyLocalOutlineMagnet(Vector2 impactPoint)
	{
		bool hasTargetEdge = targetOutline != null && targetOutline.Length >= 2;
		if (!outlineMagnetEnabled || !hasTargetEdge)
			return;

		float hitFalloffRadius = Mathf.Max(0.01f, impactRadius * Mathf.Max(1f, magnetHitFalloffMultiplier));
		for (int i = 0; i < vertices.Count; i++)
		{
			// Only verts this strike actually pushed.
			bool moved = i < movedThisStrike.Count && movedThisStrike[i];
			if (!moved)
				continue;
			if (!TryClosestPointOnOutline(vertices[i], out Vector2 closest, out float outlineDist))
				continue;

			if (outlineDist > magnetRadius)
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
			vertices[i] = Vector2.Lerp(vertices[i], closest, Mathf.Clamp01(pull));
		}
	}

	float DistanceToTargetOutline(Vector2 point)
	{
		if (targetOutline == null || targetOutline.Length < 2)
			return float.MaxValue;

		if (!TryClosestPointOnOutline(point, out _, out float dist))
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
				if (Vector2.Distance(a, impactPoint) > impactRadius * 1.25f && Vector2.Distance(b, impactPoint) > impactRadius * 1.25f)
					continue;

				if (Vector2.Distance(a, b) < minEdgeLength)
				{
					vertices[i] = (a + b) * 0.5f;
					vertices.RemoveAt(next);
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

					if (i < movedThisStrike.Count && next < movedThisStrike.Count)
					{
						movedThisStrike[i] = movedThisStrike[i] || movedThisStrike[next];
						movedThisStrike.RemoveAt(next);
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

	bool TryClosestPointOnOutline(Vector2 point, out Vector2 closest, out float distance)
	{
		closest = point;
		distance = float.MaxValue;
		if (targetOutline == null || targetOutline.Length < 2)
			return false;
		for (int i = 0; i < targetOutline.Length; i++)
		{
			Vector2 a = targetOutline[i];
			Vector2 b = targetOutline[(i + 1) % targetOutline.Length];
			Vector2 c = ClosestPointOnSegment(point, a, b);
			float d = Vector2.Distance(point, c);
			if (d < distance)
			{
				distance = d;
				closest = c;
			}
		}

		return distance < float.MaxValue;
	}

	static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float denom = Vector2.Dot(ab, ab);
		if (denom < 0.000001f)
			return a;

		float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
		return a + ab * t;
	}
}
