using System.Collections.Generic;
using UnityEngine;

public enum ShapeQuality
{
	Incomplete,
	Flawed,
	Good,
	Excellent,
	Perfect
}

public class ShapeMatchEvaluator : MonoBehaviour
{
	const int MinimumGhostFillSortingOrder = 10;
	const float GhostFillDepthOffset = 0.05f;

	[Header("Target")]
	[SerializeField] Vector2[] targetVertices;
	[SerializeField] Color targetOutlineColor = new Color(0.75f, 0.9f, 1f, 0.55f);
	[Tooltip("Drawn above the metal so the guide stays visible where the billet covers the outline.")]
	[SerializeField] Color targetGhostOutlineColor = new Color(0.7f, 0.9f, 1f, 0.7f);
	[SerializeField] Color targetGhostFillColor = new Color(0.55f, 0.8f, 1f, 0.12f);
	[SerializeField] float targetLineWidth = 0.05f;
	[SerializeField] float ghostLineWidth = 0.04f;
	[SerializeField] int outlineSortingOrder = 2;
	[SerializeField] int ghostOutlineSortingOrder = 12;
	[SerializeField] int ghostFillSortingOrder = 10;
	[SerializeField] bool showGhostOverMetal = true;
	[SerializeField] bool showGhostFill = true;

	[Header("Sampling")]
	[SerializeField] int sampleResolution = 28;
	[SerializeField] float boundsPadding = 0.35f;

	[Header("Quality Thresholds (match %)")]
	[Range(0f, 1f)]
	[SerializeField] float perfectThreshold = 0.95f;
	[Range(0f, 1f)]
	[SerializeField] float excellentThreshold = 0.88f;
	[Range(0f, 1f)]
	[SerializeField] float goodThreshold = 0.72f;
	[Range(0f, 1f)]
	[SerializeField] float flawedThreshold = 0.45f;

	[Header("Score Weights (normalized)")]
	[Tooltip("How much filling the target interior matters.")]
	[Range(0f, 1f)]
	[SerializeField] float coverageWeight = 0.5f;
	[Tooltip("How much metal area outside the target hurts (grid sample).")]
	[Range(0f, 1f)]
	[SerializeField] float overflowPenaltyWeight = 0.25f;
	[Tooltip("How much far-out vertices / spikes hurt. Thin spikes are caught here.")]
	[Range(0f, 1f)]
	[SerializeField] float spikePenaltyWeight = 0.25f;

	[Header("Spike / Vertex Protrusion")]
	[Tooltip("Outside distance ignored before spike penalty starts.")]
	[SerializeField] float spikeFreeDistance = 0.05f;
	[Tooltip("Outside distance at which spike score reaches zero.")]
	[SerializeField] float spikeFailDistance = 0.55f;
	[SerializeField] float spikePenaltyExponent = 1.35f;
	[Tooltip("If max vertex protrusion exceeds this, Perfect is blocked.")]
	[SerializeField] float perfectMaxSpikeDistance = 0.1f;
	[Tooltip("If max vertex protrusion exceeds this, Excellent is blocked.")]
	[SerializeField] float excellentMaxSpikeDistance = 0.22f;
	[Tooltip("If max vertex protrusion exceeds this, Good is blocked.")]
	[SerializeField] float goodMaxSpikeDistance = 0.4f;
	[Tooltip("If max vertex protrusion exceeds this, Flawed is blocked (Incomplete only).")]
	[SerializeField] float flawedMaxSpikeDistance = 0.85f;

	[Header("References")]
	[SerializeField] MetalDeformer2D metal;
	[SerializeField] LineRenderer targetOutline;
	[SerializeField] LineRenderer targetGhostOutline;
	[SerializeField] MeshFilter ghostFillFilter;
	[SerializeField] MeshRenderer ghostFillRenderer;

	Mesh _ghostFillMesh;
	Material _ghostFillMaterial;
	readonly PolygonMeshBuilder ghostBuilder = new PolygonMeshBuilder();
	
	void OnDestroy()
	{
		if (_ghostFillMesh != null)
			ForgingVisualUtility.DestroyGenerated(_ghostFillMesh);
	}

	public float CoveragePercent { get; private set; }
	public float OverflowPercent { get; private set; }
	public float SpikePenaltyPercent { get; private set; }
	public float MaxVertexProtrusion { get; private set; }
	public float MatchPercent { get; private set; }
	public ShapeQuality Quality { get; private set; } = ShapeQuality.Incomplete;
	public IReadOnlyList<Vector2> TargetVertices => targetVertices;

	public void Configure(MetalDeformer2D deformer, Vector2[] target)
	{
		if (metal != null)
			metal.VerticesChanged -= Evaluate;

		metal = deformer;
		targetVertices = target;
		if (metal != null)
			metal.SetTargetOutline(target);

		EnsureOutline();
		EnsureGhostOutline();
		EnsureGhostFill();
		RebuildTargetVisuals();

		if (metal != null)
		{
			metal.VerticesChanged -= Evaluate;
			metal.VerticesChanged += Evaluate;
		}

		Evaluate();
	}

	void OnEnable()
	{
		if (metal != null)
			metal.VerticesChanged += Evaluate;
	}

	void OnDisable()
	{
		if (metal != null)
			metal.VerticesChanged -= Evaluate;
	}

	public void Evaluate()
	{
		bool hasMetalOutline = metal != null && metal.VertexCount >= PolygonGeometry.MinimumVertexCount;
		bool hasTargetOutline = targetVertices != null && targetVertices.Length >= PolygonGeometry.MinimumVertexCount;
		if (!hasMetalOutline || !hasTargetOutline)
		{
			CoveragePercent = 0f;
			OverflowPercent = 0f;
			SpikePenaltyPercent = 1f;
			MaxVertexProtrusion = 0f;
			MatchPercent = 0f;
			Quality = ShapeQuality.Incomplete;
			return;
		}

		Bounds combined = GetTargetBounds();
		combined.Encapsulate(metal.GetBounds());
		combined.Expand(boundsPadding);

		int totalTargetSamples = 0;
		int coveredTargetSamples = 0;
		int totalMetalSamples = 0;
		int overflowMetalSamples = 0;

		float minX = combined.min.x;
		float minY = combined.min.y;
		float maxX = combined.max.x;
		float maxY = combined.max.y;
		int res = Mathf.Max(8, sampleResolution);

		for (int y = 0; y < res; y++)
		{
			float v = (y + 0.5f) / res;
			float py = Mathf.Lerp(minY, maxY, v);
			for (int x = 0; x < res; x++)
			{
				float u = (x + 0.5f) / res;
				float px = Mathf.Lerp(minX, maxX, u);
				var p = new Vector2(px, py);

				bool inTarget = PointInPolygon(p, targetVertices);
				bool inMetal = metal.ContainsPoint(p);

				if (inTarget)
				{
					totalTargetSamples++;
					if (inMetal)
						coveredTargetSamples++;
				}

				if (inMetal)
				{
					totalMetalSamples++;
					if (!inTarget)
						overflowMetalSamples++;
				}
			}
		}

		CoveragePercent = totalTargetSamples > 0 ? coveredTargetSamples / (float)totalTargetSamples : 0f;

		OverflowPercent = totalMetalSamples > 0 ? overflowMetalSamples / (float)totalMetalSamples : 0f;

		MaxVertexProtrusion = ComputeMaxVertexProtrusion();
		float spikeScore = ComputeSpikeScore(MaxVertexProtrusion);
		SpikePenaltyPercent = 1f - spikeScore;

		float coverageScore = CoveragePercent;
		float overflowScore = 1f - OverflowPercent;

		float wC = Mathf.Max(0f, coverageWeight);
		float wO = Mathf.Max(0f, overflowPenaltyWeight);
		float wS = Mathf.Max(0f, spikePenaltyWeight);
		float wSum = wC + wO + wS;
		if (wSum <= 0.0001f)
		{
			wC = wO = wS = 1f;
			wSum = 3f;
		}

		MatchPercent = Mathf.Clamp01((coverageScore * wC + overflowScore * wO + spikeScore * wS) / wSum);

		Quality = ResolveQuality(MatchPercent, MaxVertexProtrusion);
	}

	float ComputeMaxVertexProtrusion()
	{
		float maxDist = 0f;
		IReadOnlyList<Vector2> verts = metal.Vertices;
		for (int i = 0; i < verts.Count; i++)
		{
			Vector2 p = verts[i];
			if (PointInPolygon(p, targetVertices))
				continue;

			float dist = DistanceToPolygonBoundary(p, targetVertices);
			if (dist > maxDist)
				maxDist = dist;
		}

		return maxDist;
	}

	float ComputeSpikeScore(float maxProtrusion)
	{
		float free = Mathf.Max(0f, spikeFreeDistance);
		float fail = Mathf.Max(free + 0.001f, spikeFailDistance);
		if (maxProtrusion <= free)
			return 1f;

		float t = Mathf.InverseLerp(free, fail, maxProtrusion);
		return 1f - Mathf.Pow(Mathf.Clamp01(t), Mathf.Max(0.01f, spikePenaltyExponent));
	}

	ShapeQuality ResolveQuality(float match, float maxProtrusion)
	{
		bool canPerfect = maxProtrusion <= perfectMaxSpikeDistance;
		bool canExcellent = maxProtrusion <= excellentMaxSpikeDistance;
		bool canGood = maxProtrusion <= goodMaxSpikeDistance;
		bool canFlawed = maxProtrusion <= flawedMaxSpikeDistance;

		if (match >= perfectThreshold && canPerfect)
			return ShapeQuality.Perfect;

		if (match >= excellentThreshold && canExcellent)
			return ShapeQuality.Excellent;

		if (match >= goodThreshold && canGood)
			return ShapeQuality.Good;

		if (match >= flawedThreshold && canFlawed)
			return ShapeQuality.Flawed;

		return ShapeQuality.Incomplete;
	}

	static float DistanceToPolygonBoundary(Vector2 point, Vector2[] polygon)
	{
		float best = float.MaxValue;
		for (int i = 0; i < polygon.Length; i++)
		{
			Vector2 a = polygon[i];
			Vector2 b = polygon[(i + 1) % polygon.Length];
			best = Mathf.Min(best, DistancePointToSegment(point, a, b));
		}

		return best;
	}

	static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float denom = Vector2.Dot(ab, ab);
		if (denom < 0.000001f)
			return Vector2.Distance(p, a);

		float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
		return Vector2.Distance(p, a + ab * t);
	}

	void EnsureOutline()
	{
		if (targetOutline != null)
			return;

		var go = new GameObject("TargetOutline");
		go.transform.SetParent(transform, false);
		go.layer = gameObject.layer;
		targetOutline = go.AddComponent<LineRenderer>();
		ConfigureOutlineLine(targetOutline, targetOutlineColor, targetLineWidth, outlineSortingOrder, -0.05f);
	}

	void EnsureGhostOutline()
	{
		if (targetGhostOutline != null)
			return;

		var go = new GameObject("TargetGhostOutline");
		go.transform.SetParent(transform, false);
		go.layer = gameObject.layer;
		targetGhostOutline = go.AddComponent<LineRenderer>();
		ConfigureOutlineLine(targetGhostOutline, targetGhostOutlineColor, ghostLineWidth, ghostOutlineSortingOrder, -0.9f);
	}

	void EnsureGhostFill()
	{
		if (ghostFillFilter == null)
		{
			Transform existing = transform.Find("TargetGhostFill");
			if (existing != null)
			{
				ghostFillFilter = existing.GetComponent<MeshFilter>();
				ghostFillRenderer = existing.GetComponent<MeshRenderer>();
			}
		}

		if (ghostFillFilter == null)
		{
			var go = new GameObject("TargetGhostFill");
			go.transform.SetParent(transform, false);
			go.layer = gameObject.layer;
			ghostFillFilter = go.AddComponent<MeshFilter>();
			ghostFillRenderer = go.AddComponent<MeshRenderer>();
		}

		if (_ghostFillMesh == null)
		{
			_ghostFillMesh = new Mesh
			{
				name = "TargetGhostFill"
			};
			_ghostFillMesh.MarkDynamic();
		}

		ghostFillFilter.sharedMesh = _ghostFillMesh;
		_ghostFillMaterial = ForgingVisualUtility.GetSharedVertexColorMaterial();
		if (ghostFillRenderer != null)
		{
			ghostFillRenderer.sharedMaterial = _ghostFillMaterial;
			ghostFillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			ghostFillRenderer.receiveShadows = false;
			ghostFillRenderer.sortingOrder = Mathf.Max(MinimumGhostFillSortingOrder, ghostFillSortingOrder);
		}
	}

	static void ConfigureOutlineLine(LineRenderer line, Color color, float width, int sortingOrder, float z)
	{
		line.useWorldSpace = true;
		line.loop = true;
		line.widthMultiplier = width;
		ForgingVisualUtility.ApplyLineRendererDefaults(line, color, width, sortingOrder);
		line.startColor = color;
		line.endColor = color;
		line.sortingOrder = sortingOrder;
		line.numCornerVertices = 4;
		line.numCapVertices = 4;
		line.positionCount = 0;
	}

	void RebuildTargetVisuals()
	{
		EnsureOutline();
		EnsureGhostOutline();
		EnsureGhostFill();

		if (targetVertices == null || targetVertices.Length < 2)
		{
			targetOutline.positionCount = 0;
			targetGhostOutline.positionCount = 0;
			if (_ghostFillMesh != null)
			{
				_ghostFillMesh.Clear();
			}

			return;
		}

		ApplyLine(targetOutline, targetOutlineColor, targetLineWidth, outlineSortingOrder, 0.15f);
		if (showGhostOverMetal)
		{
			targetGhostOutline.enabled = true;
			ApplyLine(targetGhostOutline, targetGhostOutlineColor, ghostLineWidth, ghostOutlineSortingOrder, -0.85f);
		}
		else
			targetGhostOutline.enabled = false;

		RebuildGhostFill();
	}

	void ApplyLine(LineRenderer line, Color color, float width, int sortingOrder, float z)
	{
		line.sharedMaterial = ForgingVisualUtility.GetSpritesDefaultMaterial();
		line.positionCount = targetVertices.Length;
		for (int i = 0; i < targetVertices.Length; i++)
		{
			Vector2 v = targetVertices[i];
			line.SetPosition(i, new Vector3(v.x, v.y, z));
		}

		line.startColor = color;
		line.endColor = color;
		line.widthMultiplier = width;
		line.sortingOrder = sortingOrder;
	}

	void RebuildGhostFill()
	{
		bool hasGhostMesh = ghostFillFilter != null && _ghostFillMesh != null;
		bool hasVisibleGhostMesh = showGhostFill && hasGhostMesh;
		bool hasTargetOutline = hasVisibleGhostMesh && targetVertices.Length >= PolygonGeometry.MinimumVertexCount;
		if (!hasVisibleGhostMesh || !hasTargetOutline)
		{
			if (_ghostFillMesh != null)
				_ghostFillMesh.Clear();

			if (ghostFillRenderer != null)
				ghostFillRenderer.enabled = false;

			return;
		}

		ghostFillRenderer.enabled = true;
		ForgingVisualUtility.SetTint(ghostFillRenderer, targetGhostFillColor);
		ghostFillRenderer.sortingOrder = Mathf.Max(MinimumGhostFillSortingOrder, ghostFillSortingOrder);
		ghostBuilder.Build(_ghostFillMesh, targetVertices, ghostFillFilter.transform.position.z - GhostFillDepthOffset, Color.white, ghostFillFilter.transform);
	}

	void RebuildTargetOutline()
	{
		RebuildTargetVisuals();
	}

	Bounds GetTargetBounds()
	{
		Vector2 min = targetVertices[0];
		Vector2 max = targetVertices[0];
		for (int i = 1; i < targetVertices.Length; i++)
		{
			min = Vector2.Min(min, targetVertices[i]);
			max = Vector2.Max(max, targetVertices[i]);
		}

		var center = (min + max) * 0.5f;
		var size = max - min;
		return new Bounds(center, new Vector3(size.x, size.y, 0.1f));
	}

	public static bool PointInPolygon(Vector2 point, Vector2[] polygon) => PolygonGeometry.Contains(point, polygon);

	void OnDrawGizmosSelected()
	{
		if (targetVertices == null || targetVertices.Length < 2)
			return;

		Gizmos.color = targetOutlineColor;
		for (int i = 0; i < targetVertices.Length; i++)
		{
			Vector3 a = targetVertices[i];
			Vector3 b = targetVertices[(i + 1) % targetVertices.Length];
			Gizmos.DrawLine(a, b);
		}
	}
}
