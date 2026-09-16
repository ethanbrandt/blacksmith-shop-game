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
	[Header("Sampling")]
	[SerializeField] int sampleResolution = 96;
	[SerializeField] float boundsPadding = 0.35f;
	[Tooltip("Small silhouette errors within this distance are accepted when measuring coverage and overflow.")]
	[SerializeField, Min(0f)] float boundaryTolerance = 0.02f;

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

	private class CachedQuality
	{
		public IReadOnlyList<Vector2> metalVertices;
		public IReadOnlyList<Vector2> targetVertices;
		public ShapeQuality quality;
	}

	private CachedQuality cachedQuality;

	public ShapeQuality EvaluateQuality(IReadOnlyList<Vector2> _metalVertices, IReadOnlyList<Vector2> _targetVertices)
	{
		bool hasMetalOutline = _metalVertices != null && _metalVertices.Count >= PolygonGeometry.MinimumVertexCount;
		bool hasTargetOutline = _targetVertices != null && _targetVertices.Count >= PolygonGeometry.MinimumVertexCount;
		if (!hasMetalOutline || !hasTargetOutline)
			return ShapeQuality.Incomplete;

		if (IsCached(_metalVertices, _targetVertices))
			return cachedQuality.quality;
		
		Bounds targetBounds = PolygonGeometry.ComputeBounds(_targetVertices);
		Bounds combined = targetBounds;
		combined.Encapsulate(PolygonGeometry.ComputeBounds(_metalVertices));
		combined.Expand(boundsPadding);

		int totalTargetSamples = 0;
		int coveredTargetSamples = 0;
		int totalMetalSamples = 0;
		int overflowMetalSamples = 0;

		float spacing = Mathf.Max(0.001f, Mathf.Max(targetBounds.size.x, targetBounds.size.y) / Mathf.Max(8, sampleResolution));
		Vector2 origin = targetBounds.min;
		int minX = Mathf.FloorToInt((combined.min.x - origin.x) / spacing);
		int minY = Mathf.FloorToInt((combined.min.y - origin.y) / spacing);
		int maxX = Mathf.CeilToInt((combined.max.x - origin.x) / spacing);
		int maxY = Mathf.CeilToInt((combined.max.y - origin.y) / spacing);

		for (int y = minY; y < maxY; y++)
		{
			float py = origin.y + (y + 0.5f) * spacing;
			for (int x = minX; x < maxX; x++)
			{
				float px = origin.x + (x + 0.5f) * spacing;
				var p = new Vector2(px, py);

				bool inTarget = PolygonGeometry.Contains(p, _targetVertices);
				bool inMetal = PolygonGeometry.Contains(p, _metalVertices);

				if (inTarget)
				{
					totalTargetSamples++;
					if (inMetal || DistanceToPolygonBoundary(p, _metalVertices) <= boundaryTolerance)
						coveredTargetSamples++;
				}

				if (inMetal)
				{
					totalMetalSamples++;
					if (!inTarget && DistanceToPolygonBoundary(p, _targetVertices) > boundaryTolerance)
						overflowMetalSamples++;
				}
			}
		}

		float maxVertexProtrusion = ComputeMaxVertexProtrusion(_metalVertices, _targetVertices);
		float spikeScore = ComputeSpikeScore(maxVertexProtrusion);

		float coverageScore = totalTargetSamples > 0 ? coveredTargetSamples / (float)totalTargetSamples : 0f;
		float overflowScore = 1f - (totalMetalSamples > 0 ? overflowMetalSamples / (float)totalMetalSamples : 0f);

		float wC = Mathf.Max(0f, coverageWeight);
		float wO = Mathf.Max(0f, overflowPenaltyWeight);
		float wS = Mathf.Max(0f, spikePenaltyWeight);
		float wSum = wC + wO + wS;
		if (wSum <= 0.0001f)
		{
			wC = wO = wS = 1f;
			wSum = 3f;
		}

		float matchPercent = Mathf.Clamp01((coverageScore * wC + overflowScore * wO + spikeScore * wS) / wSum);

		cachedQuality = new CachedQuality
		{
			metalVertices = new List<Vector2>(_metalVertices),
			targetVertices = new List<Vector2>(_targetVertices),
			quality = ResolveQuality(matchPercent, maxVertexProtrusion)
		};
		return cachedQuality.quality;
	}

	bool IsCached(IReadOnlyList<Vector2> _metalVertices, IReadOnlyList<Vector2> _targetVertices)
	{
		if (cachedQuality == null)
			return false;
		
		if (cachedQuality.metalVertices.Count != _metalVertices.Count || cachedQuality.targetVertices.Count != _targetVertices.Count)
			return false;
		
		for (int i = 0; i < _metalVertices.Count; i++)
			if (!_metalVertices[i].Equals(cachedQuality.metalVertices[i]))
				return false;

		for (int i = 0; i < _targetVertices.Count; i++)
			if (!_targetVertices[i].Equals(cachedQuality.targetVertices[i]))
				return false;
		
		return true;
	}
	
	float ComputeMaxVertexProtrusion(IReadOnlyList<Vector2> _metalVertices, IReadOnlyList<Vector2> _targetVertices)
	{
		float maxDist = 0f;
		for (int i = 0; i < _metalVertices.Count; i++)
		{
			Vector2 p = _metalVertices[i];
			if (PolygonGeometry.Contains(p, _targetVertices))
				continue;

			float dist = DistanceToPolygonBoundary(p, _targetVertices);
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

	static float DistanceToPolygonBoundary(Vector2 point, IReadOnlyList<Vector2> polygon)
	{
		float best = float.MaxValue;
		for (int i = 0; i < polygon.Count; i++)
		{
			Vector2 a = polygon[i];
			Vector2 b = polygon[(i + 1) % polygon.Count];
			best = Mathf.Min(best, Vector2.Distance(point, PolygonGeometry.ClosestOnSegment(point, a, b)));
		}

		return best;
	}


}
