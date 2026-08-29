using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Movable/rotatable blade polygon for the grindstone minigame.
/// Grind amount drives edge banding only — silhouette stays fixed.
/// </summary>
public class GrindBladeBody : MonoBehaviour
{
	[Header("Grind Feel")]
	[SerializeField] float maxGrindAmount = 1.75f;
	[Tooltip("Grind amount treated as fully sharp. Past this is overgrind.")]
	[SerializeField] float idealGrindAmount = 1f;
	[Tooltip("How hard it is to grind past the ideal. Higher = much slower overgrind.")]
	[SerializeField, Min(1f)] float overgrindResistance = 18f;
	[Tooltip("Start slowing grind as you approach ideal (0-1 along the way to ideal).")]
	[SerializeField, Range(0f, 1f)] float approachSlowdownStart = 0.7f;
	[Tooltip("Extra slowdown multiplier at ideal, before true overgrind resistance kicks in.")]
	[SerializeField, Min(1f)] float approachSlowdownStrength = 3.5f;
	[SerializeField] float contactFalloff = 1.6f;
	[Tooltip("Edge outward normal must face the stone above this dot product to receive grind.")]
	[SerializeField, Range(0f, 1f)] float stoneFacingThreshold = 0.25f;

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
			position = value;
			RaiseChanged();
		}
	}

	public float RotationDegrees
	{
		get => rotationDegrees;
		set
		{
			rotationDegrees = value;
			RaiseChanged();
		}
	}

	public Vector2 Centroid => position;
	public event System.Action VerticesChanged;

	public void LoadShape(IReadOnlyList<Vector2> localVerts, IReadOnlyList<float> amounts, Vector2 origin, float rotation)
	{
		localVertices.Clear();
		grindAmounts.Clear();

		if (localVerts == null || localVerts.Count < 3)
		{
			RaiseChanged();
			return;
		}

		for (int i = 0; i < localVerts.Count; i++)
		{
			localVertices.Add(localVerts[i]);
			float amount = amounts != null && i < amounts.Count ? Mathf.Max(0f, amounts[i]) : 0f;
			grindAmounts.Add(amount);
		}

		position = origin;
		rotationDegrees = rotation;
		RaiseChanged();
	}

	/// <summary>
	/// Subdivide long perimeter edges so grind banding can vary along a face.
	/// Interpolates grind amounts on inserted vertices.
	/// </summary>
	public static void DensifyShape(List<Vector2> verts, List<float> amounts, float maxEdgeLength, int maxVertices = 64)
	{
		if (verts == null || verts.Count < 3 || maxEdgeLength <= 0.0001f)
			return;

		amounts ??= new List<float>();
		while (amounts.Count < verts.Count)
			amounts.Add(0f);

		bool grew = true;
		while (grew && verts.Count < maxVertices)
		{
			grew = false;
			for (int i = 0; i < verts.Count && verts.Count < maxVertices; i++)
			{
				int j = (i + 1) % verts.Count;
				float len = Vector2.Distance(verts[i], verts[j]);
				if (len <= maxEdgeLength)
					continue;

				Vector2 mid = (verts[i] + verts[j]) * 0.5f;
				float midAmount = (amounts[i] + amounts[j]) * 0.5f;
				verts.Insert(j, mid);
				amounts.Insert(j, midAmount);
				grew = true;
				i++;
			}
		}
	}

	public void CopyLocalVerticesTo(List<Vector2> dst)
	{
		dst.Clear();
		for (int i = 0; i < localVertices.Count; i++)
			dst.Add(localVertices[i]);
	}

	public void CopyInitialLocalVerticesTo(List<Vector2> dst)
	{
		CopyLocalVerticesTo(dst);
	}

	public void CopyGrindAmountsTo(List<float> dst)
	{
		dst.Clear();
		for (int i = 0; i < grindAmounts.Count; i++)
			dst.Add(grindAmounts[i]);
	}

	public void GetWorldVertices(List<Vector2> dst)
	{
		dst.Clear();
		float rad = rotationDegrees * Mathf.Deg2Rad;
		float cos = Mathf.Cos(rad);
		float sin = Mathf.Sin(rad);
		for (int i = 0; i < localVertices.Count; i++)
		{
			Vector2 local = localVertices[i];
			dst.Add(position + new Vector2(local.x * cos - local.y * sin, local.x * sin + local.y * cos));
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
		if (localVertices.Count < 3 || stoneRadius <= 0f)
			return false;

		GetWorldVertices(worldScratch);
		float bestDist = float.MaxValue;
		Vector2 bestPoint = worldScratch[0];
		for (int i = 0; i < worldScratch.Count; i++)
		{
			Vector2 a = worldScratch[i];
			Vector2 b = worldScratch[(i + 1) % worldScratch.Count];
			Vector2 closest = ClosestOnSegment(stoneCenter, a, b);
			float dist = Vector2.Distance(stoneCenter, closest);
			if (dist < bestDist)
			{
				bestDist = dist;
				bestPoint = closest;
			}
		}

		bool inside = PointInPolygon(stoneCenter, worldScratch);
		if (inside)
		{
			penetration = stoneRadius + bestDist;
			contactPoint = bestPoint;
			pushNormal = (bestPoint - stoneCenter).sqrMagnitude > 0.0001f
				? (bestPoint - stoneCenter).normalized
				: (position - stoneCenter).normalized;
			return true;
		}

		if (bestDist >= stoneRadius)
			return false;

		penetration = stoneRadius - bestDist;
		contactPoint = bestPoint;
		pushNormal = (bestPoint - stoneCenter).normalized;
		if (pushNormal.sqrMagnitude < 0.0001f)
			pushNormal = (position - stoneCenter).normalized;
		return true;
	}

	public float IdealGrindAmount => Mathf.Max(0.05f, idealGrindAmount);
	public float MaxGrindAmount => maxGrindAmount;

	public float ApplyGrind(Vector2 worldContact, Vector2 stoneCenter, float radius, float amountDelta)
	{
		if (amountDelta <= 0f || radius <= 0.0001f || localVertices.Count < 3)
			return 0f;

		GetWorldVertices(worldScratch);
		BuildVertexGrindMask(worldScratch, stoneCenter);

		float rad = rotationDegrees * Mathf.Deg2Rad;
		float cos = Mathf.Cos(rad);
		float sin = Mathf.Sin(rad);
		float invCos = cos;
		float invSin = -sin;

		Vector2 localContact = worldContact - position;
		localContact = new Vector2(localContact.x * invCos - localContact.y * invSin, localContact.x * invSin + localContact.y * invCos);

		float ideal = IdealGrindAmount;
		float totalApplied = 0f;
		float radiusSq = radius * radius;
		for (int i = 0; i < localVertices.Count; i++)
		{
			if (i >= vertexCanGrindScratch.Count || !vertexCanGrindScratch[i])
				continue;

			float distSq = (localVertices[i] - localContact).sqrMagnitude;
			if (distSq > radiusSq)
				continue;

			float t = 1f - Mathf.Sqrt(distSq) / radius;
			t = Mathf.Pow(Mathf.Clamp01(t), contactFalloff);
			float before = grindAmounts[i];
			float resistance = GrindResistance(before, ideal);
			float after = Mathf.Min(maxGrindAmount, before + amountDelta * t / resistance);
			float applied = after - before;
			if (applied <= 0f)
				continue;

			grindAmounts[i] = after;
			totalApplied += applied;
		}

		if (totalApplied > 0f)
			RaiseChanged();

		return totalApplied;
	}

	void BuildVertexGrindMask(IReadOnlyList<Vector2> worldVerts, Vector2 stoneCenter)
	{
		edgeFacesStoneScratch.Clear();
		vertexCanGrindScratch.Clear();

		int n = worldVerts.Count;
		if (n < 3)
			return;

		bool ccw = SignedArea(worldVerts) > 0f;
		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			Vector2 a = worldVerts[i];
			Vector2 b = worldVerts[j];
			Vector2 outward = OutwardNormal(a, b, ccw);
			Vector2 mid = (a + b) * 0.5f;
			Vector2 toStone = stoneCenter - mid;
			bool facesStone = outward.sqrMagnitude > 0.000001f
				&& toStone.sqrMagnitude > 0.000001f
				&& Vector2.Dot(outward, toStone.normalized) > stoneFacingThreshold;
			edgeFacesStoneScratch.Add(facesStone);
		}

		for (int v = 0; v < n; v++)
		{
			int prevEdge = (v - 1 + n) % n;
			bool canGrind = edgeFacesStoneScratch[prevEdge] || edgeFacesStoneScratch[v];
			vertexCanGrindScratch.Add(canGrind);
		}
	}

	static Vector2 OutwardNormal(Vector2 edgeStart, Vector2 edgeEnd, bool ccw)
	{
		Vector2 edge = edgeEnd - edgeStart;
		if (edge.sqrMagnitude < 0.000001f)
			return Vector2.zero;

		// Inward for CCW is (-y, x); outward is the opposite.
		return ccw
			? new Vector2(edge.y, -edge.x).normalized
			: new Vector2(-edge.y, edge.x).normalized;
	}

	static float SignedArea(IReadOnlyList<Vector2> verts)
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

	float GrindResistance(float currentGrind, float ideal)
	{
		float resistance = 1f;

		// Soft slowdown while approaching full sharpness.
		float approach = currentGrind / ideal;
		if (approach > approachSlowdownStart && approachSlowdownStart < 0.999f)
		{
			float t = Mathf.InverseLerp(approachSlowdownStart, 1f, Mathf.Min(approach, 1f));
			resistance *= Mathf.Lerp(1f, approachSlowdownStrength, t * t);
		}

		// Hard resistance once past ideal (overgrind).
		if (currentGrind > ideal)
		{
			float past = (currentGrind - ideal) / Mathf.Max(0.05f, maxGrindAmount - ideal);
			resistance *= 1f + overgrindResistance * (1f + past * past * overgrindResistance);
		}

		return Mathf.Max(1f, resistance);
	}

	void RaiseChanged() => VerticesChanged?.Invoke();

	static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float denom = Vector2.Dot(ab, ab);
		if (denom < 0.000001f)
			return a;
		float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
		return a + ab * t;
	}

	static bool PointInPolygon(Vector2 point, List<Vector2> poly)
	{
		bool inside = false;
		for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
		{
			Vector2 pi = poly[i];
			Vector2 pj = poly[j];
			bool intersect = ((pi.y > point.y) != (pj.y > point.y)) &&
				(point.x < (pj.x - pi.x) * (point.y - pi.y) / Mathf.Max(0.000001f, pj.y - pi.y) + pi.x);
			if (intersect)
				inside = !inside;
		}

		return inside;
	}
}
