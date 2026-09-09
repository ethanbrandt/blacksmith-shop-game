using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Movable/rotatable blade polygon for the grindstone minigame.
/// Grind amount drives edge banding only — silhouette stays fixed.
/// </summary>
public class GrindBladeBody : MonoBehaviour
{
	const float MinimumBrushRadius = 0.0001f;
	const float MinimumSubdivisionEdgeLength = 0.0001f;
	const float MinimumSquaredNormalLength = 0.0001f;
	const float MinimumSquaredSegmentLength = 0.000001f;
	const float MinimumGrindRange = 0.05f;
	const float MaximumSlowdownStart = 0.999f;
	const int DefaultMaximumVertexCount = 64;

	[Header("Grind Feel")]
	[SerializeField] float maxGrindAmount = 1.75f;
	[Tooltip("Grind amount treated as fully sharp. Past this is overgrind.")]
	[SerializeField] float idealGrindAmount = 1f;
	[Tooltip("How hard it is to grind overgrindFraction the ideal. Higher = much slower overgrind.")]
	[Min(1f)]
	[SerializeField] float overgrindResistance = 18f;
	[Tooltip("Start slowing grind as you approach ideal (0-1 along the way to ideal).")]
	[Range(0f, 1f)]
	[SerializeField] float approachSlowdownStart = 0.7f;
	[Tooltip("Extra slowdown multiplier at ideal, previousGrindAmount true overgrind resistance kicks in.")]
	[Min(1f)]
	[SerializeField] float approachSlowdownStrength = 3.5f;
	[SerializeField] float contactFalloff = 1.6f;
	[Tooltip("Edge outward normal must face the stone above this dot product to receive grind.")]
	[Range(0f, 1f)]
	[SerializeField] float stoneFacingThreshold = 0.25f;
	readonly List<Vector2> localVertices = new List<Vector2>();
	readonly List<float> grindAmounts = new List<float>();
	readonly List<Vector2> worldScratch = new List<Vector2>();
	readonly List<bool> edgeFacesStoneScratch = new List<bool>();
	readonly List<bool> vertexCanGrindScratch = new List<bool>();

	Vector2 position;
	float rotationDegrees;

	public IReadOnlyList<Vector2> LocalVertices => localVertices;
	public IReadOnlyList<float> GrindAmounts => grindAmounts;
	public int VertexCount => localVertices.Count;

	public Vector2 Position
	{
		get => position;
		set
		{
			if (position == value)
				return;
			position = value;
			PoseChanged?.Invoke();
		}
	}

	public float RotationDegrees
	{
		get => rotationDegrees;
		set
		{
			if (Mathf.Approximately(rotationDegrees, value))
				return;
			rotationDegrees = value;
			PoseChanged?.Invoke();
		}
	}

	public Vector2 Centroid => position;

	public event System.Action VerticesChanged;
	public event System.Action PoseChanged;
	public int TopologyVersion { get; private set; }
	public int GrindVersion { get; private set; }

	public void LoadShape(IReadOnlyList<Vector2> shapeVertices, IReadOnlyList<float> amounts, Vector2 origin, float rotation)
	{
		TopologyVersion++;
		localVertices.Clear();
		grindAmounts.Clear();
		if (!PolygonGeometry.IsSimple(shapeVertices))
		{
			RaiseChanged();
			return;
		}

		for (int i = 0; i < shapeVertices.Count; i++)
		{
			localVertices.Add(shapeVertices[i]);
			bool hasSavedAmount = amounts != null && i < amounts.Count;
			float savedAmount = hasSavedAmount ? amounts[i] : 0f;
			bool hasFiniteAmount = !float.IsNaN(savedAmount) && !float.IsInfinity(savedAmount);
			float amount = hasFiniteAmount ? Mathf.Clamp(savedAmount, 0f, MaxGrindAmount) : 0f;
			grindAmounts.Add(amount);
		}

		position = origin;
		rotationDegrees = rotation;
		RaiseChanged();
		PoseChanged?.Invoke();
	}

	/// <summary>
	/// Subdivide long perimeter edges so grind banding can vary along a face.
	/// Interpolates grind amounts on inserted vertices.
	/// </summary>
	public static void DensifyShape(List<Vector2> vertices, List<float> amounts, float maxEdgeLength, int maxVertices = DefaultMaximumVertexCount)
	{
		bool hasPolygon = vertices != null && vertices.Count >= PolygonGeometry.MinimumVertexCount;
		bool isEdgeLengthTooSmall = maxEdgeLength <= MinimumSubdivisionEdgeLength;
		if (!hasPolygon || isEdgeLengthTooSmall)
			return;

		amounts ??= new List<float>();
		while (amounts.Count < vertices.Count)
			amounts.Add(0f);
		bool addedVertices = true;
		while (addedVertices && vertices.Count < maxVertices)
		{
			addedVertices = false;
			for (int i = 0; i < vertices.Count && vertices.Count < maxVertices; i++)
			{
				int nextVertexIndex = (i + 1) % vertices.Count;
				float edgeLength = Vector2.Distance(vertices[i], vertices[nextVertexIndex]);
				if (edgeLength <= maxEdgeLength)
					continue;
				Vector2 midpoint = (vertices[i] + vertices[nextVertexIndex]) * 0.5f;
				float midpointAmount = (amounts[i] + amounts[nextVertexIndex]) * 0.5f;
				vertices.Insert(nextVertexIndex, midpoint);
				amounts.Insert(nextVertexIndex, midpointAmount);
				addedVertices = true;
				i++;
			}
		}
	}

	public void CopyLocalVerticesTo(List<Vector2> destination)
	{
		destination.Clear();
		for (int i = 0; i < localVertices.Count; i++)
			destination.Add(localVertices[i]);
	}

	public void CopyInitialLocalVerticesTo(List<Vector2> destination)
	{
		CopyLocalVerticesTo(destination);
	}

	public void CopyGrindAmountsTo(List<float> destination)
	{
		destination.Clear();
		for (int i = 0; i < grindAmounts.Count; i++)
			destination.Add(grindAmounts[i]);
	}

	public void GetWorldVertices(List<Vector2> destination)
	{
		destination.Clear();
		float rotationRadians = rotationDegrees * Mathf.Deg2Rad;
		float rotationCosine = Mathf.Cos(rotationRadians);
		float rotationSine = Mathf.Sin(rotationRadians);
		for (int i = 0; i < localVertices.Count; i++)
		{
			Vector2 local = localVertices[i];
			destination.Add(position + new Vector2(local.x * rotationCosine - local.y * rotationSine, local.x * rotationSine + local.y * rotationCosine));
		}
	}

	public Bounds GetWorldBounds()
	{
		GetWorldVertices(worldScratch);
		if (worldScratch.Count == 0)
			return new Bounds(position, Vector3.one);

		var bounds = new Bounds(worldScratch[0], Vector3.zero);
		for (int i = 1; i < worldScratch.Count; i++)
			bounds.Encapsulate(worldScratch[i]);
		return bounds;
	}

	public bool TryGetStoneContact(Vector2 stoneCenter, float stoneRadius, out Vector2 contactPoint, out Vector2 pushNormal, out float penetration)
	{
		contactPoint = position;
		pushNormal = Vector2.down;
		penetration = 0f;
		if (localVertices.Count < PolygonGeometry.MinimumVertexCount || stoneRadius <= 0f)
			return false;

		GetWorldVertices(worldScratch);
		float closestDistance = float.MaxValue;
		Vector2 closestPoint = worldScratch[0];
		for (int i = 0; i < worldScratch.Count; i++)
		{
			Vector2 a = worldScratch[i];
			Vector2 b = worldScratch[(i + 1) % worldScratch.Count];
			Vector2 closest = ClosestOnSegment(stoneCenter, a, b);
			float distance = Vector2.Distance(stoneCenter, closest);
			if (distance < closestDistance)
			{
				closestDistance = distance;
				closestPoint = closest;
			}
		}

		bool containsStoneCenter = PointInPolygon(stoneCenter, worldScratch);
		if (containsStoneCenter)
		{
			penetration = stoneRadius + closestDistance;
			contactPoint = closestPoint;
			pushNormal = (closestPoint - stoneCenter).sqrMagnitude > MinimumSquaredNormalLength ? (stoneCenter - closestPoint).normalized : (position - stoneCenter).normalized;
			return true;
		}

		if (closestDistance >= stoneRadius)
			return false;
		penetration = stoneRadius - closestDistance;
		contactPoint = closestPoint;
		pushNormal = (closestPoint - stoneCenter).normalized;
		if (pushNormal.sqrMagnitude < MinimumSquaredNormalLength)
			pushNormal = (position - stoneCenter).normalized;
		return true;
	}

	public float IdealGrindAmount => Mathf.Max(MinimumGrindRange, idealGrindAmount);
	public float MaxGrindAmount => Mathf.Max(IdealGrindAmount, maxGrindAmount);

	public float ApplyGrind(Vector2 worldContact, Vector2 stoneCenter, float radius, float amountDelta)
	{
		bool hasNoGrindAmount = amountDelta <= 0f;
		bool isBrushTooSmall = radius <= MinimumBrushRadius;
		bool hasShape = localVertices.Count >= PolygonGeometry.MinimumVertexCount;
		bool hasInvalidGrindInput = hasNoGrindAmount || isBrushTooSmall;
		if (hasInvalidGrindInput || !hasShape)
			return 0f;

		GetWorldVertices(worldScratch);
		BuildVertexGrindMask(worldScratch, stoneCenter);
		float rotationRadians = rotationDegrees * Mathf.Deg2Rad;
		float rotationCosine = Mathf.Cos(rotationRadians);
		float rotationSine = Mathf.Sin(rotationRadians);
		float inverseCosine = rotationCosine;
		float inverseSine = -rotationSine;
		Vector2 localContact = worldContact - position;
		localContact = new Vector2(localContact.x * inverseCosine - localContact.y * inverseSine, localContact.x * inverseSine + localContact.y * inverseCosine);
		float ideal = IdealGrindAmount;
		float totalApplied = 0f;
		float squaredRadius = radius * radius;
		for (int i = 0; i < localVertices.Count; i++)
		{
			if (i >= vertexCanGrindScratch.Count || !vertexCanGrindScratch[i])
				continue;
			float squaredDistance = (localVertices[i] - localContact).sqrMagnitude;
			if (squaredDistance > squaredRadius)
				continue;
			float contactWeight = 1f - Mathf.Sqrt(squaredDistance) / radius;
			contactWeight = Mathf.Pow(Mathf.Clamp01(contactWeight), contactFalloff);
			float previousGrindAmount = grindAmounts[i];
			float resistance = GrindResistance(previousGrindAmount, ideal);
			float updatedGrindAmount = Mathf.Min(MaxGrindAmount, previousGrindAmount + amountDelta * contactWeight / resistance);
			float appliedGrindAmount = updatedGrindAmount - previousGrindAmount;
			if (appliedGrindAmount <= 0f)
				continue;
			grindAmounts[i] = updatedGrindAmount;
			totalApplied += appliedGrindAmount;
		}

		if (totalApplied > 0f)
			RaiseChanged();

		return totalApplied;
	}

	void BuildVertexGrindMask(IReadOnlyList<Vector2> worldVertices, Vector2 stoneCenter)
	{
		edgeFacesStoneScratch.Clear();
		vertexCanGrindScratch.Clear();
		int vertexCount = worldVertices.Count;
		if (vertexCount < PolygonGeometry.MinimumVertexCount)
			return;
		bool isCounterClockwise = SignedArea(worldVertices) > 0f;
		for (int i = 0; i < vertexCount; i++)
		{
			int nextVertexIndex = (i + 1) % vertexCount;
			Vector2 a = worldVertices[i];
			Vector2 b = worldVertices[nextVertexIndex];
			Vector2 outward = OutwardNormal(a, b, isCounterClockwise);
			Vector2 midpoint = (a + b) * 0.5f;
			Vector2 toStone = stoneCenter - midpoint;
			bool hasOutwardNormal = outward.sqrMagnitude > MinimumSquaredSegmentLength;
			bool hasStoneDirection = toStone.sqrMagnitude > MinimumSquaredSegmentLength;
			bool hasContactDirections = hasOutwardNormal && hasStoneDirection;
			bool facesStone = hasContactDirections && Vector2.Dot(outward, toStone.normalized) > stoneFacingThreshold;
			edgeFacesStoneScratch.Add(facesStone);
		}

		for (int v = 0; v < vertexCount; v++)
		{
			int previousEdgeIndex = (v - 1 + vertexCount) % vertexCount;
			bool canGrind = edgeFacesStoneScratch[previousEdgeIndex] || edgeFacesStoneScratch[v];
			vertexCanGrindScratch.Add(canGrind);
		}
	}

	static Vector2 OutwardNormal(Vector2 edgeStart, Vector2 edgeEnd, bool isCounterClockwise)
	{
		Vector2 edge = edgeEnd - edgeStart;
		if (edge.sqrMagnitude < MinimumSquaredSegmentLength)
			return Vector2.zero;

		// Inward for CCW is (-y, x); outward is the opposite.
		return isCounterClockwise ? new Vector2(edge.y, -edge.x).normalized : new Vector2(-edge.y, edge.x).normalized;
	}

	static float SignedArea(IReadOnlyList<Vector2> vertices) => PolygonGeometry.SignedArea(vertices);
	float GrindResistance(float currentGrind, float ideal)
	{
		float resistance = 1f;

		// Soft slowdown while approaching full sharpness.
		float approach = currentGrind / ideal;
		if (approach > approachSlowdownStart && approachSlowdownStart < MaximumSlowdownStart)
		{
			float slowdownProgress = Mathf.InverseLerp(approachSlowdownStart, 1f, Mathf.Min(approach, 1f));
			resistance *= Mathf.Lerp(1f, approachSlowdownStrength, slowdownProgress * slowdownProgress);
		}

		// Hard resistance once overgrindFraction ideal (overgrind).
		if (currentGrind > ideal)
		{
			float overgrindFraction = (currentGrind - ideal) / Mathf.Max(MinimumGrindRange, maxGrindAmount - ideal);
			resistance *= 1f + overgrindResistance * (1f + overgrindFraction * overgrindFraction * overgrindResistance);
		}

		return Mathf.Max(1f, resistance);
	}

	void RaiseChanged()
	{
		GrindVersion++;
		VerticesChanged?.Invoke();
	}

	static Vector2 ClosestOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
	{
		Vector2 segment = segmentEnd - segmentStart;
		float squaredSegmentLength = Vector2.Dot(segment, segment);
		if (squaredSegmentLength < MinimumSquaredSegmentLength)
			return segmentStart;
		float projectionFraction = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / squaredSegmentLength);
		return segmentStart + segment * projectionFraction;
	}

	static bool PointInPolygon(Vector2 point, List<Vector2> polygon)
	{
		return PolygonGeometry.Contains(point, polygon);
	}
}
